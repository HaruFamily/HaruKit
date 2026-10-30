namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static readonly string[] groupActions = { "configure_group", "group_tabs", "add_group_tab", "rename_group_tab",
        "remove_group_tab", "activate_group_tab", "group_member_tab", "duplicate_group" };

    private static object GroupCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var state = draft.Session.InspectObjects().OfType<GraphViewState>().SingleOrDefault();
        var group = state?.NodeGroups.SingleOrDefault(item => item?.Id == input.Get("group_id"));
        if (group == null) return Error("group_unavailable", "Select a unique group_id from groups.");
        if (action == "group_tabs") return new SuccessResponse("Generation-local tab indices. Member counts include all group members assigned to that tab, including implicit first-tab membership.", new
        {
            generation = draft.Session.Generation, total = group.Tabs.Count, active_tab = group.ActiveTab,
            next_offset = (long)offset + pageSize < group.Tabs.Count ? (int?)(offset + pageSize) : null,
            entries = group.Tabs.Select((tab, index) => new { tab_index = index, name = tab?.Name,
                member_count = group.Members.Count(id => group.TabOf(id) == index) }).Skip(offset).Take(pageSize).ToArray(),
        });
        if (action == "configure_group")
        {
            var comment = input.GetRaw("comment");
            var color = input.GetRaw("custom_color");
            var reset = input.GetRaw("reset_color");
            if (comment != null && (comment.Type != JTokenType.String && comment.Type != JTokenType.Null
                || comment.Type == JTokenType.String && ((string)comment).Length > 16384)) return Error("invalid_value", "comment must be a string up to 16384 characters or null.");
            if (reset != null && reset.Type != JTokenType.Boolean) return Error("invalid_value", "reset_color must be a JSON boolean.");
            Color? parsed = null;
            if (color != null)
            {
                if (!TryValue(color, typeof(Color), out var value)) return Error("invalid_value", "custom_color requires finite r/g/b/a components; alpha is stored as 1.");
                parsed = (Color)value;
            }
            bool resetColor = reset?.Value<bool>() == true;
            if (parsed.HasValue && resetColor) return Error("invalid_value", "custom_color and reset_color=true are mutually exclusive.");
            if (comment == null && color == null && !resetColor) return Error("value_required", "Provide comment, custom_color or reset_color=true.");
            return CommandResult(draft, draft.Session.ConfigureGroup(group, comment != null,
                comment?.Type == JTokenType.Null ? null : (string)comment, parsed, resetColor), action, new { group_id = group.Id });
        }
        if (action == "duplicate_group")
        {
            string scope = input.Get("scope_id");
            if (scope == null || !draft.Session.GroupScopes().ContainsKey(scope)) return Error("scope_required", "Choose destination scope_id from group_scopes.");
            var delta = new Vector2(40, 40);
            if (input.GetRaw("position_offset") is JToken raw)
            {
                if (!TryValue(raw, typeof(Vector2), out var value)) return Error("invalid_value", "position_offset requires finite x/y components.");
                delta = (Vector2)value;
            }
            string[] originalIds = group.Members.ToArray();
            var result = draft.Session.DuplicateGroup(group, scope, delta, out string created, out var nodeIds);
            return CommandResult(draft, result, action, new
            {
                source_group_id = group.Id, group_id = created, scope_id = scope,
                mappings = nodeIds.Select((id, index) => new { source_node_id = originalIds[index], node_id = id }).ToArray(),
                hint = "Only explicit members copied. Internal edges, tabs and style retained; outside slot connections cleared. Missing/ambiguous members reject the entire operation.",
            });
        }
        int index = -1;
        if (action != "add_group_tab")
        {
            var raw = input.GetRaw("tab_index");
            if (raw?.Type != JTokenType.Integer || !int.TryParse(raw.ToString(), out index) || index < 0)
                return Error("invalid_value", "tab_index must be a nonnegative int32 from group_tabs at this generation.");
        }
        string operation = action switch
        {
            "add_group_tab" => "add", "rename_group_tab" => "rename", "remove_group_tab" => "remove",
            "activate_group_tab" => "activate", _ => "member",
        };
        string name = input.Get("name");
        if ((operation == "add" || operation == "rename") && (string.IsNullOrWhiteSpace(name) || name.Length > 256))
            return Error("invalid_name", "Tab name must contain 1..256 characters.");
        int selected = action == "add_group_tab" ? group.Tabs.Count : index;
        var member = operation == "member" ? FindNode(draft, input.Get("node_id")) : null;
        return CommandResult(draft, draft.Session.EditGroupTab(group, operation, index, name, member), action,
            new { group_id = group.Id, tab_index = selected, node_id = member?.Id });
    }

    private static object LayoutSelection(Draft draft, ToolParams input)
    {
        if (input.GetRaw("node_ids") is not JArray ids || ids.Count < 1 || ids.Count > 64
            || ids.Any(id => id.Type != JTokenType.String || string.IsNullOrEmpty((string)id) || ((string)id).Length > 128)
            || ids.Select(id => (string)id).Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Error("invalid_selection", "Select 1..64 distinct nonempty node IDs from nodes.");
        var all = draft.Session.InspectObjects().OfType<GraphNode>().ToArray();
        var nodes = new GraphNode[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            var matches = all.Where(node => node.Id == (string)ids[i]).ToArray();
            if (matches.Length != 1) return Error("node_unavailable", "Each node ID must uniquely identify a current document node.");
            nodes[i] = matches[0];
        }
        string mode = input.Get("layout_mode");
        if (!new[] { "translate", "align_x", "align_y", "distribute_x", "distribute_y", "grid", "clear" }.Contains(mode))
            return Error("invalid_value", "layout_mode must be translate, align_x, align_y, distribute_x, distribute_y, grid or clear.");
        var origin = Vector2.zero;
        var spacing = new Vector2(320, 200);
        int columns = 4;
        if (input.GetRaw("position") is JToken position)
        {
            if (!TryValue(position, typeof(Vector2), out var value)) return Error("invalid_value", "position requires finite x/y components.");
            origin = (Vector2)value;
        }
        if (input.GetRaw("spacing") is JToken rawSpacing)
        {
            if (!TryValue(rawSpacing, typeof(Vector2), out var value)) return Error("invalid_value", "spacing requires finite x/y components.");
            spacing = (Vector2)value;
        }
        if (input.GetRaw("columns") is JToken rawColumns
            && (rawColumns.Type != JTokenType.Integer || !int.TryParse(rawColumns.ToString(), out columns)))
            return Error("invalid_value", "columns must be int32.");
        var before = nodes.Select(NodeState).ToArray();
        var result = draft.Session.LayoutNodes(nodes, mode, origin, spacing, columns);
        return CommandResult(draft, result, "layout_nodes", new { layout_mode = mode, before,
            after = nodes.Select(NodeState).ToArray(), hint = "Only node anchors moved, not node dimensions or group rectangles. Non-grid/clear modes require manual positions; distribution preserves endpoint coordinates." });
    }
}
}
