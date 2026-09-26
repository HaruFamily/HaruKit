namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 畫布滑鼠與鍵盤互動：拖曳、框選、複製貼上、刪除，以及視圖與焦點切換。
/// </summary>
public partial class HaruGraphWindow
{
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
                // 走到這裡代表這一下不是按在輸入接點上（按在輸入接點的那一下已經被 DrawInputPortRow 吃掉），
                // 所以先清掉殘留：在畫布外放開滑鼠時 MouseUp 收不到，記錄會留到下一次操作。
                inputPortClickPort = null;
                CancelNodeGroupDrag();
                // 這一下多半會被下面 e.Use() 掉，左欄與焦點標題列的改名欄就再也收不到它——先替它們收尾。
                inlineName.Commit();
                if (e.button == 0 && InputPortAt(graphMouse) is HGPort inputPort)
                {
                    inputPortClickPort = inputPort;
                    inputPortClickStart = graphMouse;
                    e.Use();
                    break;
                }
                if (e.button == 0 && OutputPortAt(graphMouse) is HGPort outputPort)
                {
                    // PropertySlot 清單的新增接點是輸出角色，但和取值清單同一個手勢：原地放開＝折疊，拖出去才拉線。
                    if (outputPort.Binding is IHGListAppendBinding)
                    {
                        inputPortClickPort = outputPort;
                        inputPortClickStart = graphMouse;
                        e.Use();
                        break;
                    }
                    BeginLink(outputPort);
                    e.Use();
                    break;
                }
                // 放置模式吃掉這一下點擊：左鍵落下節點，右鍵取消，兩者都不再往下走選取與框選。
                if (placingSlot != null)
                {
                    if (e.button == 0) PlaceNewSource(placingSlot, graphMouse);
                    placingSlot = null;
                    e.Use();
                    break;
                }

