namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

    /// <summary>Returns bounded commands for this binding. Handles expire when the window binds another document.</summary>
    public HGWindowSession GetDocumentCommands()
        => model == null ? null : new HGWindowSession(this, model);

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
            || (!port.IsInput && port.Source is not HGPropertyWriteSource)
            || port.Binding is IHGListAppendBinding
            || port.Presentation.Locked || !port.Presentation.Visible)
            return HGSessionCommandResult.Rejected;
        return CutLink((port.Source as HGPropertyWriteSource)?.Slot ?? port.InputSlot) switch
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
        if (HasUnsavedWork && !EditorUtility.DisplayDialog(
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
            if (model != null && TryReturnToIdle()) ReturnToIdle();
            return;
        }
        if (model != null && ReferenceEquals(picked, model.Owner)) return;

        bool busy = model != null && (model.Dirty || focus.Kind == HGFocusKind.Asset);
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
        if (usesExplicitToolEntry && sessionBinding == null) requiresToolReopenAfterReload = true;
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

        if (model?.Dirty == true && !DoSave(false))
            throw new InvalidOperationException($"編輯對象驗證失敗，{HGGraph.WindowTitle(model?.Doc)} 保留未儲存內容並取消關閉。");

        UpdateUnsavedState();
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        ClearPortInteractionState();
        if (focus.Kind == HGFocusKind.Asset) ExitAsset();
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
        hasUnsavedChanges = HasUnsavedWork;
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
        AssetDatabase.SaveAssets();
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

        var host = slotType != null ? HGReflect.CreateInstance(slotType) as GraphSlotBase : null;
        if (host == null)
        {
            ShowNotification(new GUIContent("無法編輯：找不到這個資產對應的欄位型別"));
            return;
        }

        // 內容、候選、Token 與 ProtoProperty 庫同一次複製，才能保留節點對庫定義的共享引用。
        var pack = new List<object>
        {
            HGReflect.AssetRoot(asset),
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
        HGReflect.SetNode(host, packCopy?[0] as GraphNode);

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
        if (asset == null || Selection.activeObject == asset) return;
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
        if (rootCarrier?.Kind is NodeKind.Asset or NodeKind.Token or NodeKind.Property)
        {
            if (showDialog)
                ShowNotification(new GUIContent("無法存檔：資產根內容必須是公式或動作；Property 請接到內容的輸入欄位"));
            return false;
        }

        var setRoot = asset.GetType().GetMethod("SetRoot");
        if (setRoot == null)
        {
            Debug.LogError($"[GraphKit] {asset.GetType().Name} 沒有 SetRoot，無法寫回。");
            return false;
        }

        // 寫回仍同一次複製四部分，節點必須引用實際寫入 Token／ProtoProperty 庫的物件。
        var pack = new List<object>
        {
            rootCarrier?.Kind is NodeKind.Inline or NodeKind.Empty ? rootCarrier : null,
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
        setRoot.Invoke(asset, new object[] { packCopy?[0] as GraphNode });

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
        AssetDatabase.SaveAssets();
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
