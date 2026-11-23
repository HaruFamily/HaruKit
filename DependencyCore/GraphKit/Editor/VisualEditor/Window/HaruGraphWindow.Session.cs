namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Domain Reload 後替 Tool 入口重建文件 binding 與 context。兩者持有委派，無法跨重載保存；
/// Tool 在 [InitializeOnLoad] 以 <see cref="HaruGraphWindow.RegisterReloadResolver"/> 登記解析器，視窗才能自動接回原文件。
/// documentId 為 null 表示重載前是以 Owner 的唯一文件欄位開啟。不認得的 Owner 或文件回 false。
/// </summary>
public delegate bool HGReloadBindingResolver(UnityEngine.Object owner, string documentId,
    out HGDocumentBinding binding, out HGEditorExtensionContext context);

/// <summary>
/// 開窗入口、Owner 綁定、選取切換、存檔／取消／驗證交易，以及共用資產焦點的進出與引用者重驗。
/// </summary>
public partial class HaruGraphWindow : IHasCustomMenu
{
    // ===== 主題（分頁右上角 ⋮ 選單） =====

    /// <summary>主題切換放在分頁選單：它是本機視圖偏好，不佔工具列空間，也不進文件或 Undo。</summary>
    public void AddItemsToMenu(GenericMenu menu)
    {
        string current = HGTheme.CurrentGuid;
        menu.AddItem(new GUIContent("主題/" + HGTheme.DefaultName), string.IsNullOrEmpty(current), () => HGTheme.Select(""));
        foreach (var (guid, label) in HGTheme.Available())
        {
            string pick = guid;
            menu.AddItem(new GUIContent("主題/" + label), guid == current, () => HGTheme.Select(pick));
        }
        menu.AddSeparator("主題/");
        menu.AddItem(new GUIContent("主題/重新載入"), false, HGTheme.Reload);
        menu.AddItem(new GUIContent("主題/匯出目前主題為範本…"), false, ExportTheme);
    }

