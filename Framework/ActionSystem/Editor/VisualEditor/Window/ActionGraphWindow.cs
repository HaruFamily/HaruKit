namespace HaruFamily.Framework.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ActionSystem 視覺化編輯器：左欄變數庫、中欄節點圖 + Console；所有時機畫在同一張畫布，右欄只在資產焦點出現。
/// 所有編輯都改工作副本，按「存檔」才寫回 Owner 資產。
/// 狀態欄位與主繪製流程在本檔，其餘責任見 ActionGraphWindow.*.cs。
/// </summary>
public partial class ActionGraphWindow : EditorWindow
{
    private const float ToolbarHeight = 22f;
    private const float DefaultLeftWidth = 220f;
    private const float MinLeftWidth = 160f;
    private const float MinCenterWidth = 320f;
    private const float ResizeHandleWidth = 6f;
    private const float HeaderHeight = 46f;
    private const float MinConsole = 22f;
    private const float NodeCornerRadius = 6f;
    private const float LinkSnapDistance = 24f;
    private const float LinkThickness = 4f;
    private const float TokenCellHeight = 30f;
    private const float AssetCellHeight = 30f;
    /// <summary>引用列比清單格矮：它只有名稱與驗證狀態，沒有 chip 也沒有第二行。</summary>
    private const float RefRowHeight = 22f;
    /// <summary>左欄變數區的最小高度：三顆固定控制項 + 一列，再小就有東西被切掉（標題由面板標題兼任）。</summary>
    private const float MinTokenSection = 106f;
    private const float MinAssetSection = 80f;
    /// <summary>引用區的最小高度：標題列 + 一筆引用。它只在資產焦點出現，另外兩區跟著讓出高度。</summary>
    private const float MinRefSection = 76f;
    private const float DefaultTokenSection = 240f;
    private const float DefaultRefSection = 140f;
    private const string PrefConsoleHeight = "ActionGraph.ConsoleHeight";
    private const string PrefConsoleCollapsed = "ActionGraph.ConsoleCollapsed";
    private const string PrefLeftWidth = "ActionGraph.LeftWidth";
    private const string PrefTokenSection = "ActionGraph.TokenSectionHeight";
    private const string PrefRefSection = "ActionGraph.RefSectionHeight";

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
    private Vector2 tokenScroll, assetLibraryScroll, consoleScroll;
    private float consoleHeight = 150f;
    private bool consoleCollapsed;
    private bool resizingConsole;
    private float leftWidth = DefaultLeftWidth;
    private bool resizingLeftPanel;
    private int consoleTab;                  // 0 全部 / 1 錯誤 / 2 警告
    // 左欄變數／資產上下分區：存變數區的高度，資產區吃剩下的。編資產時兩份清單要同時看得到，不能再用分頁互斥。
    private float tokenSectionHeight = DefaultTokenSection;
    private bool resizingLibrarySplit;
    // 左欄第三區（引用此資產）：只在資產焦點出現，存自己的高度，資產區吃剩下的。
    private float refSectionHeight = DefaultRefSection;
    private bool resizingRefSplit;
    private string tokenSearch = "";
    private string assetSearch = "";
    private object editingNameTarget;
    private string editingNameDraft = "";
    // 就地改名的提交入口，由 DrawInlineName 每幀存進來：畫布吃掉點擊時，改名那一欄已經沒機會自己收尾。
    private Func<string, bool> editingNameSubmit;

    // 互動
    private AGNode dragNode;
    private Vector2 dragOffset;
    private readonly Dictionary<string, Vector2> dragStartPositions = new();

    // 這一次按下之後有沒有真的拖動過。沒動過就不落盤座標——還沒有座標記憶的節點會因此
    // 被寫進 AutoLayout 的結果，讓「只是點一下節點」變成未存檔。
    private bool dragMoved;

    // Header 的 ▾ 落在拖曳抓取區裡，所以仍要分辨拖曳：按下時先記著，放開時沒移動超過門檻才算點擊。
    private AGNode titleClickNode;
    private Vector2 titleClickStart;
    private const float TitleClickSlop = 4f;
    /// <summary>Header 右端 ▾ 的寬度。放大到 18px 是因為 0.45 倍縮放下它只剩 8px，再小就按不到。</summary>
    private const float SourceArrowWidth = 18f;

    private bool linking;
    private AGRow linkRow;
    private AGNode linkNode;

    // 接點一個熱區兩種手勢：按下先記著，移動超過 PortClickSlop 才起拉線，原地放開就是收合這一段。
    // 判定跟 Header 的 ▾ 同一套。刻意不在 MouseDown 當下起拉線：想收合卻抖了一下的話，
    // 放開時那條線會落在畫布空白處，於是憑空多一顆空節點。
    private AGRow portClickRow;
    private Vector2 portClickStart;
    private const float PortClickSlop = 4f;

    // 待開的就地確認框（見 RequestConfirm）。錨點是視窗座標。
    private AGConfirmPopup pendingConfirm;
    private Rect pendingConfirmAnchor;

