namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;
using HGEditor = HaruFamily.DependencyCore.GraphKit.Editor;

public static partial class HaruGraphDraft
{
    /// <summary>Places an existing Composition source inside a read slot: the background form of dragging a Node into a Slot.</summary>
    private static object Compose(Draft draft, ToolParams input)
    {
        var map = new HaruGraphDraftConnections(draft.Session);
        string slotKey = input.Get("slot_key");
        string sourceKey = input.Get("source_key");
        if (slotKey == null || !map.Slots.TryGetValue(slotKey, out var slot))
            return Error("slot_unavailable", "Select a slot key from endpoints at this generation.");
        if (sourceKey == null || !map.Nodes.TryGetValue(sourceKey, out var node))
            return Error("source_unavailable", "Select a source key from endpoints at this generation.");
        if (slot is PropertySlotBase)
            return Error("slot_unsupported", "Property write targets are selected with connect; compose places a read source inside a slot.");
        var check = map.Check(slotKey, sourceKey);
        if (check != HGEditor.HGPortConnectionResult.Allowed) return Error("connection_rejected", check.ToString());
        if (node.Mode != GraphNodeMode.Composition)
            return Error("node_mode_connection", "Source is in Connection mode. Set configure_node node_mode:\"Composition\" first (at most one reader), or use connect to keep it shared.");
        if (!draft.Session.CanReferenceNode(node, slot))
            return Error("node_shared", "Another slot already reads this Composition source. Share the value through a named Token or Property, or use connect for an explicit shared wire.");
        string previousNodeId = slot.Node?.Id;
        var result = draft.Session.ReconnectInput(map.Registry, HaruGraphDraftConnections.InputKey(slotKey), map.Sources[sourceKey]);
        return CommandResult(draft, result, "compose", new { slot_key = slotKey, source_key = sourceKey, source_node_id = node.Id,
            previous_node_id = previousNodeId, node_mode = "Composition" });
    }

    /// <summary>Sets the Label shown on one action item, so lists read as "verb + object" steps.</summary>
    private static object LabelStep(Draft draft, ToolParams input)
    {
        var map = new HaruGraphDraftConnections(draft.Session);
        string slotKey = input.Get("slot_key");
        if (slotKey == null || !map.Slots.TryGetValue(slotKey, out var slot) || slot is not ActionSlotBase step)
            return Error("slot_unavailable", "Select an action item slot key (an ActionSlot entry in endpoints) at this generation.");
        string label = input.Get("name");
        if (label?.Length > 256) return Error("invalid_name", "Labels are limited to 256 characters.");
        string before = step.Label;
        var result = draft.Session.SetActionLabel(step, label);
        return CommandResult(draft, result, "label_step", new { slot_key = slotKey, node_id = step.Node?.Id, before,
            after = string.IsNullOrEmpty(label) ? null : label });
    }

    private static List<(string First, string Second)> LayoutOverlaps(HGEditor.HGLayoutSnapshot layout, out int total)
    {
        var byId = new Dictionary<string, HGEditor.HGLayoutNode>(StringComparer.Ordinal);
        foreach (var node in layout.Nodes)
            if (node.Id != null && !byId.ContainsKey(node.Id)) byId.Add(node.Id, node);
        bool Contains(HGEditor.HGLayoutNode parent, HGEditor.HGLayoutNode child)
        {
            for (int depth = 0; depth <= 64 && child.ParentNodeId != null; depth++)
            {
                if (child.ParentNodeId == parent.Id) return true;
                if (!byId.TryGetValue(child.ParentNodeId, out var next)) break;
                child = next;
            }
            return false;
        }
        var pairs = new List<(string First, string Second)>();
        total = 0;
        for (int i = 0; i < layout.Nodes.Count; i++)
            for (int j = i + 1; j < layout.Nodes.Count; j++)
                if (!Contains(layout.Nodes[i], layout.Nodes[j]) && !Contains(layout.Nodes[j], layout.Nodes[i])
                    && layout.Nodes[i].Rect.Overlaps(layout.Nodes[j].Rect))
                {
                    total++;
                    if (pairs.Count < 64) pairs.Add((layout.Nodes[i].Id, layout.Nodes[j].Id));
                }
        return pairs;
    }

