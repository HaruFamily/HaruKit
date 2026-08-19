namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 開窗入口、Owner 綁定、選取切換、存檔／取消／驗證交易，以及共用資產焦點的進出與引用者重驗。
/// </summary>
public partial class ActionGraphWindow
{
    // ===== 開啟 =====

    // Core 的 Inspector 按鈕透過這個掛勾開窗；Runtime 不能引用 Editor assembly，只能反向註冊。
    [InitializeOnLoadMethod]
    private static void RegisterOpenHook() => ActionSystemEditorHooks.OpenGraphWindow = so => OpenFor(so);

    /// <summary>開窗並聚焦到指定對象。Owner 直接編輯，共用資產則借引用者當上下文下鑽。</summary>
    public static void OpenFor(UnityEngine.Object target)
    {
        var window = OpenWindow();
        if (target == null) return;

        var owner = ResolveOwner(target);
        if (owner != null) { window.Bind(owner); return; }
        if (target is ScriptableObject so && IsSharedAsset(so)) window.OpenSharedAsset(so);
    }

    [MenuItem("PinTools/ActionSystemGraph")]
    public static void OpenFromMenu() => OpenWindow();

    private static ActionGraphWindow OpenWindow()
    {
        var window = GetWindow<ActionGraphWindow>("ActionSystemGraph");
        window.minSize = new Vector2(980f, 560f);
        window.Show();
        return window;
    }

    /// <summary>從資產開啟（Project 視窗右鍵）。Owner 直接編輯；公式／動作資產則找一個引用它的 Owner 當上下文後下鑽。</summary>
    [MenuItem("Assets/ActionSystemGraph", false, 30)]
    public static void OpenFromAsset() => OpenFor(Selection.activeObject);

    [MenuItem("Assets/ActionSystemGraph", true)]
    public static bool OpenFromAssetValidate()
        => Selection.activeObject is ScriptableObject so && (AGModel.CanEdit(so) || IsSharedAsset(so));

