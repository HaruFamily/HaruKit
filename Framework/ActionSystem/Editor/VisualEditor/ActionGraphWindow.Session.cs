namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 開窗入口、Owner 綁定、選取切換、存檔／取消／驗證交易，以及共用資產焦點的進出與訂閱同步。
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

        ScriptableObject owner = null;
        if (AGReflect.Get(asset, "_subscribers") is IList subs)
        {
            foreach (var s in subs)
                if (s is ScriptableObject so && AGModel.CanEdit(so)) { owner = so; break; }
        }
        if (owner == null)
        {
            EditorUtility.DisplayDialog("無法開啟",
                "這個資產沒有登記引用者，找不到可用的上下文。\n請改從引用它的對象進入，或先在資產編輯畫面按「重建引用清單」。", "好");
            return;
        }

        if (!Bind(owner)) return;
        if (TryEnterSharedAsset(asset)) return;
        EditorUtility.DisplayDialog("找不到引用點",
            $"'{owner.name}' 的引用清單登記了這個資產，但實際內容裡找不到指向它的欄位。\n請在資產編輯畫面重建引用清單。", "好");
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

        // 磁碟上的 Owner 已不再引用時，這筆 subscriber 是舊快取；資產仍可借其型別上下文獨立編輯。
        if (model.Owner is ScriptableObject owner && !OwnerReferencesAsset(owner, asset))
            RemoveStaleSubscriber(asset, owner);

        EnterAsset(asset, compatibleSlot);
        return true;
    }

    private static void RemoveStaleSubscriber(ScriptableObject asset, ScriptableObject owner)
    {
        if (asset == null || owner == null) return;
        if (AGReflect.Get(asset, "_subscribers") is not IList subscribers || !subscribers.Contains(owner)) return;

        asset.GetType().GetMethod("UnregisterSubscriber")?.Invoke(asset, new object[] { owner });
        AssetDatabase.SaveAssets();
    }

    private static bool OwnerReferencesAsset(ScriptableObject owner, ScriptableObject asset)
    {
        var field = AGModel.FindSystemField(owner);
        var system = field?.GetValue(owner);
        if (system == null) return false;

        foreach (var slot in AGModel.SlotsOfSystem(system))
            if (AGReflect.GetAsset(slot) == asset) return true;
        return false;
    }

    /// <summary>Owner 存檔時才依最終工作副本同步訂閱，取消與 Undo 不會污染衍生快取。</summary>
    private void SyncOwnerAssetSubscriptions()
    {
        if (model?.Owner is not ScriptableObject owner) return;

        var referenced = CollectAssets(model.AllSlots());
        var candidates = new HashSet<ScriptableObject>(referenced);

        var field = AGModel.FindSystemField(owner);
        var storedSystem = field?.GetValue(owner);
        if (storedSystem != null)
            foreach (var asset in CollectAssets(AGModel.SlotsOfSystem(storedSystem))) candidates.Add(asset);

        // 涵蓋「剛轉存後又刪除」：它不在舊資料與新資料內，但可能殘留舊版立即登記的 subscriber。
        foreach (var entry in AGAssetIndex.Entries)
            if (entry.Asset != null) candidates.Add(entry.Asset);

        foreach (var asset in candidates)
        {
            string method = referenced.Contains(asset) ? "RegisterSubscriber" : "UnregisterSubscriber";
            asset.GetType().GetMethod(method)?.Invoke(asset, new object[] { owner });
        }
    }

    private static HashSet<ScriptableObject> CollectAssets(IEnumerable<object> slots)
    {
        var result = new HashSet<ScriptableObject>();
        foreach (var slot in slots)
            if (AGReflect.GetAsset(slot) is ScriptableObject asset) result.Add(asset);
        return result;
    }

    public bool Bind(UnityEngine.Object owner)
    {
        if (HasUnsavedWork && !EditorUtility.DisplayDialog(
                "尚未儲存", $"'{(model?.Owner != null ? model.Owner.name : "?")}' 有未儲存的修改，切換後會遺失。要繼續嗎？", "捨棄並切換", "取消"))
            return false;

        SaveCurrentTiming();
        returnFocus = null;
        assetDirty = false;
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
        RestoreCurrentTiming();
        graphDirty = true;
        verifiedOnce = false;
        report = AGValidator.Run(model, includeMissingTypes: true);
        verifiedOnce = true;
        reportStale = false;
        UpdateUnsavedState();
        Repaint();
        return true;
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
            if (choice == 0)
            {
                SaveAsset();
                if (focus.Kind == AGFocusKind.Asset) return false;
            }
            else ExitAsset();
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
        SaveCurrentTiming();
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
        currentTiming = null;
        tokenSearch = "";
        assetSearch = "";
        pendingTarget = null;
        returnFocus = null;
        assetDirty = false;
        selectedIds.Clear();
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
        SaveCurrentTiming();
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
            focus = new AGFocus();
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

    private string TimingPrefKey()
    {
        if (model?.Owner == null) return null;
        string path = AssetDatabase.GetAssetPath(model.Owner);
        if (string.IsNullOrEmpty(path)) return null;
        return PrefTimingPrefix + AssetDatabase.AssetPathToGUID(path);
    }

    private void SaveCurrentTiming()
    {
        string key = TimingPrefKey();
        if (key == null) return;
        if (currentTiming == null) EditorPrefs.DeleteKey(key);
        else EditorPrefs.SetString(key, currentTiming.ToString());
    }

    private void RestoreCurrentTiming()
    {
        currentTiming = null;
        string key = TimingPrefKey();
        if (key == null || !EditorPrefs.HasKey(key)) return;

        string saved = EditorPrefs.GetString(key);
        if (!Enum.IsDefined(model.TimingType, saved)) return;
        currentTiming = Enum.Parse(model.TimingType, saved) as Enum;
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
            if (showDialog)
                EditorUtility.DisplayDialog("無法存檔", $"還有 {report.ErrorCount} 個錯誤，請先在 Console 修正。", "好");
            return false;
        }
        if (!model.Save())
        {
            if (showDialog)
                EditorUtility.DisplayDialog("無法存檔", "Core 驗證未通過，Owner 未寫入。請查看 Unity Console。", "好");
            return false;
        }
        SyncOwnerAssetSubscriptions();
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
        focus = new AGFocus();
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

    /// <summary>slotType 只是用來合成一個型別正確的容器槽，讓資產內容能沿用一般的節點圖流程。</summary>
    private void EnterAsset(UnityEngine.Object asset, Type slotType)
    {
        if (asset == null) return;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetObject == asset) return;
        AGFocus back = focus.Kind == AGFocusKind.Asset ? returnFocus : focus;
        if (focus.Kind == AGFocusKind.Asset && !ConfirmLeaveAsset()) return;

        object host = slotType != null ? AGReflect.CreateInstance(slotType) : null;
        if (host == null)
        {
            EditorUtility.DisplayDialog("無法編輯", "找不到這個資產對應的欄位型別。", "好");
            return;
        }

        var target = AGReflect.Get(asset, "_target") ?? AGReflect.Get(asset, "_action");
        if (target is ActionSystemNode source) AGReflect.SetFormula(host, source.EditorClone());
        else AGReflect.ClearNode(host);

        SetFocus(new AGFocus { Kind = AGFocusKind.Asset, AssetObject = asset, AssetHostSlot = host });
        returnFocus = back ?? new AGFocus();
        assetDirty = false;
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
        if (!assetReport.CanSave)
        {
            consoleCollapsed = false;
            consoleTab = 1;
            if (showDialog)
                EditorUtility.DisplayDialog("無法存檔", $"這個資產還有 {assetReport.ErrorCount} 個錯誤。", "好");
            return false;
        }

        int useType = AGReflect.UseType(host);
        if (useType == 2 || useType == 3)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("無法存檔", "資產的內容只能是公式或動作，不能再指向另一個資產或變數。", "好");
            return false;
        }

        var content = useType == 1 ? AGReflect.GetFormula(host) : null;
        var setTarget = asset.GetType().GetMethod("SetTarget");
        if (setTarget == null)
        {
            Debug.LogError($"[ActionGraph] {asset.GetType().Name} 沒有 SetTarget，無法寫回。");
            return false;
        }
        setTarget.Invoke(asset, new object[] { content });
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        NotifyAssetSubscribers(asset);
        AssetDatabase.SaveAssets();

        assetDirty = false;
        assetVerifiedOnce = true;
        assetReportStale = false;
        ShowNotification(new GUIContent("資產已存檔"));
        ExitAsset();
        UpdateUnsavedState();
        return true;
    }

    private void CancelAsset()
    {
        if (!ConfirmLeaveAsset()) return;
        ExitAsset();
    }

    private bool ConfirmLeaveAsset()
    {
        if (!assetDirty) return true;
        return EditorUtility.DisplayDialog("捨棄資產修改",
            "這個資產自進入後的修改會被丟掉，確定嗎？", "捨棄", "繼續編輯");
    }

    private void ExitAsset()
    {
        var back = returnFocus;
        returnFocus = null;
        assetDirty = false;
        assetVerifiedOnce = false;
        assetReportStale = false;
        assetReport = new AGReport();
        SetFocus(back ?? new AGFocus());
        DoVerify(true);
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>資產內容變了，所有引用它的 Owner 都要重新驗證。</summary>
    private static void NotifyAssetSubscribers(UnityEngine.Object asset)
    {
        if (AGReflect.Get(asset, "_subscribers") is not IList subscribers) return;
        foreach (var s in subscribers)
        {
            if (s is not IActionSystemOwner owner) continue;
            owner.MarkActionSystemDirty();
            if (s is UnityEngine.Object so) EditorUtility.SetDirty(so);
        }
    }
}

}
