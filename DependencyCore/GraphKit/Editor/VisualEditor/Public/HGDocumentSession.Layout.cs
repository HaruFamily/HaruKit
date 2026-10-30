namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class HGDocumentSession<TDocument> where TDocument : class, IGraphDocument
{
    private bool OwnsGroup(GraphNodeGroup group) => Document is IGraphViewStateOwner owner
        && group != null && owner.ViewState.NodeGroups.Contains(group);

    public HGSessionCommandResult ConfigureGroup(GraphNodeGroup group, bool setComment, string comment,
        Color? customColor, bool resetColor) => Guard(() =>
    {
        if (!OwnsGroup(group) || setComment && comment?.Length > 16384 || customColor.HasValue && resetColor)
            return HGSessionCommandResult.Rejected;
        if (customColor.HasValue)
        {
            var color = customColor.Value;
            if (!FiniteCoordinate(color.r) || !FiniteCoordinate(color.g) || !FiniteCoordinate(color.b) || !FiniteCoordinate(color.a))
                return HGSessionCommandResult.Rejected;
        }
        var normalized = customColor;
        if (normalized.HasValue) { var color = normalized.Value; color.a = 1; normalized = color; }
        bool changed = setComment && group.Title != comment || resetColor && group.UseCustomColor
            || normalized.HasValue && (!group.UseCustomColor || group.CustomColor != normalized.Value);
        if (!changed) return HGSessionCommandResult.NoChange;
        return Apply(() =>
        {
            if (setComment) group.SetTitle(comment);
            if (normalized.HasValue) group.SetCustomColor(normalized.Value);
            else if (resetColor) group.SetPaletteColor(group.ColorIndex);
        }, content: false);
    });

    public HGSessionCommandResult EditGroupTab(GraphNodeGroup group, string operation, int index, string name, GraphNode member = null) => Guard(() =>
    {
        if (!OwnsGroup(group)) return HGSessionCommandResult.Rejected;
        if (operation == "add" || operation == "rename")
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256) return HGSessionCommandResult.Rejected;
        if (operation == "add")
        {
            if (group.Tabs.Count >= 32) return HGSessionCommandResult.Rejected;
            return Apply(() => group.AddTab(name.Trim()), content: false);
        }
        if (index < 0 || index >= group.Tabs.Count || group.Tabs[index] == null) return HGSessionCommandResult.Rejected;
        if (operation == "rename")
        {
            if (group.Tabs[index].Name == name.Trim()) return HGSessionCommandResult.NoChange;
            return Apply(() => group.SetTabName(index, name.Trim()), content: false);
        }
        if (operation == "remove")
        {
            if (group.Tabs.Count <= 1) return HGSessionCommandResult.Rejected;
            return Apply(() => group.RemoveTab(index), content: false);
        }
        if (operation == "activate")
        {
            if (group.ActiveTab == index) return HGSessionCommandResult.NoChange;
            return Apply(() => group.SetActiveTab(index), content: false);
        }
        if (operation == "member")
        {
            if (!IsDocumentOwned(member) || !group.Members.Contains(member.Id)) return HGSessionCommandResult.Rejected;
            if (group.TabOf(member.Id) == index) return HGSessionCommandResult.NoChange;
            return Apply(() => group.SetMemberTab(member.Id, index), content: false);
        }
        return HGSessionCommandResult.Rejected;
    });

    public HGSessionCommandResult LayoutNodes(IReadOnlyList<GraphNode> nodes, string mode, Vector2 origin,
        Vector2 spacing, int columns) => Guard(() =>
    {
        if (nodes == null || nodes.Count < 1 || nodes.Count > 64 || nodes.Distinct().Count() != nodes.Count
            || nodes.Any(node => node == null || !IsDocumentOwned(node))
            || !new[] { "translate", "align_x", "align_y", "distribute_x", "distribute_y", "grid", "clear" }.Contains(mode)
            || !FiniteCoordinate(origin.x) || !FiniteCoordinate(origin.y) || !FiniteCoordinate(spacing.x) || !FiniteCoordinate(spacing.y))
            return HGSessionCommandResult.Rejected;
        if (mode == "clear")
        {
            if (nodes.All(node => !node.HasPos && node.Pos.Equals(Vector2.zero))) return HGSessionCommandResult.NoChange;
            return Apply(() => { foreach (var node in nodes) node.ClearPos(); }, content: false);
        }
        if (mode == "grid" && (columns < 1 || columns > 64 || spacing.x <= 0 || spacing.y <= 0)) return HGSessionCommandResult.Rejected;
        if (mode != "grid" && nodes.Any(node => !node.HasPos || !FiniteCoordinate(node.Pos.x) || !FiniteCoordinate(node.Pos.y)))
            return HGSessionCommandResult.Rejected;
        var positions = nodes.Select(node => node.Pos).ToArray();
        if (mode == "distribute_x" || mode == "distribute_y")
        {
            bool horizontal = mode == "distribute_x";
            var order = Enumerable.Range(0, nodes.Count).OrderBy(i => horizontal ? positions[i].x : positions[i].y).ToArray();
            double first = horizontal ? positions[order[0]].x : positions[order[0]].y;
            double last = horizontal ? positions[order[order.Length - 1]].x : positions[order[order.Length - 1]].y;
            for (int rank = 0; rank < order.Length; rank++)
            {
                float value = order.Length == 1 ? (float)first : (float)(first + (last - first) * rank / (order.Length - 1));
                if (horizontal) positions[order[rank]].x = value; else positions[order[rank]].y = value;
            }
        }
        else for (int i = 0; i < nodes.Count; i++)
        {
            if (mode == "translate") positions[i] += origin;
            else if (mode == "align_x") positions[i].x = origin.x;
            else if (mode == "align_y") positions[i].y = origin.y;
            else positions[i] = new Vector2((float)((double)origin.x + (i % columns) * (double)spacing.x),
                (float)((double)origin.y + (i / columns) * (double)spacing.y));
        }
        if (positions.Any(position => !FiniteCoordinate(position.x) || !FiniteCoordinate(position.y))) return HGSessionCommandResult.Rejected;
        if (Enumerable.Range(0, nodes.Count).All(i => nodes[i].HasPos && nodes[i].Pos.Equals(positions[i]))) return HGSessionCommandResult.NoChange;
        return Apply(() => { for (int i = 0; i < nodes.Count; i++) nodes[i].Pos = positions[i]; }, content: false);
    });

    public HGSessionCommandResult DuplicateGroup(GraphNodeGroup source, string scope, Vector2 offset,
        out string groupId, out IReadOnlyList<string> nodeIds)
    {
        string created = null;
        var createdNodes = new List<string>();
        var result = Guard(() =>
        {
            if (!OwnsGroup(source) || scope == null || !GroupScopes().ContainsKey(scope) || source.Members.Count > 64
                || source.Tabs.Count > 32 || source.Tabs.Any(tab => tab == null)
                || !FiniteCoordinate(offset.x) || !FiniteCoordinate(offset.y)) return HGSessionCommandResult.Rejected;
            var owned = InspectObjects().OfType<GraphNode>().ToArray();
            var selected = new List<GraphNode>();
            foreach (string id in source.Members)
            {
                var matches = owned.Where(node => node.Id == id).ToArray();
                if (string.IsNullOrEmpty(id) || matches.Length != 1 || selected.Contains(matches[0])) return HGSessionCommandResult.Rejected;
                selected.Add(matches[0]);
            }
            var rect = source.Rect; rect.position += offset;
            if (!FiniteCoordinate(rect.x) || !FiniteCoordinate(rect.y) || !FiniteCoordinate(rect.xMax) || !FiniteCoordinate(rect.yMax)
                || !FiniteCoordinate(rect.width) || !FiniteCoordinate(rect.height) || rect.width < 0 || rect.height < 0
                || selected.Any(node => node.HasPos && (!FiniteCoordinate(node.Pos.x + offset.x) || !FiniteCoordinate(node.Pos.y + offset.y))))
                return HGSessionCommandResult.Rejected;
            var pool = scope == RootGroupScope ? Document.Orphans
                : DocumentTokens().Single(token => token != null && TokenGroupScope(token) == scope).Orphans;
            return Apply(() =>
            {
                var copies = CloneNodesIntoPool(selected, pool, offset);
                var group = new GraphNodeGroup(scope, source.Title, rect, source.ColorIndex);
                if (source.UseCustomColor) group.SetCustomColor(source.CustomColor);
                group.SetCollapsed(source.Collapsed);
                foreach (var tab in source.Tabs) group.AddTab(tab.Name);
                for (int i = 0; i < copies.Count; i++)
                {
                    group.SetMember(copies[i].Id, true);
                    group.SetMemberTab(copies[i].Id, source.TabOf(selected[i].Id));
                    createdNodes.Add(copies[i].Id);
                }
                group.SetActiveTab(source.ActiveTab);
                ((IGraphViewStateOwner)Document).ViewState.AddNodeGroup(group);
                created = group.Id;
            }, content: selected.Count > 0);
        });
        groupId = result == HGSessionCommandResult.Changed ? created : null;
        nodeIds = result == HGSessionCommandResult.Changed ? createdNodes.AsReadOnly() : Array.Empty<string>();
        return result;
    }
}
}
