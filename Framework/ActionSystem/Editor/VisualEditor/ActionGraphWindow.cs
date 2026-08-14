namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ActionSystem 視覺化編輯器：左欄變數庫、中欄節點圖 + Console、右欄時機與動作清單。
/// 所有編輯都改工作副本，按「存檔」才寫回 Owner 資產。
/// </summary>
public class ActionGraphWindow : EditorWindow
{
    private const float ToolbarHeight = 22f;
    private const float DefaultLeftWidth = 220f;
    private const float DefaultRightWidth = 280f;
    private const float MinLeftWidth = 160f;
    private const float MinRightWidth = 180f;
    private const float MinCenterWidth = 320f;
    private const float ResizeHandleWidth = 6f;
    private const float HeaderHeight = 46f;
    private const float MinConsole = 22f;
    private const float NodeCornerRadius = 6f;
    private const float LinkSnapDistance = 24f;
    private const float LinkThickness = 4f;
    private const float TokenCellHeight = 30f;
    private const float ActionCellHeight = TokenCellHeight;
    private const float AssetCellHeight = 34f;
    private const string PrefConsoleHeight = "ActionGraph.ConsoleHeight";
    private const string PrefConsoleCollapsed = "ActionGraph.ConsoleCollapsed";
    private const string PrefLeftWidth = "ActionGraph.LeftWidth";
    private const string PrefRightWidth = "ActionGraph.RightWidth";
    private const string PrefTimingPrefix = "ActionGraph.Timing.";

    private AGModel model;
    private AGFocus focus = new();
    private AGGraphView graph;
    private bool graphDirty = true;
    private AGReport report = new();
    private bool verifiedOnce;
    private bool reportStale;
    private bool assetVerifiedOnce;
    private bool assetReportStale;

    // 畫布
    private Vector2 pan = new(20f, 20f);
    private float zoom = 1f;
    private Rect canvasRect;
    private Matrix4x4 canvasGuiMatrix;
    private Rect rootGuiGroupRect;

    // 面板狀態
    private Vector2 tokenScroll, assetLibraryScroll, actionScroll, consoleScroll;
    private float consoleHeight = 150f;
    private bool consoleCollapsed;
    private bool resizingConsole;
    private float leftWidth = DefaultLeftWidth;
    private float rightWidth = DefaultRightWidth;
    private bool resizingLeftPanel;
    private bool resizingRightPanel;
    private int consoleTab;                  // 0 全部 / 1 錯誤 / 2 警告
    private int libraryTab;                  // 0 變數 / 1 資產
    private string tokenSearch = "";
    private string assetSearch = "";
    private Enum currentTiming;
    private object editingNameTarget;
    private string editingNameDraft = "";

    // 互動
    private AGNode dragNode;
    private Vector2 dragOffset;
    private readonly Dictionary<string, Vector2> dragStartPositions = new();
    private bool linking;
    private AGRow linkRow;
    private AGNode linkNode;

    // 拉線期間的相容性：起手時對全圖判定一次，之後高亮與吸附都讀這份，不必每幀重算。
    private readonly HashSet<string> linkCompatibleNodeIds = new();
    private readonly HashSet<AGRow> linkCompatibleRows = new();
    private AGToken dragToken;
    private bool dragTokenActive;
    private AGToken pendingTokenFocus;
    private ScriptableObject dragAsset;
    private bool dragAssetActive;
    private ScriptableObject pendingAssetFocus;
    private AGFocus pendingActionFocus;
    // 選取用 id 記，節點物件每次重建圖都會換一份。
    private readonly HashSet<string> selectedIds = new();
    private bool boxSelecting;
    private Vector2 boxStart;
    private Vector2 boxEnd;
    private int dragActionIndex = -1;
    private AGRow dragListRow;
    private int dragListIndex = -1;
    private object pendingCenterTarget;
    private static readonly List<object> clipboard = new();

    // 有未儲存變更時不硬切對象，先記在這裡等使用者按確認
    private UnityEngine.Object pendingTarget;

    // 資產焦點（獨立存檔交易）
    private AGFocus returnFocus;
    private bool assetDirty;
    private AGReport assetReport = new();
    private Vector2 referenceScroll;

    private bool HasUnsavedWork => model?.Dirty == true || assetDirty;
    private bool IsCurrentReportFresh => focus.Kind == AGFocusKind.Asset
        ? assetVerifiedOnce && !assetReportStale
        : verifiedOnce && !reportStale;

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

    // ===== 主繪製 =====

    private void OnGUI()
    {
        GetLayout(out var toolbar, out var left, out var right, out var center, out var leftHandle, out var rightHandle);
        HandlePanelResize(leftHandle, rightHandle);
        GetLayout(out toolbar, out left, out right, out center, out leftHandle, out rightHandle);

        if (model == null || model.Owner == null)
        {
            DrawIdle(toolbar, left, right, center);
            DrawPanelResizeHandles(leftHandle, rightHandle);
            UpdateUnsavedState();
            return;
        }

        HandleGlobalKeys();
        EnsureGraph();
        if (Event.current.type == EventType.MouseDrag && dragToken != null) dragTokenActive = true;
        if (Event.current.type == EventType.MouseDrag && dragAsset != null) dragAssetActive = true;

        // 縮放畫布先畫；固定面板最後畫，吸收 IMGUI 縮放在邊界可能漏出的次像素。
        DrawCenter(center);
        DrawLibraryPanel(left);
        if (focus.Kind == AGFocusKind.Asset) DrawReferencePanel(right);
        else DrawTimingPanel(right);
        DrawToolbar(toolbar);
        DrawPanelResizeHandles(leftHandle, rightHandle);

        if (dragTokenActive) DrawDragTokenGhost();
        if (dragAssetActive) DrawDragAssetGhost();
        if (Event.current.rawType == EventType.MouseUp)
        {
            if (Event.current.button == 0) EndLink();
            dragTokenActive = false;
            dragToken = null;
            pendingTokenFocus = null;
            dragAssetActive = false;
            dragAsset = null;
            pendingAssetFocus = null;
            pendingActionFocus = null;
            dragActionIndex = -1;
            dragListRow = null;
            dragListIndex = -1;
        }
        if (Event.current.type == EventType.MouseDrag || linking || dragTokenActive) Repaint();
        UpdateUnsavedState();
    }

    private void GetLayout(out Rect toolbar, out Rect left, out Rect right, out Rect center, out Rect leftHandle, out Rect rightHandle)
    {
        float maxLeft = Mathf.Max(MinLeftWidth, position.width - rightWidth - MinCenterWidth);
        leftWidth = Mathf.Clamp(leftWidth, MinLeftWidth, maxLeft);
        float maxRight = Mathf.Max(MinRightWidth, position.width - leftWidth - MinCenterWidth);
        rightWidth = Mathf.Clamp(rightWidth, MinRightWidth, maxRight);

        toolbar = new Rect(0f, 0f, position.width, ToolbarHeight);
        left = new Rect(0f, ToolbarHeight, leftWidth, position.height - ToolbarHeight);
        right = new Rect(position.width - rightWidth, ToolbarHeight, rightWidth, position.height - ToolbarHeight);
        center = new Rect(left.xMax, ToolbarHeight, right.xMin - left.xMax, position.height - ToolbarHeight);
        leftHandle = new Rect(left.xMax - ResizeHandleWidth * 0.5f, ToolbarHeight, ResizeHandleWidth, left.height);
        rightHandle = new Rect(right.xMin - ResizeHandleWidth * 0.5f, ToolbarHeight, ResizeHandleWidth, right.height);
    }

    /// <summary>分隔把手先處理事件，畫布不能攔截欄位縮放拖曳。</summary>
    private void HandlePanelResize(Rect leftHandle, Rect rightHandle)
    {
        var e = Event.current;
        EditorGUIUtility.AddCursorRect(leftHandle, MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(rightHandle, MouseCursor.ResizeHorizontal);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (leftHandle.Contains(e.mousePosition)) resizingLeftPanel = true;
            else if (rightHandle.Contains(e.mousePosition)) resizingRightPanel = true;
            else return;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && resizingLeftPanel)
        {
            leftWidth = Mathf.Clamp(leftWidth + e.delta.x, MinLeftWidth, position.width - rightWidth - MinCenterWidth);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingRightPanel)
        {
            rightWidth = Mathf.Clamp(rightWidth - e.delta.x, MinRightWidth, position.width - leftWidth - MinCenterWidth);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && (resizingLeftPanel || resizingRightPanel))
        {
            resizingLeftPanel = false;
            resizingRightPanel = false;
            e.Use();
        }
    }

    private void DrawPanelResizeHandles(Rect leftHandle, Rect rightHandle)
    {
        DrawResizeGrip(leftHandle, true, resizingLeftPanel);
        DrawResizeGrip(rightHandle, true, resizingRightPanel);
    }

    /// <summary>所有區塊縮放共用同一種細分隔線與刻度，不用粗色塊搶畫面。</summary>
    private static void DrawResizeGrip(Rect handle, bool vertical, bool dragging)
    {
        bool hover = handle.Contains(Event.current.mousePosition);
        var color = dragging || hover ? AGStyles.Link : new Color(0.34f, 0.36f, 0.40f, 0.65f);
        if (vertical)
        {
            float x = handle.center.x;
            AGStyles.Fill(new Rect(x, handle.y, 1f, handle.height), color);
            float y = handle.center.y - 8f;
            for (int i = 0; i < 3; i++, y += 6f)
                AGStyles.Fill(new Rect(x - 1f, y, 3f, 1f), color);
            return;
        }

        float lineY = handle.center.y;
        AGStyles.Fill(new Rect(handle.x, lineY, handle.width, 1f), color);
        float xStart = handle.center.x - 8f;
        for (int i = 0; i < 3; i++, xStart += 6f)
            AGStyles.Fill(new Rect(xStart, lineY - 1f, 1f, 3f), color);
    }

    /// <summary>
    /// 還沒選對象時的閒置版型：三欄框架照畫，但全部停用，等使用者從 Project／Hierarchy 點一個支援的對象。
    /// </summary>
    private void DrawIdle(Rect toolbar, Rect left, Rect right, Rect center)
    {
        AGStyles.Fill(toolbar, new Color(0.20f, 0.21f, 0.24f));
        GUI.Label(new Rect(toolbar.x + 6f, toolbar.y + 2f, toolbar.width - 320f, 18f),
            "尚未選擇編輯對象", EditorStyles.boldLabel);

        GUI.enabled = false;
        float x = toolbar.xMax - 6f;
        x -= 96f; GUI.Button(new Rect(x, toolbar.y + 1f, 94f, 19f), "存檔");
        x -= 62f; GUI.Button(new Rect(x, toolbar.y + 1f, 60f, 19f), "取消");
        x -= 92f; GUI.Button(new Rect(x, toolbar.y + 1f, 90f, 19f), "換編輯對象");
        GUI.enabled = true;

        DrawIdlePanel(left, "資料庫");
        DrawIdlePanel(right, "時機");

        AGStyles.Fill(center, AGStyles.Canvas);
        var header = new Rect(center.x, center.y, center.width, HeaderHeight);
        AGStyles.Fill(header, new Color(0.22f, 0.23f, 0.26f));
        AGStyles.Frame(header, AGStyles.NodeBorder);
        GUI.Label(new Rect(header.x + 6f, header.y + 3f, header.width - 12f, 18f), "（沒有編輯對象）", EditorStyles.boldLabel);
        GUI.Label(new Rect(header.x + 6f, header.y + 24f, header.width - 12f, 16f),
            "從 Project 或 Hierarchy 點選一個含 ActionSystem 的對象，這裡就會自動切過去。", AGStyles.Tiny);

        var canvas = new Rect(center.x, center.y + HeaderHeight, center.width, center.height - HeaderHeight - MinConsole);
        AGStyles.Fill(canvas, AGStyles.Canvas);
        DrawGrid(canvas);

        var console = new Rect(center.x, canvas.yMax, center.width, MinConsole);
        AGStyles.Fill(console, new Color(0.17f, 0.18f, 0.20f));
        AGStyles.Frame(console, AGStyles.NodeBorder);
        GUI.Label(new Rect(console.x + 6f, console.y + 3f, console.width - 12f, 16f), "尚未驗證", AGStyles.Tiny);
    }

