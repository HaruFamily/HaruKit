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

                    if (new Rect(hit.Pos.x, hit.Pos.y, hit.Width, AGGraph.HeaderHeight).Contains(graphMouse))
                    {
                        dragNode = hit;
                        dragOffset = graphMouse - hit.Pos;
                        dragStartPositions.Clear();
                        foreach (var n in graph.Nodes)
                            if (selectedIds.Contains(n.Id)) dragStartPositions[n.Id] = n.Pos;

                        titleClickNode = hit.HasSourceSelector && hit.TitleRect.Contains(graphMouse) ? hit : null;
                        titleClickStart = graphMouse;
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
                if (titleClickNode != null)
                {
                    var clicked = titleClickNode;
                    titleClickNode = null;
                    if ((graphMouse - titleClickStart).sqrMagnitude <= TitleClickSlop * TitleClickSlop)
                    {
                        dragNode = null;
                        dragStartPositions.Clear();
                        // 選單在 clip 外開，錨點 rect 必須換回 window space。
                        ShowNodeSourceSelector(clicked, GraphToWindowRect(clicked.TitleRect));
                        e.Use();
                        break;
                    }
                }
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
}

}