    // 拉線期間的相容性：起手時對全圖判定一次，之後高亮與吸附都讀這份，不必每幀重算。
    private readonly HashSet<string> linkCompatibleNodeIds = new();
    private readonly HashSet<AGRow> linkCompatibleRows = new();
    private ScriptableObject dragAsset;
    private bool dragAssetActive;
    private ScriptableObject pendingAssetFocus;
    // 變數的拖曳與下鑽和資產同一套：按下先記著，拖出去是建節點，原地放開是進它的畫布。
    private GraphEndpoint dragEndpoint;
    private bool dragEndpointActive;
    private GraphEndpoint pendingVariableFocus;
    // 「建立節點」的放置模式：新節點跟著滑鼠，點一下才落在畫布上。Esc 或右鍵取消。
    private object placingSlot;
    // 候選池裡的空節點屬於哪一族（值＝代表性的 Slot 型別）。key 是載體 Id，所以撐得過 Undo 與重建圖。
    // 純編輯期提示，不進資料：視窗關掉就沒了，那顆節點退回一般空節點。
    private readonly Dictionary<string, Type> orphanKindHints = new();
    // 選取用 id 記，節點物件每次重建圖都會換一份。
    private readonly HashSet<string> selectedIds = new();
    // 空註解框是暫態：只跟著這一顆被選取的節點活著，不寫進資料。
    private string noteOpenId;
    // 手動收起的註解：只影響顯示，內容仍留在載體上。
    private readonly HashSet<string> noteCollapsed = new();
    // 每個載體被幾個欄位指著。只在圖重建後才會變，但 Header 每幀都要拿它畫 tooltip，不快取就是每幀掃全部欄位。
    private readonly Dictionary<GraphNode, int> carrierUsers = new();
    private bool boxSelecting;
    private Vector2 boxStart;
    private Vector2 boxEnd;
    private AGRow dragListRow;
    private int dragListIndex = -1;
    // 拖曳期間只算目標位置、畫插入線；MouseUp 才真的搬動。拖曳中改資料會讓整張圖重建、列在指標底下亂跳。
    private int dragListTarget = -1;
    // 清單折疊只是視覺狀態，不進資料：key 見 AGGraph.CollapseKey，沒有記錄的清單依項數自動決定。
    private readonly Dictionary<string, bool> listCollapse = new();
    // Slot 的分支收合狀態，key 同樣是 AGGraph.CollapseKey。只認手動切換過的記錄，沒記錄就是展開。
    // 純視覺，切換只能設 graphDirty，不可以走 Invalidate。
    private readonly Dictionary<string, bool> slotHidden = new();
    // 上一次算出來的實際結果：開關要畫成什麼樣子直接查這裡，不必再重算一次自動規則。
    private readonly HashSet<string> effectiveHidden = new();
    // Alt 按＝solo：只留這一個 Slot 的子樹。退出時還原成 soloRestore 記下的手動記錄，不是全展開。
    private string soloSlotKey;
    private readonly Dictionary<string, bool> soloRestore = new();
    private object pendingCenterTarget;
    private static readonly List<object> clipboard = new();

    // 有未儲存變更時不硬切對象，先記在這裡等使用者按確認
    private UnityEngine.Object pendingTarget;

    // 資產焦點（獨立存檔交易）
    private AGFocus returnFocus;
    private bool assetDirty;

    // 內容真的變了（接線、換型別、綁定、刪節點）才會是 true；只搬座標不算。
    // 只有它為 true 才需要擋存檔與通知 subscriber 重新驗證——搬個位置不該驚動任何引用者。
    private bool assetContentDirty;
    private AGReport assetReport = new();
    private Vector2 referenceScroll;

    // 資產的復原歷程。資產不在 Owner 的工作副本裡，AGModel 那份 Undo 蓋不到，得自己記一份。
    private readonly AGAssetHistory assetHistory = new();

    private bool HasUnsavedWork => model?.Dirty == true || assetDirty;

    /// <summary>引用清單只在資產焦點有意義，作為左欄第三區出現（2026-08-20 由整條右欄改成分區）。</summary>
    private bool HasReferenceSection => focus.Kind == AGFocusKind.Asset;
    private bool IsCurrentReportFresh => focus.Kind == AGFocusKind.Asset
        ? assetVerifiedOnce && !assetReportStale
        : verifiedOnce && !reportStale;

    // ===== 主繪製 =====

