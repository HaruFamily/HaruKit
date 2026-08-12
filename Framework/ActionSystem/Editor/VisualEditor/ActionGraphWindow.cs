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

    // 畫布
    private Vector2 pan = new(20f, 20f);
    private float zoom = 1f;
    private Rect canvasRect;

    // 面板狀態
    private Vector2 tokenScroll, actionScroll, consoleScroll;
    private float consoleHeight = 150f;
    private bool consoleCollapsed;
    private bool resizingConsole;
    private float leftWidth = DefaultLeftWidth;
    private float rightWidth = DefaultRightWidth;
    private bool resizingLeftPanel;
    private bool resizingRightPanel;
    private int consoleTab;                  // 0 全部 / 1 錯誤 / 2 警告
    private string tokenSearch = "";
    private Type newTokenType;
    private string newTokenKey = "";
    private Enum currentTiming;

    // 互動
    private AGNode dragNode;
    private Vector2 dragOffset;
    private readonly Dictionary<string, Vector2> dragStartPositions = new();
    private bool linking;
    private AGRow linkRow;
    private AGToken dragToken;
    private bool dragTokenActive;
    private AGToken pendingTokenFocus;
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
    private UnityEngine.Object pendingOwner;

    // 資產焦點（獨立存檔交易）
    private AGFocus returnFocus;
    private bool assetDirty;
    private AGReport assetReport = new();
    private Vector2 referenceScroll;

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

        Bind(owner);
        if (model == null) return;

        foreach (var slot in model.AllSlots())
        {
            if (AGReflect.GetAsset(slot) != asset) continue;
            EnterAsset(asset, slot.GetType());
            return;
        }
        EditorUtility.DisplayDialog("找不到引用點",
            $"'{owner.name}' 的引用清單登記了這個資產，但實際內容裡找不到指向它的欄位。\n請在資產編輯畫面重建引用清單。", "好");
    }

    public void Bind(UnityEngine.Object owner)
    {
        if (model != null && model.Dirty && !EditorUtility.DisplayDialog(
                "尚未儲存", $"'{(model.Owner != null ? model.Owner.name : "?")}' 有未儲存的修改，切換後會遺失。要繼續嗎？", "捨棄並切換", "取消"))
            return;

        SaveCurrentTiming();
        model = new AGModel();
        if (!model.Bind(owner)) { model = null; return; }
        pendingOwner = null;

        focus = new AGFocus();
        RestoreCurrentTiming();
        graphDirty = true;
        verifiedOnce = false;
        report = AGValidator.Run(model);
        verifiedOnce = true;
        Repaint();
    }

    /// <summary>從 Project／Hierarchy 選到支援的對象就自動聚焦。有未儲存變更時不硬切，改成在工具列問。</summary>
    private void OnSelectionChange()
    {
        var picked = ResolveOwner(Selection.activeObject);
        if (picked == null)
        {
            if (model != null && TryReturnToIdle()) ReturnToIdle();
            return;
        }
        if (model != null && ReferenceEquals(picked, model.Owner)) return;

        bool busy = model != null && (model.Dirty || focus.Kind == AGFocusKind.Asset);
        if (busy) pendingOwner = picked;
        else { pendingOwner = null; Bind(picked); }
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
        currentTiming = null;
        tokenSearch = "";
        pendingOwner = null;
        returnFocus = null;
        assetDirty = false;
        selectedIds.Clear();
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
        consoleHeight = EditorPrefs.GetFloat(PrefConsoleHeight, 150f);
        consoleCollapsed = EditorPrefs.GetBool(PrefConsoleCollapsed, false);
        leftWidth = EditorPrefs.GetFloat(PrefLeftWidth, DefaultLeftWidth);
        rightWidth = EditorPrefs.GetFloat(PrefRightWidth, DefaultRightWidth);
    }

    private void OnDisable()
    {
        SaveCurrentTiming();
        EditorPrefs.SetFloat(PrefConsoleHeight, consoleHeight);
        EditorPrefs.SetBool(PrefConsoleCollapsed, consoleCollapsed);
        EditorPrefs.SetFloat(PrefLeftWidth, leftWidth);
        EditorPrefs.SetFloat(PrefRightWidth, rightWidth);
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
            return;
        }

        HandleGlobalKeys();
        EnsureGraph();

        DrawToolbar(toolbar);
        DrawTokenPanel(left);
        if (focus.Kind == AGFocusKind.Asset) DrawReferencePanel(right);
        else DrawTimingPanel(right);
        DrawCenter(center);
        DrawPanelResizeHandles(leftHandle, rightHandle);

        if (dragTokenActive) DrawDragTokenGhost();
        if (Event.current.type == EventType.MouseUp)
        {
            dragTokenActive = false;
            dragToken = null;
            pendingTokenFocus = null;
            pendingActionFocus = null;
            dragActionIndex = -1;
            dragListRow = null;
            dragListIndex = -1;
        }
        if (Event.current.type == EventType.MouseDrag || linking || dragTokenActive) Repaint();
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

        DrawIdlePanel(left, "變數庫");
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

        var rootSlot = focus.RootSlot;
        graph = rootSlot != null
            ? AGGraph.Build(model, rootSlot, focus.Title, OrphansOfCurrentFocus())
            : new AGGraphView();

        if (pendingCenterTarget != null) { CenterOn(pendingCenterTarget); pendingCenterTarget = null; }
    }

    /// <summary>只顯示屬於目前焦點的未連接節點。</summary>
    private IList OrphansOfCurrentFocus()
    {
        var result = new List<object>();
        var all = model.Orphans;
        if (all == null) return result;
        string focusId = focus.Id;
        foreach (var o in all)
        {
            if (o is not ActionSystemNode n) continue;
            if (model.GetFocusId(n.EditorNodeId) == focusId) result.Add(o);
        }
        return result;
    }

    /// <summary>目前畫面該用哪一份驗證結果：資產焦點只看資產自己的。</summary>
    private AGReport Rep => focus.Kind == AGFocusKind.Asset ? assetReport : report;

    private void Invalidate()
    {
        graphDirty = true;
        // 資產是獨立存檔交易，改它不算改 Owner，也不進 Owner 的 Undo 堆疊。
        if (focus.Kind == AGFocusKind.Asset) assetDirty = true;
        else model.MarkDirty();
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

        string crumb = $"{model.Owner.name}{(model.Dirty ? "●" : "")}　›　";
        crumb += focus.Kind == AGFocusKind.Asset
            ? $"{(returnFocus != null ? returnFocus.Title : "—")}　›　{focus.Title}{(assetDirty ? "　●未儲存" : "")}"
            : $"{focus.Title}{(model.Dirty ? "　●未儲存" : "")}";
        GUI.Label(new Rect(ownerPickerRect.xMax + 4f, r.y + 2f, r.width - 440f, 18f), crumb, EditorStyles.boldLabel);

        bool inAsset = focus.Kind == AGFocusKind.Asset;
        bool blocked = inAsset ? !assetReport.CanSave : verifiedOnce && !report.CanSave;

        float x = r.xMax - 6f;
        x -= 96f;
        GUI.enabled = !blocked;
        if (GUI.Button(new Rect(x, r.y + 1f, 94f, 19f),
                new GUIContent(blocked ? "存檔（有錯誤）" : inAsset ? "存檔並返回" : "存檔",
                    blocked ? "驗證有錯誤，先在 Console 修正才能存檔" : "驗證通過後寫回資產")))
        {
            if (inAsset) SaveAsset(); else DoSave();
        }
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
        if (pendingOwner != null)
        {
            var label = new GUIContent($"切換→{pendingOwner.name}", "剛才選取了別的對象，按此切換（目前的修改會依提示處理）");
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            if (GUI.Button(switchRect, label))
            {
                var target = pendingOwner;
                pendingOwner = null;
                if (inAsset)
                {
                    if (!ConfirmLeaveAsset()) { GUI.backgroundColor = old; return; }
                    ExitAsset();
                }
                Bind(target);
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
            if (assetReport.ErrorCount > 0) { consoleCollapsed = false; consoleTab = 1; }
            if (!silent && assetReport.Issues.Count == 0) ShowNotification(new GUIContent("驗證通過"));
            return;
        }

        report = AGValidator.Run(model);
        verifiedOnce = true;
        if (report.ErrorCount > 0) { consoleCollapsed = false; consoleTab = 1; }
        if (!silent && report.ErrorCount == 0 && report.WarningCount == 0)
            ShowNotification(new GUIContent("驗證通過"));
    }

    private void DoSave()
    {
        DoVerify(true);
        if (!report.CanSave)
        {
            consoleCollapsed = false;
            consoleTab = 1;
            EditorUtility.DisplayDialog("無法存檔", $"還有 {report.ErrorCount} 個錯誤，請先在 Console 修正。", "好");
            return;
        }
        model.Save();
        ShowNotification(new GUIContent("已存檔"));
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
        if (focus.Kind == AGFocusKind.Asset && !ConfirmLeaveAsset()) return;

        object host = slotType != null ? AGReflect.CreateInstance(slotType) : null;
        if (host == null)
        {
            EditorUtility.DisplayDialog("無法編輯", "找不到這個資產對應的欄位型別。", "好");
            return;
        }

        var target = AGReflect.Get(asset, "_target") ?? AGReflect.Get(asset, "_action");
        if (target is ActionSystemNode source)
        {
            var clone = source.EditorClone();
            AGReflect.SetUseType(host, 1);
            AGReflect.SetFormula(host, clone);
        }
        else
        {
            AGReflect.SetUseType(host, 0);
        }

        var back = focus;
        SetFocus(new AGFocus { Kind = AGFocusKind.Asset, AssetObject = asset, AssetHostSlot = host });
        returnFocus = back;
        assetDirty = false;
        DoVerify(true);
    }

    private void SaveAsset()
    {
        var asset = focus.AssetObject;
        var host = focus.AssetHostSlot;
        if (asset == null || host == null) { ExitAsset(); return; }

        DoVerify(true);
        if (!assetReport.CanSave)
        {
            consoleCollapsed = false;
            consoleTab = 1;
            EditorUtility.DisplayDialog("無法存檔", $"這個資產還有 {assetReport.ErrorCount} 個錯誤。", "好");
            return;
        }

        int useType = AGReflect.UseType(host);
        if (useType == 2 || useType == 3)
        {
            EditorUtility.DisplayDialog("無法存檔", "資產的內容只能是公式或動作，不能再指向另一個資產或變數。", "好");
            return;
        }

        var content = useType == 1 ? AGReflect.GetFormula(host) : null;
        var setTarget = asset.GetType().GetMethod("SetTarget");
        if (setTarget == null)
        {
            Debug.LogError($"[ActionGraph] {asset.GetType().Name} 沒有 SetTarget，無法寫回。");
            return;
        }
        setTarget.Invoke(asset, new object[] { content });
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        NotifyAssetSubscribers(asset);

        assetDirty = false;
        ShowNotification(new GUIContent("資產已存檔"));
        ExitAsset();
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
        assetReport = new AGReport();
        SetFocus(back ?? new AGFocus());
        DoVerify(true);
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

    // ===== 左欄：Token 庫 =====

    private void DrawTokenPanel(Rect r)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        bool inAsset = focus.Kind == AGFocusKind.Asset;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 18f),
            new GUIContent(inAsset ? "變數庫（呼叫端）" : "變數庫",
                inAsset ? "資產目前以名稱對應呼叫端的變數，沒有自己的參數宣告" : ""),
            AGStyles.PanelHeader);

        var createRect = new Rect(r.x + 2f, r.y + 20f, r.width - 4f, 50f);
        AGStyles.Fill(createRect, new Color(0.22f, 0.23f, 0.26f));
        AGStyles.Frame(createRect, AGStyles.NodeBorder);

        var kinds = model.TokenKinds();
        if (newTokenType == null && kinds.Count > 0) newTokenType = kinds[0].resultType;

        var typeLabels = new string[kinds.Count];
        int selectedType = 0;
        for (int i = 0; i < kinds.Count; i++)
        {
            typeLabels[i] = AGReflect.ResultTypeName(kinds[i].resultType);
            if (kinds[i].resultType == newTokenType) selectedType = i;
        }

        GUI.enabled = !inAsset && kinds.Count > 0;
        int pickedType = EditorGUI.Popup(new Rect(r.x + 4f, r.y + 22f, 72f, 20f), selectedType, typeLabels);
        if (kinds.Count > 0) newTokenType = kinds[pickedType].resultType;
        newTokenKey = EditorGUI.TextField(new Rect(r.x + 80f, r.y + 22f, r.width - 84f, 20f), newTokenKey);

        bool uniqueKey = !string.IsNullOrWhiteSpace(newTokenKey);
        if (uniqueKey)
            foreach (var token in model.ReadTokens())
                if (token.Key == newTokenKey.Trim()) { uniqueKey = false; break; }
        GUI.enabled = !inAsset && newTokenType != null && uniqueKey;
        if (GUI.Button(new Rect(r.x + 4f, r.y + 46f, r.width - 8f, 22f), "新增變數")) AddTokenFromFields();
        GUI.enabled = true;

        var listRect = new Rect(r.x + 2f, r.y + 72f, r.width - 4f, r.height - 98f);
        var searchRect = new Rect(r.x + 4f, r.yMax - 22f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋變數"));
        tokenSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), tokenSearch);
        var tokens = model.ReadTokens();
        var shown = new List<AGToken>();
        foreach (var t in tokens)
            if (string.IsNullOrWhiteSpace(tokenSearch)
                || t.Key?.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || t.TypeName.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                shown.Add(t);

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * 22f + 4f);
        tokenScroll = GUI.BeginScrollView(listRect, tokenScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var token = shown[i];
            var row = new Rect(0f, i * 22f, content.width, 21f);
            bool isFocus = focus.Kind == AGFocusKind.Token && focus.Token != null
                && focus.Token.Key == token.Key && focus.Token.ResultType == token.ResultType;
            if (isFocus) AGStyles.Fill(row, new Color(0.30f, 0.42f, 0.52f, 0.6f));
            else if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            GUI.Label(new Rect(row.x + 4f, row.y + 1f, row.width - 70f, 18f),
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, AGStyles.RowLabel);
            GUI.Label(new Rect(row.xMax - 62f, row.y + 1f, 40f, 18f), token.TypeName, AGStyles.Tiny);

            if (HasTokenIssue(token, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 16f, row.y + 6f, 8f, 8f);
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
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
                e.Use();
            }
        }
        GUI.EndScrollView();
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

    private bool DrawTab(Rect r, string label, bool active)
    {
        AGStyles.Fill(r, active ? new Color(0.30f, 0.34f, 0.40f) : new Color(0.23f, 0.24f, 0.27f));
        GUI.Label(r, label, AGStyles.Tiny);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void AddTokenFromFields()
    {
        string key = newTokenKey.Trim();
        if (!model.AddToken(newTokenType, key, out string error))
        {
            ShowNotification(new GUIContent(error));
            return;
        }
        foreach (var token in model.ReadTokens())
        {
            if (token.Key != key || token.ResultType != newTokenType) continue;
            SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
            break;
        }
        newTokenKey = "";
        Invalidate();
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

    // ===== 右欄：時機與動作清單 =====

    private void DrawTimingPanel(Rect r)
    {
        AGStyles.Fill(r, new Color(0.19f, 0.20f, 0.22f));
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var groups = model.ReadGroups();
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 120f, 18f), "時機", AGStyles.PanelHeader);

        string timingLabel = currentTiming != null ? currentTiming.ToString() : "（選擇時機）";
        var dropRect = new Rect(r.x + 4f, r.y + 22f, r.width - 8f, 20f);
        if (EditorGUI.DropdownButton(dropRect, new GUIContent(timingLabel), FocusType.Keyboard))
            ShowTimingMenu(groups);

        AGTimingGroup current = null;
        foreach (var g in groups)
            if (currentTiming != null && Equals(g.Timing, currentTiming)) current = g;

        GUI.enabled = currentTiming != null;
        if (GUI.Button(new Rect(r.x + 4f, r.y + 46f, r.width - 8f, 22f), "新增動作"))
            ShowAddActionMenu(currentTiming);
        GUI.enabled = true;

        var listRect = new Rect(r.x + 2f, r.y + 72f, r.width - 4f, r.height - 74f);
        AGStyles.Fill(listRect, new Color(0.16f, 0.17f, 0.19f));

        if (current?.Actions != null) DrawActionList(listRect, current);

    }

    private void DrawActionList(Rect listRect, AGTimingGroup group)
    {
        var actions = group.Actions;
        var content = new Rect(0f, 0f, listRect.width - 16f, actions.Count * 24f + 4f);
        actionScroll = GUI.BeginScrollView(listRect, actionScroll, content);

        for (int i = 0; i < actions.Count; i++)
        {
            var slot = actions[i];
            if (slot == null) continue;
            var row = new Rect(0f, i * 24f, content.width, 23f);
            bool isFocus = focus.Kind == AGFocusKind.Action && ReferenceEquals(focus.ActionSlot, slot);
            if (isFocus) AGStyles.Fill(row, new Color(0.30f, 0.42f, 0.52f, 0.6f));
            else if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            GUI.Label(new Rect(row.x + 2f, row.y + 3f, 12f, 18f), "≡", AGStyles.Tiny);

            bool disabled = AGReflect.GetDisabled(slot);
            bool enabled = !disabled;
            bool newEnabled = GUI.Toggle(new Rect(row.x + 16f, row.y + 4f, 16f, 16f), enabled, GUIContent.none);
            if (newEnabled != enabled) { AGReflect.SetDisabled(slot, !newEnabled); Invalidate(); }

            var focusOfRow = new AGFocus
            {
                Kind = AGFocusKind.Action,
                Timing = group.Timing,
                ActionList = actions,
                ActionIndex = i,
                ActionSlot = slot,
            };
            report.CountFor(focusOfRow, out int errors, out int warnings);

            string name = focusOfRow.Title;
            var nameStyle = errors > 0 ? AGStyles.RowLabelError : AGStyles.RowLabel;
            GUI.Label(new Rect(row.x + 34f, row.y + 1f, row.width - 44f, 15f),
                errors > 0 ? $"{name}（{errors} 個錯誤）" : name, nameStyle);

            string label = AGReflect.GetLabel(slot);
            string tail = disabled
                ? (string.IsNullOrEmpty(label) ? "已停用" : $"{label}・已停用")
                : label;
            GUI.Label(new Rect(row.x + 34f, row.y + 12f, row.width - 44f, 12f),
                string.IsNullOrEmpty(tail) ? (warnings > 0 ? $"{warnings} 個警告" : "") : tail, AGStyles.Tiny);

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
                int target = Mathf.Clamp(Mathf.FloorToInt(e.mousePosition.y / 24f), 0, actions.Count - 1);
                if (target != dragActionIndex)
                {
                    var moved = actions[dragActionIndex];
                    actions.RemoveAt(dragActionIndex);
                    actions.Insert(target, moved);
                    dragActionIndex = target;
                    Invalidate();
                }
            }
            if (e.type == EventType.MouseUp && pendingActionFocus != null
                && ReferenceEquals(pendingActionFocus.ActionSlot, slot) && dragActionIndex < 0 && row.Contains(e.mousePosition))
            {
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(pendingActionFocus);
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

    private void ShowAddActionMenu(Enum timing)
    {
        var slotType = model.ActionSlotType;
        if (slotType == null) return;
        var baseType = AGReflect.ActionBaseType(slotType);
        var rect = new Rect(Event.current.mousePosition, Vector2.one);

        AGTypeCatalog.ShowPicker(rect, baseType, "選擇動作", type =>
        {
            var instance = AGReflect.CreateInstance(type);
            if (instance == null) return;
            var slot = AGReflect.CreateInstance(slotType);
            if (slot == null) return;

            var group = model.AddGroup(timing);
            if (group?.Actions == null) return;
            if (instance is ActionSystemNode n) n.EnsureEditorNodeId();

            AGReflect.SetUseType(slot, 1);
            AGReflect.SetFormula(slot, instance);
            group.Actions.Add(slot);

            SetFocus(new AGFocus
            {
                Kind = AGFocusKind.Action, Timing = group.Timing,
                ActionList = group.Actions, ActionIndex = group.Actions.Count - 1, ActionSlot = slot,
            });
            Invalidate();
            Repaint();
        });
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
            if (!EditorUtility.DisplayDialog("刪除動作", "確定刪除這個動作？", "刪除", "取消")) return;
            group.Actions.RemoveAt(index);
            if (group.Actions.Count == 0) model.RemoveGroup(group);
            SetFocus(new AGFocus());
            Invalidate();
            DoVerify(true);
            Repaint();
        });
        menu.ShowAsContext();
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
        DrawFocusHeader(header);

        float consoleH = consoleCollapsed ? MinConsole : Mathf.Clamp(consoleHeight, MinConsole, r.height - HeaderHeight - 80f);
        canvasRect = new Rect(r.x, r.y + HeaderHeight, r.width, r.height - HeaderHeight - consoleH);
        var consoleRect = new Rect(r.x, canvasRect.yMax, r.width, consoleH);
        var consoleHandle = new Rect(consoleRect.x, consoleRect.y - 3f, consoleRect.width, ResizeHandleWidth);

        HandleConsoleResize(consoleHandle);

        DrawCanvas(canvasRect);
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
            GUI.Label(new Rect(r.x + 6f, r.y + 3f, 60f, 18f), "變數名稱", AGStyles.Tiny);
            var nameRect = new Rect(r.x + 66f, r.y + 3f, 180f, 18f);
            string newName = EditorGUI.DelayedTextField(nameRect, focus.Token.Key);
            if (newName != focus.Token.Key)
            {
                if (!model.RenameToken(focus.Token, newName, out string error)) EditorUtility.DisplayDialog("無法改名", error, "好");
                Invalidate();
            }
            GUI.Label(new Rect(r.x + 254f, r.y + 3f, r.width - 260f, 18f),
                $"型別 {focus.Token.TypeName}　被引用 {model.CountReferences(focus.Token)} 次", AGStyles.Tiny);
            GUI.Label(new Rect(r.x + 6f, r.y + 24f, r.width - 12f, 16f),
                "這個變數的值由下方公式決定；任何欄位都可以拖它進去共用同一個值。", AGStyles.Tiny);
            return;
        }

        GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 18f), focus.Title, EditorStyles.boldLabel);

        string desc = "";
        if (focus.Kind == AGFocusKind.Action && focus.ActionSlot != null)
        {
            var f = AGReflect.GetFormula(focus.ActionSlot);
            desc = f != null ? AGReflect.TypeDescription(f.GetType()) : "這個動作還沒有內容，在根節點按右鍵選擇。";
            if (AGReflect.GetDisabled(focus.ActionSlot)) desc += "　（已停用，不會執行）";
        }
        else if (focus.Kind == AGFocusKind.None)
        {
            desc = "從右欄選一個動作，或從左欄選一個變數開始編輯。";
        }
        GUI.Label(new Rect(r.x + 6f, r.y + 24f, r.width - 12f, 16f), desc, AGStyles.Tiny);
    }

    // ===== 畫布 =====

    private void DrawCanvas(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Canvas);
        DrawGrid(r);

        var e = Event.current;
        Vector2 clipMouse = e.mousePosition - r.position;
        Vector2 graphMouse = clipMouse / zoom - pan;
        bool mouseInCanvas = r.Contains(e.mousePosition);

        GUI.BeginClip(r);
        var oldMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(Vector2.one * zoom, Vector2.zero);

        if (graph != null)
        {
            // 連線要先於節點畫（線在節點下方），但接點座標由節點位置決定 → 先算一次。
            foreach (var node in graph.Nodes) UpdateRowGeometry(node, node.Rows);
            DrawLinks();
            foreach (var node in graph.Nodes) DrawNode(node);
            if (linking && linkRow != null)
            {
                var from = linkRow.PortPos + pan;
                DrawBezier(from, graphMouse + pan, AGStyles.Link);
            }
            if (boxSelecting)
            {
                var box = BoxRect();
                var visual = new Rect(box.position + pan, box.size);
                AGStyles.Fill(visual, new Color(0.42f, 0.78f, 1f, 0.10f));
                AGStyles.Frame(visual, AGStyles.Link);
            }
        }

        GUI.matrix = oldMatrix;
        GUI.EndClip();

        if (mouseInCanvas) HandleCanvasInput(e, graphMouse);
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

    private void DrawLinks()
    {
        foreach (var node in graph.Nodes)
        {
            if (node.ParentRow == null || node.IsRoot) continue;
            Vector2 from = node.ParentRow.PortPos + pan;
            Vector2 to = node.OutputPort + pan;
            bool err = Rep.HasIssue(node.ParentRow.Slot, out bool isError) && isError;
            Color color = err ? AGStyles.Error
                : node.TokenKey != null ? AGStyles.PortToken
                : AGStyles.Link;
            DrawBezier(from, to, color);
        }
    }

    private static void DrawBezier(Vector2 from, Vector2 to, Color color)
    {
        Handles.DrawBezier(from, to,
            from + Vector2.right * 40f, to + Vector2.left * 40f,
            color, null, 2.4f);
    }

    private void DrawNode(AGNode node)
    {
        var rect = new Rect(node.Pos + pan, new Vector2(node.Width, node.Height));

        AGStyles.Fill(rect, AGStyles.NodeBody);
        var header = new Rect(rect.x, rect.y, rect.width, 20f);
        Color headerColor = node.IsRoot ? AGStyles.NodeHeaderRoot
            : node.IsOrphan ? AGStyles.NodeHeaderOrphan
            : node.TokenKey != null ? AGStyles.NodeHeaderToken
            : node.IsAssetNode ? AGStyles.NodeHeaderAsset
            : AGStyles.NodeHeader;
        AGStyles.Fill(header, headerColor);
        GUI.Label(header, node.Title, AGStyles.NodeTitle);
        GUI.Label(new Rect(rect.x, rect.y + 19f, rect.width, 16f),
            node.IsOrphan ? node.Desc + "　・未連接" : node.Desc, AGStyles.NodeDesc);

        if (node.IsAssetNode)
        {
            var assetType = node.ParentSlot != null
                ? AGReflect.AssetType(node.ParentSlot.GetType()) ?? typeof(UnityEngine.Object)
                : typeof(UnityEngine.Object);
            var assetRect = new Rect(rect.x + 6f, rect.y + 40f, rect.width - 12f, 18f);
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(assetRect, node.Asset, assetType, false);
            if (EditorGUI.EndChangeCheck() && node.ParentSlot != null)
            {
                AGReflect.SetAsset(node.ParentSlot, picked);
                Invalidate();
            }
        }
        else if (node.TokenKey != null)
        {
            var pickRect = new Rect(rect.x + 6f, rect.y + 38f, rect.width - 12f, 15f);
            if (GUI.Button(pickRect, string.IsNullOrEmpty(node.TokenKey) ? "選擇變數…" : "換一個變數…", EditorStyles.miniButton))
                ShowTokenPicker(node);
        }
        else
        {
            DrawRows(node, node.Rows, rect);
        }

        // 節點層級的問題用徽章；參數列層級的問題直接把該列標紅。
        // 資產／變數節點自己沒有物件，問題掛在父欄位上，改查父欄位才看得到。
        object issueTarget = node.Obj ?? (node.IsAssetNode || node.TokenKey != null ? node.ParentSlot : null);
        if (Rep.HasIssue(issueTarget, out bool nodeError) || node.IsOrphan)
        {
            var badge = new Rect(rect.xMax - 16f, rect.y + 4f, 12f, 12f);
            AGStyles.Fill(badge, nodeError ? AGStyles.Error : AGStyles.Warning);
            GUI.Label(badge, new GUIContent("", node.IsOrphan ? "未連接節點：不會被執行" : "此節點有問題，詳見 Console"));
        }

        if (!node.IsRoot)
        {
            var outPort = new Rect(rect.x - 5f, rect.y + 5f, 10f, 10f);
            AGStyles.Port(outPort, AGStyles.PortLive);
        }

        bool selected = selectedIds.Contains(node.Id);
        AGStyles.Frame(rect, selected ? AGStyles.NodeBorderSelected : AGStyles.NodeBorder, selected ? 2f : 1f);
    }

    /// <summary>把每一列的圖面座標（命中測試與接點）更新成目前的節點位置。</summary>
    private static void UpdateRowGeometry(AGNode node, List<AGRow> rows)
    {
        foreach (var row in rows)
        {
            row.ScreenRect = new Rect(node.Pos.x, node.Pos.y + row.LocalY, node.Width, row.Height);
            row.PortPos = new Vector2(node.Pos.x + node.Width, node.Pos.y + row.LocalY + row.Height * 0.5f);
            UpdateRowGeometry(node, row.Children);
        }
    }

    private void DrawRows(AGNode node, List<AGRow> rows, Rect nodeRect)
    {
        foreach (var row in rows)
        {
            var rowRect = new Rect(nodeRect.x, nodeRect.y + row.LocalY, nodeRect.width, row.Height);
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
                    GUI.Label(Indent(rowRect, row.Depth, row.IsListElement), row.Label, AGStyles.RowLabel);
                    DrawRows(node, row.Children, nodeRect);
                    break;
                case AGRowKind.List:
                    DrawListHeader(row, rowRect);
                    DrawRows(node, row.Children, nodeRect);
                    var addRect = new Rect(nodeRect.x, nodeRect.y + row.AddRowY, nodeRect.width, AGGraph.RowHeight);
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
        GUI.Label(labelRect, row.Label, hasIssue && isError ? AGStyles.RowLabelError : AGStyles.RowLabel);

        var fieldRect = new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f,
            rowRect.width * 0.58f - 20f, rowRect.height - 3f);

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
                ? AGValueField.Draw(fieldRect, row.ResultType, AGReflect.GetDefault(slot))
                : AGValueField.DrawMuted(fieldRect, row.ResultType, AGReflect.GetDefault(slot), tooltip);
            if (EditorGUI.EndChangeCheck()) { AGReflect.SetDefault(slot, value); Invalidate(); }
        }

        var portRect = new Rect(rowRect.xMax - 14f, rowRect.y + rowRect.height * 0.5f - 5f, 10f, 10f);
        Color portColor = hasIssue && isError ? AGStyles.PortError
            : useType == 3 ? AGStyles.PortToken
            : useType == 1 || useType == 2 ? AGStyles.PortLive
            : AGStyles.PortEmpty;
        AGStyles.Port(portRect, portColor);

        var e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 1) { ShowSlotMenu(row); e.Use(); }
            else if (portRect.Contains(e.mousePosition)) { linking = true; linkRow = row; e.Use(); }
        }
    }

    private void DrawValueRow(AGRow row, Rect rowRect)
    {
        var labelRect = Indent(new Rect(rowRect.x, rowRect.y, rowRect.width * 0.42f, rowRect.height), row.Depth, row.IsListElement);
        GUI.Label(labelRect, row.Label, AGStyles.RowLabel);

        var fieldRect = new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f, rowRect.width * 0.58f - 20f, rowRect.height - 3f);

        if (row.Field != null && row.Target != null)
        {
            EditorGUI.BeginChangeCheck();
            var value = AGValueField.Draw(fieldRect, row.Field.FieldType, row.Field.GetValue(row.Target));
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
                zoom = Mathf.Clamp(zoom - e.delta.y * 0.03f, 0.45f, 1.8f);
                e.Use();
                break;

            case EventType.MouseDown:
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
                    Vector2 target = graphMouse - dragOffset;
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
                if (linking)
                {
                    ResolveLink(graphMouse);
                    linking = false;
                    linkRow = null;
                    e.Use();
                }
                if (dragTokenActive && dragToken != null)
                {
                    DropTokenOn(graphMouse);
                    dragTokenActive = false;
                    dragToken = null;
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
            clone.EnsureEditorNodeId();

            model.AddOrphan(clone);
            model.SetFocusId(clone.EditorNodeId, focus.Id);
            model.SetPosition(clone.EditorNodeId, graphMouse + new Vector2(offset, offset));
            selectedIds.Add(clone.EditorNodeId);
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

    /// <summary>找滑鼠附近的連線（取樣貝茲曲線比對距離）。</summary>
    private AGNode LinkAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        foreach (var node in graph.Nodes)
        {
            if (node.ParentRow == null || node.IsRoot) continue;
            Vector2 a = node.ParentRow.PortPos;
            Vector2 b = node.OutputPort;
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                Vector2 p = Bezier(a, a + Vector2.right * 40f, b + Vector2.left * 40f, b, t);
                if ((p - graphPoint).sqrMagnitude < 36f) return node;
            }
        }
        return null;
    }

    private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    /// <summary>切線：父欄位改回常數，被切下來的節點留成未連接節點。</summary>
    private void CutLink(AGNode node)
    {
        if (node.ParentSlot == null) return;
        var detached = AGReflect.GetFormula(node.ParentSlot);
        AGReflect.SetUseType(node.ParentSlot, 0);
        AGReflect.ClearUnusedSources(node.ParentSlot, 0);
        if (detached is ActionSystemNode n)
        {
            model.AddOrphan(n);
            model.SetFocusId(n.EnsureEditorNodeId(), focus.Id);
        }
        Invalidate();
    }

    private void ResolveLink(Vector2 graphMouse)
    {
        if (linkRow?.Slot == null) return;
        var slot = linkRow.Slot;
        var target = NodeAt(graphMouse);

        // 拉到既有的變數節點：直接改接同一個變數。
        if (target != null && target.TokenKey != null)
        {
            if (linkRow.IsActionSlot || target.ResultType != linkRow.ResultType)
            {
                ShowNotification(new GUIContent("型別不符，無法連接"));
                return;
            }
            AssignToken(slot, target.TokenKey);
            return;
        }

        // 拉到既有的資產節點：共用同一個資產。
        if (target != null && target.IsAssetNode)
        {
            if (target.ResultType != linkRow.ResultType)
            {
                ShowNotification(new GUIContent("型別不符，無法連接"));
                return;
            }
            AssignAsset(slot, target.Asset);
            return;
        }

        if (target != null && target.Obj != null)
        {
            var accepted = linkRow.IsActionSlot
                ? AGReflect.ActionBaseType(slot.GetType())
                : AGReflect.FormulaBaseType(slot.GetType());
            if (accepted == null || !accepted.IsInstanceOfType(target.Obj))
            {
                ShowNotification(new GUIContent("型別不符，無法連接"));
                return;
            }
            if (target.ParentSlot != null && !ReferenceEquals(target.ParentSlot, slot))
            {
                ShowNotification(new GUIContent("這個節點已經接在別的欄位上"));
                return;
            }
            Connect(slot, target.Obj);
            return;
        }

        // 空白處放開：直接開型別選單建新節點。
        var baseType = linkRow.IsActionSlot
            ? AGReflect.ActionBaseType(slot.GetType())
            : AGReflect.FormulaBaseType(slot.GetType());
        var rect = new Rect(Event.current.mousePosition, Vector2.one);
        AGTypeCatalog.ShowPicker(rect, baseType, linkRow.IsActionSlot ? "選擇動作" : "選擇公式", type =>
        {
            var instance = AGReflect.CreateInstance(type);
            if (instance == null) return;
            Connect(slot, instance);
            if (instance is ActionSystemNode n) model.SetPosition(n.EnsureEditorNodeId(), graphMouse);
            Repaint();
        });
    }

    private void Connect(object slot, object node)
    {
        var previous = AGReflect.GetFormula(slot);
        if (previous != null && !ReferenceEquals(previous, node) && previous is ActionSystemNode old)
        {
            model.AddOrphan(old);
            model.SetFocusId(old.EnsureEditorNodeId(), focus.Id);
        }

        AGReflect.SetUseType(slot, 1);
        AGReflect.SetFormula(slot, node);
        AGReflect.ClearUnusedSources(slot, 1);
        if (node is ActionSystemNode n)
        {
            n.EnsureEditorNodeId();
            model.RemoveOrphan(n);
        }
        Invalidate();
    }

    private void DropTokenOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null || row.IsActionSlot)
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

    /// <summary>把欄位接到共用變數；原本接著的公式節點留成未連接節點。</summary>
    private void AssignToken(object slot, string key)
    {
        DetachFormula(slot);
        AGReflect.SetUseType(slot, 3);
        AGReflect.SetTokenKey(slot, key);
        AGReflect.ClearUnusedSources(slot, 3);
        Invalidate();
    }

    private void AssignAsset(object slot, UnityEngine.Object asset)
    {
        DetachFormula(slot);
        AGReflect.SetUseType(slot, 2);
        AGReflect.SetAsset(slot, asset);
        AGReflect.ClearUnusedSources(slot, 2);
        Invalidate();
    }

    /// <summary>在變數節點上換一個同型別的變數。</summary>
    private void ShowTokenPicker(AGNode node)
    {
        if (node.ParentSlot == null) return;
        var menu = new GenericMenu();
        bool any = false;
        foreach (var t in model.ReadTokens())
        {
            if (t.ResultType != node.ResultType) continue;
            if (string.IsNullOrWhiteSpace(t.Key)) continue;
            any = true;
            var captured = t.Key;
            menu.AddItem(new GUIContent(t.Key), captured == node.TokenKey, () =>
            {
                AssignToken(node.ParentSlot, captured);
                Repaint();
            });
        }
        if (!any) menu.AddDisabledItem(new GUIContent($"沒有 {AGReflect.ResultTypeName(node.ResultType)} 變數，先在左欄新增"));
        menu.ShowAsContext();
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
        if (node.Obj == null && !node.IsAssetNode && node.TokenKey == null) return;
        if (node.IsRoot)
        {
            ShowNotification(new GUIContent("根節點不可刪除；要換內容請按右鍵"));
            return;
        }
        if (pushUndo) model.BreakUndoMerge();
        if (node.ParentSlot != null)
        {
            AGReflect.SetUseType(node.ParentSlot, 0);
            AGReflect.ClearUnusedSources(node.ParentSlot, 0);
        }
        if (node.Obj is ActionSystemNode n) model.RemoveOrphan(n);
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
            menu.AddItem(new GUIContent("設為常數"), useType == 0, () =>
            {
                DetachFormula(slot);
                AGReflect.SetUseType(slot, 0);
                AGReflect.ClearUnusedSources(slot, 0);
                Invalidate();
            });
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
            menu.AddItem(new GUIContent("清除這個欄位"), false, () =>
            {
                DetachFormula(slot);
                AGReflect.SetUseType(slot, 0);
                AGReflect.ClearUnusedSources(slot, 0);
                Invalidate();
            });
        }
        menu.ShowAsContext();
    }

    private void DetachFormula(object slot)
    {
        var previous = AGReflect.GetFormula(slot);
        if (previous is ActionSystemNode old)
        {
            model.AddOrphan(old);
            model.SetFocusId(old.EnsureEditorNodeId(), focus.Id);
        }
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
            AGReflect.SetUseType(t.Slot, 1);
            AGReflect.SetFormula(t.Slot, formula);
            break;
        }

        AGReflect.SetUseType(slot, 3);
        AGReflect.SetFormula(slot, null);
        AGReflect.SetTokenKey(slot, key);
        AGReflect.ClearUnusedSources(slot, 3);
        Invalidate();
        Repaint();
    }

    private void ShowNodeMenu(AGNode node)
    {
        var menu = new GenericMenu();
        var menuPos = Event.current.mousePosition;

        if (node.IsPlaceholder && node.ParentSlot != null)
        {
            bool isAction = AGReflect.IsActionSlotType(node.ParentSlot.GetType());
            menu.AddItem(new GUIContent(isAction ? "指定動作…" : "指定公式…"), false, () =>
            {
                var baseType = isAction
                    ? AGReflect.ActionBaseType(node.ParentSlot.GetType())
                    : AGReflect.FormulaBaseType(node.ParentSlot.GetType());
                AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), baseType,
                    isAction ? "選擇動作" : "選擇公式", type =>
                    {
                        var instance = AGReflect.CreateInstance(type);
                        if (instance != null) Connect(node.ParentSlot, instance);
                        Repaint();
                    });
            });
            menu.ShowAsContext();
            return;
        }

        if (node.TokenKey != null)
        {
            menu.AddItem(new GUIContent("編輯這個變數"), false, () => FocusToken(node));
            menu.AddItem(new GUIContent("換一個變數…"), false, () => ShowTokenPicker(node));
        }

        if (!node.IsRoot && node.ParentSlot != null)
        {
            // 變數／資產節點只是引用，中斷後不會留下未連接節點（沒有可保留的內容）。
            string cut = node.Obj != null ? "中斷連線（留成未連接節點）" : "中斷連線（欄位改回常數）";
            menu.AddItem(new GUIContent(cut), false, () => CutLink(node));
        }

        if (node.Obj != null)
        {
            bool isAction = node.ParentSlot != null
                ? AGReflect.IsActionSlotType(node.ParentSlot.GetType())
                : ActionBaseTypeOfCurrentSystem()?.IsInstanceOfType(node.Obj) ?? false;
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(isAction ? "轉存為動作資產…" : "轉存為公式資產…"), false, () => ExtractAsset(node));

            if (!isAction && node.ParentSlot != null && !AGReflect.IsActionSlotType(node.ParentSlot.GetType()))
            {
                var slot = node.ParentSlot;
                menu.AddItem(new GUIContent("轉存為變數（Token）…"), false, () =>
                    AGPrompt.Show("轉存為變數", "輸入變數名稱", "", key => ExtractToken(slot, key)));
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

    // ===== 轉存為資產 =====

    private const string PrefAssetDir = "ActionSystem.LastAssetSaveDir";

    /// <summary>把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。</summary>
    private void ExtractAsset(AGNode node)
    {
        if (node.Obj is not ActionSystemNode source) return;

        var assetType = AssetTypeFor(node, out bool isAction);
        if (assetType == null)
        {
            EditorUtility.DisplayDialog("無法轉存", "找不到對應的資產型別。", "好");
            return;
        }

        string dir = EditorPrefs.GetString(PrefAssetDir, "");
        if (string.IsNullOrEmpty(dir) || !AssetDatabase.IsValidFolder(dir)) dir = "Assets";

        string path = EditorUtility.SaveFilePanelInProject(
            isAction ? "儲存動作資產" : "儲存公式資產",
            AGReflect.TypeName(source.GetType()),
            "asset",
            "請選擇儲存位置",
            dir);
        if (string.IsNullOrEmpty(path)) return;

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

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetString(PrefAssetDir, System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets");

        // 讓資產記得誰在用它：資產內容變動時才通知得到 Owner 重新驗證。
        if (model.Owner is ScriptableObject ownerSo)
            assetType.GetMethod("RegisterSubscriber")?.Invoke(asset, new object[] { ownerSo });
        else
            Debug.LogWarning("[ActionGraph] Owner 不是 ScriptableObject，資產不會登記引用者。");

        model.BreakUndoMerge();
        if (node.ParentSlot != null)
        {
            // 這裡不能走 AssignAsset：原節點是被「搬進資產」，不該再留成未連接節點。
            AGReflect.SetUseType(node.ParentSlot, 2);
            AGReflect.SetAsset(node.ParentSlot, asset);
            AGReflect.ClearUnusedSources(node.ParentSlot, 2);
        }
        else
        {
            model.RemoveOrphan(source);
        }

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

    private void CreateOrphan(Type type, Vector2 graphMouse)
    {
        var instance = AGReflect.CreateInstance(type);
        if (instance is not ActionSystemNode node) return;
        node.EnsureEditorNodeId();
        model.AddOrphan(node);
        model.SetFocusId(node.EditorNodeId, focus.Id);
        model.SetPosition(node.EditorNodeId, graphMouse);
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
        focus = next;
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

        GUI.Label(new Rect(head.xMax - 200f, head.y + 3f, 196f, 16f),
            verifiedOnce ? $"上次驗證 {Rep.Time:HH:mm:ss}" : "尚未驗證", AGStyles.Tiny);

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

    private void JumpTo(AGIssue issue)
    {
        if (issue.Focus != null && !issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
