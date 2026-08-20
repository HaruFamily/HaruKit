namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 畫布滑鼠與鍵盤互動：拖曳、框選、複製貼上、刪除，以及視圖與焦點切換。
/// </summary>
public partial class ActionGraphWindow
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
                // 走到這裡代表這一下不是按在接點上（按在接點的那一下已經被 DrawSlotRow 吃掉），
                // 所以先清掉殘留：在畫布外放開滑鼠時 MouseUp 收不到，記錄會留到下一次操作。
                portClickRow = null;
                // 這一下多半會被下面 e.Use() 掉，左欄與焦點標題列的改名欄就再也收不到它——先替它們收尾。
                CommitInlineName();
                if (e.button == 0 && OutputNodeAt(graphMouse) is AGNode outputNode)
                {
                    BeginLinkFromNode(outputNode);
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
                    else ShowCanvasMenu(graphMouse);
                    e.Use();
                }
                else if (e.button == 0)
                {
                    if (e.clickCount == 2 && hit != null && hit.IsAssetNode && hit.Asset != null) { EnterAsset(hit); e.Use(); break; }
                    // 雙擊變數節點＝下鑽進那個變數的畫布，跟雙擊資產節點同一個手勢。
                    if (e.clickCount == 2 && hit != null && hit.IsVariableNode && hit.Endpoint != null) { EnterVariable(hit.Endpoint); e.Use(); break; }

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

                    if (new Rect(hit.Pos.x, hit.Pos.y, hit.Width, AGGraph.HeaderHeight).Contains(graphMouse))
                    {
                        dragNode = hit;
                        dragOffset = graphMouse - hit.Pos;
                        dragStartPositions.Clear();
                        foreach (var n in graph.Nodes)
                            if (selectedIds.Contains(n.Id)) dragStartPositions[n.Id] = n.Pos;

                        titleClickNode = hit.HasSourceSelector && hit.SourceMenuRect.Contains(graphMouse) ? hit : null;
                        titleClickStart = graphMouse;
                    }
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                // 接點按著往外拖＝拉線；沒超過門檻前什麼都不做，放開才知道是不是收合。
                if (portClickRow != null && !linking
                    && (graphMouse - portClickStart).sqrMagnitude > PortClickSlop * PortClickSlop)
                {
                    var from = portClickRow;
                    portClickRow = null;
                    if (!from.Locked) BeginLinkFromRow(from);   // 鎖住的列接上去也不會被採用
                    e.Use();
                    break;
                }
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
                if (portClickRow != null)
                {
                    var pressed = portClickRow;
                    portClickRow = null;
                    if (AGReflect.GetNode(pressed.Slot) != null)
                    {
                        ToggleSlotVisibility(AGGraph.CollapseKey(pressed.OwnerNodeId, pressed), e.alt);
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
                        dragStartPositions.Clear();
                        // 選單在 clip 外開，錨點 rect 必須換回 window space。用整條名稱區當錨點，
                        // 選單才對齊 Header 而不是縮在那顆 18px 的 ▾ 底下。
                        ShowNodeSourceSelector(clicked, GraphToWindowRect(clicked.TitleRect));
                        e.Use();
                        break;
                    }
                }
                if (dragNode != null)
                {
                    BreakUndoMerge();
                    foreach (var n in graph.Nodes)
                        if (dragStartPositions.ContainsKey(n.Id)) model.SetPosition(n.Id, n.Pos);
                    model.SetPosition(dragNode.Id, dragNode.Pos);
                    MarkPositionsChanged();
                    dragStartPositions.Clear();
                    dragNode = null;
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
                    if (linkRow != null) ResolveLink(graphMouse);
                    else ResolveLinkFromOutput(graphMouse);
                    EndLink();
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
                if (dragEndpointActive && dragEndpoint != null)
                {
                    DropEndpointOn(dragEndpoint, graphMouse);
                    dragEndpointActive = false;
                    dragEndpoint = null;
                    pendingVariableFocus = null;
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
        BreakUndoMerge();
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
        BreakUndoMerge();
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
            if (!graph.Nodes[i].Hidden && graph.Nodes[i].Rect.Contains(graphPoint)) return graph.Nodes[i];
        return null;
    }

    private AGNode OutputNodeAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (node.IsRoot || node.Hidden) continue;
            var port = new Rect(node.OutputPort - Vector2.one * AGGraph.PortRadius,
                Vector2.one * AGGraph.PortDiameter);
            if (port.Contains(graphPoint)) return node;
        }
        return null;
    }

    private void ResetLayout()
    {
        if (graph == null) return;
        foreach (var node in graph.Nodes) model.ClearPosition(node.Id);
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
        EnsureHeadIds(next);

        focus = next;
        if (model != null) model.TrackChanges = next.Kind != AGFocusKind.Asset;
        CancelInlineName();
        selectedIds.Clear();
        graphDirty = true;
        Repaint();
    }

    /// <summary>頭端第一次被聚焦時補一個穩定識別碼；焦點與座標都靠它。</summary>
    private void EnsureHeadIds(AGFocus next)
    {
        object head = next.Head;
        if (next.Kind == AGFocusKind.Action && head != null
            && string.IsNullOrEmpty(AGReflect.SlotEditorId(head)))
        {
            AGReflect.EnsureSlotEditorId(head);
            model.MarkDirty();
        }
    }

    /// <summary>
    /// 時機畫布：所有時機群組畫在同一張圖上，一個時機一顆節點。一顆群組都還沒有也照樣成立——
    /// 畫布會顯示「新增時機節點」的佔位，不在這裡偷偷建資料。
    /// </summary>
    private AGFocus AllTimingsFocus()
        => model?.Data == null
            ? new AGFocus()
            : new AGFocus { Kind = AGFocusKind.Timing, Data = model.Data };
}

}