    /// <summary>
    /// Renders this draft in a temporary capture-only window and saves a PNG. The user's own windows are not touched;
    /// this is the one command that opens a window, and it closes it before returning.
    /// </summary>
    private static object Screenshot(string draftId, Draft draft, ToolParams input)
    {
        if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return Error("editor_busy", "Take screenshots in idle Edit Mode.");
        int width = FlowInt(input, "image_width") ?? 1600, height = FlowInt(input, "image_height") ?? 1000;
        if (width < 980 || width > 4096 || height < 560 || height > 4096)
            return Error("invalid_value", "image_width must be 980..4096 and image_height 560..4096 (points).");
        string nodeId = input.Get("node_id");
        var session = draft.Session;
        if (session.Owner == null) return Error("draft_unavailable", "The draft Owner no longer exists.");
        var binding = new HGEditor.HGDocumentBinding<IGraphDocument>(session.DocumentId, _ => session.Document,
            (_, _) => throw new InvalidOperationException("Capture views are read-only."), create: null, readRevision: null);
        HGEditor.HaruGraphWindow window = null;
        bool activeBefore = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
        try
        {
            // 原生視窗只有一小塊，放在 Unity 主視窗左上角，盡量不蓋到其他程式。
            Vector2 corner = UnityEditor.EditorGUIUtility.GetMainWindowPosition().position;
            window = HGEditor.HaruGraphWindow.OpenCaptureView(session.Owner, binding, draft.Context, new Rect(corner.x, corner.y, width, height));
            if (window == null) return Error("capture_failed", "The capture view could not be opened without taking focus, or could not bind this draft.");
            if (!HaruGraphInspect.RepaintNow(window))
                return Error("capture_unsupported", "This Unity version does not expose the window repaint API used for screenshots.");
            if (!window.FrameCaptureView(nodeId))
                return Error("node_unavailable", "node_id is not drawn on the document canvas. Use a node_id from nodes/geometry, or omit it to frame the whole graph.");
            HaruGraphInspect.RepaintNow(window);
            if (!HaruGraphInspect.TryGrabWindow(window, "draft-" + draftId + ".png", out var image, out string code, out string message))
                return Error(code, message);
            return new SuccessResponse("Rendering of this draft's working copy in a temporary window that is already closed. The user's windows, selection and saved layout were not changed; the file is overwritten by the next screenshot of the same draft. Text may be too small when a large graph is framed whole: pass node_id to frame one node with everything nested inside it.", new
            {
                draft_id = draftId, generation = session.Generation, framed = nodeId == null ? "document" : "node", node_id = nodeId,
                image.path, image.project_relative_path, image.width, image.height, image.pixels_per_point,
                editor_was_active = activeBefore,
                editor_activated = !activeBefore && UnityEditorInternal.InternalEditorUtility.isApplicationActive,
            });
        }
        catch (Exception exception) { return Error("capture_failed", exception.Message); }
        finally { if (window != null) window.CloseCaptureView(); }
    }

    /// <summary>Whole-scope automatic layout: clears manual positions so the canvas tree layout places every node.</summary>
    private static object Arrange(Draft draft, ToolParams input)
    {
        string scope = input.Get("scope_id");
        if (scope == null || !draft.Session.GroupScopes().ContainsKey(scope)) return Error("scope_required", "Select scope_id from group_scopes.");
        var result = draft.Session.ArrangeScope(scope, out int cleared);
        object measured;
        try
        {
            var layout = draft.Session.CaptureLayout(scope);
            LayoutOverlaps(layout, out int overlapCount);
            measured = new { node_count = layout.Nodes.Count, overlap_count = overlapCount };
        }
        catch (Exception exception) { measured = new { unmeasured = exception.Message }; }
        return CommandResult(draft, result, "arrange", new { scope_id = scope, cleared_positions = cleared, measured,
            hint = "Layout-only. All manual positions in this scope were cleared; the canvas places HEADs top to bottom and sources to the right, nested nodes inside their slots. Group membership, tabs and collapse state are unchanged. Do not set positions afterwards unless the user asks for a specific arrangement." });
    }

    /// <summary>Read-only structural readability report for block-style graphs. Thresholds are defaults, not hard limits.</summary>
    private static object Readability(Draft draft, ToolParams input, int offset, int pageSize)
    {
        var session = draft.Session;
        int maxItems = FlowInt(input, "max_list_items") ?? 7;
        int maxDepth = FlowInt(input, "max_nesting_depth") ?? 3;
        if (maxItems < 1 || maxItems > 4096 || maxDepth < 1 || maxDepth > 64)
            return Error("invalid_value", "max_list_items must be 1..4096 and max_nesting_depth 1..64.");