                var hit = NodeAt(graphMouse);
                if (e.button == 1)
                {
                    if (hit != null) ShowNodeMenu(hit);
                    else if (NodeGroupHeaderAt(graphMouse) is GraphNodeGroup group) { SelectNodeGroup(group); ShowNodeGroupMenu(group); }
                    else ShowCanvasMenu(graphMouse, NodeGroupContaining(graphMouse));
                    e.Use();
                }
                else if (e.button == 0)
                {
                    // 左鍵按在群組標題列以外的地方都放掉群組選取；按在標題列由 TryBeginNodeGroupDrag 重新選。
                    selectedNodeGroupId = null;
                    if (e.clickCount == 2 && hit != null && hit.IsAssetNode && hit.Asset != null) { EnterAsset(hit); e.Use(); break; }
                    // 雙擊Token節點＝下鑽進那個Token的畫布，跟雙擊資產節點同一個手勢。
                    if (e.clickCount == 2 && hit != null && hit.IsTokenNode && hit.Token != null) { EnterToken(hit.Token); e.Use(); break; }

                    // 一般線畫在節點底下：點在節點上時看不到線，就不能把它剪掉；選取中的線浮在上層，才照樣可剪。
                    var link = LinkAt(graphMouse);
                    if (link != null && (hit == null || IsTracedLink(link))) { CutLink(link); e.Use(); break; }

                    // 群組框在節點與連線底下：只有按在空白處時才輪到它的標題列，群組內部空白照常框選。
                    if (hit == null && TryBeginNodeGroupDrag(graphMouse)) { e.Use(); break; }

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

                    if (new Rect(hit.Pos.x, hit.Pos.y, hit.Width, HGGraph.HeaderHeight).Contains(graphMouse))
                    {
                        titleClickNode = hit.HasSourceSelector && hit.SourceMenuRect.Contains(graphMouse) ? hit : null;
                        titleClickStart = graphMouse;
                        dragNode = hit;
                        dragOffset = graphMouse - hit.Pos;
                        dragMoved = false;
                        dragStartPositions.Clear();
                        foreach (var n in graph.Nodes)
                            if (selectedIds.Contains(n.Id)) dragStartPositions[n.Id] = n.Pos;
                        FreezeNodeGroupRects();
                    }
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                // 接點按著往外拖＝拉線；沒超過門檻前什麼都不做，放開才知道是不是收合。
                if (inputPortClickPort != null && !linking
                    && (graphMouse - inputPortClickStart).sqrMagnitude > InputPortClickSlop * InputPortClickSlop)
                {
                    var from = inputPortClickPort;
                    inputPortClickPort = null;
                    BeginLink(from);
                    e.Use();
                    break;
                }
                if (dragNode != null)
                {
                    dragMoved = true;
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
                else if (dragNodeGroup != null)
                {
                    DragNodeGroup(graphMouse);
                    e.Use();
                }
                else if (boxSelecting)
                {
                    boxEnd = graphMouse;
                    e.Use();
                }
                // 平移只認中鍵。曾經跟著 Unity Scene View 的慣例做過 Alt+左鍵平移，已移除：
                // Alt 在這張圖是 Slot 分支收合的 solo，兩者會在同一次拖曳裡打架，而中鍵已經夠用。
                // hotControl 檢查留著：拖著輸入框時按中鍵，畫布也不該跟著跑。
                else if (e.button == 2 && GUIUtility.hotControl == 0)
                {
                    pan += e.delta / zoom;
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                // 接點原地放開＝收合這個欄位底下的子樹（Alt＝solo）。沒接來源的接點沒有子樹，放開就當沒事。
                if (inputPortClickPort != null)
                {
                    var pressedPort = inputPortClickPort;
                    inputPortClickPort = null;
                    HGRow pressed = OwnerRowOfPort(pressedPort);
                    // 清單標題的新增接點原地放開＝收起／展開所有元素的子樹（Alt＝solo），與 Slot 接點同一套；
                    // 清單列的折疊只在標題文字上。沒有任何元素接線時沒有子樹可收，放開就當沒事。
                    if (pressedPort.Binding is IHGListAppendBinding && pressed != null)
                    {
                        if (HasConnectedElement(pressed))
                            ToggleSlotVisibility(HGGraph.CollapseKey(pressed.OwnerNodeId, pressed), e.alt);
                        e.Use();
                        break;
                    }
                    if (pressed?.InputSlot?.Node != null)
                    {
                        ToggleSlotVisibility(HGGraph.CollapseKey(pressed.OwnerNodeId, pressed), e.alt);
                        e.Use();
                        break;
                    }
                }
                if (titleClickNode != null)
                {
                    var clicked = titleClickNode;
                    titleClickNode = null;
                    if ((graphMouse - titleClickStart).sqrMagnitude <= TitleClickSlop * TitleClickSlop)
                    {
                        dragNode = null;
                        dragMoved = false;
                        dragStartPositions.Clear();
                        frozenNodeGroupRects.Clear();
                        // 選單在 clip 外開，錨點 rect 必須換回 window space。用整條名稱區當錨點，
                        // 選單才對齊 Header 而不是縮在那顆 18px 的 ▾ 底下。
                        ShowNodeSourceSelector(clicked, GraphToWindowRect(clicked.TitleRect));
                        e.Use();
                        break;
                    }
                }
                if (dragNode != null)
                {
                    // 只有真的拖動過才落盤。單純點一下也寫的話，還沒有座標記憶的節點會被寫入
                    // AutoLayout 算出來的位置（SetPosition 的「值沒變就跳過」對它不成立），畫面沒變卻要求存檔。
                    if (dragMoved)
                    {
                        BreakUndoMerge();
                        var moved = new List<HGNodeView>();
                        foreach (var n in graph.Nodes)
                            if (dragStartPositions.ContainsKey(n.Id)) { model.SetPosition(n.Id, n.Pos); moved.Add(n); }
                        model.SetPosition(dragNode.Id, dragNode.Pos);
                        if (!moved.Contains(dragNode)) moved.Add(dragNode);
                        MarkPositionsChanged();
                        // 放手時滑鼠在哪個群組框裡＝進那個群組，在框外＝移出；與座標同一步 Undo。
                        if (ApplyDropMembership(moved, graphMouse)) MarkViewStateChanged();
                    }
                    frozenNodeGroupRects.Clear();
                    dragMoved = false;
                    dragStartPositions.Clear();
                    dragNode = null;
                    e.Use();
                }
                if (dragNodeGroup != null)
                {
                    EndNodeGroupDrag();
                    e.Use();
                }
                if (boxSelecting)
                {
                    var box = BoxRect();
                    foreach (var n in graph.Nodes)
                        if (!n.Hidden && box.Overlaps(n.Rect)) selectedIds.Add(n.Id);
                    boxSelecting = false;
                    e.Use();
                }
                if (linking && e.button == 0)
                {
                    ResolveLink(graphMouse);
                    EndLink();
                    e.Use();
                }
                if (drag.DroppingAsset)
                {
                    DropAssetOn(graphMouse);
                    drag.ClearAsset();
                    e.Use();
                }
                if (drag.DroppingToken)
                {
                    DropTokenOn(drag.Token, graphMouse);
                    drag.ClearToken();
                    e.Use();
                }
                if (drag.DroppingProperty)
                {
                    DropPropertyOn(drag.Property, graphMouse);
                    drag.ClearProperty();
                    e.Use();
                }
                break;

            case EventType.KeyDown:
                // 選著群組時 Delete 只刪群組、節點留著；沒選群組才刪選取的節點。
                if (e.keyCode == KeyCode.Delete) { if (!DeleteSelectedNodeGroup()) DeleteSelection(); e.Use(); }
                else if (e.keyCode == KeyCode.F && !e.control) { FrameAll(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.F) { ShowNodeSearch(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.C) { CopySelection(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.V) { PasteClipboard(graphMouse); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.D) { DuplicateSelection(); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.A)
                {
                    selectedNodeGroupId = null;
                    selectedIds.Clear();
                    foreach (var n in graph.Nodes)
                        if (!n.Hidden) selectedIds.Add(n.Id);
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

    private IEnumerable<HGNodeView> SelectedNodes()
    {
        if (graph == null) yield break;
        foreach (var n in graph.Nodes)
            if (selectedIds.Contains(n.Id)) yield return n;
    }

    private void DeleteSelection()
    {
        var targets = new List<HGNodeView>(SelectedNodes());
        if (targets.Count == 0) return;
        BreakUndoMerge();
        foreach (var n in targets) DeleteNode(n, false);
        selectedIds.Clear();
        Invalidate();
    }

    /// <summary>選取中可以複製的節點。</summary>
    // 判定是「有沒有載體」而不是「Obj 是不是 GraphNodeContent」：資產、Token、目錄節點的 Obj 本來就是 null，
    // 用 Obj 判等於把這三種永遠擋在門外。HEAD 與時機節點沒有 GraphNode 載體（Carrier 天生 null），
    // 所以不必另外判它們。空節點排除——內容都還沒選，複製出來推不出族，也接不上任何欄位。
    private List<HGNodeView> CopyableSelection()
    {
        var result = new List<HGNodeView>();
        foreach (var n in SelectedNodes())
            if (n.Carrier != null && !n.IsPlaceholder) result.Add(n);
        return result;
    }

    /// <summary>
    /// 從一組載體往下掃出走得到的所有載體，以及被指到的所有 Token。
    /// Token 是葉：它的內容住在自己的畫布，跟進去會把整張圖都算成這棵子樹。
    /// </summary>
    // 形狀刻意比照 HGModel.ResetNodeIdsInternal——同一種走訪規則散成兩套遲早會不一致。
    private static void ScanSubgraph(object node, HashSet<object> visited, HashSet<object> carriers,
        HashSet<object> tokens, HashSet<object> properties)
    {
        if (node == null || !visited.Add(node)) return;
        if (node is GraphToken token) { tokens.Add(token); return; }
        if (node is GraphProperty property) { properties.Add(property); return; }
        if (node is GraphNode carrier) carriers.Add(carrier);

        foreach (var f in HGReflect.Fields(node.GetType()))
        {
            if (f.IsStatic || f.IsNotSerialized) continue;
            var val = f.GetValue(node);
            if (val == null) continue;
            var t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || val is string || val is UnityEngine.Object) continue;

            if (val is IList list)
            {
                foreach (var item in list) ScanSubgraph(item, visited, carriers, tokens, properties);
                continue;
            }
            ScanSubgraph(val, visited, carriers, tokens, properties);
        }
    }

    /// <summary>
    /// 把一組載體複製成一片獨立的子圖：這組節點**彼此之間**的線保留成共用，指到組外的線一律清成空槽。
    /// 所以單顆複製＝所有子邊都是組外邊＝完全沒有連線；多顆複製＝只有這些節點之間的線活下來。
    /// </summary>
    // shared 同時擋兩件事，理由不同：
    //   組外載體——跟著抄會憑空多出一份沒有人看得到的分身，使用者以為只複製了選取的那幾顆。
    //   GraphToken——跟著抄會變成不在清單裡的孤兒端點，參照得到卻永遠查不到值，只有存檔時
    //   HGValidator.InScope 那一關才看得出來。
    // 兩者都先原樣沿用（shared 的語意就是「不複製、沿用同一個」），抄完再把指向**組外載體**的槽清掉；
    // Token 刻意留著不清，那才是「複本引用同一個 Token」該有的結果。
    // copies 與 sources 依索引對齊：GraphDeepCopy 複製 IList 時逐項 Add，順序不變，呼叫端靠它配對座標。
    internal static List<GraphNode> CloneSubgraph(IReadOnlyList<GraphNode> sources, out List<GraphNode> roots)
    {
        roots = new List<GraphNode>();
        if (sources == null || sources.Count == 0) return null;

        var selection = new HashSet<object>(HGRefComparer.Instance);
        foreach (var c in sources) if (c != null) selection.Add(c);

        var carriers = new HashSet<object>(HGRefComparer.Instance);
        var tokens = new HashSet<object>(HGRefComparer.Instance);
        var properties = new HashSet<object>(HGRefComparer.Instance);
        var scanned = new HashSet<object>(HGRefComparer.Instance);
        foreach (var c in sources) ScanSubgraph(c, scanned, carriers, tokens, properties);

        var boundary = new HashSet<object>(HGRefComparer.Instance);
        foreach (var c in carriers) if (!selection.Contains(c)) boundary.Add(c);

        var shared = new List<object>(tokens);
        // ProtoProperty 是庫定義的引用：必須沿用同一顆，否則會抄出一份不在庫裡的定義。
        // LocalProperty 相反，它是節點私有的——跟著節點複製才不會讓複本與原件共用同一個儲存位置與型別宣告。
        foreach (var item in properties)
            if (item is GraphProperty property && property.Proto) shared.Add(property);
        shared.AddRange(boundary);

        var copies = GraphDeepCopy.Copy(new List<GraphNode>(sources), shared);
        if (copies == null) return null;

        // 走複本一遍做兩件事：指到組外載體的槽清成空槽，順便記下誰是別人的子節點。
        // 清成空槽不是錯誤狀態——公式欄位退回 _default、動作欄位變空槽，驗證不會有話說。
        // walked 先塞 Token 與組外載體：它們是**原件**，走進去等於在原圖上亂剪線。
        var children = new HashSet<object>(HGRefComparer.Instance);
        var walked = new HashSet<object>(HGRefComparer.Instance);
        foreach (var t in tokens) walked.Add(t);
        foreach (var b in boundary) walked.Add(b);
        foreach (var copy in copies)
            foreach (var slot in HGModel.WalkSlots(copy, walked))
            {
                if (HGReflect.GetNode(slot) is not GraphNode child) continue;
                if (boundary.Contains(child)) HGReflect.ClearNode(slot);
                else children.Add(child);
            }

        // 這時複本已經不指著任何組外載體，走訪跑不回原圖；Token 仍是沿用的，所以要當成走過了。
        foreach (var copy in copies) HGModel.ResetNodeIds(copy, tokens);
        foreach (var copy in copies) if (!children.Contains(copy)) roots.Add(copy);
        return copies;
    }

    /// <summary>把複本放進圖裡：只有沒有父節點的那幾顆進候選池，其餘由父節點牽著就畫得出來。</summary>
    // 全部塞進候選池也畫得出來（HGGraph.Build 依載體去重），但刪掉父節點之後子節點會無端留在畫布上。
    // 之後若使用者自己剪斷那條線，AttachSource 本來就會把它補進候選池，不必在這裡先放。
    private void PlaceCopies(List<GraphNode> copies, List<GraphNode> roots, string verb)
    {
        BreakUndoMerge();
        selectedIds.Clear();
        foreach (var root in roots) model.AddOrphan(root);
        foreach (var copy in copies) selectedIds.Add(copy.EnsureId());
        Invalidate();
        Repaint();
        ShowNotification(new GUIContent($"{verb} {copies.Count} 個節點"));
    }

    /// <summary>剪貼簿裡有沒有指向目前作用域以外的 Token。</summary>
    private bool HasForeignToken(IReadOnlyList<GraphNode> nodes)
    {
        var known = new HashSet<object>(HGRefComparer.Instance);
        foreach (var t in CurrentTokens() ?? new List<GraphToken>()) if (t != null) known.Add(t);

        var carriers = new HashSet<object>(HGRefComparer.Instance);
        var tokens = new HashSet<object>(HGRefComparer.Instance);
        var properties = new HashSet<object>(HGRefComparer.Instance);
        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var n in nodes) ScanSubgraph(n, visited, carriers, tokens, properties);

        foreach (var t in tokens) if (!known.Contains(t)) return true;
        return false;
    }

    private void CopySelection()
    {
        var picked = CopyableSelection();
        if (picked.Count == 0) { ShowNotification(new GUIContent("沒有可複製的節點")); return; }

        var sources = new List<GraphNode>();
        foreach (var n in picked) sources.Add(n.Carrier);
        var copies = CloneSubgraph(sources, out _);
        if (copies == null) { ShowNotification(new GUIContent("複製失敗")); return; }

        // 剪貼簿存相對座標，貼上時整團平移到滑鼠：多選複製的版面關係才留得住。
        var origin = picked[0].Pos;
        foreach (var n in picked) origin = Vector2.Min(origin, n.Pos);
        for (int i = 0; i < copies.Count; i++) copies[i].Pos = picked[i].Pos - origin;

        clipboard.Clear();
        clipboard.AddRange(copies);
        ShowNotification(new GUIContent($"已複製 {clipboard.Count} 個節點"));
    }

    private void PasteClipboard(Vector2 graphMouse)
    {
        if (clipboard.Count == 0) { ShowNotification(new GUIContent("剪貼簿是空的")); return; }

        // 再抄一份：剪貼簿要能貼很多次，直接把它放進圖裡會讓每一份共用同一批物件。
        var copies = CloneSubgraph(clipboard, out var roots);
        if (copies == null) { ShowNotification(new GUIContent("貼上失敗")); return; }

        var origin = SnapToGrid(graphMouse);
        for (int i = 0; i < copies.Count; i++) copies[i].Pos = origin + clipboard[i].Pos;

        // 剪貼簿是 static，可能來自別張圖或別個資產：那裡的 Token 在這個作用域查不到值。
        // 不擋（跨圖搬節點是合理的需求），但提示要換掉，否則只會在存檔時看到一條 InScope 錯誤。
        bool foreign = HasForeignToken(copies);
        PlaceCopies(copies, roots, "已貼上");
        if (foreign)
            ShowNotification(new GUIContent("貼上的節點引用了這張圖沒有的 Token，請重新指定"));
    }

    /// <summary>就地複製（Ctrl+D）：等同複製後貼在原件旁邊，不經過也不覆蓋剪貼簿。</summary>
    private void DuplicateSelection()
    {
        var picked = CopyableSelection();
        if (picked.Count == 0) { ShowNotification(new GUIContent("沒有可複製的節點")); return; }

        var sources = new List<GraphNode>();
        foreach (var n in picked) sources.Add(n.Carrier);
        var copies = CloneSubgraph(sources, out var roots);
        if (copies == null) { ShowNotification(new GUIContent("複製失敗")); return; }

        var offset = new Vector2(DuplicateOffset, DuplicateOffset);
        for (int i = 0; i < copies.Count; i++) copies[i].Pos = picked[i].Pos + offset;
        PlaceCopies(copies, roots, "已複製");
    }

    /// <summary>游標下畫面最上層的節點。`graph.Nodes` 的順序就是繪製順序，越後面越上層。</summary>
    private HGNodeView NodeAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
            if (!graph.Nodes[i].Hidden && graph.Nodes[i].Rect.Contains(graphPoint)) return graph.Nodes[i];
        return null;
    }

    /// <summary>把節點提到最上層並記住。已經在最上層時回 false，順序不動。</summary>
    private bool RaiseNode(HGNodeView node)
    {
        if (graph == null || node == null || string.IsNullOrEmpty(node.Id)) return false;
        int index = graph.Nodes.IndexOf(node);
        if (index < 0 || index == graph.Nodes.Count - 1) return false;

        graph.Nodes.RemoveAt(index);
        graph.Nodes.Add(node);
        raisedNodeIds.Remove(node.Id);
        raisedNodeIds.Add(node.Id);
        // 只需要記得最近碰過的幾顆；更早的順序讓給建圖順序，清單才不會隨使用時間一直長。
        if (raisedNodeIds.Count > RaisedNodeLimit) raisedNodeIds.RemoveAt(0);
        return true;
    }

    /// <summary>重建圖之後，把記住的上層順序套回新的節點清單。</summary>
    private void ApplyNodeOrder()
    {
        if (graph == null || raisedNodeIds.Count == 0) return;
        foreach (string id in raisedNodeIds)
        {
            int index = graph.Nodes.FindIndex(n => n.Id == id);
            if (index < 0) continue;
            var node = graph.Nodes[index];
            graph.Nodes.RemoveAt(index);
            graph.Nodes.Add(node);
        }
    }

    /// <summary>接點命中只認游標下最上層的節點：被蓋住的節點，它的接點也算被蓋住。</summary>
    private bool IsPortOnTop(HGPort port, Vector2 graphPoint)
    {
        var top = NodeAt(graphPoint);
        return top == null || ReferenceEquals(OwnerNodeOfPort(port), top);
    }

    private void ResetLayout()
    {
        if (graph == null) return;
        foreach (var node in graph.Nodes)
            model.ClearPosition(node.Id);
        MarkPositionsChanged();
        graphDirty = true;
        Repaint();
    }

    private void FrameAll()
    {
        if (graph == null || graph.Nodes.Count == 0) return;
        var bounds = graph.Nodes[0].Rect;
        foreach (var n in graph.Nodes)
        {
            if (n.Hidden) continue;      // 收起來的節點不該把視野拉到看不見的地方
            bounds.xMin = Mathf.Min(bounds.xMin, n.Rect.xMin);
            bounds.yMin = Mathf.Min(bounds.yMin, n.Rect.yMin);
            bounds.xMax = Mathf.Max(bounds.xMax, n.Rect.xMax);
            bounds.yMax = Mathf.Max(bounds.yMax, n.Rect.yMax);
        }
        FrameRect(bounds);
    }

    /// <summary>把視野對準一塊 graph space 範圍：縮放到裝得下（留邊），中心對齊。</summary>
    private void FrameRect(Rect bounds)
    {
        zoom = Mathf.Clamp(Mathf.Min(canvasRect.width / (bounds.width + 80f), canvasRect.height / (bounds.height + 80f)), 0.45f, 1.4f);
        pan = new Vector2(canvasRect.width * 0.5f / zoom - bounds.center.x, canvasRect.height * 0.5f / zoom - bounds.center.y);
        Repaint();
    }

    private void CenterOn(object slotOrNode)
    {
        if (graph == null || slotOrNode == null) return;
        foreach (var node in graph.Nodes)
        {
            bool match = ReferenceEquals(node.Obj, slotOrNode) || ReferenceEquals(node.Carrier, slotOrNode);
            if (!match)
                foreach (var row in HGGraph.AllRows(node.Rows))
                    if (ReferenceEquals(row.InputSlot, slotOrNode)) { match = true; break; }
            if (!match) continue;

            selectedIds.Clear();
            selectedIds.Add(node.Id);
            pan = new Vector2(canvasRect.width * 0.5f / zoom - node.Rect.center.x,
                              canvasRect.height * 0.5f / zoom - node.Rect.center.y);
            return;
        }
    }

    /// <summary>
    /// Ctrl+F：列出目前畫布的所有節點（含被收起的），選中後走 <see cref="FocusDocumentNode"/> 展開收合、選取並置中。
    /// 展開會寫回文件的收合版面，和 Console 跳轉一樣記成版面修改。
    /// </summary>
    private void ShowNodeSearch()
    {
        if (graph == null) return;
        var entries = new List<HGNodeSearchEntry>();
        var groups = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id)) continue;
            var top = NodeSearchTop(node);
            string group = top.IsRoot || top.IsTimingGroup ? top.Title : "候選";
            groups.Add(group);
            entries.Add(new HGNodeSearchEntry { Id = node.Id, Name = NodeSearchName(node), Group = group });
        }
        if (entries.Count == 0)
        {
            ShowNotification(new GUIContent("這張畫布沒有節點可以搜尋。"));
            return;
        }
        // Token／資產焦點只有一顆 HEAD，全部在同一個資料夾時直接攤在根層。
        if (groups.Count < 2)
            foreach (var entry in entries) entry.Group = null;

        var anchor = new Rect(canvasRect.center.x - 210f, canvasRect.y + 8f, 420f, 0f);
        HGNodeSearchDropdown.Show(anchor, entries, id => FocusDocumentNode(id));
    }

    /// <summary>沿 ParentRow 往上找到沒有父欄位的那顆：root HEAD、時機節點或候選。</summary>
    private HGNodeView NodeSearchTop(HGNodeView node)
    {
        var seen = new HashSet<HGNodeView>();
        var top = node;
        while (top.ParentRow != null && seen.Add(top))
        {
            var parent = NodeById(top.ParentRow.OwnerNodeId);
            if (parent == null) break;
            top = parent;
        }
        return top;
    }

    /// <summary>
    /// 搜尋清單的一行：只寫節點名稱。Token／資產／Property 節點的名稱只是種類，後面補上引用對象，才分得出是哪一個。
    /// AdvancedDropdown 只比對這串文字，所以搜得到的也只有這些。
    /// </summary>
    private static string NodeSearchName(HGNodeView node)
    {
        string reference = node.Token?.Name ?? (node.Asset != null ? node.Asset.name : null) ?? node.Property?.Name;
        return string.IsNullOrEmpty(reference) ? node.Title : $"{node.Title} {reference}";
    }

    private void SetFocus(HGFocus next)
    {
        if (next == null) return;
        EnsureHeadIds(next);

        ClearPortInteractionState();
        focus = next;
        if (model != null) model.TrackChanges = next.Kind != HGFocusKind.Asset;
        inlineName.Cancel();
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>頭端第一次被聚焦時補一個穩定識別碼；焦點與座標都靠它。</summary>
    // 補完不標髒：id 只是編輯期識別碼，沒落盤下次重生即可。真正需要它落盤的是記座標，
    // 而 SetPosition 自己就會 MarkLayoutChanged，會把這個 id 一起帶走——純瀏覽因此不再要求存檔。
    private void EnsureHeadIds(HGFocus next)
    {
        object head = next.Head;
        if (next.Kind == HGFocusKind.Action && head != null
            && string.IsNullOrEmpty(HGReflect.SlotEditorId(head)))
        {
            HGReflect.EnsureSlotEditorId(head);
        }
    }

    /// <summary>
    /// 時機畫布：所有時機群組畫在同一張圖上，一個時機一顆節點。一顆群組都還沒有也照樣成立——
    /// 畫布會顯示「新增時機節點」的佔位，不在這裡偷偷建資料。
    /// </summary>
    private HGFocus AllRootsFocus()
        => model?.Data == null
            ? new HGFocus()
            : new HGFocus
            {
                Kind = HGFocusKind.Root,
                Data = model.Data,
                RootGroupsProvider = model.ReadRootGroups,
            };
}

}