    private void OnGUI()
    {
        GetLayout(out var toolbar, out var left, out var center, out var leftHandle);
        HandlePanelResize(leftHandle);
        GetLayout(out toolbar, out left, out center, out leftHandle);

        if (model == null || model.Owner == null)
        {
            DrawIdle(toolbar, left, center);
            DrawResizeGrip(leftHandle, true, resizingLeftPanel);
            UpdateUnsavedState();
            return;
        }

        HandleGlobalKeys();
        EnsureGraph();

        // 新的一次按下代表上一次拖曳一定結束了。清在這裡是因為 MouseUp 不保證收得到——
        // 在視窗外放開就沒有那個事件，狀態會一直掛著，之後任何一次拖曳都會被誤判成「還在拖那個東西」。
        // 順序很重要：先清，再讓左欄在同一個 MouseDown 裡重新設定。
        if (Event.current.type == EventType.MouseDown) ClearPendingLibraryDrag();

        if (Event.current.type == EventType.MouseDrag && dragAsset != null) dragAssetActive = true;
        if (Event.current.type == EventType.MouseDrag && dragEndpoint != null) dragEndpointActive = true;

        // 縮放畫布先畫；固定面板最後畫，吸收 IMGUI 縮放在邊界可能漏出的次像素。
        DrawCenter(center);
        DrawLibraryPanel(left);
        DrawToolbar(toolbar);
        DrawResizeGrip(leftHandle, true, resizingLeftPanel);

        if (dragAssetActive) DrawDragAssetGhost();
        // 放置模式沒有按住按鍵，收不到 MouseDrag；要 MouseMove 殘影才跟得上滑鼠。
        wantsMouseMove = placingSlot != null;
        if (dragEndpointActive) DrawDragVariableGhost();
        if (placingSlot != null) DrawPlacingGhost();
        if (Event.current.rawType == EventType.MouseUp)
        {
            if (Event.current.button == 0) EndLink();
            ClearPendingLibraryDrag();
            // 放開才真的搬：拖曳中途放棄不會留下任何改動。
            if (dragListRow != null && dragListTarget >= 0 && dragListTarget != dragListIndex)
                MoveListItem(dragListRow, dragListIndex, dragListTarget);
            dragListRow = null;
            dragListIndex = -1;
            dragListTarget = -1;
        }
        if (Event.current.type == EventType.MouseDrag || linking || dragAssetActive || dragEndpointActive
            || placingSlot != null) Repaint();
        ShowPendingConfirm();
        UpdateUnsavedState();
    }

    /// <summary>
    /// 待開的就地確認框。**一律排到 OnGUI 結尾才 Show**，因為 `PopupWindow.Show` 是拿當下的 GUI 座標
    /// 換算螢幕位置的：在 ScrollView 或 zoom group 裡呼叫會偏掉，從 GenericMenu 的回呼呼叫更是完全沒有
    /// GUI 座標可用。錨點 rect 由呼叫端先換成視窗座標存進來。
    /// </summary>
    private void RequestConfirm(Rect windowAnchor, string message, string confirmLabel, Action onConfirm)
    {
        pendingConfirmAnchor = windowAnchor;
        pendingConfirm = new AGConfirmPopup(message, confirmLabel, onConfirm);
        Repaint();
    }

    private void ShowPendingConfirm()
    {
        if (pendingConfirm == null || Event.current.type != EventType.Repaint) return;
        var popup = pendingConfirm;
        pendingConfirm = null;
        PopupWindow.Show(pendingConfirmAnchor, popup);
    }

    /// <summary>清掉左欄拖曳（資產／變數）的待處理狀態。按下與放開都要清，兩邊都不能只靠一邊。</summary>
    private void ClearPendingLibraryDrag()
    {
        dragAssetActive = false;
        dragAsset = null;
        pendingAssetFocus = null;
        dragEndpointActive = false;
        dragEndpoint = null;
        pendingVariableFocus = null;
    }

    private void GetLayout(out Rect toolbar, out Rect left, out Rect center, out Rect leftHandle)
    {
        float maxLeft = Mathf.Max(MinLeftWidth, position.width - MinCenterWidth);
        leftWidth = Mathf.Clamp(leftWidth, MinLeftWidth, maxLeft);

        toolbar = new Rect(0f, 0f, position.width, ToolbarHeight);
        left = new Rect(0f, ToolbarHeight, leftWidth, position.height - ToolbarHeight);
        center = new Rect(left.xMax, ToolbarHeight, position.width - left.xMax, position.height - ToolbarHeight);
        leftHandle = new Rect(left.xMax - ResizeHandleWidth * 0.5f, ToolbarHeight, ResizeHandleWidth, left.height);
    }