        var issues = new List<object>();
        var unmeasured = new List<string>();
        int warnings = 0;
        void Warn(string code, string message, string nodeId = null, string path = null, string scopeId = null,
            string definitionId = null, string groupId = null)
        {
            warnings++;
            if (issues.Count < 512)
                issues.Add(new { code, severity = "warning", message, node_id = nodeId, path, scope_id = scopeId,
                    definition_id = definitionId, group_id = groupId });
        }

        // Nodes: unused candidates, empty content, wires that have a single reader.
        var objects = session.InspectObjects();
        var referenced = new HashSet<object>(ReferenceComparer.Instance);
        foreach (var slot in objects.OfType<GraphSlotBase>())
            if (slot.Node != null) referenced.Add(slot.Node);
        foreach (var node in objects.OfType<GraphNode>())
        {
            if (!referenced.Contains(node))
            {
                Warn("unused_node", "No slot references this node; it only sits in a candidate pool. Place it or delete it.", node.Id);
                continue;
            }
            if (node.Kind == NodeKind.Empty) Warn("empty_node", "A slot references a node without content.", node.Id);
            if (node.Mode == GraphNodeMode.Connection && session.ReadNodeUserCount(node) <= 1)
                Warn("needless_connection", "Connection-mode source with a single reader. Nest it (configure_node node_mode Composition) unless the user placed it outside on purpose.", node.Id);
        }

        // Definitions: every Token and Property needs a distinct, meaningful name.
        void Names(string library, IEnumerable<(string Id, string Name)> definitions)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var all = definitions.ToArray();
            foreach (var definition in all)
            {
                string key = (definition.Name ?? "").Trim();
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
            foreach (var definition in all)
            {
                string key = (definition.Name ?? "").Trim();
                if (key.Length == 0) Warn("unnamed_definition", library + " definition has no name.", definitionId: definition.Id);
                else if (counts[key] > 1) Warn("duplicate_definition_name", library + " name '" + key + "' is used by " + counts[key] + " definitions.", definitionId: definition.Id);
            }
        }
        Names("token", ((session.Document as ITokenOwner)?.Tokens ?? new List<GraphToken>())
            .Where(token => token != null).Select(token => (token.Id, token.Name)));
        Names("property", ((session.Document as IPropertyOwner)?.Properties ?? new List<GraphProperty>())
            .Where(property => property != null).Select(property => (property.Id, property.Name)));

        // Flow: empty steps and list length（Label 只在聚焦時顯示，不列為可讀性問題；說明用節點註解）, using the same declared structure as the flow action.
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        void Section(HGEditor.HGFlowSectionView section)
        {
            if (section.Steps.Count > maxItems)
                Warn("long_list", section.Steps.Count + " items exceed " + maxItems + ". Split into a labelled sub-flow or a shared Action asset.", path: section.Path);
            foreach (var step in section.Steps)
            {
                if (!string.IsNullOrEmpty(step.NodeId)) actionIds.Add(step.NodeId);
                if (step.Empty) Warn("empty_step", "Action item has no content.", step.NodeId, step.Path);
                foreach (var child in step.Sections) Section(child);
            }
        }
        try
        {
            var flow = session.CaptureFlow();
            foreach (var root in flow.Roots)
                foreach (var section in root.Sections) Section(section);
            foreach (var issue in flow.Issues.Take(16)) unmeasured.Add("flow " + issue.Code + " at " + issue.Path);
        }
        catch (Exception exception) { unmeasured.Add("flow: " + exception.Message); }