    private void ExportTheme()
    {
        string path = EditorUtility.SaveFilePanelInProject("匯出節點圖主題", "MyTheme.graphtheme", "json",
            "檔名需以 .graphtheme.json 結尾，節點圖才找得到。");
        if (string.IsNullOrEmpty(path)) return;
        if (!path.EndsWith(HGTheme.FileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            ShowNotification(new GUIContent($"檔名要以 {HGTheme.FileSuffix} 結尾"));
            return;
        }
        if (!HGTheme.Export(path, out string error))
        {
            Debug.LogError($"[GraphKit] 主題匯出失敗：{path}。{error}");
            ShowNotification(new GUIContent("主題匯出失敗，詳見 Console"));
            return;
        }
        AssetDatabase.ImportAsset(path);
        ShowNotification(new GUIContent("已匯出：" + path));
    }

    // ===== 開啟 =====

    /// <summary>開窗並聚焦到指定對象。Owner 直接編輯，共用資產則借引用者當上下文下鑽。</summary>
    public static void OpenFor(UnityEngine.Object target)
        => OpenFor(target, HGEditorExtensionContext.Default);

    /// <summary>以明確的 Editor extension context 開啟一個新的編輯 session。</summary>
    public static void OpenFor(UnityEngine.Object target, HGEditorExtensionContext context)
    {
        var window = OpenWindow();
        var requested = context ?? HGEditorExtensionContext.Default;
        if (target == null)
        {
            window.sessionContext = requested;
            window.activeContext = HGEditorExtensionContext.Default;
            window.usesExplicitToolEntry = !ReferenceEquals(requested, HGEditorExtensionContext.Default);
            window.requiresToolReopenAfterReload = false;
            window.graphDirty = true;
            window.Repaint();
            return;
        }

        var owner = ResolveOwner(target);
        if (owner != null) { window.Bind(owner, requested); return; }
        if (target is ScriptableObject so && IsSharedAsset(so))
        {
            window.sessionContext = requested;
            window.activeContext = window.model != null
                && window.sessionContext.Supports(window.model.Owner, window.model.Doc)
                    ? window.sessionContext
                    : HGEditorExtensionContext.Default;
            window.graphDirty = true;
            window.OpenSharedAsset(so);
        }
    }

    /// <summary>Opens one explicitly selected document on an Owner without legacy field discovery.</summary>
    public static void OpenForDocument(UnityEngine.Object owner, HGDocumentBinding binding,
        HGEditorExtensionContext context = null)
    {
        if (binding == null) throw new ArgumentNullException(nameof(binding));
        var window = OpenWindow();
        window.BindDocument(owner, binding, context ?? HGEditorExtensionContext.Default);
    }

    // ===== 截圖專用視窗 =====

    /// <summary>
    /// 開一個獨立、用完即丟的視窗來畫指定文件，給工具截圖用；不動使用者已開的視窗。
    /// 它不跟選取、不存偏好與版面、不參與重載暫存，關閉時也不詢問存檔。呼叫端負責 <see cref="CloseCaptureView"/>。
    /// <paramref name="screenRect"/> 的位置是那一小塊原生視窗出現的地方，大小是畫布的繪製尺寸。
    /// </summary>
    public static HaruGraphWindow OpenCaptureView(UnityEngine.Object owner, HGDocumentBinding binding,
        HGEditorExtensionContext context, Rect screenRect)
    {
        if (owner == null || binding == null) return null;
        var window = CreateInstance<HaruGraphWindow>();
        window.captureOnly = true;
        window.locked = true;
        window.suppressProjectSelect = true;
        window.hideFlags = HideFlags.DontSave;
        // 以 Tooltip 模式、不給焦點開啟：一般 ShowPopup 會啟用視窗，連帶把 Unity 拉到前景打斷使用者。
        // 內部 API 不存在時直接放棄，不退回會搶焦點的開法。
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var showMode = typeof(EditorWindow).Assembly.GetType("UnityEditor.ShowMode");
        var show = showMode == null ? null
            : typeof(EditorWindow).GetMethod("ShowPopupWithMode", any, null, new[] { showMode, typeof(bool) }, null);
        if (show == null || !Enum.IsDefined(showMode, "Tooltip"))
        {
            DestroyImmediate(window);
            return null;
        }
        // 原生視窗維持最小尺寸（實測會被撐到約 100×56），只把內部的 View 放大到截圖尺寸：
        // 版面與 GrabPixels 跟著 View 走，螢幕上只會短暫出現那一小塊。
        // 實測過但行不通的做法：放到螢幕外（Unity 會夾回螢幕內）、ContainerWindow.SetAlpha(0)（視窗照樣可見）。
        window.minSize = Vector2.one;
        window.position = new Rect(screenRect.position, Vector2.one);
        show.Invoke(window, new object[] { Enum.Parse(showMode, "Tooltip"), false });
        object host = typeof(EditorWindow).GetField("m_Parent", any)?.GetValue(window);
        var viewPosition = host?.GetType().GetProperty("position", any);
        if (viewPosition == null || !viewPosition.CanWrite)
        {
            window.Close();
            return null;
        }
        viewPosition.SetValue(host, new Rect(Vector2.zero, screenRect.size));
        if (window.BindDocument(owner, binding, context ?? HGEditorExtensionContext.Default)) return window;
        window.Close();
        return null;
    }

    /// <summary>
    /// 截圖視窗對準目標：<paramref name="nodeId"/> 為空時框住整張圖，否則框住該節點與內放在它裡面的節點。
    /// 要在視窗至少畫過一次（畫布大小已知）之後呼叫。
    /// </summary>
    public bool FrameCaptureView(string nodeId)
    {
        if (!captureOnly || model == null) return false;
        EnsureGraph();
        if (graph == null) return false;
        pendingCenterTarget = null;
        if (string.IsNullOrEmpty(nodeId)) { FrameAll(); return true; }
        HGNodeView target = null;
        foreach (var node in graph.Nodes)
            if (node.Id == nodeId) { target = node; break; }
        if (target == null) return false;
        if (target.Hidden)
        {
            RevealNode(target);
            graphDirty = true;
            EnsureGraph();
            target = null;
            foreach (var node in graph.Nodes)
                if (node.Id == nodeId) { target = node; break; }
            if (target == null) return false;
        }
        Rect bounds = target.Rect;
        foreach (var node in graph.Nodes)
        {
            if (node.Hidden || !HGGraph.NestedWithin(node, target)) continue;
            bounds.xMin = Mathf.Min(bounds.xMin, node.Rect.xMin);
            bounds.yMin = Mathf.Min(bounds.yMin, node.Rect.yMin);
            bounds.xMax = Mathf.Max(bounds.xMax, node.Rect.xMax);
            bounds.yMax = Mathf.Max(bounds.yMax, node.Rect.yMax);
        }
        FrameRect(bounds);
        return true;
    }

    /// <summary>關閉截圖視窗並丟掉它的工作副本。</summary>
    public void CloseCaptureView()
    {
        if (!captureOnly) return;
        hasUnsavedChanges = false;
        Close();
    }

    /// <summary>Returns bounded commands for this binding. Handles expire when the window binds another document.</summary>
    public HGWindowSession GetDocumentCommands()
        => model == null ? null : new HGWindowSession(this, model);

    /// <summary>Reads the bound document identity without rebuilding the canvas or opening a session.</summary>
    public bool TryGetDocumentInspectionInfo(out UnityEngine.Object owner, out string documentId, out bool dirty)
    {
        owner = model?.Owner;
        documentId = model?.DocumentId;
        dirty = model != null && model.Dirty;
        return owner != null && model.Data != null;
    }

    /// <summary>Reads the independent shared-asset transaction without entering or rebuilding its focus.</summary>
    public bool TryGetAssetInspectionInfo(out UnityEngine.Object asset, out bool dirty)
    {
        asset = focus?.Kind == HGFocusKind.Asset ? focus.AssetObject : null;
        dirty = asset != null && assetDirty;
        return asset != null;
    }

    /// <summary>Captures the entire bound document draft, not a shared-asset focus or visible canvas.</summary>
    public HGDocumentSnapshot CaptureDocumentSnapshot()
        => model?.Owner != null && model.Data != null
            ? HGDocumentInspection.Capture(model.Data, "window_working_copy") : null;

    /// <summary>供 Tool 的結果面板沿用目前工作副本，避免定位節點時重新綁定而丟失編輯。</summary>
    public bool IsBoundToDocument(UnityEngine.Object owner, string documentId)
        => model != null && model.Owner == owner && model.DocumentId == documentId;

    /// <summary>以穩定 Id 選取並置中目前文件中的節點或內嵌格子；只改視圖。</summary>
    public bool FocusDocumentNode(string nodeId)
    {
        if (model == null || string.IsNullOrEmpty(nodeId)) return false;
        EnsureGraph();
        if (graph == null) return false;
        HGNodeView target = null;
        foreach (var node in graph.Nodes)
        {
            if (node.Id == nodeId) { target = node; break; }
            if (target != null) break;
        }
        if (target == null)
        {
            ShowNotification(new GUIContent("目前畫布找不到這個結果節點，可能已刪除或換來源。"));
            return false;
        }
        if (target.Hidden) RevealNode(target);
        selectedIds.Clear();
        selectedIds.Add(target.Id);
        pendingCenterTarget = target.Carrier ?? target.Obj;
        graphDirty = true;
        Focus();
        Repaint();
        return true;
    }

    internal HGWindowSnapshot QueryDocument(HGModel expected)
    {
        if (!ReferenceEquals(model, expected) || model?.Owner == null) return null;
        EnsureGraph();
        var ports = new List<HGPortDescriptor>();
        var nodes = new List<HGNodeViewInfo>();
        var links = new List<HGWindowLink>();
        foreach (var port in graph.Ports)
            ports.Add(new HGPortDescriptor(port, port.IsOutput && graph.PrimaryOutputs.TryGetValue(port.Source.OutputNode, out var primary)
                && ReferenceEquals(primary, port)));
        foreach (var node in graph.Nodes) nodes.Add(new HGNodeViewInfo(node));
        foreach (var link in graph.Links)
            if (link.InputPort != null && link.OutputPort != null)
                links.Add(new HGWindowLink(link.InputPort.Key, link.OutputPort.Key));
        return new HGWindowSnapshot(graphGeneration, ports, nodes, links, focus.Kind == HGFocusKind.Asset ? assetDirty : model.Dirty);
    }

    internal HGSessionCommandResult ConnectDocument(HGModel expected, int generation, HGPortKey first, HGPortKey second)
    {
        if (QueryDocument(expected) == null || generation != graphGeneration) return HGSessionCommandResult.StaleGeneration;
        if (!graph.PortsByKey.TryGetValue(first, out var a) || !graph.PortsByKey.TryGetValue(second, out var b))
            return HGSessionCommandResult.Rejected;
        return TryConnectPorts(a, b) switch
        {
            PortCommandResult.Changed => HGSessionCommandResult.Changed,
            PortCommandResult.NoChange => HGSessionCommandResult.NoChange,
            _ => HGSessionCommandResult.Rejected,
        };
    }

    internal HGSessionCommandResult DisconnectDocument(HGModel expected, int generation, HGPortKey key)
    {
        if (QueryDocument(expected) == null || generation != graphGeneration) return HGSessionCommandResult.StaleGeneration;
        if (!graph.PortsByKey.TryGetValue(key, out var port)
            || !port.IsInput
            || port.Binding is IHGListAppendBinding
            || port.Presentation.Locked || !port.Presentation.Visible)
            return HGSessionCommandResult.Rejected;
        return CutLink(port.InputSlot) switch
        {
            PortCommandResult.Changed => HGSessionCommandResult.Changed,
            PortCommandResult.NoChange => HGSessionCommandResult.NoChange,
            _ => HGSessionCommandResult.Rejected,
        };
    }

    internal HGSessionCommandResult EditDocumentValue(HGModel expected, int generation, string nodeId, string fieldPath, object value)
    {
        if (QueryDocument(expected) == null || generation != graphGeneration) return HGSessionCommandResult.StaleGeneration;
        var node = NodeOfId(nodeId);
        if (node == null || node.InLockedSubtree) return HGSessionCommandResult.Rejected;
        return EditDescriptorValue(RowOf(nodeId, fieldPath), value);
    }

    internal HGSessionCommandResult DocumentHistory(HGModel expected, bool redo)
    {
        if (!ReferenceEquals(model, expected)) return HGSessionCommandResult.StaleGeneration;
        return (redo ? DoRedo() : DoUndo()) ? HGSessionCommandResult.Changed : HGSessionCommandResult.NoChange;
    }

    internal HGSessionCommandResult CommitDocument(HGModel expected)
    {
        if (QueryDocument(expected) == null) return HGSessionCommandResult.StaleGeneration;
        bool saved = focus.Kind == HGFocusKind.Asset ? SaveAsset(false) : DoSave(false);
        if (!saved) return model.LastCommitDiagnostic != null && focus.Kind != HGFocusKind.Asset
            ? HGDocumentCommitGuard.ResultOf(model.LastCommitDiagnostic)
            : HGSessionCommandResult.ValidationFailed;
        ClearPortInteractionState();
        graphDirty = true;
        return HGSessionCommandResult.Changed;
    }

    internal IReadOnlyList<GraphDiagnostic> ValidateDocument(HGModel expected)
    {
        var diagnostics = new List<GraphDiagnostic>();
        if (QueryDocument(expected) == null)
        {
            diagnostics.Add(new GraphDiagnostic("graphkit.session.stale", GraphDiagnosticSeverity.Error,
                "The window is no longer bound to this document."));
            return diagnostics.AsReadOnly();
        }
        DoVerify(true);
        foreach (var issue in Rep.Issues) diagnostics.Add(issue.Diagnostic);
        if (focus.Kind != HGFocusKind.Asset && model.LastCommitDiagnostic != null)
            diagnostics.Add(model.LastCommitDiagnostic);
        return diagnostics.AsReadOnly();
    }

    internal HGSessionCommandResult CancelDocument(HGModel expected)
    {
        if (!ReferenceEquals(model, expected) || model?.Owner == null) return HGSessionCommandResult.StaleGeneration;
        if (focus.Kind == HGFocusKind.Asset) return HGSessionCommandResult.Rejected;
        ClearPortInteractionState();
        if (!model.TryReload()) return HGSessionCommandResult.Rejected;
        focusReturns.Clear();
        focus = AllRootsFocus();
        selectedIds.Clear();
        graphDirty = true;
        ClearValidationReport();
        UpdateUnsavedState();
        return HGSessionCommandResult.Changed;
    }

    [MenuItem("HaruFamily/GraphKit/Open")]
    public static void OpenFromMenu()
    {
        var window = OpenWindow();
        window.sessionContext = HGEditorExtensionContext.Default;
        window.activeContext = HGEditorExtensionContext.Default;
        window.usesExplicitToolEntry = false;
        window.requiresToolReopenAfterReload = false;
        window.graphDirty = true;
        window.Repaint();
    }

    private static HaruGraphWindow OpenWindow()
    {
        var window = GetWindow<HaruGraphWindow>();
        window.minSize = new Vector2(980f, 560f);
        window.ApplyWindowTitle();
        window.Show();
        return window;
    }

    /// <summary>標題跟著綁定的圖走，所以每次換對象都要重套一次。</summary>
    // GetWindow 的 title 參數只在「建立」時生效，既存視窗會沿用序列化下來的舊標題。一律自己設。
    private void ApplyWindowTitle()
    {
        string text = HGGraph.WindowTitle(model?.Doc);
        if (titleContent.text != text) titleContent = new GUIContent(text);
    }

    /// <summary>從資產開啟（Project 視窗右鍵）。Owner 直接編輯；公式／動作資產則找一個引用它的 Owner 當上下文後下鑽。</summary>
    [MenuItem("Assets/HaruFamily/Open in HaruGraph", false, 30)]
    public static void OpenFromAsset() => OpenFor(Selection.activeObject);

    [MenuItem("Assets/HaruFamily/Open in HaruGraph", true)]
    public static bool OpenFromAssetValidate()
        => Selection.activeObject is ScriptableObject so && (HGModel.CanEdit(so) || IsSharedAsset(so));

    /// <summary>是否為公式／動作資產（可下鑽編輯的共用資產）。</summary>
    private static bool IsSharedAsset(ScriptableObject so) => so is IGraphAsset;

    /// <summary>從引用者裡挑一個可以當上下文的 Owner。索引是現算的，只有專案裡真的沒人引用時才是空的。</summary>
    private static ScriptableObject FindContextOwner(ScriptableObject asset)
    {
        foreach (var so in HGReferenceIndex.Users(asset))
            if (so != null && HGModel.CanEdit(so)) return so;
        return null;
    }

    /// <summary>資產本身沒有Token清單與欄位型別，必須借一個引用它的 Owner 當上下文。</summary>
    private void OpenSharedAsset(ScriptableObject asset)
    {
        if (focus.Kind == HGFocusKind.Asset)
        {
            if (focus.AssetObject == asset) return;
            if (!ConfirmLeaveAsset()) return;
            ExitAsset();
        }
        if (TryEnterSharedAsset(asset)) return;

        var owner = FindContextOwner(asset);
        // 索引可能是這個 session 早先算的，中間有人在別的視窗存了檔。重掃一次再判定「真的沒人引用」。
        if (owner == null)
        {
            HGReferenceIndex.Refresh();
            owner = FindContextOwner(asset);
        }
        if (owner == null)
        {
            EditorUtility.DisplayDialog("找不到引用者",
                $"專案裡沒有任何已存檔的對象引用 '{asset.name}'。\n\n若引用它的對象還沒存檔，先存檔再試；" +
                "或直接從那個對象的圖上雙擊這顆資產節點下鑽。", "好");
            return;
        }

        if (!BindInSession(owner)) return;
        if (TryEnterSharedAsset(asset)) return;
        EditorUtility.DisplayDialog("找不到引用點",
            $"索引說 '{owner.name}' 引用這個資產，但它的內容裡找不到指向它的欄位。\n磁碟上的資料可能剛被外部改過，重開視窗再試。", "好");
    }

    private bool TryEnterSharedAsset(ScriptableObject asset)
    {
        if (model == null) return false;
        foreach (var slot in model.AllSlots())
        {
            if (HGReflect.GetAsset(slot) != asset) continue;
            EnterAsset(asset, slot.GetType());
            return true;
        }

        Type compatibleSlot = HGReflect.SlotTypeForAsset(asset, AssetSlotTypes());
        if (compatibleSlot == null) return false;

        // 目前的 Owner 沒有引用它也沒關係：資產只是借它的型別當上下文，不需要真的連著。
        EnterAsset(asset, compatibleSlot);
        return true;
    }

    public bool Bind(UnityEngine.Object owner)
        => Bind(owner, HGEditorExtensionContext.Default);

    /// <summary>開始一個使用明確 Editor extension context 的新 session。</summary>
    public bool Bind(UnityEngine.Object owner, HGEditorExtensionContext context)
    {
        var previous = sessionContext;
        bool previousExplicitEntry = usesExplicitToolEntry;
        bool previousReopenRequirement = requiresToolReopenAfterReload;
        sessionContext = context ?? HGEditorExtensionContext.Default;
        requiresToolReopenAfterReload = false;
        if (BindOwner(owner, null))
        {
            usesExplicitToolEntry = !ReferenceEquals(sessionContext, HGEditorExtensionContext.Default);
            requiresToolReopenAfterReload = false;
            return true;
        }
        sessionContext = previous;
        usesExplicitToolEntry = previousExplicitEntry;
        requiresToolReopenAfterReload = previousReopenRequirement;
        return false;
    }

    /// <summary>Begins a session for one Tool-selected document binding.</summary>
    public bool BindDocument(UnityEngine.Object owner, HGDocumentBinding binding, HGEditorExtensionContext context)
    {
        if (binding == null) return false;
        var previousContext = sessionContext;
        bool previousExplicitEntry = usesExplicitToolEntry;
        bool previousReopenRequirement = requiresToolReopenAfterReload;
        sessionContext = context ?? HGEditorExtensionContext.Default;
        if (BindOwner(owner, binding))
        {
            usesExplicitToolEntry = true;
            requiresToolReopenAfterReload = false;
            return true;
        }
        sessionContext = previousContext;
        usesExplicitToolEntry = previousExplicitEntry;
        requiresToolReopenAfterReload = previousReopenRequirement;
        return false;
    }

    /// <summary>在目前 context 內切換 Owner；其他 Owner 的文件由唯一欄位重新解析。</summary>
    private bool BindInSession(UnityEngine.Object owner)
        => BindOwner(owner, ReferenceEquals(owner, model?.Owner) ? sessionBinding : null);

    private bool BindOwner(UnityEngine.Object owner, HGDocumentBinding binding)
    {
        if (requiresToolReopenAfterReload && binding == null)
        {
            ShowNotification(new GUIContent("Tool 接入已因重載失效，請從原 Tool 重新開啟節點圖。"));
            return false;
        }
        // Owner 已不存在時，它的工作副本沒有地方可存，不必再問。
        if (HasUnsavedWork && (model?.Owner != null || assetDirty) && !EditorUtility.DisplayDialog(
                "尚未儲存", $"'{(model?.Owner != null ? model.Owner.name : "?")}' 有未儲存的修改，切換後會遺失。要繼續嗎？", "捨棄並切換", "取消"))
            return false;

        // 新文件成功載入前，保留目前工作副本、歷程與資產焦點。
        var nextModel = new HGModel();
        if (!nextModel.Bind(owner, binding)) return false;
        var nextContext = sessionContext.Supports(owner, nextModel.Doc)
            ? sessionContext
            : HGEditorExtensionContext.Default;
        nextModel.SetRootAdapter(nextContext.Profile?.RootAdapter);

        returnFocus = null;
        focusReturns.Clear();
        ClearPortInteractionState();
        ClearAssetDirty();
        assetReport = new HGReport();
        assetVerifiedOnce = false;
        assetReportStale = false;
        model = nextModel;
        sessionBinding = binding;
        activeContext = nextContext;
        drawerFailures.Clear();
        pendingTarget = null;

        focus = new HGFocus();
        ClearViewState();
        graphDirty = true;
        ClearValidationReport();

        // 所有時機共用一張畫布，綁定後直接進去；不再有「記住上次看的是哪個時機」這件事。
        SetFocus(AllRootsFocus());
        // Domain Reload 前暫存的草稿在這裡接回：自動還原與使用者從原 Tool 重新開啟都會經過。
        AdoptReloadStash();

        ApplyWindowTitle();
        UpdateUnsavedState();
        Repaint();
        return true;
    }

    /// <summary>
    /// 左上角選擇器選定：綁定成功後把 Inspector 也帶過去，兩邊看的是同一個對象。
    /// 順序不可顛倒——先設 Selection 會讓 OnSelectionChange 搶先 Bind 一次，接著這裡再 Bind 一次。
    /// 先 Bind 的話，OnSelectionChange 會因為「選到的就是目前 Owner」而直接跳過。
    /// </summary>
    private void PickOwner(ScriptableObject owner)
    {
        if (!BindInSession(owner)) return;
        Selection.activeObject = owner;
        Repaint();
    }

    /// <summary>從 Project／Hierarchy 選到支援的對象就自動聚焦。有未儲存變更時不硬切，改成在工具列問。</summary>
    private void OnSelectionChange()
    {
        // 鎖定時整個不動作：不換對象、不下鑽資產、也不記待切換。
        // 擋在最前面而不是逐條判斷——這個視窗跟外部選取有關的入口只有這一個，擋這裡就全涵蓋。
        if (locked) return;
        if (requiresToolReopenAfterReload && sessionBinding == null)
        {
            ShowNotification(new GUIContent("Tool 接入已因重載失效，請從原 Tool 重新開啟節點圖。"));
            return;
        }

        if (Selection.activeObject is ScriptableObject asset && IsSharedAsset(asset))
        {
            if (focus.Kind == HGFocusKind.Asset && focus.AssetObject == asset) return;

            // 同一 Owner 的工作副本已引用此資產時可安全下鑽，不會丟掉 Owner 修改。
            if (focus.Kind != HGFocusKind.Asset && TryEnterSharedAsset(asset))
            {
                pendingTarget = null;
                Repaint();
                return;
            }

            bool assetSwitchBusy = model != null && (model.Dirty || (focus.Kind == HGFocusKind.Asset && assetDirty));
            if (assetSwitchBusy) pendingTarget = asset;
            else { pendingTarget = null; OpenSharedAsset(asset); }
            Repaint();
            return;
        }

        var picked = ResolveOwner(Selection.activeObject);
        if (picked == null)
        {
            if (model == null) return;
            // 有未存修改時，點到無關的東西不打斷編輯，也不跳對話框；沒有修改才回閒置。
            // Owner 已不存在時沒有東西可存，照常回閒置。
            if (model.Owner != null && HasUnsavedWork) return;
            if (TryReturnToIdle()) ReturnToIdle();
            return;
        }
        if (model != null && ReferenceEquals(picked, model.Owner)) return;

        // Owner 已被刪除時工具列不會畫出待切換提示，直接綁到新對象。
        bool busy = model != null && model.Owner != null && (model.Dirty || focus.Kind == HGFocusKind.Asset);
        if (busy) pendingTarget = picked;
        else { pendingTarget = null; BindInSession(picked); }
        Repaint();
    }

    /// <summary>離開目前編輯交易並回到無選取版型；任一存檔失敗或取消都留在原畫面。</summary>
    private bool TryReturnToIdle()
    {
        // Owner 被刪除或重載後會成為 Unity 的偽 null，不能再讀取其名稱或嘗試存檔。
        if (model?.Owner == null) return true;

        if (focus.Kind == HGFocusKind.Asset && assetDirty)
        {
            int choice = EditorUtility.DisplayDialogComplex("資產未儲存",
                "目前資產有未儲存的修改。", "存檔並離開", "捨棄並離開", "取消");
            if (choice == 2)
            {
                RestoreOwnerSelection();
                return false;
            }
            // 存檔本身不再退出資產，所以要自己往上退；存檔失敗就留在原畫面。
            if (choice == 0 && !SaveAsset()) return false;
        }

        if (focus.Kind == HGFocusKind.Asset) ExitAsset();

        if (!model.Dirty) return true;

        int ownerChoice = EditorUtility.DisplayDialogComplex("編輯對象未儲存",
            $"'{model.Owner.name}' 有未儲存的修改。", "存檔並離開", "捨棄並離開", "取消");
        if (ownerChoice == 2)
        {
            RestoreOwnerSelection();
            return false;
        }
        if (ownerChoice == 0)
        {
            DoSave();
            return !model.Dirty;
        }
        return true;
    }

    /// <summary>取消離開時，Graph 焦點與 Unity Project／Hierarchy 的選取必須維持同一個 Owner。</summary>
    private void RestoreOwnerSelection()
    {
        var owner = model?.Owner;
        if (owner == null) return;

        Selection.activeObject = owner;
        // OnSelectionChange 期間的寫入會被 Unity 的原選取事件覆蓋，下一個 Editor tick 再確認一次。
        EditorApplication.delayCall += () =>
        {
            if (this == null || model?.Owner != owner) return;
            if (ResolveOwner(Selection.activeObject) == null) Selection.activeObject = owner;
        };
    }

    /// <summary>清除工作副本與互動狀態，保留視窗的閒置三欄版型。</summary>
    private void ReturnToIdle()
    {
        ClearPortInteractionState();
        model = null;
        activeContext = HGEditorExtensionContext.Default;
        sessionBinding = null;
        usesExplicitToolEntry = false;
        requiresToolReopenAfterReload = false;
        focus = new HGFocus();
        graph = null;
        graphDirty = true;
        report = new HGReport();
        assetReport = new HGReport();
        verifiedOnce = false;
        reportStale = false;
        assetVerifiedOnce = false;
        assetReportStale = false;
        tokenLibrary.Reset();
        assetLibrary.Reset();
        propertyLibrary.Reset();
        pendingTarget = null;
        returnFocus = null;
        focusReturns.Clear();
        pendingFocusStart = false;
        ClearAssetDirty();
        ClearViewState();
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>把選取物解析成可編輯對象：SO 直接用，GameObject 找身上帶 LogicGraph 的元件。</summary>
    private static UnityEngine.Object ResolveOwner(UnityEngine.Object selected)
    {
        if (selected == null) return null;
        if (HGModel.CanEdit(selected)) return selected;

        if (selected is GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && HGModel.CanEdit(c)) return c;
        }
        return null;
    }

    private void OnEnable()
    {
        EditorApplication.update += UpdateExecutionView;
        EditorApplication.playModeStateChanged += ExecutionPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += CancelObservedExecutions;
        // 退訂放在 OnDestroy：重載時 OnDisable 與 beforeAssemblyReload 的先後不該影響暫存。
        AssemblyReloadEvents.beforeAssemblyReload -= StashForReload;
        AssemblyReloadEvents.beforeAssemblyReload += StashForReload;
        if (usesExplicitToolEntry && sessionBinding == null) requiresToolReopenAfterReload = true;
        if (reloadPending)
        {
            // 版面檔帶回來的是上一次 Editor 行程的殘留：草稿裡的物件引用已失效，直接丟掉。
            if (!SessionState.GetBool(ReloadAliveKey, false)) ClearReloadStash();
            else if (model == null)
            {
                // 用 update 輪詢而不是 delayCall：實測重載期間登記的 delayCall 沒有把視窗接回來，
                // 而且 Tool 的解析器要等它的 [InitializeOnLoad] 跑完才存在。
                reloadRestoreDeadline = EditorApplication.timeSinceStartup + 10d;
                EditorApplication.update -= PollReloadRestore;
                EditorApplication.update += PollReloadRestore;
            }
        }
        sessionContext ??= HGEditorExtensionContext.Default;
        activeContext ??= HGEditorExtensionContext.Default;
        saveChangesMessage = $"{HGGraph.DefaultWindowTitle} 有未儲存的修改。是否在關閉前存檔？";
        inlineName ??= new HGInlineRename(Repaint);
        console.LoadPrefs();
        leftWidth = EditorPrefs.GetFloat(PrefLeftWidth, DefaultLeftWidth);
        refSectionHeight = EditorPrefs.GetFloat(PrefRefSection, DefaultRefSection);
        LoadLibraryLayout();
        UpdateUnsavedState();
    }

    private void OnDestroy()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= StashForReload;
        EditorApplication.update -= PollReloadRestore;
        ClearReloadStash();
    }