    /// <summary>分隔把手先處理事件，畫布不能攔截欄位縮放拖曳。</summary>
    private void HandlePanelResize(Rect leftHandle)
    {
        var e = Event.current;
        EditorGUIUtility.AddCursorRect(leftHandle, MouseCursor.ResizeHorizontal);

        if (e.type == EventType.MouseDown && e.button == 0 && leftHandle.Contains(e.mousePosition))
        {
            resizingLeftPanel = true;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingLeftPanel)
        {
            leftWidth = Mathf.Clamp(leftWidth + e.delta.x, MinLeftWidth, position.width - MinCenterWidth);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingLeftPanel)
        {
            resizingLeftPanel = false;
            e.Use();
        }
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
    /// 還沒選對象時的閒置版型：兩欄框架照畫，只有左上角的對象選擇器可用，其餘全部停用。
    /// </summary>
    private void DrawIdle(Rect toolbar, Rect left, Rect center)
    {
        AGStyles.Fill(toolbar, AGStyles.Toolbar);

        // Bind 不依賴既有狀態，閒置沒理由只留 Project 選取一條路；位置與綁定後的 DrawToolbar 一致。
        var ownerPickerRect = new Rect(toolbar.x + 4f, toolbar.y + 2f, 18f, 18f);
        if (GUI.Button(ownerPickerRect, new GUIContent("", "選擇編輯對象"), EditorStyles.popup))
            AGOwnerIndex.ShowPicker(ownerPickerRect, PickOwner);

        GUI.Label(new Rect(toolbar.x + 26f, toolbar.y + 2f, toolbar.width - 200f, 18f),
            "尚未選擇編輯對象", EditorStyles.boldLabel);

        GUI.enabled = false;
        float x = toolbar.xMax - 6f;
        x -= 96f; GUI.Button(new Rect(x, toolbar.y + 1f, 94f, 19f), "存檔");
        x -= 62f; GUI.Button(new Rect(x, toolbar.y + 1f, 60f, 19f), "取消");
        GUI.enabled = true;

        DrawIdlePanel(left, "變數庫");

        AGStyles.Fill(center, AGStyles.Canvas);
        var header = new Rect(center.x, center.y, center.width, HeaderHeight);
        AGStyles.Fill(header, AGStyles.PanelSection);
        AGStyles.Frame(header, AGStyles.NodeBorder);
        GUI.Label(new Rect(header.x + 6f, header.y + 3f, header.width - 12f, 18f), "（沒有編輯對象）", EditorStyles.boldLabel);
        GUI.Label(new Rect(header.x + 6f, header.y + 24f, header.width - 12f, 16f),
            "按左上角的選擇器挑一個對象，或從 Project／Hierarchy 點選含 ActionSystem 的對象。", AGStyles.Tiny);

        var canvas = new Rect(center.x, center.y + HeaderHeight, center.width, center.height - HeaderHeight - MinConsole);
        AGStyles.Fill(canvas, AGStyles.Canvas);
        DrawGrid(canvas);

        var console = new Rect(center.x, canvas.yMax, center.width, MinConsole);
        AGStyles.Fill(console, AGStyles.Console);
        AGStyles.Frame(console, AGStyles.NodeBorder);
        GUI.Label(new Rect(console.x + 6f, console.y + 3f, console.width - 12f, 16f), "尚未驗證", AGStyles.Tiny);
    }

    private static void DrawIdlePanel(Rect r, string title)
    {
        AGStyles.Fill(r, AGStyles.Panel);
        AGStyles.Frame(r, AGStyles.NodeBorder);
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), title, AGStyles.PanelHeader);

