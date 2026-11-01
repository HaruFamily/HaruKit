namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class HaruGraphWindow
{
    private HGRow NodeSocketAt(Vector2 point, HGNodeView moving)
    {
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var owner = graph.Nodes[i];
            if (owner.Hidden || HGGraph.NestedWithin(owner, moving) || !NodeContainsPoint(owner, point)) continue;
            foreach (var row in HGGraph.AllRows(owner.Rows))
                if (row.NodeSocket && !row.Hidden && !row.IsTabHidden && row.ScreenRect.Contains(point)) return row;
            return null;
        }
        return null;
    }

    private bool CanComposeDrop(HGNodeView node, HGRow row, GraphSlotBase existingListSlot = null)
    {
        var target = row?.InputSlot ?? existingListSlot;
        if (target is UnionSlotBase union) target = union.SelectedSlot ?? target;
        var previous = node?.NestedSlot?.InputSlot;
        if (previous is UnionSlotBase wrapper) previous = wrapper.SelectedSlot ?? previous;
        foreach (var user in HGModel.NodeUsers(node.Carrier, SlotsInCurrentGraph()))
            if (!ReferenceEquals(user, target) && !ReferenceEquals(user, previous)) return false;
        return true;
    }

    private bool CanDropNodeInSocket(HGNodeView node, HGRow row)
        => node?.Carrier != null && node.Carrier.Kind is NodeKind.Empty or NodeKind.Inline or NodeKind.Token or NodeKind.Asset or NodeKind.Property
            && row?.InputSlot != null && CanComposeDrop(node, row)
            && graph.PrimaryOutputs.TryGetValue(node.Carrier, out var output)
            && graph.PrimaryInputs.TryGetValue(row.InputSlot, out var input)
            && HGPortConnection.Check(output, input, graphGeneration) == HGPortConnectionResult.Allowed;

    private static bool HasSlotPanel(HGRow row)
        => !row.Hidden && !row.IsTabHidden
            && (row.NodeList || row.NestedNode != null && row.ItemOwnerRow?.NodeList != true);

    private static bool NodeContainsPoint(HGNodeView node, Vector2 point)
    {
        if (node.Rect.Contains(point)) return true;
        if (node.Collapsed) return false;
        foreach (var row in HGGraph.AllRows(node.Rows))
            if (HasSlotPanel(row) && HGGraph.SlotBounds(node, row).Contains(point)) return true;
        return false;
    }

    private static void DrawSlotPanel(Rect rect)
    {
        HGStyles.RoundedFill(rect, HGStyles.ListBand, 3f);
        HGStyles.RoundedFrame(rect, HGStyles.ListRule, 3f);
    }

    private void DrawNodeFrameWithSlots(HGNodeView node, Rect rect, Color color, float radius, float thickness)
    {
        var cuts = new List<Vector2>();
        if (!node.Collapsed)
            foreach (var row in HGGraph.AllRows(node.Rows))
                if (HasSlotPanel(row))
                {
                    var slot = HGGraph.SlotBounds(node, row);
                    if (slot.xMax > node.Rect.xMax)
                        cuts.Add(new Vector2(slot.yMin + pan.y, slot.yMax + pan.y));
                }
        if (cuts.Count == 0) { HGStyles.RoundedFrame(rect, color, radius, thickness); return; }
        cuts.Sort((a, b) => a.x.CompareTo(b.x));
        void DrawClipped(Rect clip)
        {
            if (clip.width <= 0f || clip.height <= 0f) return;
            GUI.BeginClip(clip);
            var local = rect; local.position -= clip.position;
            HGStyles.RoundedFrame(local, color, radius, thickness);
            GUI.EndClip();
        }
        float edge = rect.xMax - radius - thickness;
        DrawClipped(new Rect(rect.xMin, rect.yMin, edge - rect.xMin, rect.height));
        float y = rect.yMin;
        foreach (var cut in cuts)
        {
            float start = Mathf.Clamp(cut.x, rect.yMin, rect.yMax);
            DrawClipped(new Rect(edge, y, rect.xMax - edge, Mathf.Max(0f, start - y)));
            y = Mathf.Max(y, Mathf.Min(cut.y, rect.yMax));
        }
        DrawClipped(new Rect(edge, y, rect.xMax - edge, rect.yMax - y));
    }

    private void DrawNodeSocket(HGRow row, Rect rect)
    {
        var owner = OwnerOfRow(row);
        if (owner != null && row.NestedNode != null)
        {
            rect = HGGraph.SlotBounds(owner, row); rect.position += pan;
            if (HasSlotPanel(row)) DrawSlotPanel(rect);
        }
        if (dragMoved && dragNode != null && ReferenceEquals(row.NestedNode, dragNode) && owner != null)
        {
            var vacancy = SocketNodePreviewRect(owner, row, dragNode);
            HGStyles.RoundedFill(vacancy, new Color(0.4f, 0.65f, 0.7f, 0.08f), 4f);
            HGStyles.RoundedFrame(vacancy, new Color(0.5f, 0.7f, 0.75f, 0.35f), 4f);
        }
    }

    private Rect SocketNodePreviewRect(HGNodeView owner, HGRow row, HGNodeView node)
        => new Rect(owner.Pos + pan + new Vector2(HGGraph.NestedContentInset(row),
            row.SocketContentY + HGGraph.NestedPadding), new Vector2(node.Width, node.Height));

    private void DrawSocketDropPreview()
    {
        if (!dragMoved || dragNode == null || selectedIds.Count != 1 || Event.current.type != EventType.Repaint) return;
        // 清單新增落點由插入線表示，不與單一 Slot 的填入預覽重疊。
        if (ActionGapAt(Event.current.mousePosition - pan, dragNode, out _) != null) return;
        var row = NodeSocketAt(Event.current.mousePosition - pan, dragNode);
        if (row == null) return;
        var owner = OwnerOfRow(row);
        if (owner == null) return;
        bool allowed = CanDropNodeInSocket(dragNode, row);
        var accent = allowed ? new Color(0.3f, 0.8f, 0.85f, 0.9f) : HGStyles.Error;
        // 高亮與實際落點使用同一範圍，不依拖入 Node 的大小延伸。
        var slot = row.ScreenRect; slot.position += pan;
        HGStyles.RoundedFill(slot, new Color(accent.r, accent.g, accent.b, 0.13f), 3f);
        HGStyles.RoundedFrame(slot, accent, 3f);
    }

    private bool FinishNodeSocketDrag(HGNodeView node, Vector2 point)
    {
        if (node?.Carrier == null) return false;
        if (!graph.ByCarrier.TryGetValue(node.Carrier, out var current) || !ReferenceEquals(current, node))
        { graphDirty = true; Repaint(); return true; }
        if (node.NestedParent != null && !canvasRect.Contains(Event.current.mousePosition))
        { RestoreNodeSocketDrag(); return true; }
        if (selectedIds.Count != 1)
        {
            if (node.NestedParent == null) return false;
            RestoreNodeSocketDrag(); return true;
        }
        var listRow = ActionGapAt(point, node, out int gap);
        if (listRow != null) return FinishActionListDrop(node, listRow, gap);
        var row = NodeSocketAt(point, node);
        var previous = node.NestedSlot?.InputSlot;
        if (row == null && previous == null) return false;
        if (row != null && node.NestedSlot?.IsItem == true && !ReferenceEquals(row.InputSlot, node.NestedSlot.InputSlot))
        {
            ShowNotification(new GUIContent("請將項目拖到清單插入線，以保留原設定。"));
            RestoreNodeSocketDrag(); return true;
        }
        if (row != null && !CanDropNodeInSocket(node, row))
        {
            ShowNotification(new GUIContent("這個槽位不接受此 Node，或會形成循環。"));
            RestoreNodeSocketDrag(); return true;
        }
        if (row != null && ReferenceEquals(row.InputSlot.Node, node.Carrier))
        {
            if (node.Carrier.Mode == GraphNodeMode.Connection) SetNodeMode(node, GraphNodeMode.Composition);
            else RestoreNodeSocketDrag();
            return true;
        }
        if (row == null && NodeContainsPoint(node.NestedParent, point))
        { RestoreNodeSocketDrag(); return true; }
        if (row == null)
            foreach (var other in graph.Nodes)
                if (!other.Hidden && !HGGraph.NestedWithin(other, node) && NodeContainsPoint(other, point))
                { RestoreNodeSocketDrag(); return true; }
        if (row == null && previous != null)
        {
            SetNodeMode(node, GraphNodeMode.Connection);
            return true;
        }
        BreakUndoMerge();
        ApplyGraphMutation(() =>
        {
            PreserveVisibleNodePositions();
            node.Carrier.Mode = GraphNodeMode.Composition;
            if (row != null) AttachSource(row.InputSlot, node.Carrier);
            if (node.NestedSlot?.IsItem == true && node.NestedSlot.ItemSource is HGListItemSource source)
            {
                var oldRow = node.NestedSlot;
                if (!ReferenceEquals(source.Get(oldRow.ItemIndex), previous) || !source.RemoveAt(oldRow.ItemIndex))
                    throw new ArgumentException("來源清單已變更。");
                if (!IsCarrierUsed(node.Carrier))
                {
                    model.AddOrphan(node.Carrier);
                    RememberOrphanKind(node.Carrier, previous.GetType());
                }
            }
            else if (previous != null) AttachSource(previous, null);
            if (row == null) node.Carrier.Pos = SnapToGrid(node.Pos);
        });
        Repaint();
        return true;
    }

    private void ShowSocketSourcePicker(HGRow row, Rect anchor)
    {
        var slot = row.InputSlot;
        var expected = model;
        int generation = graphGeneration;
        var choices = new List<HGSourceOption>();
        IEnumerable<Type> types = slot is FormulaSlotBase formula
            ? HGTypeIndex.FormulasFor(formula) : HGTypeIndex.Concrete(((ActionSlotBase)slot).BodyBaseType);
        foreach (var type in types)
        {
            if (HGReflect.CreateInstance(type) is not GraphNodeContent sample || !slot.AcceptsBody(sample)) continue;
            var selected = type;
            choices.Add(new HGSourceOption { Group = HGReflect.TypeCategory(type), Name = HGReflect.TypeName(type), Apply = () =>
            {
                if (!ReferenceEquals(expected, model) || generation != graphGeneration || slot.Node != null)
                { ShowNotification(new GUIContent("圖已變更，請重新選擇來源。")); return; }
                ApplyGraphMutation(() =>
                {
                    if (HGReflect.CreateInstance(selected) is not GraphNodeContent body || !slot.AcceptsBody(body))
                        throw new ArgumentException("此槽位不接受所選 Node。");
                    PreserveVisibleNodePositions();
                    var carrier = NewSource(slot);
                    carrier.SetBody(body);
                    carrier.EnsureId();
                });
            } });
        }
        RequestPopup(anchor, new HGSourceDropdown(choices, "放入 Node"));
    }

    private HGRow ActionGapAt(Vector2 point, HGNodeView moving, out int gap)
    {
        gap = -1;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var owner = graph.Nodes[i];
            if (owner.Hidden || owner.Collapsed || HGGraph.NestedWithin(owner, moving) || !NodeContainsPoint(owner, point)) continue;
            foreach (var row in HGGraph.AllRows(owner.Rows))
            {
                if (!row.NodeList || row.Collapsed || row.Hidden || row.IsTabHidden) continue;
                var slotBounds = HGGraph.SlotBounds(owner, row);
                if (point.x < slotBounds.xMin || point.x > slotBounds.xMax) continue;
                if (point.y >= owner.Pos.y + row.LocalY && point.y <= owner.Pos.y + row.LocalY + HGGraph.RowHeight)
                { gap = 0; return row; }
                for (int n = 0; n < row.Children.Count; n++)
                {
                    if (Mathf.Abs(point.y - owner.Pos.y - row.Children[n].LocalY) <= 8f)
                    { gap = n; return row; }
                    var item = row.Children[n];
                    if (item.NestedNode == null && point.y >= owner.Pos.y + item.LocalY
                        && point.y <= owner.Pos.y + item.LocalY + item.Height)
                    { gap = n + (point.y > owner.Pos.y + item.LocalY + item.Height * 0.5f ? 1 : 0); return row; }
                }
                if (Mathf.Abs(point.y - owner.Pos.y - row.AddRowY) <= 8f)
                { gap = row.Children.Count; return row; }
            }
        }
        return null;
    }

    private bool AcceptsListReference(HGRow row, GraphNode reference)
    {
        if (row?.NodeList != true || row.Items is not HGListItemSource items || !items.CanEditStructure
            || !items.TryCreateElement(out var element) || element is not GraphSlotBase slot) return false;
        return reference.Kind switch
        {
            NodeKind.Asset => CanAssignAsset(slot, reference.AssetObject),
            NodeKind.Token => slot.AcceptsToken(reference.Token),
            NodeKind.Property => slot.AcceptsProperty(reference.Property),
            _ => false,
        };
    }

    private bool InsertListReference(Vector2 point, GraphNode reference)
    {
        var row = ActionGapAt(point, null, out int gap);
        if (row == null) return false;
        if (!AcceptsListReference(row, reference))
        { ShowNotification(new GUIContent("此清單不接受這個引用。")); return true; }
        var items = (HGListItemSource)row.Items;
        BreakUndoMerge();
        ApplyGraphMutation(() =>
        {
            if (!items.TryCreateElement(out var element) || element is not GraphSlotBase slot)
                throw new ArgumentException("無法建立清單項目。");
            PreserveVisibleNodePositions();
            var owner = OwnerOfRow(row);
            reference.EnsureId();
            reference.Pos = SnapToGrid(owner.Pos + new Vector2(HGGraph.NestedVisualWidth(owner) + HGGraph.ColumnGap, row.LocalY));
            slot.SetNode(reference);
            if (slot is ActionSlotBase action) action.EnsureId();
            if (!items.Insert(gap, slot)) throw new ArgumentException("無法新增引用項目。");
        });
        return true;
    }

    private static GraphSlotBase ExistingListSlot(HGNodeView node, HGRow row)
    {
        if (node?.Carrier == null || row?.Items is not HGListItemSource list) return null;
        for (int i = 0; i < list.Count; i++)
            if (list.Get(i) is GraphSlotBase slot && ReferenceEquals(slot.Node, node.Carrier)) return slot;
        return null;
    }

    private bool CanDropAction(HGNodeView node, HGRow row)
    {
        if (node?.Carrier == null || row?.Items is not HGListItemSource list || !list.CanEditStructure
            || !CanComposeDrop(node, null, ExistingListSlot(node, row))
            || HGGraph.NestedWithin(OwnerOfRow(row), node) || HGFlowAddress.Contains(node.Carrier, list.List)) return false;
        return list.TryCreateElement(out var item) && item is GraphSlotBase slot
            && SourceAcceptance(node, slot) == HGPortConnectionResult.Allowed;
    }

    private void DrawActionDropHint(HGNodeView owner, HGRow row, Rect nodeRect)
    {
        if (!dragMoved || dragNode == null || selectedIds.Count != 1) return;
        var target = ActionGapAt(Event.current.mousePosition - pan, dragNode, out int gap);
        if (!ReferenceEquals(target, row)) return;
        float y = gap < row.Children.Count ? row.Children[gap].LocalY : row.AddRowY;
        float inset = HGGraph.NestedContentInset(row);
        float width = HGGraph.SlotBounds(owner, row).xMax - owner.Pos.x - inset - HGGraph.NestedPadding;
        HGStyles.Fill(new Rect(nodeRect.x + inset, nodeRect.y + y - 2f, width, 4f),
            CanDropAction(dragNode, row) ? HGStyles.NodeBorderSelected : HGStyles.Error);
    }

    private bool FinishActionListDrop(HGNodeView node, HGRow row, int gap)
    {
        try
        {
            if (!CanDropAction(node, row)) throw new ArgumentException("清單族不相容，或會形成循環。");
            // 外部接線拖回原清單只改模式，保留原 Slot 的順序及設定。
            if (node.NestedSlot == null && ExistingListSlot(node, row) != null)
            {
                SetNodeMode(node, GraphNodeMode.Composition);
                return true;
            }
            var destination = (HGListItemSource)row.Items;
            var previous = node.NestedSlot;
            if (previous?.IsItem == true && previous.ItemSource is HGListItemSource source)
            {
                var original = source.Get(previous.ItemIndex);
                if (!source.CanEditStructure || !ReferenceEquals(original, previous.InputSlot)
                    || !destination.ElementType.IsInstanceOfType(original))
                    throw new ArgumentException("清單項目型別不相容，或來源已變更。");
                int insertion = ReferenceEquals(source.List, destination.List) && gap > previous.ItemIndex ? gap - 1 : gap;
                if (original is ActionSlotBase)
                {
                    var plan = HGFlowStructure.ResolveTransfer(source, previous.ItemIndex, 1, destination, gap);
                    insertion = plan.Gap;
                }
                if (ReferenceEquals(source.List, destination.List) && insertion == previous.ItemIndex)
                { RestoreNodeSocketDrag(); return true; }
                BreakUndoMerge();
                ApplyGraphMutation(() =>
                {
                    PreserveVisibleNodePositions();
                    if (!source.RemoveAt(previous.ItemIndex) || !destination.Insert(insertion, original))
                        throw new ArgumentException("無法搬移原項目。");
                });
            }
            else
            {
                if (!destination.TryCreateElement(out var element) || element is not GraphSlotBase slot)
                    throw new ArgumentException("無法建立清單槽位。");
                if (slot is ActionSlotBase action)
                {
                    if (previous?.InputSlot is ActionSlotBase old) { action.Label = old.Label; action.Disabled = old.Disabled; }
                    action.EnsureId();
                }
                BreakUndoMerge();
                ApplyGraphMutation(() =>
                {
                    PreserveVisibleNodePositions();
                    node.Carrier.Mode = GraphNodeMode.Composition;
                    slot.SetNode(HGModel.PropertyReferenceForSlot(previous?.InputSlot ?? slot, node.Carrier, SlotsInCurrentGraph()));
                    if (!destination.Insert(gap, slot)) throw new ArgumentException("無法插入項目。");
                    if (previous != null) AttachSource(previous.InputSlot, null);
                    model.RemoveOrphan(node.Carrier);
                });
            }
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); RestoreNodeSocketDrag(); }
        return true;
    }

    private void ToggleNodeCollapsed(HGNodeView node)
    {
        BreakUndoMerge();
        ClearPortInteractionState();
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
        SetListFolded(HGGraph.NodeCollapseKey(node.Id), !node.Collapsed);
        graphDirty = true;
        Repaint();
    }

    private void SetNodeMode(HGNodeView node, GraphNodeMode mode)
    {
        if (node?.Carrier == null || node.InLockedSubtree || !graph.ByCarrier.TryGetValue(node.Carrier, out var current)
            || !ReferenceEquals(node, current) || node.Carrier.Mode == mode) return;
        if (mode == GraphNodeMode.Composition && HGModel.NodeUsers(node.Carrier, SlotsInCurrentGraph()).Count > 1)
        {
            ShowNotification(new GUIContent("此 Node 被多個 Slot 使用，請先解除多餘引用再改為組合模式。"));
            return;
        }
        Vector2 position = node.NestedParent == null ? node.Pos
            : node.NestedParent.Pos + new Vector2(HGGraph.NestedVisualWidth(node.NestedParent) + HGGraph.ColumnGap, node.NestedSlot.LocalY);
        if (ReferenceEquals(dragNode, node) && dragMoved) position = node.Pos;
        BreakUndoMerge();
        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            node.Carrier.Mode = mode;
            if (mode == GraphNodeMode.Connection) node.Carrier.Pos = SnapToGrid(position);
        }, out var error)) { ShowNotification(new GUIContent(error)); return; }
        Invalidate();
        Repaint();
    }

    private void AddNestedListOptions(HGNodeView node, List<HGSourceOption> options)
    {
        if (node.NestedSlot == null || node.InLockedSubtree) return;
        options.Add(new HGSourceOption { Group = "清單與槽位", Name = "拔出到畫布", Apply = () => DetachNestedNode(node) });
        if (!node.NestedSlot.IsItem || node.NestedSlot.ItemSource is not HGListItemSource items || !items.CanEditStructure) return;
        if (node.NestedSlot.InputSlot is FormulaSlotBase)
            options.Add(new HGSourceOption { Group = "清單與槽位", Name = "改為常數", Apply = () =>
            {
                if (!IsCurrentNestedItem(node, items)) return;
                BreakUndoMerge();
                ApplyGraphMutation(() => { PreserveVisibleNodePositions(); AttachSource(node.NestedSlot.InputSlot, null); });
            } });
        options.Add(new HGSourceOption { Group = "清單與槽位", Name = "移除此項目", Apply = () =>
        {
            if (IsCurrentNestedItem(node, items)) RemoveListItem(items, node.NestedSlot.ItemIndex);
        } });
    }

    private bool IsCurrentNestedItem(HGNodeView node, HGListItemSource items)
        => node?.NestedSlot != null && graph.ByCarrier.TryGetValue(node.Carrier, out var current)
            && ReferenceEquals(current, node) && !node.InLockedSubtree
            && ReferenceEquals(items.Get(node.NestedSlot.ItemIndex), node.NestedSlot.InputSlot);



    private void DetachNestedNode(HGNodeView node)
        => SetNodeMode(node, GraphNodeMode.Connection);

    private void RestoreNodeSocketDrag()
    {
        foreach (var node in graph.Nodes)
            if (dragStartPositions.TryGetValue(node.Id, out var start)) node.Pos = start;
        HGGraph.PositionNestedSlots(graph);
        linkVisualsDirty = true;
        Repaint();
    }

    private bool IsNestedSlotLink(HGLink link)
        => link?.OutputOwner?.NestedSlot != null && ReferenceEquals(link.ParentRow, link.OutputOwner.NestedSlot)
            && !ReferenceEquals(dragNode, link.OutputOwner);
}
}