    // ===== Domain Reload：暫存與還原 =====

    /// <summary>登記重載後的 binding 解析器。靜態狀態每次重載都會清空，Tool 要在 [InitializeOnLoad] 裡登記。</summary>
    public static void RegisterReloadResolver(HGReloadBindingResolver resolver)
    {
        if (resolver != null && !reloadResolvers.Contains(resolver)) reloadResolvers.Add(resolver);
    }

    /// <summary>
    /// Domain Reload（編譯、進出 Play Mode）前收起綁定對象與未存草稿；工作副本不序列化，不收就會無聲消失。
    /// 文件草稿以 Unity 的序列化規則轉成 JSON，保留的內容等同「存檔再讀回」；Undo 歷程不保留。
    /// </summary>
    private void StashForReload()
    {
        if (this == null || captureOnly) return;
        // 沒有綁定時不動既有暫存：可能正等著使用者從原 Tool 重新開啟。
        if (model?.Owner == null || model.Data == null) return;
        ClearReloadStash();

        reloadOwner = model.Owner;
        reloadDocumentId = sessionBinding != null ? model.DocumentId : null;
        reloadNeedsTool = usesExplicitToolEntry || sessionBinding != null;
        reloadPan = pan;
        reloadZoom = zoom;
        reloadLocked = locked;
        reloadTokenId = focus.Kind == HGFocusKind.Token ? focus.Token?.Id : null;
        if (model.Dirty)
        {
            try
            {
                reloadDraftJson = JsonUtility.ToJson(model.Data);
                reloadDraftContentDirty = model.ContentDirty;
            }
            catch (Exception exception)
            {
                reloadDraftJson = null;
                Debug.LogWarning($"[GraphKit] '{model.Owner.name}' 的未儲存修改無法暫存，重載後會回到已存檔的內容：{exception.Message}");
            }
        }
        if (focus.Kind == HGFocusKind.Asset && focus.AssetObject is ScriptableObject asset)
        {
            reloadAsset = asset;
            if (assetDirty) reloadAssetStash = CreateAssetStash(asset);
        }
        SessionState.SetBool(ReloadAliveKey, true);
        reloadPending = true;
    }

