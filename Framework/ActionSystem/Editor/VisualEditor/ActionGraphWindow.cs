namespace PinPlugin.ActionSystem.Editor
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
    private const float AssetCellHeight = 34f;
    private const string PrefConsoleHeight = "ActionGraph.ConsoleHeight";
    private const string PrefConsoleCollapsed = "ActionGraph.ConsoleCollapsed";
    private const string PrefLeftWidth = "ActionGraph.LeftWidth";
    private const string PrefRightWidth = "ActionGraph.RightWidth";

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
    private float rightWidth = DefaultRightWidth;
    private bool resizingLeftPanel;
    private bool resizingRightPanel;
    private int consoleTab;                  // 0 全部 / 1 錯誤 / 2 警告
    private int libraryTab;                  // 0 變數 / 1 資產
    private string tokenSearch = "";
    private string assetSearch = "";
    private object editingNameTarget;
    private string editingNameDraft = "";

    // 互動
    private AGNode dragNode;
    private Vector2 dragOffset;
    private readonly Dictionary<string, Vector2> dragStartPositions = new();

    // Header 的 ▾ 落在拖曳抓取區裡，所以仍要分辨拖曳：按下時先記著，放開時沒移動超過門檻才算點擊。
    private AGNode titleClickNode;
    private Vector2 titleClickStart;
    private const float TitleClickSlop = 4f;
    /// <summary>Header 右端 ▾ 的寬度。放大到 18px 是因為 0.45 倍縮放下它只剩 8px，再小就按不到。</summary>
    private const float SourceArrowWidth = 18f;

    private bool linking;
    private AGRow linkRow;
    private AGNode linkNode;

    // 拉線期間的相容性：起手時對全圖判定一次，之後高亮與吸附都讀這份，不必每幀重算。
    private readonly HashSet<string> linkCompatibleNodeIds = new();
    private readonly HashSet<AGRow> linkCompatibleRows = new();
    private ScriptableObject dragAsset;
    private bool dragAssetActive;
    private ScriptableObject pendingAssetFocus;
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
    private AGReport assetReport = new();
    private Vector2 referenceScroll;

    private bool HasUnsavedWork => model?.Dirty == true || assetDirty;

    /// <summary>
    /// 右欄還有沒有內容。時機節點與動作清單都在畫布上，所以右欄只剩下資產焦點的引用清單
    /// ——那份沒有別的地方可去（資產焦點沒有時機節點）。
    /// </summary>
    private bool HasRightPanel => focus.Kind == AGFocusKind.Asset;
    private bool IsCurrentReportFresh => focus.Kind == AGFocusKind.Asset
        ? assetVerifiedOnce && !assetReportStale
        : verifiedOnce && !reportStale;

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
        if (Event.current.type == EventType.MouseDrag && dragAsset != null) dragAssetActive = true;

        // 縮放畫布先畫；固定面板最後畫，吸收 IMGUI 縮放在邊界可能漏出的次像素。
        DrawCenter(center);
        DrawLibraryPanel(left);
        if (HasRightPanel) DrawReferencePanel(right);
        DrawToolbar(toolbar);
        DrawPanelResizeHandles(leftHandle, rightHandle);

        if (dragAssetActive) DrawDragAssetGhost();
        if (Event.current.rawType == EventType.MouseUp)
        {
            if (Event.current.button == 0) EndLink();
            dragAssetActive = false;
            dragAsset = null;
            pendingAssetFocus = null;
            // 放開才真的搬：拖曳中途放棄不會留下任何改動。
            if (dragListRow != null && dragListTarget >= 0 && dragListTarget != dragListIndex)
                MoveListItem(dragListRow, dragListIndex, dragListTarget);
            dragListRow = null;
            dragListIndex = -1;
            dragListTarget = -1;
        }
        if (Event.current.type == EventType.MouseDrag || linking || dragAssetActive) Repaint();
        UpdateUnsavedState();
    }

    private void GetLayout(out Rect toolbar, out Rect left, out Rect right, out Rect center, out Rect leftHandle, out Rect rightHandle)
    {
        float maxLeft = Mathf.Max(MinLeftWidth, position.width - rightWidth - MinCenterWidth);
        leftWidth = Mathf.Clamp(leftWidth, MinLeftWidth, maxLeft);
        float maxRight = Mathf.Max(MinRightWidth, position.width - leftWidth - MinCenterWidth);
        rightWidth = Mathf.Clamp(rightWidth, MinRightWidth, maxRight);

        // Timing 焦點的內容全在畫布上，右欄沒有東西可放，整條收掉把寬度讓給畫布。
        // 記住的 rightWidth 不動，切回別的焦點時原封不動地回來。
        float shownRight = HasRightPanel ? rightWidth : 0f;

        toolbar = new Rect(0f, 0f, position.width, ToolbarHeight);
        left = new Rect(0f, ToolbarHeight, leftWidth, position.height - ToolbarHeight);
        right = new Rect(position.width - shownRight, ToolbarHeight, shownRight, position.height - ToolbarHeight);
        center = new Rect(left.xMax, ToolbarHeight, right.xMin - left.xMax, position.height - ToolbarHeight);
        leftHandle = new Rect(left.xMax - ResizeHandleWidth * 0.5f, ToolbarHeight, ResizeHandleWidth, left.height);
        // 右欄收起時把把手也收成零寬：留著會在畫布右緣壓出一條抓不到東西的縮放區。
        rightHandle = HasRightPanel
            ? new Rect(right.xMin - ResizeHandleWidth * 0.5f, ToolbarHeight, ResizeHandleWidth, right.height)
            : new Rect(position.width, ToolbarHeight, 0f, right.height);
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
    /// 還沒選對象時的閒置版型：三欄框架照畫，只有左上角的對象選擇器可用，其餘全部停用。
    /// </summary>
    private void DrawIdle(Rect toolbar, Rect left, Rect right, Rect center)
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

        DrawIdlePanel(left, "資料庫");
        DrawIdlePanel(right, "引用");

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

        bool bindingsChanged = false;
        foreach (var carrier in CurrentTokenScope())
            if (model.EnsureAssetBindings(carrier)) bindingsChanged = true;
        if (bindingsChanged)
        {
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
            UpdateUnsavedState();
        }

        // 候選池掛在焦點的頭端上，不必再依 FocusId 過濾。
        model.OrphanHead = focus.Head;

        // 一顆 HEAD 都沒有也要建：時機畫布可能還沒有任何時機節點，但候選節點仍要畫出來。
        graph = focus.Kind == AGFocusKind.None
            ? new AGGraphView()
            : AGGraph.Build(model, focus.Roots, OrphansOfCurrentFocus(), focus.Id, focus.HeadTitle,
                listCollapse, noteOpenId, noteCollapsed);

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

        // 先把所有「該收起來」的欄位挑出來，再一起收：邊挑邊收會讓後面的欄位落在已經收掉的節點上而查不到。
        foreach (var n in graph.Nodes)
        {
            foreach (var row in AGGraph.AllRows(n.Rows))
            {
                if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
                if (IsSlotHidden(n, row)) effectiveHidden.Add(AGGraph.CollapseKey(n.Id, row));
            }
        }

        foreach (var n in graph.Nodes)
        {
            foreach (var row in AGGraph.AllRows(n.Rows))
            {
                if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
                if (!effectiveHidden.Contains(AGGraph.CollapseKey(n.Id, row))) continue;
                if (graph.BySlot.TryGetValue(row.Slot, out var target)) HideSubtree(target);
            }
        }

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

    private void HideSubtree(AGNode node)
    {
        if (node?.Carrier == null || node.IsRoot || node.Hidden) return;
        // 被別的欄位共用的節點留著：它不只屬於這一段，收掉會讓另一條線斷在空白處。
        if (graph.CarrierUsers.TryGetValue(node.Carrier, out int users) && users > 1) return;

        node.Hidden = true;
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Slot == null) continue;
            if (graph.BySlot.TryGetValue(row.Slot, out var child)) HideSubtree(child);
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
            assetReport = AGValidator.RunSubtree(model, focus, focus.AssetHostSlot, focus.AssetOrphans, focus.Title);
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
}

}
