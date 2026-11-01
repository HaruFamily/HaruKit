namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class HGGraph
{
    public const float NestedPadding = 4f;
    internal const float ListAddRowHeight = RowHeight + 8f;
    private const float HiddenLabelIndent = 12f;
    public const int MaxNestedDepth = 16;

    internal static float NestedLabelWidth(HGRow row)
    {
        // 隱藏名稱只保留收合與把手欄；新增按鈕位於清單底部。
        if (row.HideLabel && row.NodeList) return (row.TimingActions ? 0f : 12f) + ListGutter;
        if (row.IsItem || row.HideLabel) return row.SlotSettingsRow != null ? 14f : 0f;
        var (units, ratio) = row.Descriptor != null
            ? (row.LabelWidthUnits, row.LabelWidthRatio) : HGReflect.LabelWidth(row.Field);
        // 用原始節點寬決定 Label 欄，避免深層子樹撐寬父節點後，Label 欄再遞迴膨脹。
        float width = LabelWidthOf(NodeWidth, units, ratio);
        // 預設欄多留一格給標題 ＋，避免與清單序號／把手擠掉名稱；明確寬度宣告仍優先。
        return units <= 0 && ratio <= 0f ? width + GridSize : width;
    }

    internal static float NestedContentInset(HGRow row)
    {
        if (row.IsItem && row.ItemOwnerRow?.NodeList == true) return NestedContentInset(row.ItemOwnerRow);
        float inset = NestedPadding + row.LeftPad + row.Depth * IndentWidth;
        // 上下排列共用同一個前置拖曳欄，清單不再額外疊加縮排。
        if (row.LabelAbove) return inset + ListGutter;
        return inset + NestedLabelWidth(row) + (row.HideLabel && !row.IsItem ? HiddenLabelIndent : 0f);
    }

    private static void PrepareNestedSlots(HGModel model, HGGraphView view, IReadOnlyDictionary<string, bool> listCollapse)
    {
        var users = new Dictionary<GraphNode, int>();
        var countedSlots = new HashSet<GraphSlotBase>();
        void CountSlot(GraphSlotBase slot)
        {
            if (slot is UnionSlotBase union) slot = union.SelectedSlot ?? slot;
            if (slot == null || slot is PropertySlotBase || !countedSlots.Add(slot) || slot.Node == null) return;
            users.TryGetValue(slot.Node, out int count);
            users[slot.Node] = count + 1;
        }
        foreach (var slot in HGModel.WalkSlots(model.Data, new HashSet<object>(ReferenceComparer.Instance)))
            CountSlot(slot);
        // Asset／Token 焦點可能使用不在外層文件裡的獨立工作副本。
        // 納入實際畫布 Slot，且與文件走訪去重，不能將同一引用誤判成共用。
        foreach (var owner in view.Nodes)
            foreach (var row in AllRows(owner.Rows)) CountSlot(row.InputSlot);
        var state = (model.Data as IGraphViewStateOwner)?.ViewState;
        var groups = new Dictionary<string, GraphNodeGroup>();
        if (state != null)
            foreach (var group in state.NodeGroups)
                if (group != null) foreach (string id in group.Members) groups[id] = group;
        foreach (var owner in view.Nodes)
        {
            foreach (var row in AllRows(owner.Rows))
            {
                row.ActionSequence = IsActionSequence(row) && !owner.InLockedSubtree;
                row.NodeList = IsNodeList(row) && !owner.InLockedSubtree;
                string key = CollapseKey(owner.Id, row);
                bool explicitlyFolded = listCollapse != null && listCollapse.TryGetValue(key, out bool folded)
                    ? folded : state?.Folded.Contains(key) == true;
                if (row.TimingActions || row.NodeList && !explicitlyFolded) row.Collapsed = false;
            }
            MeasureNode(owner);
        }
        foreach (var owner in view.Nodes)
        {
            if (owner.InLockedSubtree) continue;
            foreach (var row in AllRows(owner.Rows))
            {
                // 清單收合只影響可見性，不解除唯一來源的內放歸屬。
                if (!IsSimpleNodeSocket(row) || row.IsTabHidden) continue;
                row.NodeSocket = true;
                var source = row.InputSlot.Node;
                if (source == null || source.Mode == GraphNodeMode.Connection || source.Kind is not (NodeKind.Inline or NodeKind.Token or NodeKind.Asset or NodeKind.Property)
                    || !users.TryGetValue(source, out int count) || count != 1
                    || !view.ByCarrier.TryGetValue(source, out var child) || child.IsRoot || child.NestedParent != null
                    || state?.Hidden.Contains(CollapseKey(owner.Id, row)) == true) continue;
                groups.TryGetValue(owner.Id, out var parentGroup); groups.TryGetValue(child.Id, out var childGroup);
                if (!ReferenceEquals(parentGroup, childGroup) || parentGroup != null && parentGroup.TabOf(owner.Id) != parentGroup.TabOf(child.Id)) continue;
                bool cycle = false;
                for (var parent = owner; parent != null; parent = parent.NestedParent)
                    if (ReferenceEquals(parent, child)) { cycle = true; break; }
                if (cycle) continue;
                row.NestedNode = child; child.NestedParent = owner; child.NestedSlot = row;
            }
        }
        foreach (var root in view.Nodes.Where(node => node.NestedParent == null)) LimitDepth(root, 0);
        OrderNestedSlots(view);
        PositionNestedSlots(view);
    }

    private static void LimitDepth(HGNodeView node, int depth)
    {
        foreach (var row in AllRows(node.Rows))
        {
            if (row.NestedNode == null) continue;
            var child = row.NestedNode;
            if (depth >= MaxNestedDepth)
            { row.NestedNode = null; child.NestedParent = null; child.NestedSlot = null; }
            else LimitDepth(child, depth + 1);
        }
    }

    internal static bool IsActionSequence(HGRow row)
        => row.Kind == HGRowKind.List && row.Items is HGListItemSource list && list.CanEditStructure
            && typeof(ActionSlotBase).IsAssignableFrom(list.ElementType) && !row.Locked
            && row.Descriptor?.ReadOnly != true && row.Field?.IsInitOnly != true;

    internal static bool IsNodeList(HGRow row)
        => row.Kind == HGRowKind.List && row.Items is HGListItemSource list && list.CanEditStructure
            && (typeof(ActionSlotBase).IsAssignableFrom(list.ElementType) || typeof(FormulaSlotBase).IsAssignableFrom(list.ElementType))
            && !row.Locked && row.Descriptor?.ReadOnly != true && row.Field?.IsInitOnly != true;

    internal static bool IsSimpleNodeSocket(HGRow row)
        => row.Kind == HGRowKind.InputPort && row.InputSlot != null && row.InputSlot is not PropertySlotBase
            && (!row.IsItem || row.ItemOwnerRow?.NodeList == true)
            && !row.Locked && row.Descriptor?.ReadOnly != true && row.Field?.IsInitOnly != true
            && (row.Children.Count == 0 || row.SlotSettingsRow != null) && row.DefaultListRow == null;

    public static bool NestedWithin(HGNodeView node, HGNodeView ancestor)
    {
        for (var parent = node; parent != null; parent = parent.NestedParent)
            if (ReferenceEquals(parent, ancestor)) return true;
        return false;
    }

    internal static float NestedVisualWidth(HGNodeView node)
    {
        float width = node.Width;
        if (node.Collapsed) return width;
        foreach (var row in AllRows(node.Rows))
            if (row.NestedNode != null && !row.Hidden && !row.IsTabHidden)
                width = Mathf.Max(width, NestedContentInset(row) + NestedVisualWidth(row.NestedNode) + NestedPadding);
        return width;
    }

    internal static Rect VisualBounds(HGNodeView node)
        => new Rect(node.Pos, new Vector2(NestedVisualWidth(node), node.Height));

    internal static Rect SlotBounds(HGNodeView owner, HGRow row)
    {
        // Slot 框只包直屬子 Node；後代延伸由各自的 Slot 框呈現。
        // 整棵子樹的裁切與排版範圍仍由 NestedVisualWidth 計算。
        float right = owner.Width - NestedPadding;
        // 同一父 Node 的 Slot 共用直屬內容右緣，空分支也不會突然變窄。
        if (!owner.Collapsed)
            foreach (var item in AllRows(owner.Rows))
                if (item.NestedNode != null && !item.Hidden && !item.IsTabHidden)
                    right = Mathf.Max(right, NestedContentInset(item) + item.NestedNode.Width + NestedPadding);
        float height = row.NodeList && !row.Collapsed ? Mathf.Max(RowHeight, row.AddRowY - row.LocalY + (row.CanAddItem ? ListAddRowHeight : 0f)) : row.Height;
        // 完整列高度仍包含 Slot 設定，但延伸框在內放內容結束處收尾。
        if (row.NestedNode != null)
            height = row.SocketContentY - row.LocalY + row.NestedNode.Height + NestedPadding * 2f;
        return new Rect(owner.Pos.x + NestedPadding, owner.Pos.y + row.LocalY + 1f,
            right - NestedPadding, Mathf.Max(1f, height - 2f));
    }

    public static void PositionNestedSlots(HGGraphView view, HGNodeView dragging = null)
    {
        for (int i = view.Nodes.Count - 1; i >= 0; i--)
        {
            var node = view.Nodes[i]; node.NestedMinWidth = 0f;
            MeasureNode(node);
        }
        foreach (var node in view.Nodes)
            if (node.NestedParent != null)
                node.Hidden = node.NestedParent.Hidden || node.NestedParent.Collapsed || node.NestedSlot.Hidden;
        foreach (var node in view.Nodes)
            if (node.NestedParent != null && !ReferenceEquals(node, dragging))
                node.Pos = node.NestedParent.Pos + new Vector2(NestedContentInset(node.NestedSlot),
                    node.NestedSlot.SocketContentY + NestedPadding);
    }

    public static void OrderNestedSlots(HGGraphView view)
    {
        var ordered = new List<HGNodeView>();
        void Add(HGNodeView node)
        {
            ordered.Add(node);
            foreach (var row in AllRows(node.Rows)) if (row.NestedNode != null) Add(row.NestedNode);
        }
        foreach (var node in view.Nodes) if (node.NestedParent == null) Add(node);
        view.Nodes.Clear(); view.Nodes.AddRange(ordered);
    }
}
}