    /// <summary>
    /// 把資產焦點的未存草稿寫進一份隱藏複本，交給 Unity 帶過 Domain Reload；原資產不動。
    /// 寫入步驟與 <see cref="SaveAsset"/> 相同，四部分同一次深複製以保留共享引用。
    /// </summary>
    private ScriptableObject CreateAssetStash(ScriptableObject asset)
    {
        ScriptableObject stash = null;
        try
        {
            var host = focus.AssetHostSlot;
            if (host == null) return null;
            var packCopy = GraphDeepCopy.Copy(new List<object>
            {
                host,
                focus.AssetOrphans ?? new List<GraphNode>(),
                focus.AssetTokens ?? new List<GraphToken>(),
                focus.AssetProperties ?? new List<GraphProperty>(),
            });
            if (packCopy == null) return null;

            stash = Instantiate(asset);
            stash.hideFlags = HideFlags.HideAndDontSave;
            var storedInput = packCopy[0] as GraphSlotBase;
            if (stash is IFormulaGraphAsset formulaStash) formulaStash.SetInput((FormulaSlotBase)storedInput);
            else
            {
                var setRoot = stash.GetType().GetMethod("SetRoot");
                if (setRoot == null) throw new InvalidOperationException(stash.GetType().Name + " 沒有 SetRoot。");
                setRoot.Invoke(stash, new object[] { storedInput?.Node });
            }

            var storedOrphans = HGReflect.Orphans(stash);
            if (storedOrphans != null)
            {
                storedOrphans.Clear();
                if (packCopy[1] is List<GraphNode> orphanCopy) storedOrphans.AddRange(orphanCopy);
            }
            if (HGReflect.Tokens(stash) is List<GraphToken> storedTokens)
            {
                storedTokens.Clear();
                if (packCopy[2] is List<GraphToken> endpointCopy) storedTokens.AddRange(endpointCopy);
            }
            if (HGReflect.Properties(stash) is List<GraphProperty> storedProperties)
            {
                storedProperties.Clear();
                if (packCopy[3] is List<GraphProperty> propertyCopy) storedProperties.AddRange(propertyCopy);
            }
            return stash;
        }
        catch (Exception exception)
        {
            if (stash != null) DestroyImmediate(stash);
            Debug.LogWarning($"[GraphKit] 資產 '{asset.name}' 的未儲存修改無法暫存，重載後會回到已存檔的內容：{exception.Message}");
            return null;
        }
    }

