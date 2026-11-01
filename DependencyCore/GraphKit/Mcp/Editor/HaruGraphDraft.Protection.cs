namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class HaruGraphDraft
{
    private sealed class EditPolicy
    {
        public bool LayoutOnly;
        public string ContentHash;
        public string[] Nodes;
        public string[] Groups;
        public Dictionary<string, string> Protected;
    }

    // Canonical reference numbering is local to the selected closure. Ignored view records cannot shift content keys.
    private static string SnapshotHash(HGDocumentSnapshot snapshot, string root, bool contentOnly)
    {
        if (!snapshot.Complete) throw new InvalidOperationException("Cannot compare incomplete snapshots.");
        var records = snapshot.Records.ToDictionary(record => record.Key, StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        JToken Visit(string key, int depth)
        {
            if (depth > 256) throw new InvalidOperationException("Comparison nesting exceeds 256.");
            if (seen.TryGetValue(key, out int id)) return new JObject { ["ref"] = id };
            if (!records.TryGetValue(key, out var record)) throw new InvalidOperationException("Unresolved snapshot reference.");
            id = seen.Count; seen.Add(key, id);
            var fields = new JArray();
            foreach (var field in record.Fields)
            {
                if (contentOnly && (field.Type == typeof(GraphViewState).AssemblyQualifiedName
                    || record.Type == typeof(GraphNode).AssemblyQualifiedName && (field.Name == typeof(GraphNode).FullName + "/_pos"
                        || field.Name == typeof(GraphNode).FullName + "/_hasPos"))) continue;
                fields.Add(new JObject { ["name"] = field.Name, ["type"] = field.Type, ["kind"] = field.Kind,
                    ["value"] = field.Kind == "reference" ? Visit(field.Value, depth + 1) : field.Value == null ? JValue.CreateNull() : new JValue(field.Value) });
            }
            return new JObject { ["id"] = id, ["type"] = record.Type, ["fields"] = fields };
        }
        string text = Visit(root ?? snapshot.Root, 0).ToString(Formatting.None);
        if (text.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Comparison exceeds 8 MiB.");
        return Hash(Encoding.UTF8.GetBytes(text));
    }

    // Saving invalidates the consumer's declared validation state. Normalize both sides on detached copies.
    private static string ContentFingerprint(IGraphDocument document)
    {
        var copy = GraphDeepCopy.Copy(document);
        if (copy == null || ReferenceEquals(copy, document)) throw new InvalidOperationException("Cannot isolate content comparison.");
        copy.InvalidateValidation();
        var snapshot = HGDocumentInspection.Capture(copy, "content");
        return SnapshotHash(snapshot, null, true);
    }

    private static string PolicyCommitViolation(Draft draft)
    {
        if (draft.Policy == null) return null;
        var staged = GraphDeepCopy.Copy(draft.Session.Document);
        if (staged == null || ReferenceEquals(staged, draft.Session.Document)) return "Cannot isolate policy save check.";
        staged.InvalidateValidation();
        return PolicyViolation(draft, staged);
    }

    private static HGSnapshotRecord UniqueRecord(HGDocumentSnapshot snapshot, string id, bool group)
    {
        var type = group ? typeof(GraphNodeGroup) : typeof(GraphNode);
        var matches = snapshot.Records.Where(record => record.Type == type.AssemblyQualifiedName && (group
            ? record.Fields.Any(field => field.Name == type.FullName + "/_id" && field.Value == id) : record.StableId == id)).ToArray();
        if (matches.Length != 1) throw new ArgumentException("Protected identity missing or ambiguous: " + id);
        return matches[0];
    }

    private static Dictionary<string, string> ProtectedHashes(IGraphDocument document, HGDocumentSnapshot snapshot, string[] nodes, string[] groups)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var records = snapshot.Records.ToDictionary(record => record.Key, StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<(string Key, string Path)>();
        pending.Enqueue((snapshot.Root, "document"));
        while (pending.Count > 0)
        {
            var item = pending.Dequeue();
            if (paths.ContainsKey(item.Key)) continue;
            var record = records[item.Key];
            string path = string.IsNullOrEmpty(record.StableId) ? item.Path : record.Type + ":" + record.StableId;
            if (path.Length > 16384) throw new InvalidOperationException("Protection path exceeds 16384 characters.");
            paths.Add(item.Key, path);
            foreach (var field in record.Fields)
                if (field.Kind == "reference" && field.Type != typeof(GraphViewState).AssemblyQualifiedName)
                    pending.Enqueue((field.Value, path + "/" + field.Name));
        }
        var protectedNodes = new HashSet<string>(nodes, StringComparer.Ordinal);
        foreach (string id in groups)
        {
            var group = (document as IGraphViewStateOwner)?.ViewState?.NodeGroups.SingleOrDefault(item => item.Id == id);
            if (group == null) throw new ArgumentException("Protected group missing: " + id);
            result.Add("group:" + id, SnapshotHash(snapshot, UniqueRecord(snapshot, id, true).Key, false));
            foreach (string member in group.Members) protectedNodes.Add(member);
        }
        foreach (string id in protectedNodes)
        {
            var record = UniqueRecord(snapshot, id, false);
            result.Add("node:" + id, SnapshotHash(snapshot, record.Key, false));
            // Incoming references are part of protection: deletion/reconnection outside the protected closure must not detach it.
            var incoming = snapshot.Records.SelectMany(owner => owner.Fields.Where(field => field.Kind == "reference" && field.Value == record.Key)
                .Select(field => (paths.TryGetValue(owner.Key, out string path) ? path : owner.Type) + "|" + field.Name))
                .OrderBy(value => value, StringComparer.Ordinal);
            result.Add("incoming:" + id, Hash(Encoding.UTF8.GetBytes(string.Join("\n", incoming))));
        }
        return result;
    }

    private static string PolicyViolation(Draft draft, IGraphDocument document = null)
    {
        if (draft.Policy == null) return null;
        try
        {
            document ??= draft.Session.Document;
            var snapshot = HGDocumentInspection.Capture(document, "policy");
            if (draft.Policy.LayoutOnly && ContentFingerprint(document) != draft.Policy.ContentHash)
                return "Layout-only policy rejected a content/reference change.";
            var current = ProtectedHashes(document, snapshot, draft.Policy.Nodes, draft.Policy.Groups);
            foreach (var pair in draft.Policy.Protected)
                if (!current.TryGetValue(pair.Key, out string hash) || hash != pair.Value) return "Protected range changed: " + pair.Key;
            return null;
        }
        catch (Exception exception) { return "Cannot verify protected ranges: " + exception.Message; }
    }

    private static object Protect(Draft draft, ToolParams input)
    {
        if (draft.Policy != null) return Error("policy_already_set", "Policy is fixed for this draft. Discard and reopen to choose another policy.");
        string[] Ids(string name)
        {
            if (input.GetRaw(name) == null) return Array.Empty<string>();
            if (input.GetRaw(name) is not JArray array || array.Count > 64 || array.Any(id => id.Type != JTokenType.String || (string.IsNullOrEmpty((string)id) || ((string)id).Length > 256))
                || array.Select(id => (string)id).Distinct(StringComparer.Ordinal).Count() != array.Count) throw new ArgumentException(name + " requires 0..64 distinct IDs of at most 256 characters.");
            return array.Select(id => (string)id).ToArray();
        }
        var flag = input.GetRaw("layout_only");
        if (flag != null && flag.Type != JTokenType.Boolean) return Error("invalid_policy", "layout_only must be a boolean.");
        var nodes = Ids("protected_node_ids"); var groups = Ids("protected_group_ids");
        bool layoutOnly = flag?.Value<bool>() == true;
        if (!layoutOnly && nodes.Length == 0 && groups.Length == 0) return Error("invalid_policy", "Choose layout_only and/or protected nodes/groups.");
        var snapshot = HGDocumentInspection.Capture(draft.Session.Document, "policy");
        var policy = new EditPolicy { Nodes = nodes, Groups = groups, LayoutOnly = layoutOnly,
            ContentHash = ContentFingerprint(draft.Session.Document), Protected = ProtectedHashes(draft.Session.Document, snapshot, nodes, groups) };
        draft.Policy = policy;
        draft.PreviewToken = draft.SaveAsToken = null;
        return new SuccessResponse("Policy fixed at the current draft. Protected groups include all tabs and member dependency closures. No document mutation. History controls are disabled while protected; discard/reopen to change policy.", new
        { generation = draft.Session.Generation, layout_only = layoutOnly, protected_node_ids = nodes, protected_group_ids = groups, content_hash = policy.ContentHash });
    }

    private static object LayoutDiff(Draft draft)
    {
        var before = draft.Baseline;
        var after = HGDocumentInspection.Capture(draft.Session.Document, "diff");
        string beforeContent = draft.BaselineContentHash, afterContent = ContentFingerprint(draft.Session.Document);
        var ids = before.Records.Concat(after.Records).Where(record => record.Type == typeof(GraphNode).AssemblyQualifiedName)
            .Select(record => record.StableId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        var changes = new List<object>();
        foreach (string id in ids.Take(256))
        {
            var old = before.Records.Where(record => record.Type == typeof(GraphNode).AssemblyQualifiedName && record.StableId == id).ToArray();
            var current = after.Records.Where(record => record.Type == typeof(GraphNode).AssemblyQualifiedName && record.StableId == id).ToArray();
            if (old.Length != 1 || current.Length != 1)
            { changes.Add(new { node_id = id, change = old.Length == 0 ? "added" : current.Length == 0 ? "removed" : "ambiguous" }); continue; }
            if (SnapshotHash(before, old[0].Key, false) != SnapshotHash(after, current[0].Key, false))
                changes.Add(new { node_id = id, change = SnapshotHash(before, old[0].Key, true) == SnapshotHash(after, current[0].Key, true) ? "layout_in_closure" : "content_in_closure" });
        }
        return new SuccessResponse("Final-state comparison with the opened/last committed baseline; content excludes GraphViewState and GraphNode positions and normalizes declared validation invalidation on both copies. Includes aliases, hidden fields, disconnected nodes and external identities. No execution or visual acceptance.", new
        { generation = draft.Session.Generation, content_unchanged = beforeContent == afterContent, before_content_hash = beforeContent,
            after_content_hash = afterContent, state_changed = SnapshotHash(before, null, false) != SnapshotHash(after, null, false),
            protection_passed = PolicyViolation(draft) == null, protection_error = PolicyViolation(draft),
            changed_node_count = changes.Count, node_comparison_complete = ids.Length <= 256, compared_node_count = Math.Min(ids.Length, 256),
            nodes = changes.Take(64).ToArray(), truncated = changes.Count > 64 || ids.Length > 256 });
    }

    private static bool NeedsPolicyGuard(Draft draft, JObject args, string action)
    {
        if (draft.Policy == null || draft.PolicyGuardActive || action == "protect" || action == "geometry" || action == "layout_preview" || action == "layout_diff") return false;
        if (action == "slot_value" || action == "slot_asset_reference") return args.Properties().Any(property => property.Name.Replace("_", "").ToLowerInvariant() == "value");
        return !checkQueries.Contains(action) && (authoringActions.Contains(action) || new[] { "set_value", "connect", "disconnect" }.Contains(action));
    }

    private static object GuardedCommand(Draft draft, JObject args)
    {
        object response = null;
        string violation = null;
        var history = draft.History.ToArray(); int dropped = draft.HistoryDropped;
        var dependencies = draft.ParameterAssets.ToArray();
        draft.PreviewToken = draft.SaveAsToken = null;
        draft.PolicyGuardActive = true;
        HGSessionCommandResult outcome;
        try
        {
            outcome = draft.Session.RunAtomic(() =>
            {
                response = HandleCommand(args);
                return JObject.FromObject(response)["success"]?.Value<bool>() == true && (violation = PolicyViolation(draft)) == null;
            });
        }
        finally { draft.PolicyGuardActive = false; }
        if (outcome == HGSessionCommandResult.Changed || outcome == HGSessionCommandResult.NoChange) return response;
        draft.History.Clear(); draft.History.AddRange(history); draft.HistoryDropped = dropped;
        draft.ParameterAssets.Clear(); foreach (var pair in dependencies) draft.ParameterAssets.Add(pair.Key, pair.Value);
        return new ErrorResponse("harugraph.draft.protected_edit_rejected", new
        { generation = draft.Session.Generation, rolled_back = true, reason = violation, command_response = response });
    }
}
}