    /// <summary>是否為公式／動作資產（可下鑽編輯的共用資產）。</summary>
    private static bool IsSharedAsset(ScriptableObject so)
    {
        if (so is FormulaAssetBase) return true;
        for (var t = so.GetType(); t != null && t != typeof(object); t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ActionAssetBase<>)) return true;
        return false;
    }

    /// <summary>從引用者裡挑一個可以當上下文的 Owner。索引是現算的，只有專案裡真的沒人引用時才是空的。</summary>
    private static ScriptableObject FindContextOwner(ScriptableObject asset)
    {
        foreach (var so in AGReferenceIndex.Users(asset))
            if (so != null && AGModel.CanEdit(so)) return so;
        return null;
    }

    /// <summary>資產本身沒有變數清單與欄位型別，必須借一個引用它的 Owner 當上下文。</summary>
    private void OpenSharedAsset(ScriptableObject asset)
    {
        if (focus.Kind == AGFocusKind.Asset)
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
            AGReferenceIndex.Refresh();
            owner = FindContextOwner(asset);
        }
        if (owner == null)
        {
            EditorUtility.DisplayDialog("找不到引用者",
                $"專案裡沒有任何已存檔的對象引用 '{asset.name}'。\n\n若引用它的對象還沒存檔，先存檔再試；" +
                "或直接從那個對象的圖上雙擊這顆資產節點下鑽。", "好");
            return;
        }

        if (!Bind(owner)) return;
        if (TryEnterSharedAsset(asset)) return;
        EditorUtility.DisplayDialog("找不到引用點",
            $"索引說 '{owner.name}' 引用這個資產，但它的內容裡找不到指向它的欄位。\n磁碟上的資料可能剛被外部改過，重開視窗再試。", "好");
    }

    private bool TryEnterSharedAsset(ScriptableObject asset)
    {
        if (model == null) return false;
        foreach (var slot in model.AllSlots())
        {
            if (AGReflect.GetAsset(slot) != asset) continue;
            EnterAsset(asset, slot.GetType());
            return true;
        }

        Type compatibleSlot = SlotTypeForAsset(asset, AssetSlotTypes());
        if (compatibleSlot == null) return false;

        // 目前的 Owner 沒有引用它也沒關係：資產只是借它的型別當上下文，不需要真的連著。
        EnterAsset(asset, compatibleSlot);
        return true;
    }

    public bool Bind(UnityEngine.Object owner)
    {
        if (HasUnsavedWork && !EditorUtility.DisplayDialog(
                "尚未儲存", $"'{(model?.Owner != null ? model.Owner.name : "?")}' 有未儲存的修改，切換後會遺失。要繼續嗎？", "捨棄並切換", "取消"))
            return false;

        returnFocus = null;
        ClearAssetDirty();
        assetReport = new AGReport();
        assetVerifiedOnce = false;
        assetReportStale = false;
        model = new AGModel();
        if (!model.Bind(owner))
        {
            model = null;
            UpdateUnsavedState();
            return false;
        }
        pendingTarget = null;

        focus = new AGFocus();
        ClearViewState();
        graphDirty = true;
        verifiedOnce = false;
        report = AGValidator.Run(model, includeMissingTypes: true);
        verifiedOnce = true;
        reportStale = false;

        // 所有時機共用一張畫布，綁定後直接進去；不再有「記住上次看的是哪個時機」這件事。
        SetFocus(AllTimingsFocus());

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
        if (!Bind(owner)) return;
        Selection.activeObject = owner;
        Repaint();
    }

    /// <summary>從 Project／Hierarchy 選到支援的對象就自動聚焦。有未儲存變更時不硬切，改成在工具列問。</summary>
    private void OnSelectionChange()
    {
        if (Selection.activeObject is ScriptableObject asset && IsSharedAsset(asset))
        {
            if (focus.Kind == AGFocusKind.Asset && focus.AssetObject == asset) return;

            // 同一 Owner 的工作副本已引用此資產時可安全下鑽，不會丟掉 Owner 修改。
            if (focus.Kind != AGFocusKind.Asset && TryEnterSharedAsset(asset))
            {
                pendingTarget = null;
                Repaint();
                return;
            }

            bool assetSwitchBusy = model != null && (model.Dirty || (focus.Kind == AGFocusKind.Asset && assetDirty));
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

        bool busy = model != null && (model.Dirty || focus.Kind == AGFocusKind.Asset);
        if (busy) pendingTarget = picked;
        else { pendingTarget = null; Bind(picked); }
        Repaint();
    }

    /// <summary>離開目前編輯交易並回到無選取版型；任一存檔失敗或取消都留在原畫面。</summary>
    private bool TryReturnToIdle()
    {
        if (focus.Kind == AGFocusKind.Asset && assetDirty)
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

        if (focus.Kind == AGFocusKind.Asset) ExitAsset();

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
        model = null;
        focus = new AGFocus();
        graph = null;
        graphDirty = true;
        report = new AGReport();
        assetReport = new AGReport();
        verifiedOnce = false;
        reportStale = false;
        assetVerifiedOnce = false;
        assetReportStale = false;
        tokenSearch = "";
        assetSearch = "";
        pendingTarget = null;
        returnFocus = null;
        ClearAssetDirty();
        ClearViewState();
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>把選取物解析成可編輯對象：SO 直接用，GameObject 找身上帶 ActionSystem 的元件。</summary>
    private static UnityEngine.Object ResolveOwner(UnityEngine.Object selected)
    {
        if (selected == null) return null;
        if (AGModel.CanEdit(selected)) return selected;

        if (selected is GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && AGModel.CanEdit(c)) return c;
        }
        return null;
    }

    private void OnEnable()
    {
        saveChangesMessage = "ActionSystemGraph 有未儲存的修改。是否在關閉前存檔？";
        consoleHeight = EditorPrefs.GetFloat(PrefConsoleHeight, 150f);
        consoleCollapsed = EditorPrefs.GetBool(PrefConsoleCollapsed, false);
        leftWidth = EditorPrefs.GetFloat(PrefLeftWidth, DefaultLeftWidth);
        rightWidth = EditorPrefs.GetFloat(PrefRightWidth, DefaultRightWidth);
        UpdateUnsavedState();
    }

    private void OnDisable()
    {
        EditorPrefs.SetFloat(PrefConsoleHeight, consoleHeight);
        EditorPrefs.SetBool(PrefConsoleCollapsed, consoleCollapsed);
        EditorPrefs.SetFloat(PrefLeftWidth, leftWidth);
        EditorPrefs.SetFloat(PrefRightWidth, rightWidth);
    }

    public override void SaveChanges()
    {
        if (!HasUnsavedWork)
        {
            base.SaveChanges();
            return;
        }

        if (focus.Kind == AGFocusKind.Asset)
        {
            if (assetDirty && !SaveAsset(false))
                throw new InvalidOperationException("共用資產驗證失敗，ActionSystemGraph 保留未儲存內容並取消關閉。");
            if (focus.Kind == AGFocusKind.Asset) ExitAsset();
        }

        if (model?.Dirty == true && !DoSave(false))
            throw new InvalidOperationException("編輯對象驗證失敗，ActionSystemGraph 保留未儲存內容並取消關閉。");

        UpdateUnsavedState();
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        if (focus.Kind == AGFocusKind.Asset) ExitAsset();
        if (model?.Dirty == true)
        {
            model.Reload();
            focus = AllTimingsFocus();
            graphDirty = true;
        }
        UpdateUnsavedState();
        base.DiscardChanges();
    }

    private void UpdateUnsavedState()
    {
        hasUnsavedChanges = HasUnsavedWork;
        if (!hasUnsavedChanges) return;

        string ownerName = model?.Owner != null ? model.Owner.name : "ActionSystemGraph";
        saveChangesMessage = focus.Kind == AGFocusKind.Asset && assetDirty
            ? $"共用資產與 '{ownerName}' 有未儲存的修改。是否在關閉前存檔？"
            : $"'{ownerName}' 有未儲存的修改。是否在關閉前存檔？";
    }

    private void DoVerify(bool silent)
    {
        if (focus.Kind == AGFocusKind.Asset)
        {
            assetReport = AGValidator.RunSubtree(model, focus, focus.AssetHostSlot, focus.Title);
            assetVerifiedOnce = true;
            assetReportStale = false;
            if (assetReport.ErrorCount > 0) { consoleCollapsed = false; consoleTab = 1; }
            if (!silent && assetReport.Issues.Count == 0) ShowNotification(new GUIContent("驗證通過"));
            return;
        }

        report = AGValidator.Run(model, includeMissingTypes: true);
        verifiedOnce = true;
        reportStale = false;
        if (report.ErrorCount > 0) { consoleCollapsed = false; consoleTab = 1; }
        if (!silent && report.ErrorCount == 0 && report.WarningCount == 0)
            ShowNotification(new GUIContent("驗證通過"));
    }

    private bool DoSave(bool showDialog = true)
    {
        DoVerify(true);
        if (!report.CanSave)
        {
            consoleCollapsed = false;
            consoleTab = 1;
            // Console 已經被展開切到錯誤頁，細節都在那裡；再彈一個要按「好」的框只是多一次跨螢幕來回。
            if (showDialog)
                ShowNotification(new GUIContent($"無法存檔：還有 {report.ErrorCount} 個錯誤，請先在 Console 修正"));
            return false;
        }
        if (!model.Save())
        {
            if (showDialog)
                ShowNotification(new GUIContent("無法存檔：Core 驗證未通過，Owner 未寫入。詳見 Unity Console"));
            return false;
        }
        // Owner 的引用內容變了，反向索引跟著失效。下次要用時才重算，這裡不掃。
        AGReferenceIndex.Invalidate();
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
        model.Reload();
        // 重抓工作副本＝焦點抓的是舊資料，直接回到時機畫布（不回去的話畫面會空白）。
        focus = AllTimingsFocus();
        selectedIds.Clear();
        graphDirty = true;
        DoVerify(true);
        UpdateUnsavedState();
    }

    // ===== 資產焦點（獨立存檔交易）=====

    /// <summary>下鑽進資產內部編輯。編輯的是資產內容的工作副本，存檔才寫回資產檔案。</summary>
    private void EnterAsset(AGNode node)
    {
        if (node.Asset == null || node.ParentSlot == null) return;
        EnterAsset(node.Asset, node.ParentSlot.GetType());
    }

    /// <summary>
    /// 下鑽進一個變數的畫布。端點是頭端，它的取值欄位是唯一的來源接點，候選池也掛在它身上。
    /// 資產的變數留在 Asset 焦點裡（只換頭端），資產的存檔交易因此不受影響。
    /// </summary>
    private void EnterVariable(GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        if (ReferenceEquals(focus.Endpoint, endpoint)) return;

        if (focus.Kind == AGFocusKind.Asset)
        {
            SetFocus(new AGFocus
            {
                Kind = AGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetEndpoints = focus.AssetEndpoints,
                Endpoint = endpoint,
            });
        }
        else
        {
            SetFocus(new AGFocus { Kind = AGFocusKind.Variable, Endpoint = endpoint });
        }
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>離開變數畫布：資產的變數回資產本體，Owner 的變數回時機畫布。</summary>
    private void ExitVariable()
    {
        if (focus.Endpoint == null) return;
        if (focus.Kind == AGFocusKind.Asset)
        {
            SetFocus(new AGFocus
            {
                Kind = AGFocusKind.Asset,
                AssetObject = focus.AssetObject,
                AssetHostSlot = focus.AssetHostSlot,
                AssetOrphans = focus.AssetOrphans,
                AssetEndpoints = focus.AssetEndpoints,
            });
        }
        else SetFocus(AllTimingsFocus());
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>slotType 只是用來合成一個型別正確的容器槽，讓資產內容能沿用一般的節點圖流程。</summary>
    private void EnterAsset(UnityEngine.Object asset, Type slotType)
    {
        if (asset == null) return;
        // 已經在這個資產裡：從變數子畫布回到資產本體，不重開交易。
        if (focus.Kind == AGFocusKind.Asset && focus.AssetObject == asset)
        {
            if (focus.Endpoint != null) ExitVariable();
            return;
        }
        AGFocus back = focus.Kind == AGFocusKind.Asset ? returnFocus : focus;
        if (focus.Kind == AGFocusKind.Asset && !ConfirmLeaveAsset()) return;

        object host = slotType != null ? AGReflect.CreateInstance(slotType) : null;
        if (host == null)
        {
            ShowNotification(new GUIContent("無法編輯：找不到這個資產對應的欄位型別"));
            return;
        }

        // 內容、候選與變數必須同一次複製：變數節點指著端點物件，分幾次抄就會抄成幾份不相干的端點。
        var pack = new List<object>
        {
            AGReflect.AssetRoot(asset),
            AGReflect.Orphans(asset) ?? new List<GraphNode>(),
            AGReflect.Endpoints(asset) ?? new List<GraphEndpoint>(),
        };
        var packCopy = ActionSystemDeepCopy.Copy(pack);
        // 根節點連載體一起抄進容器槽：座標、備註、Id 都在載體上，容器槽本身是拋棄式的。
        AGReflect.SetNode(host, packCopy?[0] as GraphNode);

        SetFocus(new AGFocus
        {
            Kind = AGFocusKind.Asset,
            AssetObject = asset,
            AssetHostSlot = host,
            AssetOrphans = packCopy?[1] as List<GraphNode> ?? new List<GraphNode>(),
            AssetEndpoints = packCopy?[2] as List<GraphEndpoint> ?? new List<GraphEndpoint>(),
        });
        returnFocus = back ?? new AGFocus();
        ClearAssetDirty();
        assetVerifiedOnce = false;
        assetReportStale = false;
        DoVerify(true);
        UpdateUnsavedState();
    }

    private bool SaveAsset(bool showDialog = true)
    {
        var asset = focus.AssetObject;
        var host = focus.AssetHostSlot;
        if (asset == null || host == null) return false;

        DoVerify(true);
        // 只搬過座標時不擋：寫回去的內容跟磁碟上完全一樣，錯誤是它本來就有的，沒必要連位置都存不了。
        if (assetContentDirty && !assetReport.CanSave)
        {
            consoleCollapsed = false;
            consoleTab = 1;
            if (showDialog)
                ShowNotification(new GUIContent($"無法存檔：這個資產還有 {assetReport.ErrorCount} 個錯誤"));
            return false;
        }

        int useType = AGReflect.UseType(host);
        if (useType == 2 || useType == 3)
        {
            if (showDialog)
                ShowNotification(new GUIContent("無法存檔：資產的內容只能是公式或動作，不能再指向另一個資產或變數"));
            return false;
        }

        var setRoot = asset.GetType().GetMethod("SetRoot");
        if (setRoot == null)
        {
            Debug.LogError($"[ActionGraph] {asset.GetType().Name} 沒有 SetRoot，無法寫回。");
            return false;
        }

        // 寫回也是一次抄三份：內容裡的變數節點與變數清單必須指到同一批端點物件。
        var pack = new List<object>
        {
            useType == 1 ? AGReflect.GetNode(host) : null,
            focus.AssetOrphans ?? new List<GraphNode>(),
            focus.AssetEndpoints ?? new List<GraphEndpoint>(),
        };
        var packCopy = ActionSystemDeepCopy.Copy(pack);
        setRoot.Invoke(asset, new object[] { packCopy?[0] as GraphNode });

        var storedOrphans = AGReflect.Orphans(asset);
        if (storedOrphans != null)
        {
            storedOrphans.Clear();
            if (packCopy?[1] is List<GraphNode> orphanCopy) storedOrphans.AddRange(orphanCopy);
        }
        if (AGReflect.Endpoints(asset) is List<GraphEndpoint> storedEndpoints)
        {
            storedEndpoints.Clear();
            if (packCopy?[2] is List<GraphEndpoint> endpointCopy) storedEndpoints.AddRange(endpointCopy);
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        // 內容沒變就不要驚動別人：座標是編輯器視覺，改它不會讓任何引用者的驗證結果不一樣。
        if (assetContentDirty) VerifyAssetUsers(asset);

        ClearAssetDirty();
        assetVerifiedOnce = true;
        assetReportStale = false;
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

    private void ExitAsset()
    {
        var back = returnFocus;
        returnFocus = null;
        ClearAssetDirty();
        assetVerifiedOnce = false;
        assetReportStale = false;
        assetReport = new AGReport();
        SetFocus(back != null && back.Kind != AGFocusKind.None ? back : AllTimingsFocus());
        DoVerify(true);
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>
    /// 資產內容變了，所有引用它的 Owner 都要重新驗證。**當場驗完**而不是只標記未驗證：
    /// 「未驗證」在別人的畫面上看不出來，等到執行時才被 runtime 擋下就太晚了。
    ///
    /// 驗證本身不碰檔案，所以名單再長也只是跑一遍記憶體。**只有驗證結果真的翻轉的 Owner 才 SetDirty**
    /// ——沒被改壞的人不該因為別人存了個資產就被改寫一次。
    /// </summary>
    private static void VerifyAssetUsers(UnityEngine.Object asset)
    {
        var failed = new List<string>();
        var touched = 0;
        foreach (var so in AGReferenceIndex.Users(asset as ScriptableObject))
        {
            // 索引是這個 session 算的，中間可能有人刪掉資產；碰 name 前先擋掉已銷毀的引用。
            if (so == null || so is not IActionSystemOwner owner) continue;

            bool wasValidated = owner.IsActionSystemValidated();
            owner.MarkActionSystemDirty();
            owner.VerifyActionSystem();
            bool nowValidated = owner.IsActionSystemValidated();

            if (!nowValidated) failed.Add(so.name);
            if (wasValidated == nowValidated) continue;

            EditorUtility.SetDirty(so);
            touched++;
        }

        if (touched > 0) AssetDatabase.SaveAssets();
        if (failed.Count == 0) return;

        Debug.LogError($"[ActionGraph] 資產 '{asset.name}' 存檔後，這些引用它的對象驗證不通過（多半是參數被改名／刪除／換型別）：" +
            string.Join("、", failed), asset);
    }
}

}