    /// <summary>把暫存複本的內容換成目前資產焦點的工作副本，並標成未存檔。</summary>
    private bool ApplyAssetStash(ScriptableObject stash)
    {
        if (stash == null || focus.Kind != HGFocusKind.Asset || focus.AssetHostSlot == null) return false;
        var formulaStash = stash as IFormulaGraphAsset;
        var packCopy = GraphDeepCopy.Copy(new List<object>
        {
            formulaStash != null ? (object)formulaStash.Input : HGReflect.AssetRoot(stash),
            HGReflect.Orphans(stash) ?? new List<GraphNode>(),
            HGReflect.Tokens(stash) ?? new List<GraphToken>(),
            HGReflect.Properties(stash) ?? new List<GraphProperty>(),
        });
        if (packCopy == null) return false;

        GraphSlotBase host;
        if (formulaStash != null) host = packCopy[0] as GraphSlotBase;
        else
        {
            host = focus.AssetHostSlot;
            HGReflect.SetNode(host, packCopy[0] as GraphNode);
        }
        if (host == null) return false;

        ApplyAssetState(new HGAssetSnapshot
        {
            Input = host,
            Orphans = packCopy[1] as List<GraphNode> ?? new List<GraphNode>(),
            Tokens = packCopy[2] as List<GraphToken> ?? new List<GraphToken>(),
            Properties = packCopy[3] as List<GraphProperty> ?? new List<GraphProperty>(),
        });
        return true;
    }

    private void ClearReloadStash()
    {
        reloadPending = false;
        reloadOwner = null;
        reloadDocumentId = null;
        reloadNeedsTool = false;
        reloadDraftJson = null;
        reloadDraftContentDirty = false;
        reloadTokenId = null;
        reloadAsset = null;
        if (reloadAssetStash != null)
        {
            // 複本是短命資產：銷毀前移除求值快取，避免 static dictionary 留著已銷毀的物件。
            AssetGraphSchema.InvalidateCache(reloadAssetStash);
            DestroyImmediate(reloadAssetStash);
        }
        reloadAssetStash = null;
    }

    private static bool TryResolveReloadBinding(UnityEngine.Object owner, string documentId,
        out HGDocumentBinding binding, out HGEditorExtensionContext context)
    {
        foreach (var resolver in reloadResolvers)
        {
            try
            {
                if (resolver(owner, documentId, out var resolvedBinding, out var resolvedContext) && resolvedBinding != null)
                {
                    binding = resolvedBinding;
                    context = resolvedContext;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GraphKit] 重載後的 binding 解析器失敗：{exception.Message}");
            }
        }
        binding = null;
        context = null;
        return false;
    }