        var body = new Rect(r.x + 2f, r.y + 22f, r.width - 4f, r.height - 26f);
        AGStyles.Fill(body, AGStyles.PanelList);
    }


    private void EnsureGraph()
    {
        if (!graphDirty && graph != null) return;
        graphDirty = false;
        carrierUsers.Clear();
        model.ClearAssetParameterCache();

        // 補參數列不標髒：它是冪等的重建產物（新列預設不覆蓋，不改執行結果），
        // 沒存到就下次重建再補。標髒會讓「只是切焦點看一眼」變成要求存檔。
        bool bindingsChanged = false;
        foreach (var carrier in CurrentCarrierScope())
            if (model.EnsureAssetBindings(carrier)) bindingsChanged = true;
        if (bindingsChanged && focus.Kind != AGFocusKind.Asset)
            reportStale = true;   // 參數列變了，驗證報告要重跑

        // 候選池掛在焦點的頭端上，不必再依 FocusId 過濾。
        model.OrphanHead = focus.Head;

        // 一顆 HEAD 都沒有也要建：時機畫布可能還沒有任何時機節點，但候選節點仍要畫出來。
        graph = focus.Kind == AGFocusKind.None
            ? new AGGraphView()
            : AGGraph.Build(model, focus.Roots, OrphansOfCurrentFocus(), focus.Id, focus.HeadTitle,
                listCollapse, noteOpenId, noteCollapsed, focus.HeadCarrier, orphanKindHints);

        ApplyVisibility();
        if (pendingCenterTarget != null) { CenterOn(pendingCenterTarget); pendingCenterTarget = null; }
    }

    /// <summary>
    /// 套用 Slot 的分支收合。圖一律建到底再標記，因為「有沒有別的欄位在用」要走完整張圖才算得準；
    /// 建圖時邊走邊剪會看不到後面才出現的引用。
    /// </summary>
    private void ApplyVisibility()
    {
        foreach (var n in graph.Nodes) n.Hidden = false;
        effectiveHidden.Clear();
        if (graph.Nodes.Count == 0) return;

        if (soloSlotKey != null)
        {
            ApplySolo();
            effectiveHidden.Add(soloSlotKey);
            MarkHiddenSlots();
            return;
        }

        // 收合的是**欄位**不是節點：先把所有「該收起來」的欄位挑出來。
        foreach (var n in graph.Nodes)
        {
            foreach (var row in AGGraph.AllRows(n.Rows))
            {
                if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
                if (IsSlotHidden(n, row)) effectiveHidden.Add(AGGraph.CollapseKey(n.Id, row));
            }
        }

        // 節點畫不畫，看它還有沒有一條「從 HEAD／候選出發、中途不經過任何收合欄位」的路徑。
        // 共用節點因此在最後一個還要求顯示它的欄位被收起來時才跟著消失。用可達性算而不是引用計數：
        // 計數擋不住「引用者自己也被收掉了」這種間接情況。
        var visible = new HashSet<AGNode>();
        foreach (var n in graph.Nodes)
            if (n.ParentRow == null) MarkVisibleFrom(n, visible);

        foreach (var n in graph.Nodes) n.Hidden = !visible.Contains(n);

        MarkHiddenSlots();
    }

    /// <summary>
    /// 清掉所有「只跟目前這張圖有關」的視覺狀態。換編輯對象或回到閒置時一定要呼叫：
    /// 這些集合的 key 是節點 Id + 欄位路徑，換了對象就再也對不到人，留著只會累積，
    /// 還可能讓下一個對象一打開就是收合的樣子。
    /// </summary>
    private void ClearViewState()
    {
        slotHidden.Clear();
        effectiveHidden.Clear();
        soloRestore.Clear();
        soloSlotKey = null;
        listCollapse.Clear();
        noteCollapsed.Clear();
        noteOpenId = null;
        carrierUsers.Clear();
        selectedIds.Clear();
    }

    /// <summary>
    /// 這個欄位收起來了沒。只認使用者手動切換過的記錄，沒有記錄就是展開。
    /// 曾經加過自動收起未選取動作子樹，已移除：`slotHidden` 是 window 欄位，
    /// 重開視窗會清空，於是自動規則整批重新套用，看起來就是「我沒收的也被收走了」。
    /// 效能真的成為問題時再處理，不要用會讓畫面自己變動的規則換。
    /// </summary>
    private bool IsSlotHidden(AGNode owner, AGRow row)
        => slotHidden.TryGetValue(AGGraph.CollapseKey(owner.Id, row), out bool stored) && stored;

    /// <summary>
    /// 把「目標已經被藏起來」的欄位補進 effectiveHidden，收合鈕才會畫成 +。
    /// solo 模式尤其需要：那些欄位沒有手動記錄，是被 solo 連帶收掉的。
    /// </summary>
    private void MarkHiddenSlots()
    {
        foreach (var n in graph.Nodes)
        {
            if (n.Hidden) continue;
            foreach (var row in AGGraph.AllRows(n.Rows))
            {
                if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
                if (!graph.BySlot.TryGetValue(row.Slot, out var target) || !target.Hidden) continue;
                effectiveHidden.Add(AGGraph.CollapseKey(n.Id, row));
            }
        }
    }

    /// <summary>solo：只留下這個 Slot 的子樹，以及持有它的節點與祖先。</summary>
    private void ApplySolo()
    {
        var keep = new HashSet<AGNode>();
        var row = FindSlotRow(soloSlotKey);
        if (row?.Slot != null && graph.BySlot.TryGetValue(row.Slot, out var target)) MarkSubtree(target, keep);

        // 持有這個欄位的節點、以及它一路往上的祖先都要留著。把來路藏掉的話，
        // 畫面上會剩一段浮在空中、看不出從哪裡接出來的子樹，連要退出 solo 的那顆開關都不見了。
        KeepAncestors(NodeById(row?.OwnerNodeId), keep);

        foreach (var n in graph.Nodes)
        {
            if (n.IsRoot) continue;
            n.Hidden = !keep.Contains(n);
        }
    }

    /// <summary>從這顆節點沿 ParentRow 一路往上留到根。seen 獨立於 keep，避免資料成環時停不下來。</summary>
    private void KeepAncestors(AGNode node, HashSet<AGNode> keep)
    {
        var seen = new HashSet<AGNode>();
        while (node != null && seen.Add(node))
        {
            keep.Add(node);
            node = node.ParentRow != null ? NodeById(node.ParentRow.OwnerNodeId) : null;
        }
    }

    private AGNode NodeById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var n in graph.Nodes)
            if (n.Id == id) return n;
        return null;
    }

    private void MarkSubtree(AGNode node, HashSet<AGNode> into)
    {
        if (node == null || !into.Add(node)) return;
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Slot == null) continue;
            if (graph.BySlot.TryGetValue(row.Slot, out var child)) MarkSubtree(child, into);
        }
    }

    /// <summary>
    /// 這條線畫不畫。三種都要擋：目標被收起來、起點節點被收起來、以及**起點那一列自己被收合**——
    /// 最後一種在目標被別的欄位撐著仍要顯示時才看得到差別，漏掉的話收合鈕畫成 + 卻還牽著一條線。
    /// </summary>
    private bool IsLinkVisible(AGLink link)
    {
        if (link?.ParentRow == null || link.Target == null || link.Target.Hidden) return false;
        if (link.Owner != null && link.Owner.Hidden) return false;
        return !effectiveHidden.Contains(AGGraph.CollapseKey(link.ParentRow.OwnerNodeId, link.ParentRow));
    }

    /// <summary>
    /// 從這顆節點沿「沒有收起來」的欄位往下走，走得到的節點都要畫。沒有父列的節點（HEAD 與候選）
    /// 是起點，永遠畫。visible 兼作環的護欄。
    /// </summary>
    private void MarkVisibleFrom(AGNode node, HashSet<AGNode> visible)
    {
        if (node == null || !visible.Add(node)) return;
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Slot == null) continue;
            if (effectiveHidden.Contains(AGGraph.CollapseKey(node.Id, row))) continue;
            if (graph.BySlot.TryGetValue(row.Slot, out var child)) MarkVisibleFrom(child, visible);
        }
    }

    private AGRow FindSlotRow(string key)
    {
        foreach (var n in graph.Nodes)
            foreach (var row in AGGraph.AllRows(n.Rows))
                if (row.Kind == AGRowKind.Slot && AGGraph.CollapseKey(n.Id, row) == key) return row;
        return null;
    }

    /// <summary>
    /// 切換一個 Slot 的顯示。alt＝solo（只留這一段），再按一次還原成 solo 之前的隱藏集合，
    /// 不是全部展開——不然會把使用者原本收好的東西一起吹掉。
    /// </summary>
    private void ToggleSlotVisibility(string key, bool solo)
    {
        if (solo && soloSlotKey != key)
        {
            // 進 solo 前把手動記錄整份存起來，退出時才還原得回去，而不是變成全展開。
            if (soloSlotKey == null)
            {
                soloRestore.Clear();
                foreach (var kv in slotHidden) soloRestore[kv.Key] = kv.Value;
            }
            soloSlotKey = key;
        }
        else
        {
            // 退出 solo（再按一次、或在 solo 中按了一般開關）：先把世界還原成 solo 之前的樣子。
            bool wasSolo = soloSlotKey != null;
            if (wasSolo)
            {
                soloSlotKey = null;
                slotHidden.Clear();
                foreach (var kv in soloRestore) slotHidden[kv.Key] = kv.Value;
                soloRestore.Clear();
            }
            // 從 solo 退出的那一下只負責退出：使用者還沒看到還原後的樣子，不該同時再改動一項。
            if (!solo && !wasSolo) slotHidden[key] = !effectiveHidden.Contains(key);
        }

        // 純視覺：只重建圖與重畫，不可以走 Invalidate，否則按個收合鈕就把資產標成未存檔。
        graphDirty = true;
        Repaint();
    }

    /// <summary>
    /// 目前焦點的候選節點。時機畫布的候選掛在 ActionSystem 身上（＝model.OrphanHead），
    /// 但合併畫布之前存下來的候選掛在個別動作頭端上，所以要一起讀回來，不然舊資產一開就少一批節點。
    /// </summary>
    private IList OrphansOfCurrentFocus()
    {
        if (focus.Kind != AGFocusKind.Timing) return AGReflect.Orphans(focus.Head);

        var all = new List<object>();
        Append(all, AGReflect.Orphans(model.Data));
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            foreach (var slot in g.Actions) Append(all, AGReflect.Orphans(slot));
        }
        return all;
    }

    private static void Append(List<object> into, List<GraphNode> nodes)
    {
        if (nodes == null) return;
        foreach (var n in nodes)
            if (n != null) into.Add(n);
    }

    /// <summary>目前畫面該用哪一份驗證結果：資產焦點只看資產自己的。</summary>
    private AGReport Rep => focus.Kind == AGFocusKind.Asset ? assetReport : report;

    /// <summary>
    /// 座標這種「寫進載體、但不動圖結構也不必重跑驗證」的修改。
    /// Owner 焦點由 `AGModel.SetPosition` 內部的 `MarkDirty()` 記；**資產焦點的 `TrackChanges` 是關的**，
    /// 那條路整個 early-return，所以要在這裡補記 `assetDirty`——否則搬完節點存檔鈕還是灰的，一離開位置就沒了。
    /// </summary>
    private void MarkPositionsChanged()
    {
        if (focus.Kind != AGFocusKind.Asset) return;
        // 資產本體畫布的 HEAD 座標直接寫在資產 SO 上（不在工作副本裡），Unity 要 SetDirty 才會落檔。
        if (focus.Endpoint == null && focus.AssetObject != null) EditorUtility.SetDirty(focus.AssetObject);
        // 只設 assetDirty：座標不影響執行語意，存檔時不必重驗、也不必通知任何 subscriber。
        assetDirty = true;
        // 座標也要進歷程：Owner 側的 SetPosition 本來就記 Undo，兩邊行為不一致比沒有還難用。
        assetHistory.Record(CaptureAssetState());
        UpdateUnsavedState();
    }

    /// <summary>資產內容變更（會改變執行語意的修改）。純座標／視覺調整請走 <see cref="MarkPositionsChanged"/>。</summary>
    private void MarkAssetContentChanged()
    {
        assetDirty = true;
        assetContentDirty = true;
        assetReportStale = true;
        assetHistory.Record(CaptureAssetState());
    }

    /// <summary>
    /// 目前資產工作副本的快照。內容、候選與變數**同一次深複製**：分次抄會把同一顆端點抄成
    /// 幾份不相干的物件，變數節點指到的就不是清單裡那一顆（進出資產的交易也是同一條規則）。
    /// </summary>
    private AGAssetSnapshot CaptureAssetState()
    {
        if (focus.Kind != AGFocusKind.Asset || focus.AssetHostSlot == null) return null;

        var pack = new List<object>
        {
            AGReflect.GetNode(focus.AssetHostSlot),
            focus.AssetOrphans ?? new List<GraphNode>(),
            focus.AssetEndpoints ?? new List<GraphEndpoint>(),
        };
        var copy = ActionSystemDeepCopy.Copy(pack);
        if (copy == null)
        {
            Debug.LogError("[ActionGraph] 無法建立資產快照，這一步不會進復原歷程。");
            return null;
        }
        return new AGAssetSnapshot
        {
            Root = copy[0] as GraphNode,
            Orphans = copy[1] as List<GraphNode> ?? new List<GraphNode>(),
            Endpoints = copy[2] as List<GraphEndpoint> ?? new List<GraphEndpoint>(),
        };
    }

    /// <summary>
    /// 把快照換成活的工作副本。整批端點物件都被換掉，所以正在編的變數子畫布要**靠 Id 重指**；
    /// 那顆變數在這一步被刪掉的話就退回資產本體，畫面上不會停在一張查不到主人的空白圖。
    /// </summary>
    private void ApplyAssetState(AGAssetSnapshot snapshot)
    {
        if (snapshot == null || focus.AssetHostSlot == null) return;

        AGReflect.SetNode(focus.AssetHostSlot, snapshot.Root);
        focus.AssetOrphans = snapshot.Orphans;
        focus.AssetEndpoints = snapshot.Endpoints;

        string endpointId = focus.Endpoint?.Id;
        focus.Endpoint = string.IsNullOrEmpty(endpointId)
            ? null
            : snapshot.Endpoints.Find(e => e != null && e.Id == endpointId);

        // 套用後的那份快照已經是活資料，歷程不能跟它共用參考，否則下一次修改會連歷程一起改掉。
        assetHistory.Rebase(CaptureAssetState());

        // 退回去的狀態和磁碟上那份多半仍不同，一律當未存檔；驗證就地重跑，不必先標 stale。
        assetDirty = true;
        assetContentDirty = true;
        selectedIds.Clear();
        graphDirty = true;
        DoVerify(true);
        UpdateUnsavedState();
        Repaint();
    }

    /// <summary>復原一步。資產焦點走自己的歷程，其餘走 Owner 工作副本那份。</summary>
    private bool CanUndoNow => focus.Kind == AGFocusKind.Asset ? assetHistory.CanUndo : model?.CanUndo == true;
    private bool CanRedoNow => focus.Kind == AGFocusKind.Asset ? assetHistory.CanRedo : model?.CanRedo == true;

    private bool DoUndo()
    {
        if (focus.Kind != AGFocusKind.Asset)
        {
            if (!model.Undo()) return false;
            AfterHistorySwap();
            return true;
        }
        var snapshot = assetHistory.Undo(CaptureAssetState());
        if (snapshot == null) return false;
        ApplyAssetState(snapshot);
        return true;
    }

    private bool DoRedo()
    {
        if (focus.Kind != AGFocusKind.Asset)
        {
            if (!model.Redo()) return false;
            AfterHistorySwap();
            return true;
        }
        var snapshot = assetHistory.Redo(CaptureAssetState());
        if (snapshot == null) return false;
        ApplyAssetState(snapshot);
        return true;
    }

    /// <summary>強制切一個復原記錄點。呼叫點不必知道現在是哪一種焦點，路由在這裡。</summary>
    private void BreakUndoMerge()
    {
        if (focus.Kind == AGFocusKind.Asset) assetHistory.BreakMerge();
        else model?.BreakUndoMerge();
    }

    private void ClearAssetDirty()
    {
        assetDirty = false;
        assetContentDirty = false;
    }

    private void Invalidate()
    {
        graphDirty = true;
        // 資產是獨立存檔交易，改它不算改 Owner，也不進 Owner 的 Undo 堆疊。
        if (focus.Kind == AGFocusKind.Asset)
        {
            MarkAssetContentChanged();
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
        if (e.type != EventType.KeyDown) return;

        // 放置模式攔在最前面：Esc 取消，不必先把滑鼠移回畫布。
        if (e.keyCode == KeyCode.Escape && placingSlot != null)
        {
            placingSlot = null;
            e.Use();
            Repaint();
            return;
        }
        if (!e.control) return;

        if (e.keyCode == KeyCode.Z && !e.shift)
        {
            if (!DoUndo()) ShowNotification(new GUIContent("沒有可復原的步驟"));
            e.Use();
        }
        else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift))
        {
            if (!DoRedo()) ShowNotification(new GUIContent("沒有可重做的步驟"));
            e.Use();
        }
        else if (e.keyCode == KeyCode.S)
        {
            if (focus.Kind == AGFocusKind.Asset) SaveAsset(); else DoSave();
            e.Use();
        }
    }

    /// <summary>
    /// Undo/Redo 換掉整份資料後，焦點抓的是舊圖的參考。時機畫布只認工作副本本身、群組清單是現讀的，
    /// 所以重指一次就好；標註節點也住在同一張畫布上，不需要另外解析。
    /// </summary>
    private void AfterHistorySwap()
    {
        focus = focus.Kind == AGFocusKind.Timing ? AllTimingsFocus() : new AGFocus();

        selectedIds.Clear();
        graphDirty = true;
        DoVerify(true);
        UpdateUnsavedState();
        Repaint();
    }

    // ===== 頂部 =====

    private void DrawToolbar(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Toolbar);

        // 麵包屑：資產是獨立一層，未儲存狀態與外層各記各的。
        var ownerPickerRect = new Rect(r.x + 4f, r.y + 2f, 18f, 18f);
        GUI.enabled = focus.Kind != AGFocusKind.Asset;
        if (GUI.Button(ownerPickerRect, new GUIContent("", "換編輯對象"), EditorStyles.popup))
            AGOwnerIndex.ShowPicker(ownerPickerRect, PickOwner);
        GUI.enabled = true;

        string ownerPath = AssetDatabase.GetAssetPath(model.Owner);
        if (string.IsNullOrEmpty(ownerPath)) ownerPath = "Scene";
        bool inAsset = focus.Kind == AGFocusKind.Asset;
        string crumb = $"{model.Owner.name} ({model.Owner.GetType().Name})({ownerPath})";
        GUI.Label(new Rect(ownerPickerRect.xMax + 4f, r.y + 2f, r.width - 440f, 18f), crumb, EditorStyles.boldLabel);

        // 即時檢查一有錯就把存檔鈕關掉；沒有錯時仍可按，存檔當下再跑一次嚴格驗證。
        bool blocked = !Rep.CanSave;
        // 資產只搬過座標時不擋：內容沒變，存回去的東西跟磁碟上一樣，不該被它本來就有的錯誤鎖住位置。
        if (inAsset && !assetContentDirty) blocked = false;
        // 共用資產存檔會把引用它的 Owner 標成未驗證，但工作副本一個字都沒改（Dirty=false）。
        // 存檔是唯一會重跑 Core Verify 並寫回 Owner 的入口，這時候不開它就沒有任何路可以把圖救回已驗證。
        bool needsRevalidate = !inAsset && model.Owner is IActionSystemOwner asOwner && !asOwner.IsActionSystemValidated();
        bool hasChanges = inAsset ? assetDirty : (model.Dirty || needsRevalidate);
        bool canSave = hasChanges && !blocked;

        float x = r.xMax - 6f;
        x -= 96f;
        var saveRect = new Rect(x, r.y + 1f, 94f, 19f);
        GUI.enabled = canSave;
        var saveColor = GUI.backgroundColor;
        if (canSave) GUI.backgroundColor = new Color(0.85f, 0.28f, 0.28f);
        bool revalidateOnly = needsRevalidate && !model.Dirty;
        string saveLabel = blocked ? "存檔（有錯誤）" : revalidateOnly ? "存檔（未驗證）" : "存檔";
        string saveTooltip = blocked ? "驗證有錯誤，先在 Console 修正才能存檔"
            : revalidateOnly ? "這份圖目前未驗證（多半是引用的資產改過），按下後重跑 Core 驗證並寫回"
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
        // 資產焦點的「返回」與存檔分開：存檔留在畫布上，返回才退出（有未存修改會先問要不要捨棄）。
        var backLabel = inAsset
            ? new GUIContent("返回", "回到上一層；有未儲存的修改會先問要不要捨棄")
            : new GUIContent("取消", "捨棄自上次存檔以來的所有修改");
        if (GUI.Button(new Rect(x, r.y + 1f, 60f, 19f), backLabel))
        {
            if (inAsset) LeaveAsset(); else DoCancel();
        }

        // 資產焦點也有復原：它走自己的歷程（AGAssetHistory），與 Owner 那份互不干擾。
        x -= 48f;
        GUI.enabled = CanRedoNow;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("重做", "Ctrl+Y / Ctrl+Shift+Z"))) DoRedo();
        GUI.enabled = true;

        x -= 48f;
        GUI.enabled = CanUndoNow;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("復原", "Ctrl+Z"))) DoUndo();
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
}

}