        // Canvas: formula nesting depth inside one action, and overlapping standalone nodes, per scope.
        foreach (string scope in session.GroupScopes().Keys.ToArray())
        {
            try
            {
                var layout = session.CaptureLayout(scope);
                var byId = new Dictionary<string, HGEditor.HGLayoutNode>(StringComparer.Ordinal);
                foreach (var node in layout.Nodes)
                    if (node.Id != null && !byId.ContainsKey(node.Id)) byId.Add(node.Id, node);
                foreach (var node in layout.Nodes)
                {
                    if (node.SyntheticId || node.Id == null || actionIds.Contains(node.Id)) continue;
                    int depth = 1;
                    var current = node;
                    while (depth <= 64 && current.ParentNodeId != null && byId.TryGetValue(current.ParentNodeId, out var parent)
                        && !parent.SyntheticId && !actionIds.Contains(parent.Id))
                    {
                        depth++;
                        current = parent;
                    }
                    // Report the first level past the threshold only; deeper descendants would repeat the same finding.
                    if (depth == maxDepth + 1)
                        Warn("deep_nesting", "Formula nesting passes " + maxDepth + " levels here. Extract the inner part into a named Token (recomputed on each read) or a Property (fixed once written).", node.Id, scopeId: scope);
                }
                var overlaps = LayoutOverlaps(layout, out int overlapCount);
                foreach (var pair in overlaps.Take(16))
                    Warn("overlap", "Standalone nodes overlap (" + pair.First + ", " + pair.Second + "). Run arrange for this scope.", pair.First, scopeId: scope);
                if (overlapCount > 16) unmeasured.Add("layout " + scope + ": " + (overlapCount - 16) + " further overlaps not listed");
            }
            catch (Exception exception) { unmeasured.Add("layout " + scope + ": " + exception.Message); }
        }

        // Groups: a group is only useful to the reader when its comment says what it is for.
        var viewState = objects.OfType<GraphViewState>().FirstOrDefault();
        foreach (var group in viewState?.NodeGroups ?? Array.Empty<GraphNodeGroup>())
        {
            if (group == null) continue;
            string comment = (group.Title ?? "").Trim();
            if (comment.Length == 0 || comment == "群組")
                Warn("group_without_comment", "Group has no comment stating its purpose.", scopeId: group.Scope, groupId: group.Id);
        }

        return new SuccessResponse("Structural readability report on the working copy. Read-only: no cleanup, no layout change, no validation flag. passed means no warnings at these thresholds; it is not validation and not visual acceptance.", new
        {
            generation = session.Generation, passed = warnings == 0, warning_count = warnings,
            thresholds = new { max_list_items = maxItems, max_nesting_depth = maxDepth },
            total = issues.Count, issues_truncated = warnings > issues.Count,
            next_offset = (long)offset + pageSize < issues.Count ? (int?)(offset + pageSize) : null,
            issues = issues.Skip(offset).Take(pageSize).ToArray(),
            unmeasured = unmeasured.ToArray(),
            not_checked = new[] { "whether names and labels are meaningful", "step order (guards, computation, effects, Property writes)", "Token versus Property choice", "wire crossings", "text clipping" },
        });
    }

    private static object NodeState(GraphNode node, int readUsers) => new
    {
        node_id = node.Id, kind = node.Kind.ToString(), node_mode = node.Mode.ToString(), read_user_count = readUsers, disabled = node.Disabled, note = node.Note,
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
                entries = selected.Skip(offset).Take(pageSize).Select(node => NodeState(node, draft.Session.ReadNodeUserCount(node))).ToArray(),
            });
        }
        if (string.IsNullOrEmpty(id) || nodes.Count(node => node.Id == id) != 1)
            return Error("node_unavailable", "Select a unique nonempty node_id from nodes at this generation.");
        var target = nodes.Single(node => node.Id == id);
        GraphNodeMode? mode = null;
        if (input.GetRaw("node_mode") is JToken rawMode)
        {
            if (rawMode.Type != JTokenType.String || ((string)rawMode != "Composition" && (string)rawMode != "Connection"))
                return Error("invalid_node_mode", "node_mode must be exactly Composition or Connection.");
            mode = (string)rawMode == "Composition" ? GraphNodeMode.Composition : GraphNodeMode.Connection;
            if (mode == GraphNodeMode.Composition && draft.Session.ReadNodeUserCount(target) > 1)
                return Error("node_mode_shared", "Disconnect extra read Slots before switching to Composition. Connection preserves one-to-many references; no automatic copies or disconnections.");
        }
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
        if (!mode.HasValue && disabled == null && note == null && position == null && !clear)
            return Error("value_required", "Provide node_mode, disabled, note, position or clear_position=true.");
        var before = NodeState(target, draft.Session.ReadNodeUserCount(target));
        var result = draft.Session.ConfigureNode(target, disabled?.Value<bool>(), note != null,
            note?.Type == JTokenType.Null ? null : (string)note, parsedPosition, clear, mode);
        return CommandResult(draft, result, action, new { node_id = id, before, after = NodeState(target, draft.Session.ReadNodeUserCount(target)) });
    }
}
}