    private static void DrawIdlePanel(Rect r, string title)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), title, AGStyles.PanelHeader);

        var body = new Rect(r.x + 2f, r.y + 22f, r.width - 4f, r.height - 26f);
        AGStyles.Fill(body, new Color(0.16f, 0.17f, 0.19f));
    }


    private void EnsureGraph()
    {
        if (!graphDirty && graph != null) return;
        graphDirty = false;

        // 候選池掛在焦點的頭端上，不必再依 FocusId 過濾。
        model.OrphanHead = focus.Head;

        var rootSlot = focus.RootSlot;
        graph = rootSlot != null
            ? AGGraph.Build(model, rootSlot, OrphansOfCurrentFocus(), focus.Id)
            : new AGGraphView();

        if (pendingCenterTarget != null) { CenterOn(pendingCenterTarget); pendingCenterTarget = null; }
    }

    /// <summary>目前焦點頭端自己的候選節點。</summary>
    private IList OrphansOfCurrentFocus() => AGReflect.Orphans(focus.Head);

    /// <summary>目前畫面該用哪一份驗證結果：資產焦點只看資產自己的。</summary>
    private AGReport Rep => focus.Kind == AGFocusKind.Asset ? assetReport : report;

    private void Invalidate()
    {
        graphDirty = true;
        // 資產是獨立存檔交易，改它不算改 Owner，也不進 Owner 的 Undo 堆疊。
        if (focus.Kind == AGFocusKind.Asset)
        {
            assetDirty = true;
            assetReportStale = true;
        }
        else
        {
            model.MarkDirty();
            reportStale = true;
        }
        LiveVerify();
        UpdateUnsavedState();
    }

    /// <summary>
    /// 每次修改後重跑驗證：接上來源、換型別、刪節點都會立刻反映在徽章與 Console。
    /// 規則與存檔時完全相同（含 Token 循環、Asset 參照循環）；差別只有 SerializeReference
    /// 型別遺失那一項——它看的是 Owner 本體，編輯工作副本不會改變它。
    /// </summary>
    private void LiveVerify()
    {
        if (model?.Data == null) return;

        if (focus.Kind == AGFocusKind.Asset)
        {
            if (focus.AssetHostSlot == null) return;
            assetReport = AGValidator.RunSubtree(model, focus, focus.AssetHostSlot, focus.Title);
            assetVerifiedOnce = true;
            return;
        }

        report = AGValidator.Run(model);
        verifiedOnce = true;
    }

    // ===== 全域快捷鍵 =====

    private void HandleGlobalKeys()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || !e.control) return;

        if (focus.Kind == AGFocusKind.Asset && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
        {
            ShowNotification(new GUIContent("資產編輯中不支援復原；取消可整批捨棄"));
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Z && !e.shift)
        {
            if (model.Undo()) AfterHistorySwap(); else ShowNotification(new GUIContent("沒有可復原的步驟"));
            e.Use();
        }
        else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift))
        {
            if (model.Redo()) AfterHistorySwap(); else ShowNotification(new GUIContent("沒有可重做的步驟"));
            e.Use();
        }
        else if (e.keyCode == KeyCode.S)
        {
            if (focus.Kind == AGFocusKind.Asset) SaveAsset(); else DoSave();
            e.Use();
        }
    }

    /// <summary>Undo/Redo 換掉整份資料後，焦點抓的是舊圖的參考，要用「時機+索引 / 變數名稱」重新指回去。</summary>
    private void AfterHistorySwap()
    {
        var kind = focus.Kind;
        var timing = focus.Timing;
        int index = focus.ActionIndex;
        string tokenKey = focus.Token?.Key;
        var tokenType = focus.Token?.ResultType;

        focus = new AGFocus();
        if (kind == AGFocusKind.Action)
        {
            foreach (var g in model.ReadGroups())
            {
                if (!Equals(g.Timing, timing) || g.Actions == null) continue;
                if (index < 0 || index >= g.Actions.Count) break;
                focus = new AGFocus
                {
                    Kind = AGFocusKind.Action, Timing = g.Timing,
                    ActionList = g.Actions, ActionIndex = index, ActionSlot = g.Actions[index],
                };
                break;
            }
        }
        else if (kind == AGFocusKind.Token)
        {
            foreach (var t in model.ReadTokens())
            {
                if (t.Key != tokenKey || t.ResultType != tokenType) continue;
                focus = new AGFocus { Kind = AGFocusKind.Token, Token = t };
                break;
            }
        }

        selectedIds.Clear();
        graphDirty = true;
        DoVerify(true);
        UpdateUnsavedState();
        Repaint();
    }

    // ===== 頂部 =====

    private void DrawToolbar(Rect r)
    {
        AGStyles.Fill(r, new Color(0.20f, 0.21f, 0.24f));

        // 麵包屑：資產是獨立一層，未儲存狀態與外層各記各的。
        var ownerPickerRect = new Rect(r.x + 4f, r.y + 2f, 18f, 18f);
        GUI.enabled = focus.Kind != AGFocusKind.Asset;
        if (GUI.Button(ownerPickerRect, new GUIContent("", "換編輯對象"), EditorStyles.popup))
            AGOwnerIndex.ShowPicker(ownerPickerRect, owner => { Bind(owner); Repaint(); });
        GUI.enabled = true;

        string ownerPath = AssetDatabase.GetAssetPath(model.Owner);
        if (string.IsNullOrEmpty(ownerPath)) ownerPath = "Scene";
        bool inAsset = focus.Kind == AGFocusKind.Asset;
        string crumb = $"{model.Owner.name} ({model.Owner.GetType().Name})({ownerPath})";
        GUI.Label(new Rect(ownerPickerRect.xMax + 4f, r.y + 2f, r.width - 440f, 18f), crumb, EditorStyles.boldLabel);

        // 即時檢查一有錯就把存檔鈕關掉；沒有錯時仍可按，存檔當下再跑一次嚴格驗證。
        bool blocked = !Rep.CanSave;
        bool hasChanges = inAsset ? assetDirty : model.Dirty;
        bool canSave = hasChanges && !blocked;

        float x = r.xMax - 6f;
        x -= 96f;
        var saveRect = new Rect(x, r.y + 1f, 94f, 19f);
        GUI.enabled = canSave;
        var saveColor = GUI.backgroundColor;
        if (canSave) GUI.backgroundColor = new Color(0.85f, 0.28f, 0.28f);
        string saveLabel = blocked ? "存檔（有錯誤）" : inAsset ? "存檔並返回" : "存檔";
        string saveTooltip = blocked ? "驗證有錯誤，先在 Console 修正才能存檔"
            : !hasChanges ? "目前沒有未儲存的修改"
            : !IsCurrentReportFresh ? "按下後先做完整驗證（含循環與型別遺失），通過才會存檔"
            : "驗證通過後寫回資產";
        if (GUI.Button(saveRect, new GUIContent(saveLabel, saveTooltip)))
        {
            if (inAsset) SaveAsset(); else DoSave();
        }
        GUI.backgroundColor = saveColor;
        GUI.enabled = true;

        x -= 62f;
        if (GUI.Button(new Rect(x, r.y + 1f, 60f, 19f), inAsset ? "捨棄返回" : "取消"))
        {
            if (inAsset) CancelAsset(); else DoCancel();
        }

        x -= 48f;
        GUI.enabled = !inAsset && model.CanRedo;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("重做", "Ctrl+Y / Ctrl+Shift+Z")))
            if (model.Redo()) AfterHistorySwap();
        GUI.enabled = true;

        x -= 48f;
        GUI.enabled = !inAsset && model.CanUndo;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("復原", "Ctrl+Z")))
            if (model.Undo()) AfterHistorySwap();
        GUI.enabled = true;

        x -= 132f;
        var switchRect = new Rect(x, r.y + 1f, 130f, 19f);
        if (pendingTarget != null)
        {
            var label = new GUIContent($"切換→{pendingTarget.name}", "剛才選取了別的對象，按此切換（目前的修改會依提示處理）");
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            if (GUI.Button(switchRect, label))
            {
                var target = pendingTarget;
                pendingTarget = null;
                if (inAsset)
                {
                    if (!ConfirmLeaveAsset()) { GUI.backgroundColor = old; return; }
                    ExitAsset();
                }
                if (target is ScriptableObject asset && IsSharedAsset(asset)) OpenSharedAsset(asset);
                else Bind(target);
            }
            GUI.backgroundColor = old;
        }
        else
        {
        }
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

    // ===== 左欄：Token／Asset 庫 =====

    private void DrawLibraryPanel(Rect r)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        bool inAsset = focus.Kind == AGFocusKind.Asset;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 18f), "資料庫", AGStyles.PanelHeader);

        float tabWidth = (r.width - 8f) * 0.5f;
        if (DrawTab(new Rect(r.x + 4f, r.y + 22f, tabWidth, 22f), "變數", libraryTab == 0)) libraryTab = 0;
        if (DrawTab(new Rect(r.x + 4f + tabWidth, r.y + 22f, tabWidth, 22f), "資產", libraryTab == 1)) libraryTab = 1;

        if (libraryTab == 0) DrawTokenLibrary(r, r.y + 48f, inAsset);
        else DrawAssetLibrary(r, r.y + 48f);
    }

    private void DrawTokenLibrary(Rect r, float top, bool inAsset)
    {
        GUI.Label(new Rect(r.x + 6f, top, r.width - 12f, 16f),
            new GUIContent(inAsset ? "呼叫端變數" : "共用變數",
                inAsset ? "資產目前以名稱對應呼叫端的變數，沒有自己的參數宣告" : ""), AGStyles.Tiny);

        var createRect = new Rect(r.x + 2f, top + 18f, r.width - 4f, 28f);
        AGStyles.Fill(createRect, new Color(0.22f, 0.23f, 0.26f));
        AGStyles.Frame(createRect, AGStyles.NodeBorder);

        var kinds = model.TokenKinds();
        GUI.enabled = !inAsset && kinds.Count > 0;
        if (GUI.Button(new Rect(r.x + 4f, top + 21f, r.width - 8f, 22f), "新增變數")) ShowAddTokenMenu();
        GUI.enabled = true;

        var removeRect = new Rect(r.x + 4f, top + 50f, r.width - 8f, 20f);
        bool canRemoveToken = !inAsset && focus.Kind == AGFocusKind.Token && focus.Token != null;
        GUI.enabled = canRemoveToken;
        if (GUI.Button(removeRect, "移除變數")) RemoveToken(focus.Token);
        GUI.enabled = true;

        var searchRect = new Rect(r.x + 4f, top + 74f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋變數"));
        tokenSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), tokenSearch);

        var listRect = new Rect(r.x + 2f, top + 98f, r.width - 4f, r.yMax - top - 100f);
        var tokens = model.ReadTokens();
        var shown = new List<AGToken>();
        foreach (var t in tokens)
            if (string.IsNullOrWhiteSpace(tokenSearch)
                || t.Key?.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || t.TypeName.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                shown.Add(t);

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * TokenCellHeight + 4f);
        tokenScroll = GUI.BeginScrollView(listRect, tokenScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var token = shown[i];
            var row = new Rect(2f, i * TokenCellHeight + 2f, content.width - 4f, TokenCellHeight - 3f);
            bool isFocus = focus.Kind == AGFocusKind.Token && focus.Token != null
                && focus.Token.Key == token.Key && focus.Token.ResultType == token.ResultType;
            AGStyles.Fill(row, isFocus ? AGStyles.LibraryCellFocused
                : i % 2 == 0 ? AGStyles.LibraryCell : AGStyles.LibraryCellAlt);
            AGStyles.Frame(row, isFocus ? AGStyles.Link : AGStyles.LibraryCellBorder);

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 70f, 18f),
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, AGStyles.RowLabel);
            var typeRect = new Rect(row.xMax - 58f, row.y + 6f, 42f, 15f);
            AGStyles.Fill(typeRect, new Color(0.27f, 0.24f, 0.38f));
            GUI.Label(typeRect, token.TypeName, AGStyles.Tiny);

            if (HasTokenIssue(token, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 1) { ShowTokenMenu(token); e.Use(); }
                else
                {
                    dragToken = token;
                    pendingTokenFocus = token;
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDrag && dragToken == token) dragTokenActive = true;
            if (e.type == EventType.MouseUp && pendingTokenFocus != null
                && pendingTokenFocus.Key == token.Key && pendingTokenFocus.ResultType == token.ResultType
                && !dragTokenActive && row.Contains(e.mousePosition))
            {
                dragToken = null;
                pendingTokenFocus = null;
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    private void DrawAssetLibrary(Rect r, float top)
    {
        if (GUI.Button(new Rect(r.x + 4f, top, r.width - 8f, 22f), "重新掃描資產")) AGAssetIndex.Refresh();

        var searchRect = new Rect(r.x + 4f, top + 26f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋資產"));
        assetSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y,
            searchRect.width - 20f, searchRect.height), assetSearch);

        var shown = new List<(AGAssetEntry entry, Type slotType)>();
        var slotTypes = AssetSlotTypes();
        foreach (var entry in AGAssetIndex.Entries)
        {
            Type slotType = SlotTypeForAsset(entry.Asset, slotTypes);
            if (slotType == null) continue;
            if (!string.IsNullOrWhiteSpace(assetSearch)
                && entry.Name.IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0
                && entry.TypeName.IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0
                && (entry.ResultType == null
                    || AGReflect.ResultTypeName(entry.ResultType).IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            shown.Add((entry, slotType));
        }

        var listRect = new Rect(r.x + 2f, top + 50f, r.width - 4f, r.yMax - top - 52f);
        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * AssetCellHeight + 4f);
        assetLibraryScroll = GUI.BeginScrollView(listRect, assetLibraryScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i].entry;
            var asset = entry.Asset;
            var row = new Rect(2f, i * AssetCellHeight + 2f, content.width - 4f, AssetCellHeight - 3f);
            bool isFocus = focus.Kind == AGFocusKind.Asset && focus.AssetObject == asset;
            AGStyles.Fill(row, isFocus ? AGStyles.LibraryCellFocused
                : i % 2 == 0 ? AGStyles.LibraryCell : AGStyles.LibraryCellAlt);
            AGStyles.Frame(row, isFocus ? AGStyles.Link : AGStyles.LibraryCellBorder);

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 64f, 17f), asset.name, AGStyles.RowLabel);
            string kind = entry.IsAction ? "ACT" : AGReflect.ResultTypeName(entry.ResultType);
            var typeRect = new Rect(row.xMax - 54f, row.y + 5f, 46f, 15f);
            AGStyles.Fill(typeRect, entry.IsAction ? new Color(0.42f, 0.27f, 0.20f) : new Color(0.24f, 0.36f, 0.34f));
            GUI.Label(typeRect, kind, AGStyles.Tiny);
            GUI.Label(new Rect(row.x + 8f, row.y + 18f, row.width - 70f, 13f), entry.TypeName, AGStyles.Tiny);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                dragAsset = asset;
                pendingAssetFocus = asset;
                e.Use();
            }
            if (e.type == EventType.MouseDrag && dragAsset == asset) dragAssetActive = true;
            if (e.type == EventType.MouseUp && pendingAssetFocus == asset
                && !dragAssetActive && row.Contains(e.mousePosition))
            {
                pendingAssetFocus = null;
                dragAsset = null;
                EnterAsset(asset, shown[i].slotType);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>找目前 ActionSystem 中能承載此資產內容的 Slot；找不到代表本 Owner 不相容。</summary>
    private List<(Type acceptedAssetType, Type slotType)> AssetSlotTypes()
    {
        var result = new List<(Type acceptedAssetType, Type slotType)>();
        foreach (var (_, list) in model.TokenKinds())
        {
            Type entryType = list.GetType().GetGenericArguments()[0];
            if (AGReflect.CreateInstance(entryType) is not ITokenEntry entry || entry.Slot == null) continue;
            Type slotType = entry.Slot.GetType();
            Type accepted = AGReflect.AssetType(slotType);
            if (accepted != null) result.Add((accepted, slotType));
        }

        Type actionSlotType = model.ActionSlotType;
        Type actionAssetType = actionSlotType != null ? AGReflect.ActionAssetType(actionSlotType) : null;
        if (actionAssetType != null) result.Add((actionAssetType, actionSlotType));
        return result;
    }

    private static Type SlotTypeForAsset(ScriptableObject asset, List<(Type acceptedAssetType, Type slotType)> slotTypes)
    {
        if (asset == null) return null;
        foreach (var candidate in slotTypes)
            if (candidate.acceptedAssetType.IsInstanceOfType(asset)) return candidate.slotType;
        return null;
    }

    private bool HasTokenIssue(AGToken token, out string reason, out bool isError)
    {
        reason = null; isError = false;
        foreach (var issue in report.Issues)
        {
            if (issue.Focus == null || issue.Focus.Kind != AGFocusKind.Token) continue;
            if (issue.Focus.Token == null || issue.Focus.Token.Key != token.Key) continue;
            reason = issue.Line;
            isError = issue.IsError;
            if (isError) return true;
        }
        return reason != null;
    }

    private bool HasActionIssue(AGFocus action, out string reason, out bool isError)
    {
        reason = null; isError = false;
        foreach (var issue in report.Issues)
        {
            if (issue.Focus == null || !issue.Focus.SameAs(action)) continue;
            reason = issue.Line;
            isError = issue.IsError;
            if (isError) return true;
        }
        return reason != null;
    }

    private bool DrawTab(Rect r, string label, bool active)
    {
        AGStyles.Fill(r, active ? new Color(0.30f, 0.34f, 0.40f) : new Color(0.23f, 0.24f, 0.27f));
        GUI.Label(r, label, AGStyles.Tiny);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void ShowAddTokenMenu()
    {
        var menu = new GenericMenu();
        foreach (var (resultType, _) in model.TokenKinds())
        {
            var capturedType = resultType;
            menu.AddItem(new GUIContent(AGReflect.ResultTypeName(capturedType)), false, () => AddToken(capturedType));
        }
        menu.ShowAsContext();
    }

    private void AddToken(Type resultType)
    {
        string typeName = AGReflect.ResultTypeName(resultType).ToLowerInvariant();
        int index = 0;
        string key;
        do { key = $"t_{typeName}_{index++}"; }
        while (TokenKeyExists(key));

        if (!model.AddToken(resultType, key, out string error))
        {
            ShowNotification(new GUIContent(error));
            return;
        }
        foreach (var token in model.ReadTokens())
        {
            if (token.Key != key || token.ResultType != resultType) continue;
            SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
            break;
        }
        Invalidate();
        Repaint();
    }

    private bool TokenKeyExists(string key)
    {
        foreach (var token in model.ReadTokens())
            if (token.Key == key) return true;
        return false;
    }

    private void RemoveToken(AGToken token)
    {
        if (token == null) return;
        int refs = model.CountReferences(token);
        string msg = refs > 0
            ? $"'{token.Key}' 還有 {refs} 個欄位在引用，刪除後那些欄位會指向不存在的變數。"
            : $"確定刪除 '{token.Key}'？";
        if (!EditorUtility.DisplayDialog("刪除變數", msg, "刪除", "取消")) return;
        model.RemoveToken(token);
        SetFocus(new AGFocus());
        Invalidate();
        DoVerify(true);
        Repaint();
    }

    private void ShowTokenMenu(AGToken token)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("改名"), false, () =>
            AGPrompt.Show("變數改名", "輸入新名稱（所有引用處會同步更新）", token.Key, key =>
            {
                if (!model.RenameToken(token, key, out string error)) EditorUtility.DisplayDialog("無法改名", error, "好");
                Invalidate();
                Repaint();
            }));
        menu.AddItem(new GUIContent("刪除"), false, () =>
        {
            RemoveToken(token);
        });
        menu.ShowAsContext();
    }

    private void DrawDragTokenGhost()
    {
        if (dragToken == null) return;
        var p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 140f, 18f);
        AGStyles.Fill(r, new Color(0.30f, 0.24f, 0.42f, 0.95f));
        GUI.Label(r, $"@{dragToken.Key}", AGStyles.Chip);
    }

    private void DrawDragAssetGhost()
    {
        if (dragAsset == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        AGStyles.Fill(r, new Color(0.24f, 0.36f, 0.34f, 0.95f));
        GUI.Label(r, dragAsset.name, AGStyles.Chip);
    }

    // ===== 右欄：時機與動作清單 =====

    private void DrawTimingPanel(Rect r)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var groups = model.ReadGroups();
        var timingSection = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 50f);
        AGStyles.Fill(timingSection, new Color(0.22f, 0.23f, 0.26f));
        AGStyles.Frame(timingSection, AGStyles.NodeBorder);
        GUI.Label(new Rect(r.x + 4f, r.y + 4f, 120f, 18f), "時機", AGStyles.PanelHeader);

        string timingLabel = currentTiming != null ? currentTiming.ToString() : "（選擇時機）";
        var dropRect = new Rect(r.x + 4f, r.y + 26f, r.width - 8f, 24f);
        if (EditorGUI.DropdownButton(dropRect, new GUIContent(timingLabel), FocusType.Keyboard))
            ShowTimingMenu(groups);

        AGTimingGroup current = null;
        foreach (var g in groups)
            if (currentTiming != null && Equals(g.Timing, currentTiming)) current = g;

        GUI.enabled = currentTiming != null;
        if (GUI.Button(new Rect(r.x + 4f, r.y + 56f, r.width - 8f, 20f), "新增動作"))
            AddEmptyAction(currentTiming);
        GUI.enabled = true;

        var removeRect = new Rect(r.x + 4f, r.y + 78f, r.width - 8f, 20f);
        bool canRemoveAction = focus.Kind == AGFocusKind.Action && focus.ActionSlot != null
            && Equals(focus.Timing, currentTiming);
        GUI.enabled = canRemoveAction;
        if (GUI.Button(removeRect, "移除動作")) RemoveAction(focus);
        GUI.enabled = true;

        var listRect = new Rect(r.x + 2f, r.y + 102f, r.width - 4f, r.height - 104f);
        AGStyles.Fill(listRect, new Color(0.16f, 0.17f, 0.19f));

        if (current?.Actions != null) DrawActionList(listRect, current);

    }

    private void DrawActionList(Rect listRect, AGTimingGroup group)
    {
        var actions = group.Actions;
        var content = new Rect(0f, 0f, listRect.width - 16f, actions.Count * ActionCellHeight + 4f);
        actionScroll = GUI.BeginScrollView(listRect, actionScroll, content);

        for (int i = 0; i < actions.Count; i++)
        {
            var slot = actions[i];
            if (slot == null) continue;
            var row = new Rect(2f, i * ActionCellHeight + 2f, content.width - 4f, ActionCellHeight - 3f);
            bool isFocus = focus.Kind == AGFocusKind.Action && ReferenceEquals(focus.ActionSlot, slot);
            AGStyles.Fill(row, isFocus ? AGStyles.LibraryCellFocused
                : i % 2 == 0 ? AGStyles.LibraryCell : AGStyles.LibraryCellAlt);
            AGStyles.Frame(row, isFocus ? AGStyles.Link : AGStyles.LibraryCellBorder);

            GUI.Label(new Rect(row.x + 5f, row.y + 5f, 12f, 18f), "≡", AGStyles.Tiny);

            bool disabled = AGReflect.GetDisabled(slot);
            bool enabled = !disabled;
            bool newEnabled = GUI.Toggle(new Rect(row.x + 20f, row.y + 6f, 16f, 16f), enabled, GUIContent.none);
            if (newEnabled != enabled) { AGReflect.SetDisabled(slot, !newEnabled); Invalidate(); }

            var focusOfRow = new AGFocus
            {
                Kind = AGFocusKind.Action,
                Timing = group.Timing,
                ActionList = actions,
                ActionIndex = i,
                ActionSlot = slot,
            };

            string typeName = AGFocus.ActionName(slot);
            string label = AGReflect.GetLabel(slot);
            string name = string.IsNullOrEmpty(label) ? typeName : label;
            GUI.Label(new Rect(row.x + 42f, row.y + 2f, row.width - 60f, 18f), name, AGStyles.RowLabel);

            if (HasActionIssue(focusOfRow, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 1) { ShowActionMenu(group, i); e.Use(); }
                else
                {
                    pendingActionFocus = focusOfRow;
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDrag && dragActionIndex < 0
                && pendingActionFocus != null && ReferenceEquals(pendingActionFocus.ActionSlot, slot))
                dragActionIndex = i;
            if (e.type == EventType.MouseDrag && dragActionIndex >= 0 && dragActionIndex < actions.Count)
            {
                int target = Mathf.Clamp(Mathf.FloorToInt(e.mousePosition.y / ActionCellHeight), 0, actions.Count - 1);
                if (target != dragActionIndex)
                {
                    var moved = actions[dragActionIndex];
                    actions.RemoveAt(dragActionIndex);
                    actions.Insert(target, moved);
                    dragActionIndex = target;
                    RefreshActionIndices(actions);
                    Invalidate();
                }
            }
            if (e.type == EventType.MouseUp && pendingActionFocus != null
                && ReferenceEquals(pendingActionFocus.ActionSlot, slot) && dragActionIndex < 0 && row.Contains(e.mousePosition))
            {
                var nextFocus = pendingActionFocus;
                pendingActionFocus = null;
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(nextFocus);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    private void ShowTimingMenu(List<AGTimingGroup> groups)
    {
        var menu = new GenericMenu();
        foreach (Enum timing in Enum.GetValues(model.TimingType))
        {
            AGTimingGroup group = null;
            foreach (var candidate in groups)
                if (Equals(candidate.Timing, timing)) { group = candidate; break; }

            int actionCount = group?.Actions?.Count ?? 0;
            int errors = 0;
            if (group?.Actions != null)
            {
                for (int i = 0; i < group.Actions.Count; i++)
                {
                    var f = new AGFocus
                    {
                        Kind = AGFocusKind.Action, Timing = group.Timing,
                        ActionList = group.Actions, ActionIndex = i, ActionSlot = group.Actions[i],
                    };
                    report.CountFor(f, out int e, out _);
                    errors += e;
                }
            }
            string countLabel = actionCount > 0 ? actionCount.ToString() : "+";
            string label = errors > 0
                ? $"{timing} ({countLabel})　{errors} 個錯誤"
                : $"{timing} ({countLabel})";
            var captured = timing;
            menu.AddItem(new GUIContent(label), Equals(currentTiming, timing), () =>
            {
                currentTiming = captured;
                SaveCurrentTiming();
                Repaint();
            });
        }
        menu.ShowAsContext();
    }

    private void AddEmptyAction(Enum timing)
    {
        var slotType = model.ActionSlotType;
        if (slotType == null || timing == null) return;
        var slot = AGReflect.CreateInstance(slotType);
        if (slot == null) return;

        model.BreakUndoMerge();
        var group = model.AddGroup(timing);
        if (group?.Actions == null) return;
        group.Actions.Add(slot);

        SetFocus(new AGFocus
        {
            Kind = AGFocusKind.Action, Timing = group.Timing,
            ActionList = group.Actions, ActionIndex = group.Actions.Count - 1, ActionSlot = slot,
        });
        Invalidate();
        Repaint();
    }

    private void ShowActionMenu(AGTimingGroup group, int index)
    {
        var menu = new GenericMenu();
        var slot = group.Actions[index];

        menu.AddItem(new GUIContent("設定標籤"), false, () =>
            AGPrompt.Show("動作標籤", "用來區分同名動作（例如：主傷害 / 濺射）", AGReflect.GetLabel(slot) ?? "", text =>
            {
                AGReflect.SetLabel(slot, text);
                Invalidate();
                Repaint();
            }));

        menu.AddItem(new GUIContent(AGReflect.GetDisabled(slot) ? "啟用" : "停用"), false, () =>
        {
            AGReflect.SetDisabled(slot, !AGReflect.GetDisabled(slot));
            Invalidate();
            Repaint();
        });

        menu.AddItem(new GUIContent("刪除"), false, () =>
        {
            RemoveAction(new AGFocus
            {
                Kind = AGFocusKind.Action, Timing = group.Timing,
                ActionList = group.Actions, ActionIndex = index, ActionSlot = slot,
            });
        });
        menu.ShowAsContext();
    }

    private void RemoveAction(AGFocus action)
    {
        if (action?.ActionList == null || action.ActionSlot == null) return;
        int index = IndexOfReference(action.ActionList, action.ActionSlot);
        if (index < 0) return;
        if (!EditorUtility.DisplayDialog("刪除動作", "確定刪除這個動作？", "刪除", "取消")) return;
        action.ActionList.RemoveAt(index);
        foreach (var group in model.ReadGroups())
        {
            if (!ReferenceEquals(group.Actions, action.ActionList)) continue;
            if (group.Actions.Count == 0) model.RemoveGroup(group);
            break;
        }
        SetFocus(new AGFocus());
        Invalidate();
        DoVerify(true);
        Repaint();
    }

    private void RefreshActionIndices(IList actions)
    {
        if (focus.Kind == AGFocusKind.Action && ReferenceEquals(focus.ActionList, actions))
            focus.ActionIndex = IndexOfReference(actions, focus.ActionSlot);
        if (pendingActionFocus != null && ReferenceEquals(pendingActionFocus.ActionList, actions))
            pendingActionFocus.ActionIndex = IndexOfReference(actions, pendingActionFocus.ActionSlot);
    }

    private static int IndexOfReference(IList list, object item)
    {
        if (list == null || item == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    // ===== 右欄（資產焦點）：引用清單 =====

    private void DrawReferencePanel(Rect r)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var asset = focus.AssetObject;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), "引用此資產的對象", AGStyles.PanelHeader);

        var subscribers = AGReflect.Get(asset, "_subscribers") as IList;
        int count = subscribers?.Count ?? 0;
        GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 16f),
            count > 0 ? $"共 {count} 個對象（清單可能不完整，可重建）" : "清單是空的，按下方重建掃描整個專案", AGStyles.Tiny);

        var listRect = new Rect(r.x + 2f, r.y + 40f, r.width - 4f, r.height - 92f);
        AGStyles.Fill(listRect, new Color(0.16f, 0.17f, 0.19f));

        var content = new Rect(0f, 0f, listRect.width - 16f, count * 24f + 4f);
        referenceScroll = GUI.BeginScrollView(listRect, referenceScroll, content);
        for (int i = 0; i < count; i++)
        {
            var so = subscribers[i] as ScriptableObject;
            var row = new Rect(0f, i * 24f, content.width, 23f);
            if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            if (so == null)
            {
                GUI.Label(new Rect(row.x + 4f, row.y + 3f, row.width - 8f, 17f), "（已遺失的對象）", AGStyles.RowLabelError);
                continue;
            }

            bool validated = so is IActionSystemOwner o && o.IsActionSystemValidated();
            GUI.Label(new Rect(row.x + 4f, row.y + 3f, row.width - 110f, 17f), so.name, AGStyles.RowLabel);
            GUI.Label(new Rect(row.xMax - 104f, row.y + 3f, 46f, 17f),
                validated ? "✓ 已驗證" : "✗ 未驗證", validated ? AGStyles.Tiny : AGStyles.RowLabelError);

            if (GUI.Button(new Rect(row.xMax - 54f, row.y + 3f, 50f, 17f), "切換"))
            {
                var target = so;
                if (ConfirmLeaveAsset())
                {
                    ExitAsset();
                    Bind(target);
                    EditorGUIUtility.PingObject(target);
                }
            }
        }
        GUI.EndScrollView();

        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 50f, r.width - 8f, 20f), "重建引用清單（掃描整個專案）"))
            RebuildReferences(asset);
        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 26f, r.width - 8f, 20f), "全部重新驗證"))
            VerifyAllSubscribers(asset);
    }

    /// <summary>掃描專案裡所有 Owner，重建這個資產的引用清單。只看磁碟上的內容，未存檔的修改不算。</summary>
    private void RebuildReferences(UnityEngine.Object asset)
    {
        var found = new List<ScriptableObject>();
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("重建引用清單", $"{i + 1}/{guids.Length}", (float)i / guids.Length))
                    break;

                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not IActionSystemOwner) continue;

                var field = AGModel.FindSystemField(so);
                var system = field?.GetValue(so);
                if (system == null) continue;

                foreach (var slot in AGModel.SlotsOfSystem(system))
                {
                    if (AGReflect.GetAsset(slot) != asset) continue;
                    found.Add(so);
                    break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        asset.GetType().GetMethod("ClearSubscribers")?.Invoke(asset, null);
        var register = asset.GetType().GetMethod("RegisterSubscriber");
        foreach (var so in found) register?.Invoke(asset, new object[] { so });

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"找到 {found.Count} 個引用"));
    }

    private void VerifyAllSubscribers(UnityEngine.Object asset)
    {
        if (AGReflect.Get(asset, "_subscribers") is not IList subscribers) return;
        int ok = 0, fail = 0;
        foreach (var s in subscribers)
        {
            if (s is not IActionSystemOwner owner) continue;
            owner.VerifyActionSystem();
            if (owner.IsActionSystemValidated()) ok++; else fail++;
            if (s is UnityEngine.Object so) EditorUtility.SetDirty(so);
        }
        ShowNotification(new GUIContent($"驗證完成：{ok} 通過 / {fail} 失敗"));
    }

    // ===== 中欄 =====

    private void DrawCenter(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Canvas);

        var header = new Rect(r.x, r.y, r.width, HeaderHeight);

        float consoleH = consoleCollapsed ? MinConsole : Mathf.Clamp(consoleHeight, MinConsole, r.height - HeaderHeight - 80f);
        canvasRect = new Rect(r.x, r.y + HeaderHeight, r.width, r.height - HeaderHeight - consoleH);
        var consoleRect = new Rect(r.x, canvasRect.yMax, r.width, consoleH);
        var consoleHandle = new Rect(consoleRect.x, consoleRect.y - 3f, consoleRect.width, ResizeHandleWidth);

        HandleConsoleResize(consoleHandle);

        DrawCanvas(canvasRect);
        DrawFocusHeader(header);
        DrawConsole(consoleRect);
        DrawResizeGrip(consoleHandle, false, resizingConsole);
    }

    private void DrawFocusHeader(Rect r)
    {
        AGStyles.Fill(r, new Color(0.22f, 0.23f, 0.26f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        if (focus.Kind == AGFocusKind.Asset)
        {
            var banner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 18f);
            AGStyles.Fill(banner, new Color(0.45f, 0.32f, 0.18f));
            GUI.Label(banner, "　共用資產：修改會影響所有引用它的對象。存檔是獨立的一次交易。", AGStyles.RowLabel);
            GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 18f),
                focus.AssetObject != null ? focus.AssetObject.name : "（資產遺失）", EditorStyles.boldLabel);
            return;
        }

        if (focus.Kind == AGFocusKind.Token && focus.Token != null)
        {
            DrawFocusName(r, focus.Token, focus.Token.Key, name =>
            {
                if (!model.RenameToken(focus.Token, name, out string error))
                {
                    EditorUtility.DisplayDialog("無法改名", error, "好");
                    return false;
                }
                Invalidate();
                return true;
            });
            GUI.Label(new Rect(r.x + 6f, r.y + 24f, r.width - 12f, 16f),
                $"型別 {focus.Token.TypeName}　被引用 {model.CountReferences(focus.Token)} 次", AGStyles.Tiny);
            return;
        }

        if (focus.Kind == AGFocusKind.Action && focus.ActionSlot != null)
        {
            DrawFocusName(r, focus.ActionSlot, focus.Title, name =>
            {
                AGReflect.SetLabel(focus.ActionSlot, name);
                Invalidate();
                return true;
            });
        }
        else GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 18f), focus.Title, EditorStyles.boldLabel);

        string desc = "";
        if (focus.Kind == AGFocusKind.Action && focus.ActionSlot != null)
        {
            var f = AGReflect.GetFormula(focus.ActionSlot);
            desc = f != null ? AGReflect.TypeDescription(f.GetType()) : "這個動作還沒有內容，請從根節點下拉選擇。";
            if (AGReflect.GetDisabled(focus.ActionSlot)) desc += "　（已停用，不會執行）";
        }
        else if (focus.Kind == AGFocusKind.None)
        {
            desc = "從右欄選一個動作，或從左欄選一個變數開始編輯。";
        }
        GUI.Label(new Rect(r.x + 6f, r.y + 24f, r.width - 12f, 16f), desc, AGStyles.Tiny);
    }

    /// <summary>焦點名稱平常只讀；按左側按鈕才進入編輯，再按一次才提交。</summary>
    private void DrawFocusName(Rect header, object target, string displayName, Func<string, bool> submit)
    {
        bool editing = ReferenceEquals(editingNameTarget, target);
        var editRect = new Rect(header.x + 4f, header.y + 3f, 20f, 20f);
        var nameRect = new Rect(editRect.xMax + 4f, header.y + 2f, header.width - 32f, 22f);
        if (GUI.Button(editRect, new GUIContent(editing ? "✓" : "✎", editing ? "提交名稱" : "編輯名稱"), EditorStyles.miniButton))
        {
            if (!editing)
            {
                editingNameTarget = target;
                editingNameDraft = displayName ?? "";
                GUI.FocusControl(null);
            }
            else if (submit(editingNameDraft.Trim()))
            {
                editingNameTarget = null;
                editingNameDraft = "";
            }
            Repaint();
        }

        if (editing)
        {
            GUI.SetNextControlName("actionGraphFocusName");
            editingNameDraft = EditorGUI.TextField(nameRect, editingNameDraft);
            EditorGUI.FocusTextInControl("actionGraphFocusName");
        }
        else GUI.Label(nameRect, displayName, AGStyles.FocusTitle);
    }

    // ===== 畫布 =====

    private void DrawCanvas(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Canvas);
        DrawGrid(r);

        var e = Event.current;
        Vector2 clipMouse = e.mousePosition - r.position;
        HandleLinkNavigation(e, clipMouse);
        Vector2 graphMouse = clipMouse / zoom - pan;
        bool mouseInCanvas = r.Contains(e.mousePosition);

        if (graph != null)
        {
            foreach (var node in graph.Nodes) UpdateRowGeometry(node, node.Rows);
            DrawLinks(graphMouse);
        }

        BeginZoomedCanvas(r);
        try
        {
            if (graph != null)
            {
                AGNode linkTarget = LinkTargetNode(graphMouse);
                foreach (var node in graph.Nodes) DrawNode(node, ReferenceEquals(node, linkTarget));
                if (boxSelecting)
                {
                    var box = BoxRect();
                    var visual = new Rect(box.position + pan, box.size);
                    AGStyles.Fill(visual, new Color(0.42f, 0.78f, 1f, 0.10f));
                    AGStyles.Frame(visual, AGStyles.Link);
                }
            }
        }
        finally
        {
            EndZoomedCanvas();
        }

        if (mouseInCanvas && !HandleAssetDrag(e, graphMouse)) HandleCanvasInput(e, graphMouse);
    }

    /// <summary>連線期間先更新視圖，讓同一事件的預覽端點仍精準對齊滑鼠。</summary>
    private void HandleLinkNavigation(Event e, Vector2 clipMouse)
    {
        if (!linking) return;
        if (e.type == EventType.ScrollWheel)
        {
            ZoomAt(clipMouse, e.delta.y);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            pan += e.delta / zoom;
            e.Use();
            Repaint();
        }
    }

    /// <summary>離開 EditorWindow 的隱式群組，建立不受外層 clip matrix 干擾的縮放畫布。</summary>
    private void BeginZoomedCanvas(Rect r)
    {
        Vector2 rootOffset = GUIUtility.GUIToScreenPoint(Vector2.zero) - position.position;
        rootGuiGroupRect = new Rect(rootOffset, position.size);

        GUI.EndGroup();

        var clippedArea = new Rect(
            r.x + rootOffset.x,
            r.y + rootOffset.y,
            r.width / zoom,
            r.height / zoom);
        GUI.BeginGroup(clippedArea);

        canvasGuiMatrix = GUI.matrix;
        var translation = Matrix4x4.TRS(clippedArea.position, Quaternion.identity, Vector3.one);
        var scale = Matrix4x4.Scale(new Vector3(zoom, zoom, 1f));
        GUI.matrix = translation * scale * translation.inverse * GUI.matrix;
    }

    /// <summary>結束縮放畫布並恢復 EditorWindow 原本的局部座標與裁切。</summary>
    private void EndZoomedCanvas()
    {
        GUI.matrix = canvasGuiMatrix;
        GUI.EndGroup();
        GUI.BeginGroup(rootGuiGroupRect);
    }

    private void DrawGrid(Rect r)
    {
        Handles.BeginGUI();
        float step = 20f * zoom;
        if (step > 4f)
        {
            Vector2 offset = new Vector2(pan.x * zoom % step, pan.y * zoom % step);
            Handles.color = AGStyles.Grid;
            for (float x = r.x + offset.x; x < r.xMax; x += step)
                Handles.DrawLine(new Vector3(x, r.y), new Vector3(x, r.yMax));
            for (float y = r.y + offset.y; y < r.yMax; y += step)
                Handles.DrawLine(new Vector3(r.x, y), new Vector3(r.xMax, y));
        }
        Handles.EndGUI();
    }

    /// <summary>連線在未縮放的視窗座標繪製，避免 GUI.matrix 旋轉造成起終點偏移。</summary>
    private void DrawLinks(Vector2 graphMouse)
    {
        Handles.BeginGUI();
        foreach (var link in graph.Links)
        {
            if (link.ParentRow == null || link.Target == null) continue;
            DrawGraphLine(link.ParentRow.PortPos, link.Target.OutputPort);
        }
        if (linking && (linkRow != null || linkNode != null))
        {
            Vector2 from = linkRow != null ? linkRow.PortPos : linkNode.OutputPort;
            DrawGraphLine(from, LinkPreviewEnd(graphMouse));
        }
        Handles.EndGUI();
    }

    private void DrawGraphLine(Vector2 graphFrom, Vector2 graphTo)
    {
        Vector2 from = canvasRect.position + (graphFrom + pan) * zoom;
        Vector2 to = canvasRect.position + (graphTo + pan) * zoom;
        if (!ClipLine(canvasRect, ref from, ref to)) return;

        Color oldColor = Handles.color;
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(LinkThickness, new Vector3(from.x, from.y), new Vector3(to.x, to.y));
        Handles.color = oldColor;
    }

    private static bool ClipLine(Rect rect, ref Vector2 from, ref Vector2 to)
    {
        Vector2 start = from;
        Vector2 delta = to - from;
        float min = 0f;
        float max = 1f;
        if (!ClipLineEdge(-delta.x, start.x - rect.xMin, ref min, ref max)
            || !ClipLineEdge(delta.x, rect.xMax - start.x, ref min, ref max)
            || !ClipLineEdge(-delta.y, start.y - rect.yMin, ref min, ref max)
            || !ClipLineEdge(delta.y, rect.yMax - start.y, ref min, ref max)) return false;
        from = start + delta * min;
        to = start + delta * max;
        return true;
    }

    private static bool ClipLineEdge(float direction, float distance, ref float min, ref float max)
    {
        if (Mathf.Approximately(direction, 0f)) return distance >= 0f;
        float ratio = distance / direction;
        if (direction < 0f)
        {
            if (ratio > max) return false;
            if (ratio > min) min = ratio;
        }
        else
        {
            if (ratio < min) return false;
            if (ratio < max) max = ratio;
        }
        return true;
    }

    private void DrawNode(AGNode node, bool isLinkTarget)
    {
        var rect = new Rect(node.Pos + pan, new Vector2(node.Width, node.Height));

        AGStyles.RoundedFill(rect, AGStyles.NodeBody, NodeCornerRadius);
        var header = new Rect(rect.x, rect.y, rect.width, 20f);
        Color headerColor = node.IsOrphan ? AGStyles.NodeHeaderOrphan
            : node.TokenKey != null ? AGStyles.NodeHeaderToken
            : node.IsAssetNode ? AGStyles.NodeHeaderAsset
            : node.IsActionNode
                ? node.IsRoot ? AGStyles.NodeHeaderRootAction : AGStyles.NodeHeaderAction
                : node.IsRoot ? AGStyles.NodeHeaderRootFormula : AGStyles.NodeHeaderFormula;
        AGStyles.RoundedTopFill(header, headerColor, NodeCornerRadius);
        float buttonX = rect.xMax - 4f;
        bool canEditTips = node.Obj != null;
        float titleInset = node.IsRoot ? 0f : AGGraph.PortDiameter + 2f;
        float titleWidth = Mathf.Max(24f, buttonX - rect.x - titleInset - 4f);
        GUI.Label(new Rect(rect.x + titleInset, rect.y, titleWidth, 20f), node.Title, AGStyles.NodeTitle);

        float y = rect.y + AGGraph.HeaderHeight;
        if (node.HasNodeTypeSelector)
        {
            var selector = new Rect(rect.x + 6f, y + 1f, rect.width - 12f, 18f);
            string label = node.IsPlaceholder
                ? node.IsActionNode
                    ? "選擇 Action 類型"
                    : $"選擇 {AGReflect.ResultTypeName(node.ResultType)} Formula"
                : node.ConcreteTypeName;
            if (EditorGUI.DropdownButton(selector, new GUIContent(label), FocusType.Keyboard))
                ShowNodeSourceSelector(node, selector);
            y += AGGraph.TypeSelectorHeight;
        }
        if (node.DescriptionHeight > 0f)
        {
            AGStyles.Fill(new Rect(rect.x, y, rect.width, node.DescriptionHeight), new Color(0f, 0f, 0f, 0.12f));
            GUI.Label(new Rect(rect.x, y, rect.width, node.DescriptionHeight), node.IsOrphan ? node.Desc + "　・候選" : node.Desc, AGStyles.NodeDesc);
            y += node.DescriptionHeight;
        }

        if (node.TokenKey != null)
        {
            var selector = new Rect(rect.x + 6f, rect.y + node.BodyStart + 1f, rect.width - 12f, 18f);
            string label = string.IsNullOrEmpty(node.TokenKey) ? "（選擇 Token）" : "@" + node.TokenKey;
            if (EditorGUI.DropdownButton(selector, new GUIContent(label), FocusType.Keyboard))
                ShowNodeSourceSelector(node, selector);
        }
        else if (node.IsAssetNode)
        {
            var selector = new Rect(rect.x + 6f, rect.y + node.BodyStart + 1f, rect.width - 12f, 18f);
            string label = node.Asset != null ? node.Asset.name : "（選擇 Asset）";
            if (EditorGUI.DropdownButton(selector, new GUIContent(label), FocusType.Keyboard))
                ShowNodeSourceSelector(node, selector);
        }
        else if (!node.IsPlaceholder)
        {
            DrawRows(node, node.Rows, rect);
        }

        if (canEditTips && node.TipsHeight > 0f)
        {
            var tipsLabel = new Rect(rect.x + 6f, rect.y + node.ContentHeight - node.TipsHeight - 4f, 36f, 16f);
            var tipsField = new Rect(tipsLabel.xMax, tipsLabel.y, rect.width - 48f, node.TipsHeight);
            var noteRect = new Rect(rect.x + 4f, tipsLabel.y - 3f, rect.width - 8f, node.TipsHeight + 6f);
            AGStyles.Fill(noteRect, AGStyles.NodeNote);
            AGStyles.Frame(noteRect, AGStyles.NodeNoteBorder);
            GUI.Label(tipsLabel, "NOTE", AGStyles.Tiny);
            EditorGUI.BeginChangeCheck();
            string tips = EditorGUI.TextArea(tipsField, node.Tips ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                model.SetNodeTips(node.Id, tips);
                Invalidate();
            }
        }

        // 節點層級的問題用徽章；參數列層級的問題直接把該列標紅。
        // 資產／變數／空節點自己沒有物件，問題掛在父欄位上，改查父欄位才看得到。
        object issueTarget = node.Obj ?? (node.IsAssetNode || node.TokenKey != null || node.IsPlaceholder
            ? node.ParentSlot : null);
        if (Rep.HasIssue(issueTarget, out bool nodeError))
        {
            var badge = new Rect(rect.xMax - 16f, rect.y + 4f, 12f, 12f);
            AGStyles.Fill(badge, nodeError ? AGStyles.Error : AGStyles.Warning);
            GUI.Label(badge, new GUIContent("", "此節點有問題，詳見 Console"));
        }

        bool selected = selectedIds.Contains(node.Id);
        // 拉線期間：可以接的 Node 整個亮外框，滑鼠實際吸到的那個再加粗。
        bool linkCandidate = linking && linkRow != null && linkCompatibleNodeIds.Contains(node.Id);
        Color borderColor = isLinkTarget ? AGStyles.Link
            : linkCandidate ? new Color(AGStyles.Link.r, AGStyles.Link.g, AGStyles.Link.b, 0.55f)
            : selected ? AGStyles.NodeBorderSelected : AGStyles.NodeBorder;
        AGStyles.RoundedFrame(rect, borderColor, NodeCornerRadius, isLinkTarget || selected ? 2f : linkCandidate ? 1.5f : 1f);
        DrawNodePorts(node, rect);
    }

    /// <summary>外框完成後最後畫接點；圓點完整位於 Node 內側。</summary>
    private void DrawNodePorts(AGNode node, Rect nodeRect)
    {
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.Slot) continue;
            var portRect = new Rect(nodeRect.xMax - AGGraph.PortDiameter,
                nodeRect.y + row.LocalY + row.Height * 0.5f - AGGraph.PortRadius,
                AGGraph.PortDiameter, AGGraph.PortDiameter);
            AGStyles.Port(portRect, SlotPortColor(row));
        }

        if (node.IsRoot) return;
        AGStyles.Port(new Rect(nodeRect.x, nodeRect.y + AGGraph.HeaderHeight * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter), AGStyles.PortLive);
    }

    private Color SlotPortColor(AGRow row)
    {
        // 從 Node 發點拉線時，收得下它的欄位接點先亮起來，使用者不用逐一試。
        if (linking && linkNode != null && linkCompatibleRows.Contains(row)) return AGStyles.Link;

        bool hasIssue = Rep.HasIssue(row.Slot, out bool isError);
        int useType = AGReflect.UseType(row.Slot);
        return hasIssue && isError ? AGStyles.PortError
            : useType == 3 ? AGStyles.PortToken
            : useType == 1 || useType == 2 ? AGStyles.PortLive
            : AGStyles.PortEmpty;
    }

    /// <summary>把每一列的圖面座標（命中測試與接點）更新成目前的節點位置。</summary>
    private static void UpdateRowGeometry(AGNode node, List<AGRow> rows)
    {
        foreach (var row in rows)
        {
            row.ScreenRect = new Rect(node.Pos.x, node.Pos.y + row.LocalY, node.Width, row.Height);
            row.PortPos = new Vector2(node.Pos.x + node.Width - AGGraph.PortRadius,
                node.Pos.y + row.LocalY + row.Height * 0.5f);
            UpdateRowGeometry(node, row.Children);
        }
    }

    private void DrawRows(AGNode node, List<AGRow> rows, Rect nodeRect)
    {
        foreach (var row in rows)
        {
            var rowRect = new Rect(nodeRect.x, nodeRect.y + row.LocalY, nodeRect.width, row.Height);
            if (rowRect.yMax > nodeRect.yMax) continue;
            if (row.IsListElement) DrawListElementControls(row, rowRect, nodeRect);

            switch (row.Kind)
            {
                case AGRowKind.Slot:
                    DrawSlotRow(row, rowRect);
                    break;
                case AGRowKind.Value:
                    DrawValueRow(row, rowRect);
                    break;
                case AGRowKind.Group:
                    if (!row.HideLabel)
                        GUI.Label(Indent(rowRect, row.Depth, row.IsListElement), new GUIContent(row.Label, AGReflect.FieldDescription(row.Field)), AGStyles.RowLabel);
                    DrawRows(node, row.Children, nodeRect);
                    break;
                case AGRowKind.List:
                    DrawListHeader(row, rowRect);
                    DrawRows(node, row.Children, nodeRect);
                    var addRect = new Rect(nodeRect.x, nodeRect.y + row.AddRowY, nodeRect.width, AGGraph.RowHeight);
                    if (addRect.yMax > nodeRect.yMax) break;
                    var addBtn = new Rect(addRect.x + 8f + row.Depth * AGGraph.IndentWidth, addRect.y + 2f, 60f, 15f);
                    bool fixedSize = row.List != null && row.List.IsFixedSize;
                    GUI.enabled = !fixedSize;
                    if (GUI.Button(addBtn, new GUIContent(fixedSize ? "固定長度" : "＋ 新增",
                            fixedSize ? "陣列長度固定，無法在這裡增刪項目" : null)))
                        AddListItem(row);
                    GUI.enabled = true;
                    break;
            }
        }
    }

    private static Rect Indent(Rect r, int depth) => Indent(r, depth, false);

    /// <summary>清單元素左側要留位置給拖曳把手與刪除鈕。</summary>
    private static Rect Indent(Rect r, int depth, bool listElement)
    {
        float left = 4f + depth * AGGraph.IndentWidth + (listElement ? ListControlWidth : 0f);
        return new Rect(r.x + left, r.y + 1f, r.width - left - 4f, r.height - 2f);
    }

    private const float ListControlWidth = 26f;

    /// <summary>清單元素的拖曳把手與刪除鈕，順便處理拖曳重排與右鍵插入。</summary>
    private void DrawListElementControls(AGRow row, Rect rowRect, Rect nodeRect)
    {
        var owner = row.ListOwner;
        if (owner?.List == null) return;

        float x = rowRect.x + 4f + row.Depth * AGGraph.IndentWidth;
        var handle = new Rect(x, rowRect.y + 2f, 12f, rowRect.height - 4f);
        var remove = new Rect(x + 13f, rowRect.y + 3f, 12f, rowRect.height - 6f);

        bool fixedSize = owner.List.IsFixedSize;
        bool dragging = ReferenceEquals(dragListRow, owner) && dragListIndex == row.ListIndex;
        GUI.Label(handle, new GUIContent("≡", fixedSize ? "陣列長度固定，不能重排" : "拖曳可調整順序"),
            dragging ? AGStyles.RowLabel : AGStyles.Tiny);

        GUI.enabled = !fixedSize;
        bool clickedRemove = GUI.Button(remove, new GUIContent("✕", fixedSize ? "陣列不能刪除項目" : "刪除這一項"), EditorStyles.label);
        GUI.enabled = true;
        if (clickedRemove)
        {
            model.BreakUndoMerge();
            owner.List.RemoveAt(row.ListIndex);
            Invalidate();
            return;
        }
        if (fixedSize) return;

        var e = Event.current;
        if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
        {
            dragListRow = owner;
            dragListIndex = row.ListIndex;
            e.Use();
        }
        else if (e.type == EventType.MouseDown && e.button == 1 && rowRect.Contains(e.mousePosition))
        {
            ShowListElementMenu(owner, row.ListIndex);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging)
        {
            int target = ListIndexAt(owner, nodeRect, e.mousePosition.y);
            if (target >= 0 && target != row.ListIndex)
            {
                model.BreakUndoMerge();
                var moved = owner.List[row.ListIndex];
                owner.List.RemoveAt(row.ListIndex);
                owner.List.Insert(target, moved);
                dragListIndex = target;
                Invalidate();
            }
            e.Use();
        }
    }

    /// <summary>依滑鼠 Y 算出要插到清單的第幾格。</summary>
    private static int ListIndexAt(AGRow owner, Rect nodeRect, float mouseY)
    {
        if (owner.Children.Count == 0) return -1;
        for (int i = 0; i < owner.Children.Count; i++)
        {
            var child = owner.Children[i];
            float mid = nodeRect.y + child.LocalY + child.Height * 0.5f;
            if (mouseY < mid) return i;
        }
        return owner.Children.Count - 1;
    }

    private void ShowListElementMenu(AGRow owner, int index)
    {
        var menu = new GenericMenu();
        if (owner.List != null && owner.List.IsFixedSize)
        {
            menu.AddDisabledItem(new GUIContent("陣列長度固定，無法增刪或重排"));
            menu.ShowAsContext();
            return;
        }

        menu.AddItem(new GUIContent("在此插入一項"), false, () =>
        {
            var item = owner.ElementType.IsValueType || owner.ElementType == typeof(string)
                ? DefaultOf(owner.ElementType)
                : AGReflect.CreateInstance(owner.ElementType);
            if (item == null && owner.ElementType != typeof(string)) return;
            model.BreakUndoMerge();
            owner.List.Insert(index, item);
            Invalidate();
            Repaint();
        });
        menu.AddItem(new GUIContent("往上移"), false, () => MoveListItem(owner, index, index - 1));
        menu.AddItem(new GUIContent("往下移"), false, () => MoveListItem(owner, index, index + 1));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("刪除這一項"), false, () =>
        {
            model.BreakUndoMerge();
            owner.List.RemoveAt(index);
            Invalidate();
            Repaint();
        });
        menu.ShowAsContext();
    }

    private void MoveListItem(AGRow owner, int from, int to)
    {
        if (owner?.List == null || owner.List.IsFixedSize) return;
        if (to < 0 || to >= owner.List.Count || from == to) return;
        model.BreakUndoMerge();
        var item = owner.List[from];
        owner.List.RemoveAt(from);
        owner.List.Insert(to, item);
        Invalidate();
        Repaint();
    }

    private void DrawSlotRow(AGRow row, Rect rowRect)
    {
        var slot = row.Slot;
        int useType = AGReflect.UseType(slot);
        bool hasIssue = Rep.HasIssue(slot, out bool isError);

        var labelRect = Indent(new Rect(rowRect.x, rowRect.y, rowRect.width * 0.42f, rowRect.height), row.Depth, row.IsListElement);
        if (!row.HideLabel)
            GUI.Label(labelRect, new GUIContent(row.Label, AGReflect.FieldDescription(row.Field)), hasIssue && isError ? AGStyles.RowLabelError : AGStyles.RowLabel);

        float portInset = AGGraph.PortDiameter + 10f;
        var fieldRect = row.HideLabel
            ? new Rect(rowRect.x + 4f, rowRect.y + 1f, rowRect.width - portInset - 4f, rowRect.height - 3f)
            : new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f,
                rowRect.width * 0.58f - portInset, rowRect.height - 3f);

        if (row.IsActionSlot)
        {
            string text = useType switch
            {
                1 => AGReflect.GetFormula(slot) is object f ? AGReflect.TypeName(f.GetType()) : "（空）",
                2 => AGReflect.GetAsset(slot) is UnityEngine.Object a ? a.name : "（空資產）",
                _ => "（未啟用，從接點拉線指定動作）",
            };
            GUI.Label(fieldRect, text, AGStyles.Tiny);
        }
        else
        {
            // 常數框永遠在、永遠可編輯。接了公式／資產／變數時它是解析失敗的保底值，只是視覺上轉灰。
            string tooltip = useType switch
            {
                1 => "已接公式：公式解析失敗時回到這個值",
                2 => "已接資產：資產缺內容時回到這個值",
                3 => "已接變數：變數不存在或循環時回到這個值",
                _ => null,
            };
            EditorGUI.BeginChangeCheck();
            var value = useType == 0
                ? AGValueField.Draw(fieldRect, row.ResultType, AGReflect.GetDefault(slot), row.IsEnum)
                : AGValueField.DrawMuted(fieldRect, row.ResultType, AGReflect.GetDefault(slot), tooltip, row.IsEnum);
            if (EditorGUI.EndChangeCheck()) { AGReflect.SetDefault(slot, value); Invalidate(); }
        }

        var portRect = new Rect(rowRect.xMax - AGGraph.PortDiameter,
            rowRect.y + rowRect.height * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter);
        var e = Event.current;
        if (e.type == EventType.MouseDown)
        {
            if (e.button == 0 && portRect.Contains(e.mousePosition)) { BeginLinkFromRow(row); e.Use(); }
            else if (e.button == 1 && rowRect.Contains(e.mousePosition)) { ShowSlotMenu(row); e.Use(); }
        }
    }

    private void DrawValueRow(AGRow row, Rect rowRect)
    {
        var labelRect = Indent(new Rect(rowRect.x, rowRect.y, rowRect.width * 0.42f, rowRect.height), row.Depth, row.IsListElement);
        if (!row.HideLabel)
            GUI.Label(labelRect, new GUIContent(row.Label, AGReflect.FieldDescription(row.Field)), AGStyles.RowLabel);

        var fieldRect = row.HideLabel
            ? new Rect(rowRect.x + 4f, rowRect.y + 1f, rowRect.width - 8f, rowRect.height - 3f)
            : new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f, rowRect.width * 0.58f - 20f, rowRect.height - 3f);

        if (row.Field != null && row.Target != null)
        {
            EditorGUI.BeginChangeCheck();
            var value = AGValueField.Draw(fieldRect, row.Field.FieldType, row.Field.GetValue(row.Target), row.IsEnum);
            if (EditorGUI.EndChangeCheck()) { row.Field.SetValue(row.Target, value); Invalidate(); }
            return;
        }

        // 清單裡的基本型別元素沒有 FieldInfo，改用「清單 + 索引」寫回。
        var owner = row.ListOwner;
        if (!row.IsListElement || owner?.List == null || owner.ElementType == null) return;
        if (row.ListIndex < 0 || row.ListIndex >= owner.List.Count) return;

        EditorGUI.BeginChangeCheck();
        var element = AGValueField.Draw(fieldRect, owner.ElementType, owner.List[row.ListIndex]);
        if (EditorGUI.EndChangeCheck()) { owner.List[row.ListIndex] = element; Invalidate(); }
    }

    private void DrawListHeader(AGRow row, Rect rowRect)
    {
        var labelRect = Indent(rowRect, row.Depth, row.IsListElement);
        GUI.Label(labelRect, $"{row.Label}（{row.List?.Count ?? 0} 項，序號即執行順序）", AGStyles.RowLabel);
    }

    private void AddListItem(AGRow row)
    {
        if (row.List == null || row.ElementType == null || row.List.IsFixedSize) return;
        // 基本型別沒有「空實例」的問題，用 default 值；其餘走無參數建構。
        var item = row.ElementType.IsValueType || row.ElementType == typeof(string)
            ? DefaultOf(row.ElementType)
            : AGReflect.CreateInstance(row.ElementType);
        if (item == null && row.ElementType != typeof(string)) return;
        model.BreakUndoMerge();
        row.List.Add(item);
        Invalidate();
    }

    private static object DefaultOf(Type t)
        => t == typeof(string) ? "" : Activator.CreateInstance(t);

    // ===== 畫布互動 =====

    private void HandleCanvasInput(Event e, Vector2 graphMouse)
    {
        switch (e.type)
        {
            case EventType.ScrollWheel:
                ZoomAt(e.mousePosition - canvasRect.position, e.delta.y);
                e.Use();
                break;

            case EventType.MouseDown:
                if (e.button == 0 && !e.alt && OutputNodeAt(graphMouse) is AGNode outputNode)
                {
                    BeginLinkFromNode(outputNode);
                    e.Use();
                    break;
                }
                var hit = NodeAt(graphMouse);
                if (e.button == 1)
                {
                    if (hit != null) ShowNodeMenu(hit);
                    else ShowCanvasMenu(graphMouse);
                    e.Use();
                }
                else if (e.button == 0 && !e.alt)
                {
                    if (e.clickCount == 2 && hit != null && hit.TokenKey != null) { FocusToken(hit); e.Use(); break; }
                    if (e.clickCount == 2 && hit != null && hit.IsAssetNode && hit.Asset != null) { EnterAsset(hit); e.Use(); break; }

                    var link = LinkAt(graphMouse);
                    if (link != null) { CutLink(link); e.Use(); break; }

                    if (hit == null)
                    {
                        if (!e.control && !e.shift) selectedIds.Clear();
                        boxSelecting = true;
                        boxStart = boxEnd = graphMouse;
                        GUI.FocusControl(null);
                        e.Use();
                        break;
                    }

                    if (e.control || e.shift)
                    {
                        if (!selectedIds.Add(hit.Id)) selectedIds.Remove(hit.Id);
                    }
                    else if (!selectedIds.Contains(hit.Id))
                    {
                        selectedIds.Clear();
                        selectedIds.Add(hit.Id);
                    }

                    if (new Rect(hit.Pos.x, hit.Pos.y, hit.Width, 20f).Contains(graphMouse))
                    {
                        dragNode = hit;
                        dragOffset = graphMouse - hit.Pos;
                        dragStartPositions.Clear();
                        foreach (var n in graph.Nodes)
                            if (selectedIds.Contains(n.Id)) dragStartPositions[n.Id] = n.Pos;
                    }
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (dragNode != null)
                {
                    // 多選時整組一起搬：以主拖曳節點的位移量套用到其他被選節點。
                    Vector2 target = SnapToGrid(graphMouse - dragOffset);
                    Vector2 delta = target - (dragStartPositions.TryGetValue(dragNode.Id, out var start) ? start : dragNode.Pos);
                    foreach (var n in graph.Nodes)
                    {
                        if (!dragStartPositions.TryGetValue(n.Id, out var origin)) continue;
                        n.Pos = origin + delta;
                    }
                    dragNode.Pos = target;
                    e.Use();
                }
                else if (boxSelecting)
                {
                    boxEnd = graphMouse;
                    e.Use();
                }
                else if (e.button == 2 || (e.button == 0 && e.alt))
                {
                    pan += e.delta / zoom;
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                if (dragNode != null)
                {
                    model.BreakUndoMerge();
                    foreach (var n in graph.Nodes)
                        if (dragStartPositions.ContainsKey(n.Id)) model.SetPosition(n.Id, n.Pos);
                    model.SetPosition(dragNode.Id, dragNode.Pos);
                    dragStartPositions.Clear();
                    dragNode = null;
                    e.Use();
                }
                if (boxSelecting)
                {
                    var box = BoxRect();
                    foreach (var n in graph.Nodes)
                        if (box.Overlaps(n.Rect)) selectedIds.Add(n.Id);
                    boxSelecting = false;
                    e.Use();
                }
                if (linking && e.button == 0)
                {
                    if (linkRow != null) ResolveLink(graphMouse);
                    else ResolveLinkFromOutput(graphMouse);
                    EndLink();
                    e.Use();
                }
                if (dragTokenActive && dragToken != null)
                {
                    DropTokenOn(graphMouse);
                    dragTokenActive = false;
                    dragToken = null;
                    e.Use();
                }
                if (dragAssetActive && dragAsset != null)
                {
                    DropAssetOn(graphMouse);
                    dragAssetActive = false;
                    dragAsset = null;
                    pendingAssetFocus = null;
                    e.Use();
                }
                break;

            case EventType.KeyDown:
                if (e.keyCode == KeyCode.Delete) { DeleteSelection(); e.Use(); }
                else if (e.keyCode == KeyCode.F && !e.control) { FrameAll(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.C) { CopySelection(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.V) { PasteClipboard(graphMouse); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.A)
                {
                    selectedIds.Clear();
                    foreach (var n in graph.Nodes) selectedIds.Add(n.Id);
                    e.Use();
                }
                break;
        }
    }

    private Rect BoxRect()
    {
        return new Rect(
            Mathf.Min(boxStart.x, boxEnd.x), Mathf.Min(boxStart.y, boxEnd.y),
            Mathf.Abs(boxEnd.x - boxStart.x), Mathf.Abs(boxEnd.y - boxStart.y));
    }

    // ===== 選取、複製、刪除 =====

    private IEnumerable<AGNode> SelectedNodes()
    {
        if (graph == null) yield break;
        foreach (var n in graph.Nodes)
            if (selectedIds.Contains(n.Id)) yield return n;
    }

    private void DeleteSelection()
    {
        var targets = new List<AGNode>(SelectedNodes());
        if (targets.Count == 0) return;
        model.BreakUndoMerge();
        foreach (var n in targets) DeleteNode(n, false);
        selectedIds.Clear();
        Invalidate();
    }

    /// <summary>複製選取節點的子樹。資產／變數節點只是引用，不列入。</summary>
    private void CopySelection()
    {
        clipboard.Clear();
        foreach (var n in SelectedNodes())
        {
            if (n.Obj is not ActionSystemNode node) continue;
            var clone = node.EditorClone();
            if (clone == null) continue;
            AGModel.ResetNodeIds(clone);
            clipboard.Add(clone);
        }
        ShowNotification(new GUIContent(clipboard.Count > 0 ? $"已複製 {clipboard.Count} 個節點" : "沒有可複製的節點"));
    }

    private void PasteClipboard(Vector2 graphMouse)
    {
        if (clipboard.Count == 0) { ShowNotification(new GUIContent("剪貼簿是空的")); return; }
        model.BreakUndoMerge();
        selectedIds.Clear();

        float offset = 0f;
        foreach (var item in clipboard)
        {
            if (item is not ActionSystemNode source) continue;
            var clone = source.EditorClone();
            if (clone == null) continue;
            AGModel.ResetNodeIds(clone);

            // 貼上＝新載體包新內容，落在目前焦點的候選池。
            var carrier = new GraphNode(clone);
            carrier.EnsureId();
            carrier.Pos = graphMouse + new Vector2(offset, offset);
            model.AddOrphan(carrier);
            selectedIds.Add(carrier.Id);
            offset += 24f;
        }
        Invalidate();
        Repaint();
    }

    private AGNode NodeAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
            if (graph.Nodes[i].Rect.Contains(graphPoint)) return graph.Nodes[i];
        return null;
    }

    private AGNode OutputNodeAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (node.IsRoot) continue;
            var port = new Rect(node.OutputPort - Vector2.one * AGGraph.PortRadius,
                Vector2.one * AGGraph.PortDiameter);
            if (port.Contains(graphPoint)) return node;
        }
        return null;
    }

    private void BeginLinkFromRow(AGRow row)
    {
        linking = true;
        linkRow = row;
        linkNode = null;
        RebuildLinkCompatibility();
    }

    private void BeginLinkFromNode(AGNode node)
    {
        linking = true;
        linkRow = null;
        linkNode = node;
        RebuildLinkCompatibility();
    }

    private void EndLink()
    {
        linking = false;
        linkRow = null;
        linkNode = null;
        linkCompatibleNodeIds.Clear();
        linkCompatibleRows.Clear();
    }

    /// <summary>
    /// 拉線起手時把整張圖判定一次：從欄位拉出去就標出所有能當來源的 Node，從 Node 拉出去就標出所有收得下它的欄位。
    /// 判定結果整段拖曳期間不變（拖曳不改資料），所以算一次就夠，比原本每幀重算便宜。
    /// </summary>
    private void RebuildLinkCompatibility()
    {
        linkCompatibleNodeIds.Clear();
        linkCompatibleRows.Clear();
        if (graph == null) return;

        if (linkRow != null)
        {
            foreach (var node in graph.Nodes)
                if (!node.IsRoot && CanConnectLink(linkRow, node)) linkCompatibleNodeIds.Add(node.Id);
            return;
        }

        if (linkNode == null) return;
        foreach (var node in graph.Nodes)
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (row.Kind == AGRowKind.Slot && CanConnectLink(row, linkNode)) linkCompatibleRows.Add(row);
    }

    /// <summary>本次拉線中直接查快取；不在拉線中（例如放開瞬間的重算）才實算。</summary>
    private bool CanLinkTo(AGRow row, AGNode node)
        => linking && ReferenceEquals(row, linkRow)
            ? linkCompatibleNodeIds.Contains(node.Id)
            : CanConnectLink(row, node);

    private bool CanLinkFrom(AGRow row, AGNode node)
        => linking && ReferenceEquals(node, linkNode)
            ? linkCompatibleRows.Contains(row)
            : CanConnectLink(row, node);

    private AGNode LinkTargetNode(Vector2 graphMouse)
    {
        if (!linking) return null;
        if (linkRow != null) return SnappedOutputNode(graphMouse, linkRow);
        return linkNode != null ? OwnerOfRow(SnappedInputRow(graphMouse, linkNode)) : null;
    }

    private Vector2 LinkPreviewEnd(Vector2 graphMouse)
    {
        if (linkRow != null)
        {
            var target = SnappedOutputNode(graphMouse, linkRow);
            return target != null ? target.OutputPort : graphMouse;
        }

        var row = SnappedInputRow(graphMouse, linkNode);
        return row != null ? row.PortPos : graphMouse;
    }

    private AGNode SnappedOutputNode(Vector2 graphMouse, AGRow row)
    {
        if (graph == null || row == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (node.IsRoot || !node.Rect.Contains(graphMouse) || !CanLinkTo(row, node)) continue;
            return node;
        }

        float maxDistanceSqr = LinkSnapDistance * LinkSnapDistance / (zoom * zoom);
        float nearestDistanceSqr = maxDistanceSqr;
        AGNode nearest = null;
        foreach (var node in graph.Nodes)
        {
            if (node.IsRoot || !CanLinkTo(row, node)) continue;
            float distanceSqr = (node.OutputPort - graphMouse).sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr) continue;
            nearestDistanceSqr = distanceSqr;
            nearest = node;
        }
        return nearest;
    }

    private AGRow SnappedInputRow(Vector2 graphMouse, AGNode node)
    {
        if (graph == null || node == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var owner = graph.Nodes[i];
            if (!owner.Rect.Contains(graphMouse)) continue;

            AGRow nearestInNode = null;
            float nearestY = float.MaxValue;
            foreach (var row in AGGraph.AllRows(owner.Rows))
            {
                if (row.Kind != AGRowKind.Slot || !CanLinkFrom(row, node)) continue;
                float distanceY = Mathf.Abs(row.ScreenRect.center.y - graphMouse.y);
                if (distanceY >= nearestY) continue;
                nearestY = distanceY;
                nearestInNode = row;
            }
            if (nearestInNode != null) return nearestInNode;
        }

        float maxDistanceSqr = LinkSnapDistance * LinkSnapDistance / (zoom * zoom);
        float nearestDistanceSqr = maxDistanceSqr;
        AGRow nearest = null;
        foreach (var owner in graph.Nodes)
        {
            foreach (var row in AGGraph.AllRows(owner.Rows))
            {
                if (row.Kind != AGRowKind.Slot || !CanLinkFrom(row, node)) continue;
                float distanceSqr = (row.PortPos - graphMouse).sqrMagnitude;
                if (distanceSqr > nearestDistanceSqr) continue;
                nearestDistanceSqr = distanceSqr;
                nearest = row;
            }
        }
        return nearest;
    }

    private AGNode OwnerOfRow(AGRow target)
    {
        if (graph == null || target == null) return null;
        foreach (var node in graph.Nodes)
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (ReferenceEquals(row, target)) return node;
        return null;
    }

    private AGRow RowAt(Vector2 graphPoint, out AGNode owner)
    {
        owner = null;
        if (graph == null) return null;
        foreach (var node in graph.Nodes)
        {
            if (!node.Rect.Contains(graphPoint)) continue;
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (row.Kind == AGRowKind.Slot && row.ScreenRect.Contains(graphPoint)) { owner = node; return row; }
        }
        return null;
    }

    /// <summary>找滑鼠附近的直線連線。</summary>
    private AGLink LinkAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        foreach (var link in graph.Links)
        {
            if (link.ParentRow == null || link.Target == null) continue;
            Vector2 a = link.ParentRow.PortPos;
            Vector2 b = link.Target.OutputPort;
            if (PointToSegmentSqrDistance(graphPoint, a, b) < 36f) return link;
        }
        return null;
    }

    private static float PointToSegmentSqrDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.01f) return (point - from).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSqr);
        return (point - (from + segment * t)).sqrMagnitude;
    }

    /// <summary>切線：父欄位改回常數／空槽，被切下來的來源留成候選。</summary>
    private void CutLink(AGNode node)
    {
        CutLink(node?.ParentSlot);
    }

    private void CutLink(AGLink link)
    {
        CutLink(link?.ParentRow?.Slot);
    }

    private void CutLink(object slot)
    {
        if (slot == null) return;
        PreserveVisibleNodePositions();
        AttachSource(slot, null);
        Invalidate();
    }

    /// <summary>
    /// 換掉欄位接的來源載體。舊載體若沒有其他欄位在用，原地留成候選——完整子樹與座標都跟著載體走。
    /// </summary>
    private void AttachSource(object slot, GraphNode next)
    {
        if (slot == null) return;
        var old = AGReflect.GetNode(slot);
        if (ReferenceEquals(old, next)) return;

        AGReflect.SetNode(slot, next);
        if (old != null && !IsCarrierUsed(old)) model.AddOrphan(old);
        if (next != null)
        {
            next.EnsureId();
            model.RemoveOrphan(next);
        }
    }

    /// <summary>還有沒有別的欄位指著這個載體（共用來源）。</summary>
    private bool IsCarrierUsed(GraphNode carrier)
    {
        if (carrier == null) return false;
        foreach (var slot in model.AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), carrier)) return true;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null)
            return ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), carrier);
        return false;
    }

    /// <summary>建立新的空載體並接上欄位；使用者接著在節點上選具體來源。</summary>
    private GraphNode NewSource(object slot)
    {
        var carrier = new GraphNode();
        carrier.EnsureId();
        AttachSource(slot, carrier);
        return carrier;
    }

    private void ResolveLink(Vector2 graphMouse)
    {
        if (linkRow?.Slot == null) return;
        var target = SnappedOutputNode(graphMouse, linkRow);
        if (TryConnectLink(linkRow, target)) return;

        // 空白處放開：先建立空 Node，讓使用者在 Node 上決定具體型別。
        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        NewSource(linkRow.Slot).Pos = graphMouse;
        Invalidate();
        Repaint();
    }

    private void ResolveLinkFromOutput(Vector2 graphMouse)
    {
        var row = SnappedInputRow(graphMouse, linkNode);
        if (row == null || !TryConnectLink(row, linkNode))
            ShowNotification(new GUIContent("請拖到相容的參數接點"));
    }

    /// <summary>接線＝欄位指到那個節點的載體。Token／資產節點因此天然可以被多個欄位共用。</summary>
    private bool TryConnectLink(AGRow row, AGNode target)
    {
        if (row?.Slot == null || target?.Carrier == null) return false;

        if (!CanConnectLink(row, target))
        {
            ShowNotification(new GUIContent("型別或連線關係不符"));
            return true;
        }

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        AttachSource(row.Slot, target.Carrier);
        Invalidate();
        return true;
    }

    private static bool CanConnectLink(AGRow row, AGNode target)
    {
        if (row?.Slot == null || target?.Carrier == null) return false;
        if (target.TokenKey != null)
            return !row.IsActionSlot && target.ResultType == row.ResultType;
        if (target.IsAssetNode)
            return CanAssignAsset(row, target.Asset);
        if (target.Obj == null) return false;

        object slot = row.Slot;
        Type accepted = row.IsActionSlot
            ? AGReflect.ActionBaseType(slot.GetType())
            : AGReflect.FormulaBaseType(slot.GetType());
        if (accepted == null || !accepted.IsInstanceOfType(target.Obj)) return false;
        // 內嵌節點不能從別的欄位手上搶走；共用只開放給 Token 與資產這種引用型來源。
        if (target.ParentSlot != null && !ReferenceEquals(target.ParentSlot, slot)) return false;
        return !WouldCreateCycle(slot, target.Obj);
    }

    private static bool WouldCreateCycle(object slot, object node)
    {
        foreach (var childSlot in AGModel.WalkSlots(node, new HashSet<object>(AGRefComparer.Instance)))
            if (ReferenceEquals(childSlot, slot)) return true;
        return false;
    }

    /// <summary>把一個新建立的具體 Action／Formula 接到欄位（右鍵「指定公式」等入口）。</summary>
    private void Connect(object slot, object node)
    {
        if (node is not ActionSystemNode body) return;
        PreserveVisibleNodePositions();
        NewSource(slot).SetBody(body);
        Invalidate();
    }

    private void DropTokenOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddTokenReferenceNode(dragToken, graphMouse);
            return;
        }
        if (row.IsActionSlot)
        {
            ShowNotification(new GUIContent("只能拖到參數欄位上"));
            return;
        }
        if (row.ResultType != dragToken.ResultType)
        {
            ShowNotification(new GUIContent($"型別不符：這個欄位是 {AGReflect.ResultTypeName(row.ResultType)}"));
            return;
        }
        AssignToken(row.Slot, dragToken.Key);
    }

    private void DropAssetOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddAssetReferenceNode(dragAsset, graphMouse);
            return;
        }
        if (!CanAssignAsset(row, dragAsset))
        {
            ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
            return;
        }
        AssignAsset(row.Slot, dragAsset);
    }

    /// <summary>把左欄 Token 拖到空白畫布：建立一個沒有連線的變數載體，放進候選池等人來接。</summary>
    private void AddTokenReferenceNode(AGToken token, Vector2 graphMouse)
    {
        if (token == null) return;
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetToken(token.Key);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>把 Project 的共用資產拖到空白畫布：同樣建立一個候選載體。</summary>
    private void AddAssetReferenceNode(UnityEngine.Object asset, Vector2 graphMouse)
    {
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        if (asset is not ScriptableObject so)
        {
            ShowNotification(new GUIContent("資產尚未存入 Project，無法建立節點"));
            return;
        }
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetAsset(so);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>處理從 Project 拖進來的公式／動作資產；落在參數列就直接接上，空白處就建立來源節點。</summary>
    private bool HandleAssetDrag(Event e, Vector2 graphMouse)
    {
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return false;

        UnityEngine.Object asset = null;
        foreach (var candidate in DragAndDrop.objectReferences)
        {
            if (candidate is not ScriptableObject so || !IsSharedAsset(so)) continue;
            asset = so;
            break;
        }
        if (asset == null) return false;

        var row = RowAt(graphMouse, out _);
        bool canAssign = row != null && CanAssignAsset(row, asset);
        bool canCreate = row == null && CanCreateReferenceNode();
        DragAndDrop.visualMode = canAssign || canCreate ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        if (e.type == EventType.DragUpdated)
        {
            e.Use();
            return true;
        }

        if (canAssign) AssignAsset(row.Slot, asset);
        else if (canCreate) AddAssetReferenceNode(asset, graphMouse);
        else if (row == null) ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
        else ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
        DragAndDrop.AcceptDrag();
        e.Use();
        return true;
    }

    // 有頭端才有候選池可放；資產焦點的頭端是資產本身，同樣有一份。
    private bool CanCreateReferenceNode() => focus.Head != null && focus.RootSlot != null;

    private static bool CanAssignAsset(AGRow row, UnityEngine.Object asset)
    {
        if (row?.Slot == null || asset == null) return false;
        Type accepted = row.IsActionSlot
            ? AGReflect.ActionAssetType(row.Slot.GetType())
            : AGReflect.AssetType(row.Slot.GetType());
        return accepted != null && accepted.IsInstanceOfType(asset);
    }

    private void ShowNodeSourceSelector(AGNode node, Rect selector)
    {
        if (node == null) return;
        var options = new List<AGSourceOption>();
        object slot = SourceSlot(node);
        bool isAction = slot != null
            ? AGReflect.IsActionSlotType(slot.GetType())
            : node.IsActionNode || (node.IsAssetNode && node.ResultType == null);

        Type baseType = node.ParentSlot != null
            ? (isAction
                ? AGReflect.ActionBaseType(node.ParentSlot.GetType())
                : AGReflect.FormulaBaseType(node.ParentSlot.GetType()))
            : AGReflect.NodeBaseType(node.Obj?.GetType());
        if (baseType != null)
        {
            string kind = isAction ? "Action" : "Formula";
            foreach (var type in AGTypeCatalog.Concrete(baseType))
            {
                Type captured = type;
                options.Add(new AGSourceOption
                {
                    Group = kind + "/" + AGReflect.TypeCategory(type),
                    Name = AGReflect.TypeName(type),
                    IsCurrent = node.Obj?.GetType() == type,
                    Apply = () => ReplaceNodeType(node, captured),
                });
            }
        }

        Type resultType = !isAction
            ? (slot != null ? AGReflect.ResultType(slot.GetType()) : node.ResultType)
            : null;
        bool canUseReferenceSources = node.ParentSlot != null || node.Obj == null;
        if (canUseReferenceSources && !isAction && resultType != null)
        {
            foreach (var token in model.ReadTokens())
            {
                if (token.ResultType != resultType || string.IsNullOrWhiteSpace(token.Key)) continue;
                string key = token.Key;
                options.Add(new AGSourceOption
                {
                    Group = "Token",
                    Name = key,
                    IsCurrent = node.TokenKey == key,
                    Apply = () => ChangeNodeToToken(node, key),
                });
            }
        }

        if (canUseReferenceSources)
        {
            foreach (var entry in AGAssetIndex.Entries)
            {
                if (entry.Asset == null || !CanReplaceAssetNode(node, entry.Asset)) continue;
                var asset = entry.Asset;
                options.Add(new AGSourceOption
                {
                    Group = "Asset",
                    Name = entry.Name,
                    IsCurrent = node.Asset == asset,
                    Apply = () => ChangeNodeToAsset(node, asset),
                });
            }
        }

        AGTypeCatalog.ShowSourcePicker(selector, options);
    }

    private object SourceSlot(AGNode node)
    {
        if (node?.ParentSlot != null) return node.ParentSlot;
        if (graph?.Links == null) return null;
        foreach (var link in graph.Links)
            if (ReferenceEquals(link.Target, node) && link.ParentRow?.Slot != null)
                return link.ParentRow.Slot;
        return null;
    }

    /// <summary>換節點型別＝換載體裡的內容。載體 Id、座標、備註與所有連入邊都不動，這才是真的「替換」。</summary>
    private void ReplaceNodeType(AGNode node, Type type)
    {
        if (node?.Carrier == null || type == null || node.Obj?.GetType() == type) return;
        if (AGReflect.CreateInstance(type) is not ActionSystemNode instance) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetBody(instance);
        Invalidate();
        Repaint();
    }

    private bool CanReplaceAssetNode(AGNode node, ScriptableObject asset)
    {
        if (node?.ParentSlot != null)
        {
            Type slotType = node.ParentSlot.GetType();
            Type accepted = AGReflect.IsActionSlotType(slotType)
                ? AGReflect.ActionAssetType(slotType)
                : AGReflect.AssetType(slotType);
            return accepted != null && accepted.IsInstanceOfType(asset);
        }

        bool hasLink = false;
        if (graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.Target, node)) continue;
                hasLink = true;
                if (!CanAssignAsset(link.ParentRow, asset)) return false;
            }
        }
        if (hasLink) return true;

        Type acceptedType = AcceptedAssetType(node);
        return acceptedType != null && acceptedType.IsInstanceOfType(asset);
    }

    private Type AcceptedAssetType(AGNode node)
    {
        Type slotType = node.ParentSlot?.GetType();
        if (slotType == null && graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.Target, node) || link.ParentRow?.Slot == null) continue;
                slotType = link.ParentRow.Slot.GetType();
                break;
            }
        }
        if (slotType == null && node.Asset is ScriptableObject asset)
            slotType = SlotTypeForAsset(asset, AssetSlotTypes());
        if (slotType == null && node.ResultType != null)
        {
            foreach (var (resultType, list) in model.TokenKinds())
            {
                if (resultType != node.ResultType) continue;
                Type entryType = list.GetType().GetGenericArguments()[0];
                slotType = (AGReflect.CreateInstance(entryType) as ITokenEntry)?.Slot?.GetType();
                break;
            }
        }
        if (slotType == null) return null;
        return AGReflect.IsActionSlotType(slotType)
            ? AGReflect.ActionAssetType(slotType)
            : AGReflect.AssetType(slotType);
    }

    /// <summary>節點改接變數。節點是共用來源時，所有指著它的欄位一起改——這正是共用的語意。</summary>
    private void ChangeNodeToToken(AGNode node, string key)
    {
        if (node?.Carrier == null || string.IsNullOrWhiteSpace(key) || node.TokenKey == key) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetToken(key);
        Invalidate();
        Repaint();
    }

    private void ChangeNodeToAsset(AGNode node, ScriptableObject asset)
    {
        if (node?.Carrier == null || asset == null || node.Asset == asset) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetAsset(asset);
        Invalidate();
        Repaint();
    }

    /// <summary>換掉節點內容前，先把它的直接來源拆散：子載體原位變成候選，完整子樹與座標都留著。</summary>
    private void DetachChildSourcesForReplacement(AGNode node)
    {
        if (node?.Obj == null) return;
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
            var child = AGReflect.GetNode(row.Slot);
            if (child == null) continue;
            AGReflect.SetNode(row.Slot, null);
            model.AddOrphan(child);
        }
    }

    /// <summary>
    /// 取一個可以直接改內容的載體：欄位獨佔且不是內嵌內容時就地沿用；
    /// 共用中或還掛著內嵌子樹時另建一個，舊載體整棵留成候選。
    /// </summary>
    private GraphNode SoloSource(object slot)
    {
        var carrier = AGReflect.GetNode(slot);
        bool reusable = carrier != null && carrier.Kind != NodeKind.Inline && CountCarrierUsers(carrier) <= 1;
        return reusable ? carrier : NewSource(slot);
    }

    private int CountCarrierUsers(GraphNode carrier)
    {
        if (carrier == null) return 0;
        int n = 0;
        foreach (var slot in model.AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), carrier)) n++;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), carrier)) n++;
        return n;
    }

    /// <summary>把欄位接到共用變數；原本接著的公式節點留成候選。</summary>
    private void AssignToken(object slot, string key)
    {
        PreserveVisibleNodePositions();
        SoloSource(slot).SetToken(key);
        Invalidate();
    }

    private void AssignAsset(object slot, UnityEngine.Object asset)
    {
        if (asset is not ScriptableObject so) return;
        PreserveVisibleNodePositions();
        SoloSource(slot).SetAsset(so);
        Invalidate();
    }

    /// <summary>拓樸變動前固定目前畫面座標，避免 AutoLayout 因根節點順序改變而重排既有 Node。</summary>
    private void PreserveVisibleNodePositions()
    {
        if (graph?.Nodes == null) return;
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) continue;
            model.SetPosition(node.Id, node.Pos);
        }
    }

    /// <summary>切到這個變數節點指向的 Token 焦點。</summary>
    private void FocusToken(AGNode node)
    {
        foreach (var t in model.ReadTokens())
        {
            if (t.Key != node.TokenKey || t.ResultType != node.ResultType) continue;
            SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = t });
            Repaint();
            return;
        }
        ShowNotification(new GUIContent("找不到這個變數"));
    }

    private void DeleteNode(AGNode node, bool pushUndo = true)
    {
        if (node == null) return;
        if (node.IsRoot)
        {
            ShowNotification(new GUIContent("根節點不可刪除；要換內容請按右鍵"));
            return;
        }
        if (node.Carrier == null) return;
        if (pushUndo) model.BreakUndoMerge();
        PreserveVisibleNodePositions();

        // 刪節點＝斷開所有指著這個載體的欄位，並把它移出候選池。
        foreach (var slot in model.AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), node.Carrier)) AGReflect.SetNode(slot, null);
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), node.Carrier))
            AGReflect.SetNode(focus.AssetHostSlot, null);
        model.RemoveOrphan(node.Carrier);

        selectedIds.Remove(node.Id);
        Invalidate();
    }

    // ===== 右鍵選單 =====

    private void ShowSlotMenu(AGRow row)
    {
        var slot = row.Slot;
        var menu = new GenericMenu();
        int useType = AGReflect.UseType(slot);
        // GenericMenu 的回呼在 OnGUI 之外執行，Event.current 會是 null → 先把滑鼠位置抓下來。
        var menuPos = Event.current.mousePosition;

        if (!row.IsActionSlot)
        {
            menu.AddItem(new GUIContent("設為常數"), useType == 0, () => CutLink(slot));
        }

        menu.AddItem(new GUIContent(row.IsActionSlot ? "指定動作…" : "指定公式…"), false, () =>
        {
            var baseType = row.IsActionSlot
                ? AGReflect.ActionBaseType(slot.GetType())
                : AGReflect.FormulaBaseType(slot.GetType());
            AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), baseType,
                row.IsActionSlot ? "選擇動作" : "選擇公式", type =>
                {
                    var instance = AGReflect.CreateInstance(type);
                    if (instance != null) Connect(slot, instance);
                    Repaint();
                });
        });

        menu.AddItem(new GUIContent("接資產（長出資產節點）"), useType == 2, () => AssignAsset(slot, AGReflect.GetAsset(slot)));

        if (!row.IsActionSlot)
        {
            bool anyToken = false;
            foreach (var t in model.ReadTokens())
            {
                if (t.ResultType != row.ResultType || string.IsNullOrWhiteSpace(t.Key)) continue;
                anyToken = true;
                var captured = t.Key;
                menu.AddItem(new GUIContent($"接變數/{t.Key}"),
                    useType == 3 && AGReflect.GetTokenKey(slot) == captured,
                    () => { AssignToken(slot, captured); Repaint(); });
            }
            if (!anyToken) menu.AddDisabledItem(new GUIContent("接變數（此型別尚無變數）"));
        }

        if (!row.IsActionSlot && AGReflect.GetFormula(slot) != null)
        {
            menu.AddItem(new GUIContent("轉存為變數（Token）"), false, () =>
                AGPrompt.Show("轉存為變數", "輸入變數名稱", "", key => ExtractToken(slot, key)));
        }

        if (useType != 0)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清除這個欄位"), false, () => CutLink(slot));
        }
        menu.ShowAsContext();
    }

    /// <summary>把某個欄位的公式抽成共用變數，欄位本身改為變數狀態。</summary>
    private void ExtractToken(object slot, string key)
    {
        var resultType = AGReflect.ResultType(slot.GetType());
        var formula = AGReflect.GetFormula(slot);
        if (resultType == null || formula == null) return;

        if (!model.AddToken(resultType, key, out string error))
        {
            EditorUtility.DisplayDialog("無法轉存", error, "好");
            return;
        }

        foreach (var t in model.ReadTokens())
        {
            if (t.Key != key || t.ResultType != resultType) continue;
            AGReflect.SetFormula(t.Slot, formula);
            break;
        }

        // 內容已經搬進變數，這裡直接換一個乾淨的變數載體，不把舊載體留成候選（否則同一份公式會有兩個位置）。
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetToken(key);
        AGReflect.SetNode(slot, carrier);
        Invalidate();
        Repaint();
    }

    private void ShowNodeMenu(AGNode node)
    {
        var menu = new GenericMenu();
        var menuPos = Event.current.mousePosition;

        if (node.IsPlaceholder && node.ParentSlot != null)
        {
            menu.AddItem(new GUIContent("變更來源…"), false,
                () => ShowNodeSourceSelector(node, new Rect(menuPos, Vector2.one)));
            if (!node.IsRoot)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("清除空 Node"), false, () => DeleteNode(node));
            }
            menu.ShowAsContext();
            return;
        }

        if (node.TokenKey != null)
        {
            menu.AddItem(new GUIContent("編輯這個變數"), false, () => FocusToken(node));
        }

        if (!node.IsRoot && node.ParentSlot != null)
        {
            string cut = "中斷連線（來源留成候選）";
            menu.AddItem(new GUIContent(cut), false, () => CutLink(node));
        }

        if (node.Obj != null)
        {
            bool isAction = node.ParentSlot != null
                ? AGReflect.IsActionSlotType(node.ParentSlot.GetType())
                : ActionBaseTypeOfCurrentSystem()?.IsInstanceOfType(node.Obj) ?? false;
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(isAction ? "轉存為動作資產" : "轉存為公式資產"), false, () => ExtractAsset(node));

            if (!isAction && node.ParentSlot != null && !AGReflect.IsActionSlotType(node.ParentSlot.GetType()))
            {
                var slot = node.ParentSlot;
                menu.AddItem(new GUIContent("轉存為變數（Token）…"), false, () =>
                    AGPrompt.Show("轉存為變數", "輸入變數名稱", "", key => ExtractToken(slot, key)));
            }

            menu.AddSeparator("");
            if (string.IsNullOrWhiteSpace(node.Tips))
            {
                menu.AddItem(new GUIContent("新增備註"), false, () =>
                {
                    model.SetNodeTips(node.Id, "備註");
                    Invalidate();
                    Repaint();
                });
            }
            else
            {
                menu.AddItem(new GUIContent("刪除備註"), false, () =>
                {
                    model.SetNodeTips(node.Id, "");
                    Invalidate();
                    Repaint();
                });
            }
        }

        if (!node.IsRoot && (node.Obj != null || node.IsAssetNode || node.TokenKey != null))
            menu.AddItem(new GUIContent("刪除"), false, () => DeleteNode(node));

        if (node.IsAssetNode && node.Asset != null)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("編輯這個資產"), false, () => EnterAsset(node));
            menu.AddItem(new GUIContent("在 Project 中顯示"), false, () => EditorGUIUtility.PingObject(node.Asset));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
        menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
        menu.ShowAsContext();
    }

    private void ShowCanvasMenu(Vector2 graphMouse)
    {
        var menu = new GenericMenu();
        var menuPos = Event.current.mousePosition;
        bool canEditFocus = focus.Kind == AGFocusKind.Action || focus.Kind == AGFocusKind.Token;

        foreach (var (rt, list) in model.TokenKinds())
        {
            var elem = list.GetType().GetGenericArguments()[0];
            var probe = AGReflect.CreateInstance(elem) as ITokenEntry;
            var slotType = probe?.Slot?.GetType();
            var baseType = slotType != null ? AGReflect.FormulaBaseType(slotType) : null;
            if (baseType == null) continue;

            var capturedBase = baseType;
            var content = new GUIContent($"建立公式/{AGReflect.ResultTypeName(rt)}");
            if (canEditFocus)
                menu.AddItem(content, false, () =>
                    AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), capturedBase, "選擇公式",
                        type => CreateOrphan(type, graphMouse)));
            else menu.AddDisabledItem(content);
        }

        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null)
        {
            var content = new GUIContent("建立動作");
            if (canEditFocus)
                menu.AddItem(content, false, () =>
                    AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), actionBase, "選擇動作",
                        type => CreateOrphan(type, graphMouse)));
            else menu.AddDisabledItem(content);
        }

        menu.AddSeparator("");
        if (canEditFocus)
        {
            menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
            menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("整理版面"));
            menu.AddDisabledItem(new GUIContent("聚焦全部節點"));
        }
        menu.ShowAsContext();
    }

    private static Vector2 SnapToGrid(Vector2 value)
    {
        return new Vector2(
            Mathf.Round(value.x / AGGraph.GridSize) * AGGraph.GridSize,
            Mathf.Round(value.y / AGGraph.GridSize) * AGGraph.GridSize);
    }


    // ===== 轉存為資產 =====

    /// <summary>把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。</summary>
    private void ExtractAsset(AGNode node)
    {
        if (node.Obj is not ActionSystemNode source) return;

        var assetType = AssetTypeFor(node, out _);
        if (assetType == null)
        {
            EditorUtility.DisplayDialog("無法轉存", "找不到對應的資產型別。", "好");
            return;
        }

        CreateExtractedAsset(node, source, assetType, AGReflect.TypeName(source.GetType()));
    }

    private void CreateExtractedAsset(AGNode node, ActionSystemNode source, Type assetType, string assetName)
    {
        if (!AGAssetStore.TryGetUniquePath(assetName, out string path)) return;

        var asset = ScriptableObject.CreateInstance(assetType);
        if (asset == null)
        {
            Debug.LogError($"[ActionGraph] 建立 {assetType.Name} 失敗。");
            return;
        }

        var setTarget = assetType.GetMethod("SetTarget");
        if (setTarget == null)
        {
            Debug.LogError($"[ActionGraph] {assetType.Name} 沒有 SetTarget。");
            return;
        }
        setTarget.Invoke(asset, new object[] { source });

        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);

        AssetDatabase.SaveAssets();
        AGAssetIndex.Refresh();

        model.BreakUndoMerge();
        // 內容被「搬進資產」，所以是就地把載體換成資產引用，不留成候選。
        if (node.Carrier != null) node.Carrier.SetAsset(asset);
        if (node.ParentSlot == null) model.RemoveOrphan(node.Carrier);

        Invalidate();
        EditorGUIUtility.PingObject(asset);
        ShowNotification(new GUIContent("已轉存為資產"));
    }

    private Type AssetTypeFor(AGNode node, out bool isAction)
    {
        isAction = false;
        if (node.ParentSlot != null)
        {
            var slotType = node.ParentSlot.GetType();
            if (AGReflect.IsActionSlotType(slotType))
            {
                isAction = true;
                return ConcreteAssetType(AGReflect.ActionAssetType(slotType));
            }
            return AGReflect.AssetType(slotType);
        }

        // 未連接節點沒有父欄位，靠型別回推它屬於哪一族。
        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null && actionBase.IsInstanceOfType(node.Obj))
        {
            isAction = true;
            return ConcreteAssetType(ActionAssetTypeOfCurrentSystem());
        }

        foreach (var (_, list) in model.TokenKinds())
        {
            var elem = list.GetType().GetGenericArguments()[0];
            var probeSlot = (AGReflect.CreateInstance(elem) as ITokenEntry)?.Slot;
            if (probeSlot == null) continue;
            var formulaBase = AGReflect.FormulaBaseType(probeSlot.GetType());
            if (formulaBase != null && formulaBase.IsInstanceOfType(node.Obj))
                return AGReflect.AssetType(probeSlot.GetType());
        }
        return null;
    }

    private static Type ConcreteAssetType(Type baseType)
    {
        if (baseType == null) return null;
        if (!baseType.IsAbstract) return baseType;
        foreach (var t in TypeCache.GetTypesDerivedFrom(baseType))
            if (!t.IsAbstract) return t;
        return null;
    }

    private Type ActionAssetTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            var slotType = g.Actions.GetType().GetGenericArguments()[0];
            return AGReflect.ActionAssetType(slotType);
        }
        return null;
    }

    private Type ActionBaseTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            var slotType = g.Actions.GetType().GetGenericArguments()[0];
            return AGReflect.ActionBaseType(slotType);
        }
        return null;
    }

    // 候選池掛在焦點頭端上（資產有自己的一份），所以資產焦點也能建候選，不會污染 Owner。
    private void CreateOrphan(Type type, Vector2 graphMouse)
    {
        var instance = AGReflect.CreateInstance(type);
        if (instance is not ActionSystemNode node) return;

        var carrier = new GraphNode(node);
        carrier.EnsureId();
        carrier.Pos = graphMouse;
        model.AddOrphan(carrier);
        Invalidate();
        Repaint();
    }

    private void ResetLayout()
    {
        if (graph == null) return;
        foreach (var node in graph.Nodes) model.ClearPosition(node.Id);
        graphDirty = true;
        Repaint();
    }

    private void FrameAll()
    {
        if (graph == null || graph.Nodes.Count == 0) return;
        var bounds = graph.Nodes[0].Rect;
        foreach (var n in graph.Nodes)
        {
            bounds.xMin = Mathf.Min(bounds.xMin, n.Rect.xMin);
            bounds.yMin = Mathf.Min(bounds.yMin, n.Rect.yMin);
            bounds.xMax = Mathf.Max(bounds.xMax, n.Rect.xMax);
            bounds.yMax = Mathf.Max(bounds.yMax, n.Rect.yMax);
        }
        zoom = Mathf.Clamp(Mathf.Min(canvasRect.width / (bounds.width + 80f), canvasRect.height / (bounds.height + 80f)), 0.45f, 1.4f);
        pan = new Vector2(canvasRect.width * 0.5f / zoom - bounds.center.x, canvasRect.height * 0.5f / zoom - bounds.center.y);
        Repaint();
    }

    private void CenterOn(object slotOrNode)
    {
        if (graph == null) return;
        foreach (var node in graph.Nodes)
        {
            bool match = ReferenceEquals(node.Obj, slotOrNode);
            if (!match)
                foreach (var row in AGGraph.AllRows(node.Rows))
                    if (ReferenceEquals(row.Slot, slotOrNode)) { match = true; break; }
            if (!match) continue;

            selectedIds.Clear();
            selectedIds.Add(node.Id);
            pan = new Vector2(canvasRect.width * 0.5f / zoom - node.Rect.center.x,
                              canvasRect.height * 0.5f / zoom - node.Rect.center.y);
            return;
        }
    }

    private void SetFocus(AGFocus next)
    {
        if (next == null) return;
        // 頭端第一次被聚焦時補一個穩定識別碼；焦點與座標都靠它。
        object head = next.Head;
        if ((next.Kind == AGFocusKind.Action || next.Kind == AGFocusKind.Token) && head != null
            && string.IsNullOrEmpty(AGReflect.SlotEditorId(head)))
        {
            AGReflect.EnsureSlotEditorId(head);
            model.MarkDirty();
        }
        focus = next;
        editingNameTarget = null;
        editingNameDraft = "";
        if (next.Kind == AGFocusKind.Action)
        {
            currentTiming = next.Timing;
            SaveCurrentTiming();
        }
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    // ===== Console =====

    private void DrawConsole(Rect r)
    {
        AGStyles.Fill(r, new Color(0.17f, 0.18f, 0.20f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var head = new Rect(r.x, r.y, r.width, MinConsole);
        if (GUI.Button(new Rect(head.x + 2f, head.y + 2f, 18f, 17f), consoleCollapsed ? "▸" : "▾", EditorStyles.miniButton))
            consoleCollapsed = !consoleCollapsed;

        int errors = Rep.ErrorCount;
        int warnings = Rep.WarningCount;
        float tx = head.x + 24f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"全部 {Rep.Issues.Count}", consoleTab == 0)) consoleTab = 0;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"錯誤 {errors}", consoleTab == 1)) consoleTab = 1;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"警告 {warnings}", consoleTab == 2)) consoleTab = 2;

        string verifyStatus = IsCurrentReportFresh
            ? $"完整驗證 {Rep.Time:HH:mm:ss}"
            : (focus.Kind == AGFocusKind.Asset ? assetVerifiedOnce : verifiedOnce)
                ? $"即時驗證 {Rep.Time:HH:mm:ss}"
                : "尚未驗證";
        GUI.Label(new Rect(head.xMax - 274f, head.y + 3f, 270f, 16f), verifyStatus, AGStyles.Tiny);

        if (consoleCollapsed) return;

        var listRect = new Rect(r.x + 2f, r.y + MinConsole, r.width - 4f, r.height - MinConsole - 2f);
        var shown = new List<AGIssue>();
        foreach (var issue in Rep.Issues)
        {
            if (consoleTab == 1 && !issue.IsError) continue;
            if (consoleTab == 2 && issue.IsError) continue;
            shown.Add(issue);
        }

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * 20f + 4f);
        consoleScroll = GUI.BeginScrollView(listRect, consoleScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var issue = shown[i];
            var row = new Rect(0f, i * 20f, content.width, 19f);
            if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            var icon = new Rect(row.x + 4f, row.y + 5f, 9f, 9f);
            AGStyles.Fill(icon, issue.IsError ? AGStyles.Error : AGStyles.Warning);
            GUI.Label(new Rect(row.x + 18f, row.y + 1f, row.width - 22f, 17f), issue.Line, AGStyles.ConsoleRow);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                JumpTo(issue);
                Event.current.Use();
            }
        }
        GUI.EndScrollView();
    }

    private void HandleConsoleResize(Rect handle)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && !consoleCollapsed && handle.Contains(e.mousePosition))
        {
            resizingConsole = true;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingConsole)
        {
            consoleHeight = Mathf.Clamp(consoleHeight - e.delta.y, 60f, position.height - 240f);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingConsole)
        {
            resizingConsole = false;
            e.Use();
        }
    }

    private void ZoomAt(Vector2 clipMouse, float wheelDelta)
    {
        float nextZoom = Mathf.Clamp(zoom - wheelDelta * 0.03f, 0.45f, 1.8f);
        if (Mathf.Approximately(nextZoom, zoom)) return;

        // 固定滑鼠下的 Graph 點，縮放期間連線起點與預覽終點都不漂移。
        Vector2 anchor = clipMouse / zoom - pan;
        zoom = nextZoom;
        pan = clipMouse / zoom - anchor;
    }

    private void JumpTo(AGIssue issue)
    {
        if (issue.Focus != null && !issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
