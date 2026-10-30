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
    private static object NodeState(GraphNode node) => new
    {
        node_id = node.Id, kind = node.Kind.ToString(), disabled = node.Disabled, note = node.Note,
        has_position = node.HasPos, position = node.HasPos ? WireValue(node.Pos) : null,
        body_type = node.BodyObject?.GetType().AssemblyQualifiedName,
        token_id = node.Token?.Id, property_id = node.Property?.Id,
    };

    private static object DuplicateNodes(Draft draft, ToolParams input)
    {
        if (input.GetRaw("node_ids") is not JArray ids || ids.Count < 1 || ids.Count > 64
            || ids.Any(id => id.Type != JTokenType.String || string.IsNullOrEmpty((string)id) || ((string)id).Length > 128)
            || ids.Select(id => (string)id).Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Error("invalid_selection", "node_ids must contain 1..64 distinct nonempty node IDs, each at most 128 characters.");
        string scope = input.Get("scope_id");
        if (scope == null || !draft.Session.GroupScopes().ContainsKey(scope)) return Error("scope_required", "Select scope_id from group_scopes.");
        var nodes = draft.Session.InspectObjects().OfType<GraphNode>().ToArray();
        var sources = new GraphNode[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            var matches = nodes.Where(node => node.Id == (string)ids[i]).ToArray();
            if (matches.Length != 1) return Error("node_unavailable", "Every selected node_id must identify exactly one current document node.");
            sources[i] = matches[0];
        }
        var offset = new Vector2(40, 40);
        if (input.GetRaw("position_offset") is JToken raw)
        {
            if (!TryValue(raw, typeof(Vector2), out var value)) return Error("invalid_value", "position_offset requires exact finite x/y components.");
            offset = (Vector2)value;
        }
        var result = draft.Session.DuplicateNodes(sources, scope, offset, out var created);
        return CommandResult(draft, result, "duplicate_nodes", new
        {
            scope_id = scope, position_offset = WireValue(offset),
            mappings = created.Select((id, index) => new { source_node_id = sources[index].Id, node_id = id }).ToArray(),
            hint = "Only selected nodes copied. Internal edges retained; boundary slots cleared. Definitions/assets shared; group membership not copied. Nodes without manual positions remain automatic.",
        });
    }

    private static object NodeLayoutCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var nodes = draft.Session.InspectObjects().OfType<GraphNode>().ToArray();
        string id = input.Get("node_id");
        if (action == "nodes")
        {
            var selected = id == null ? nodes : nodes.Where(node => node.Id == id).ToArray();
            return new SuccessResponse("Serialized carriers in this document, including disconnected nodes; external asset contents are not traversed. No IDs assigned. Nonempty unique node_id is required for configure_node. Disabling a shared carrier affects every input that refers to it.", new
            {
                generation = draft.Session.Generation, total = selected.Length,
                next_offset = (long)offset + pageSize < selected.Length ? (int?)(offset + pageSize) : null,
                entries = selected.Skip(offset).Take(pageSize).Select(NodeState).ToArray(),
            });
        }
        if (string.IsNullOrEmpty(id) || nodes.Count(node => node.Id == id) != 1)
            return Error("node_unavailable", "Select a unique nonempty node_id from nodes at this generation.");
        var target = nodes.Single(node => node.Id == id);
        var disabled = input.GetRaw("disabled");
        var note = input.GetRaw("note");
        var position = input.GetRaw("position");
        var clearPosition = input.GetRaw("clear_position");
        if (disabled != null && disabled.Type != JTokenType.Boolean || clearPosition != null && clearPosition.Type != JTokenType.Boolean)
            return Error("invalid_value", "disabled and clear_position must be JSON booleans.");
        if (note != null && (note.Type != JTokenType.String && note.Type != JTokenType.Null || note.Type == JTokenType.String && ((string)note).Length > 16384))
            return Error("invalid_value", "note must be a string of at most 16384 characters or null to clear.");
        Vector2? parsedPosition = null;
        if (position != null)
        {
            if (!TryValue(position, typeof(Vector2), out var value)) return Error("invalid_value", "position requires exact finite float32 x/y components.");
            parsedPosition = (Vector2)value;
        }
        bool clear = clearPosition?.Value<bool>() == true;
        if (clear && position != null) return Error("invalid_value", "position and clear_position=true are mutually exclusive.");
        if (disabled == null && note == null && position == null && !clear)
            return Error("value_required", "Provide disabled, note, position or clear_position=true.");
        var before = NodeState(target);
        var result = draft.Session.ConfigureNode(target, disabled?.Value<bool>(), note != null,
            note?.Type == JTokenType.Null ? null : (string)note, parsedPosition, clear);
        return CommandResult(draft, result, action, new { node_id = id, before, after = NodeState(target) });
    }
}
}