    /// <summary>重載後每個 Editor tick 嘗試接回，直到成功、已被其他入口綁定，或超過期限。</summary>
    private void PollReloadRestore()
    {
        if (this == null)
        {
            EditorApplication.update -= PollReloadRestore;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool final = EditorApplication.timeSinceStartup >= reloadRestoreDeadline;
        if (RestoreAfterReload(final)) EditorApplication.update -= PollReloadRestore;
    }

    /// <summary>
    /// 重載後重新綁定原對象；草稿由 <see cref="AdoptReloadStash"/> 在綁定成功時接回。
    /// 回傳 true 表示這次接回已有結論（成功、放棄或不需要）；false 表示 Tool 的解析器還沒登記，稍後再試。
    /// </summary>
    private bool RestoreAfterReload(bool final)
    {
        if (this == null || !reloadPending || model != null) return true;
        if (reloadOwner == null)
        {
            ClearReloadStash();
            return true;
        }

        if (!reloadNeedsTool)
        {
            if (!Bind(reloadOwner)) ClearReloadStash();
            return true;
        }

        if (TryResolveReloadBinding(reloadOwner, reloadDocumentId, out var binding, out var context))
        {
            // 綁定失敗時草稿留著，使用者從原 Tool 重新開啟還能接回。
            BindDocument(reloadOwner, binding, context);
            Repaint();
            return true;
        }
        if (!final) return false;

        // 沒有解析器認得這份文件：草稿留著，等使用者從原 Tool 重新開啟時接回。
        if (!string.IsNullOrEmpty(reloadDraftJson) || reloadAssetStash != null)
            ShowNotification(new GUIContent("Tool 接入已因重載失效；從原 Tool 重新開啟後會還原未儲存的修改。"));
        Repaint();
        return true;
    }

    /// <summary>
    /// 綁定成功後接回重載前的暫存：文件草稿、Token／資產焦點、視角與鎖定。
    /// 綁到的不是同一份文件就放棄暫存，不把別的文件的草稿套上來。
    /// </summary>
    private void AdoptReloadStash()
    {
        if (!reloadPending) return;
        bool hasDraft = !string.IsNullOrEmpty(reloadDraftJson) || reloadAssetStash != null;
        bool sameDocument = reloadOwner != null && model.Owner == reloadOwner
            && (reloadDocumentId == null || reloadDocumentId == model.DocumentId);
        if (!sameDocument)
        {
            if (hasDraft)
                Debug.LogWarning($"[GraphKit] 已開啟另一份文件，放棄重載前 '{(reloadOwner != null ? reloadOwner.name : "?")}' 的未儲存修改。");
            ClearReloadStash();
            return;
        }

        bool restored = false, failed = false;
        if (!string.IsNullOrEmpty(reloadDraftJson))
        {
            IGraphDocument draft = null;
            try { draft = JsonUtility.FromJson(reloadDraftJson, model.Data.GetType()) as IGraphDocument; }
            catch (Exception exception) { Debug.LogWarning($"[GraphKit] 重載前的草稿讀取失敗：{exception.Message}"); }
            if (draft != null && model.AdoptDraft(draft, reloadDraftContentDirty))
            {
                restored = true;
                reportStale = true;
                // 工作副本整份換掉，焦點要重新指向新副本。
                SetFocus(AllRootsFocus());
            }
            else failed = true;
        }

        if (!string.IsNullOrEmpty(reloadTokenId))
        {
            var token = FindToken(reloadTokenId);
            if (token != null) SetFocus(new HGFocus { Kind = HGFocusKind.Token, Token = token });
        }

        var asset = reloadAsset;
        var stash = reloadAssetStash;
        if (asset != null)
        {
            bool entered;
            suppressProjectSelect = true;
            try { entered = TryEnterSharedAsset(asset); }
            finally { suppressProjectSelect = false; }
            if (stash != null)
            {
                if (entered && ApplyAssetStash(stash)) restored = true;
                else failed = true;
            }
        }

        pendingFocusStart = false;
        pan = reloadPan;
        zoom = Mathf.Clamp(reloadZoom > 0f ? reloadZoom : 1f, MinZoom, MaxZoom);
        locked = reloadLocked;
        ClearReloadStash();
        graphDirty = true;
        if (failed)
        {
            Debug.LogWarning("[GraphKit] 重載前的未儲存修改無法完整還原，還原不了的部分顯示已存檔的內容。");
            ShowNotification(new GUIContent("重載前的未儲存修改無法完整還原，詳見 Console"));
        }
        else if (restored) ShowNotification(new GUIContent("已還原重載前未儲存的修改"));
    }

    private void OnDisable()
    {
        spinnerField.Cancel();
        EditorApplication.update -= UpdateExecutionView;
        EditorApplication.playModeStateChanged -= ExecutionPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= CancelObservedExecutions;
        executionObservation?.Dispose();
        executionObservation = null;
        executionSource = null;
        selectedExecution = null;
        if (captureOnly) return;
        console.SavePrefs();
        SaveLibraryLayout();
        EditorPrefs.SetFloat(PrefLeftWidth, leftWidth);
        EditorPrefs.SetFloat(PrefRefSection, refSectionHeight);
    }

    private void OnLostFocus()
    {
        lastSaveShortcut = double.NegativeInfinity;
        saveKeyHeld = false;
        spinnerField.Cancel();
        Repaint();
    }

    public override void SaveChanges()
    {
        if (!HasUnsavedWork)
        {
            base.SaveChanges();
            return;
        }

        if (focus.Kind == HGFocusKind.Asset)
        {
            if (assetDirty && !SaveAsset(false))
                throw new InvalidOperationException($"共用資產驗證失敗，{HGGraph.WindowTitle(model?.Doc)} 保留未儲存內容並取消關閉。");
            if (focus.Kind == HGFocusKind.Asset) ExitAsset();
        }

        // Owner 已被刪除：沒有地方可存，放掉工作副本讓視窗可以關閉。
        if (model != null && model.Owner == null)
        {
            ReturnToIdle();
            base.SaveChanges();
            return;
        }

        if (model?.Dirty == true && !DoSave(false))
            throw new InvalidOperationException($"編輯對象驗證失敗，{HGGraph.WindowTitle(model?.Doc)} 保留未儲存內容並取消關閉。");

        UpdateUnsavedState();
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        ClearPortInteractionState();
        if (focus.Kind == HGFocusKind.Asset) ExitAsset();
        // Owner 已被刪除：重載一定失敗，直接放掉工作副本。
        if (model != null && model.Owner == null)
        {
            ReturnToIdle();
            base.DiscardChanges();
            return;
        }
        if (model?.Dirty == true)
        {
            if (!model.TryReload())
                throw new InvalidOperationException("文件重載失敗，保留未儲存內容並取消關閉。請查看 Unity Console。");
            focusReturns.Clear();
            focus = AllRootsFocus();
            graphDirty = true;
        }
        UpdateUnsavedState();
        base.DiscardChanges();
    }

    private void UpdateUnsavedState()
    {
        hasUnsavedChanges = !captureOnly && HasUnsavedWork;
        if (!hasUnsavedChanges) return;

        string ownerName = model?.Owner != null ? model.Owner.name : HGGraph.WindowTitle(model?.Doc);
        saveChangesMessage = focus.Kind == HGFocusKind.Asset && assetDirty
            ? $"共用資產與 '{ownerName}' 有未儲存的修改。是否在關閉前存檔？"
            : $"'{ownerName}' 有未儲存的修改。是否在關閉前存檔？";
    }

    private void DoVerify(bool silent, bool syncStoredState = true)
    {
        EnsureGraph();
        if (focus.Kind == HGFocusKind.Asset)
        {
            assetReport = HGValidator.RunSubtree(model, focus, focus.AssetHostSlot, focus.Title);
            AddExtensionDiagnostics(assetReport);
            assetReport.ReplaceGraphViewDiagnostics(graph?.Diagnostics);
            assetVerifiedOnce = true;
            assetReportStale = false;
            if (assetReport.ErrorCount > 0) console.RevealErrors();
            if (!silent && assetReport.Issues.Count == 0) ShowNotification(new GUIContent("驗證通過"));
            return;
        }

        bool hadMissingTypes = model.Owner != null && (SerializationUtility.HasManagedReferencesWithMissingTypes(model.Owner)
            || HGMissingTypeCleanup.HasLostContent(model.Doc));
        pendingValidationSave = false;
        if (!model.CleanMissingTypes())
        {
            report = new HGReport();
            verifiedOnce = true;
            reportStale = false;
            model.Doc.InvalidateValidation();
            if (model.LastCommitDiagnostic != null)
                report.Issues.Add(new HGIssue(model.LastCommitDiagnostic, "遺失資料清理", null, null, null));
            else
                report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.validation.cleanup-failed",
                    GraphDiagnosticSeverity.Error, "遺失資料無法清理，驗證未完成。"), "遺失資料清理", null, null, null));
            console.RevealErrors();
            return;
        }
        if (hadMissingTypes)
        {
            ClearPortInteractionState();
            graphDirty = true;
            UpdateUnsavedState();
            EnsureGraph();
        }
        report = HGValidator.Run(model, includeMissingTypes: true);
        AddExtensionDiagnostics(report);
        report.ReplaceGraphViewDiagnostics(graph?.Diagnostics);
        model.Doc.InvalidateValidation();
        if (report.ErrorCount == 0)
        {
            try
            {
                HGOwnerValidation.VerifyDocument(model.Doc, model.Owner);
                if (!model.Doc.IsValidated)
                    report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.validation.rejected",
                        GraphDiagnosticSeverity.Error, "文件驗證未通過。"), "文件驗證", null, null, null));
            }
            catch (Exception exception)
            {
                model.Doc.InvalidateValidation();
                report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.validation.failed",
                    GraphDiagnosticSeverity.Error, exception.Message), "文件驗證", null, null, null));
            }
        }
        verifiedOnce = true;
        reportStale = false;
        pendingValidationSave = true;
        // 沒有未存修改時，透過同一版本守衛同步驗證旗標，讓 Inspector 讀到此次結果。
        if (syncStoredState && !model.Dirty)
        {
            if (model.Save(true)) pendingValidationSave = false;
            else if (model.LastCommitDiagnostic != null)
                report.Issues.Add(new HGIssue(model.LastCommitDiagnostic, "驗證結果同步", null, null, null));
            else
                report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.validation.sync-failed",
                    GraphDiagnosticSeverity.Error, "驗證結果無法寫回，請重新存檔。"), "驗證結果同步", null, null, null));
        }
        if (report.ErrorCount > 0) console.RevealErrors();
        if (!silent && report.ErrorCount == 0 && report.WarningCount == 0)
            ShowNotification(new GUIContent("驗證通過"));
    }

    private bool DoSave(bool showDialog = true)
    {
        model.LastCommitDiagnostic = null;
        if (model.Owner != null && (SerializationUtility.HasManagedReferencesWithMissingTypes(model.Owner)
            || HGMissingTypeCleanup.HasLostContent(model.Doc))) reportStale = true;
        bool saved = model.Save(pendingValidationSave && verifiedOnce && !reportStale);
        if (!saved)
        {
            if (model.LastCommitDiagnostic != null)
            {
                report.Issues.Add(new HGIssue(model.LastCommitDiagnostic, "文件提交", null, null, null));
                console.RevealErrors();
            }
            if (showDialog)
                ShowNotification(new GUIContent(model.LastCommitDiagnostic?.Message
                    ?? "無法存檔：複製或寫入失敗，目前修改保留。詳見 Unity Console"));
            return false;
        }
        // Owner 的引用內容變了，反向索引跟著失效。下次要用時才重算，這裡不掃。
        pendingValidationSave = false;
        HGReferenceIndex.Invalidate();
        // 寫檔已在 HGModel.Save 完成，而且只寫 Owner 所在的資產檔；這裡不再呼叫全域 SaveAssets。
        UpdateUnsavedState();
        ShowNotification(new GUIContent("已存檔"));
        return true;
    }

    private void DoCancel()
    {
        if (model.Dirty && !EditorUtility.DisplayDialog(
                "捨棄修改", "會丟掉自上次存檔以來的所有修改，確定嗎？", "捨棄", "繼續編輯"))
            return;
        ClearPortInteractionState();
        if (!model.TryReload())
        {
            ShowNotification(new GUIContent("文件重載失敗，目前修改保留。請查看 Unity Console。"));
            return;
        }
        focusReturns.Clear();
        // 重抓工作副本＝焦點抓的是舊資料，直接回到時機畫布（不回去的話畫面會空白）。
        focus = AllRootsFocus();
        selectedIds.Clear();
        graphDirty = true;
        ClearValidationReport();
        UpdateUnsavedState();
    }

    // ===== 資產焦點（獨立存檔交易）=====

    /// <summary>下鑽進資產內部編輯。編輯的是資產內容的工作副本，存檔才寫回資產檔案。</summary>
    private void EnterAsset(HGNodeView node)
    {
        if (node.Asset == null || node.ParentSlot == null) return;
        EnterAsset(node.Asset, node.ParentSlot is UnionSlotBase union
            ? union.SelectedSlot?.GetType() : node.ParentSlot.GetType(), true);
    }

    /// <summary>
    /// 下鑽進一個Token的畫布。端點是頭端，它的取值欄位是唯一的來源接點，候選池也掛在它身上。
    /// 資產的Token留在 Asset 焦點裡（只換頭端），資產的存檔交易因此不受影響。
    /// </summary>
    private void EnterToken(GraphToken endpoint, bool fromCanvas = false)
    {
        if (endpoint == null) return;
        if (ReferenceEquals(focus.Token, endpoint)) return;
        var origin = (Focus: focus, Pan: pan, Zoom: zoom);

        if (focus.Kind == HGFocusKind.Asset)
        {
            SetFocus(new HGFocus
            {
                Kind = HGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetTokens = focus.AssetTokens,
                AssetProperties = focus.AssetProperties,
                Token = endpoint,
            });
        }
        else
        {
            SetFocus(new HGFocus { Kind = HGFocusKind.Token, Token = endpoint });
        }
        if (fromCanvas) focusReturns[focus.Id] = origin;
        else focusReturns.Remove(focus.Id);
        pendingFocusStart = true;
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>由節點進入時返回原畫布與視角；其他入口回資產本體或 Owner 畫布。</summary>
    private void ExitToken()
    {
        if (focus.Token == null) return;
        if (RestoreFocusReturn()) return;
        if (focus.Kind == HGFocusKind.Asset)
        {
            SetFocus(new HGFocus
            {
                Kind = HGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetTokens = focus.AssetTokens,
                AssetProperties = focus.AssetProperties,
            });
        }
        else SetFocus(AllRootsFocus());
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>slotType 只是用來合成一個型別正確的容器槽，讓資產內容能沿用一般的節點圖流程。</summary>
    private void EnterAsset(UnityEngine.Object asset, Type slotType, bool fromCanvas = false)
    {
        if (asset == null) return;
        // 已經在這個資產裡：從Token子畫布回到資產本體，不重開交易。
        if (focus.Kind == HGFocusKind.Asset && focus.AssetObject == asset)
        {
            SetFocus(new HGFocus
            {
                Kind = HGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetTokens = focus.AssetTokens,
                AssetProperties = focus.AssetProperties,
            });
            if (!fromCanvas) focusReturns.Remove(focus.Id);
            pendingFocusStart = true;
            SelectAssetInProject(asset);
            return;
        }
        HGFocus back = focus.Kind == HGFocusKind.Asset ? returnFocus : focus;
        var origin = (Focus: focus, Pan: pan, Zoom: zoom);
        if (focus.Kind == HGFocusKind.Asset && !ConfirmLeaveAsset()) return;

        var formulaAsset = asset as IFormulaGraphAsset;
        var host = formulaAsset != null ? formulaAsset.Input
            : slotType != null ? HGReflect.CreateInstance(slotType) as GraphSlotBase : null;
        if (host == null)
        {
            ShowNotification(new GUIContent("無法編輯：找不到這個資產對應的欄位型別"));
            return;
        }

        // 內容、候選、Token 與 Property 庫同一次複製，才能保留節點對庫定義的共享引用。
        var pack = new List<object>
        {
            formulaAsset != null ? (object)host : HGReflect.AssetRoot(asset),
            HGReflect.Orphans(asset) ?? new List<GraphNode>(),
            HGReflect.Tokens(asset) ?? new List<GraphToken>(),
            HGReflect.Properties(asset) ?? new List<GraphProperty>(),
        };
        var packCopy = GraphDeepCopy.Copy(pack);
        if (packCopy == null)
        {
            ShowNotification(new GUIContent("無法建立資產工作副本，保留目前畫布。"));
            return;
        }
        // 根節點連載體一起抄進容器槽：座標、備註、Id 都在載體上，容器槽本身是拋棄式的。
        if (formulaAsset != null) host = packCopy[0] as GraphSlotBase;
        else HGReflect.SetNode(host, packCopy[0] as GraphNode);

        int removedTokens = HGTokenCleanup.Clean(host, packCopy[2] as List<GraphToken>, packCopy[1] as List<GraphNode>);

        SetFocus(new HGFocus
        {
            Kind = HGFocusKind.Asset,
            AssetObject = asset,
            AssetHostSlot = host,
            AssetOrphans = packCopy?[1] as List<GraphNode> ?? new List<GraphNode>(),
            AssetTokens = packCopy?[2] as List<GraphToken> ?? new List<GraphToken>(),
            AssetProperties = packCopy?[3] as List<GraphProperty> ?? new List<GraphProperty>(),
        });
        returnFocus = back ?? new HGFocus();
        if (fromCanvas) focusReturns[focus.Id] = origin;
        else focusReturns.Remove(focus.Id);
        pendingFocusStart = true;
        ClearAssetDirty();
        if (removedTokens > 0)
        {
            assetDirty = true;
            assetContentDirty = true;
            ShowNotification(new GUIContent($"已清除 {removedTokens} 個過時 Token；來源保留在候選池，請檢查後存檔。"));
        }
        // 每個資產是一次獨立交易，復原歷程跟著交易開始；進來當下的狀態就是第一次修改要退回的地方。
        assetHistory.Reset(CaptureAssetState());
        assetVerifiedOnce = false;
        assetReportStale = false;
        ClearValidationReport();
        UpdateUnsavedState();
        SelectAssetInProject(asset);
    }

    /// <summary>把 Project／Inspector 的選取帶到焦點資產上，並在 Project 視窗閃一下定位資料夾。</summary>
    private void SelectAssetInProject(UnityEngine.Object asset)
    {
        if (asset == null || suppressProjectSelect || Selection.activeObject == asset) return;
        // 必須在焦點設定完之後呼叫：OnSelectionChange 會因為「選到的就是目前焦點資產」直接跳過，不會重開交易。
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private bool SaveAsset(bool showDialog = true)
    {
        var asset = focus.AssetObject;
        var host = focus.AssetHostSlot;
        if (asset == null || host == null) return false;

        var rootCarrier = host.Node;
        var formulaAsset = asset as IFormulaGraphAsset;
        if (formulaAsset == null && (rootCarrier?.Kind is NodeKind.Asset or NodeKind.Token or NodeKind.Property))
        {
            if (showDialog)
                ShowNotification(new GUIContent("無法存檔：資產根內容必須是公式或動作；Property 請接到內容的輸入欄位"));
            return false;
        }

        var setRoot = asset.GetType().GetMethod("SetRoot");
        if (formulaAsset == null && setRoot == null)
        {
            Debug.LogError($"[GraphKit] {asset.GetType().Name} 沒有 SetRoot，無法寫回。");
            return false;
        }

        // 寫回仍同一次複製四部分，節點必須引用實際寫入 Token／Property 庫的物件。
        var pack = new List<object>
        {
            host,
            focus.AssetOrphans ?? new List<GraphNode>(),
            focus.AssetTokens ?? new List<GraphToken>(),
            focus.AssetProperties ?? new List<GraphProperty>(),
        };
        var packCopy = GraphDeepCopy.Copy(pack);
        if (packCopy == null)
        {
            ShowNotification(new GUIContent("資產複製失敗，未寫回，請保留目前工作副本。"));
            return false;
        }
        var storedInput = packCopy[0] as GraphSlotBase;
        if (formulaAsset != null) formulaAsset.SetInput((FormulaSlotBase)storedInput);
        else setRoot.Invoke(asset, new object[] { storedInput?.Node });

        var storedOrphans = HGReflect.Orphans(asset);
        if (storedOrphans != null)
        {
            storedOrphans.Clear();
            if (packCopy?[1] is List<GraphNode> orphanCopy) storedOrphans.AddRange(orphanCopy);
        }
        if (HGReflect.Tokens(asset) is List<GraphToken> storedTokens)
        {
            storedTokens.Clear();
            if (packCopy?[2] is List<GraphToken> endpointCopy) storedTokens.AddRange(endpointCopy);
        }
        if (HGReflect.Properties(asset) is List<GraphProperty> storedProperties)
        {
            storedProperties.Clear();
            if (packCopy?[3] is List<GraphProperty> propertyCopy) storedProperties.AddRange(propertyCopy);
        }
        EditorUtility.SetDirty(asset);
        // 只寫這個資產檔；引用者的驗證旗標由下面的 InvalidateAssetUsers 自己存。
        AssetDatabase.SaveAssetIfDirty(asset);
        // 內容沒變就不要驚動別人：座標是編輯器視覺，改它不會讓任何引用者的驗證結果不一樣。
        if (assetContentDirty)
        {
            InvalidateAssetUsers(asset);
            reportStale = true;
        }

        ClearAssetDirty();
        // 存檔只是寫回資產，不退出畫布：接著要繼續編輯還是按「返回」由使用者決定。
        ShowNotification(new GUIContent("資產已存檔"));
        UpdateUnsavedState();
        Repaint();
        return true;
    }

    /// <summary>返回上一層。存檔是另一顆按鈕，所以這裡只負責退出；還有沒存的修改就先問。</summary>
    private void LeaveAsset()
    {
        if (!ConfirmLeaveAsset()) return;
        ExitAsset();
    }

    private bool ConfirmLeaveAsset()
    {
        if (!assetDirty) return true;
        return EditorUtility.DisplayDialog("捨棄資產修改",
            "這個資產還有尚未存檔的修改，返回會丟掉它們，確定嗎？", "捨棄", "繼續編輯");
    }

    private bool RestoreFocusReturn(string focusId = null)
    {
        focusId ??= focus.Id;
        if (!focusReturns.TryGetValue(focusId, out var origin)) return false;
        focusReturns.Remove(focusId);
        if (origin.Focus.Kind == HGFocusKind.Asset)
        {
            string tokenId = origin.Focus.Token?.Id;
            if (focus.AssetObject != origin.Focus.AssetObject)
            {
                string assetId = new HGFocus { Kind = HGFocusKind.Asset, AssetObject = origin.Focus.AssetObject }.Id;
                bool hadReturn = focusReturns.TryGetValue(assetId, out var assetReturn);
                EnterAsset(origin.Focus.AssetObject, origin.Focus.AssetHostSlot.GetType());
                if (hadReturn) focusReturns[assetId] = assetReturn;
                if (focus.AssetObject != origin.Focus.AssetObject) return false;
            }
            SetFocus(new HGFocus
            {
                Kind = HGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetTokens = focus.AssetTokens,
                AssetProperties = focus.AssetProperties,
                Token = focus.AssetTokens?.Find(token => token != null && token.Id == tokenId),
            });
        }
        else SetFocus(origin.Focus);
        pan = origin.Pan;
        zoom = origin.Zoom;
        return true;
    }

    private void ExitAsset()
    {
        var back = returnFocus;
        ClearAssetDirty();
        // 交易結束就丟掉歷程：留著只會佔記憶體，而且下次進來的是另一份工作副本，套用舊快照沒有意義。
        assetHistory.Reset(null);
        assetVerifiedOnce = false;
        assetReportStale = false;
        assetReport = new HGReport();
        string assetId = new HGFocus { Kind = HGFocusKind.Asset, AssetObject = focus.AssetObject }.Id;
        if (!RestoreFocusReturn(assetId))
            SetFocus(back != null && back.Kind != HGFocusKind.None ? back : AllRootsFocus());
        if (focus.Kind != HGFocusKind.Asset) returnFocus = null;
        ClearValidationReport();
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>共用資產修改撤銷引用者的舊驗證結果，實際內容留待手動檢查或執行入口驗證。</summary>
    private static void InvalidateAssetUsers(UnityEngine.Object asset)
    {
        var touched = 0;
        foreach (var so in HGReferenceIndex.Users(asset as ScriptableObject))
        {
            if (!HGOwnerValidation.CanVerify(so)) continue;
            bool changed = false;
            if (so is IGraphOwner legacy)
            {
                changed = legacy.IsGraphValidated();
                legacy.InvalidateGraphValidation();
            }
            else foreach (var field in HGOwnerValidation.DocumentFields(so.GetType()))
                if (field.GetValue(so) is IGraphDocument document)
                {
                    changed |= document.IsValidated;
                    document.InvalidateValidation();
                }
            if (!changed) continue;

            EditorUtility.SetDirty(so);
            touched++;
        }

        if (touched > 0) AssetDatabase.SaveAssets();
    }
}

}
