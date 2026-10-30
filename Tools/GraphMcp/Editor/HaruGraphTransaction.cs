namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

[McpForUnityTool("haru_graph_transaction", Description = "Inspect reverse asset dependencies, preview and commit 1..16 graph drafts together. Every discovered document in affected files must be explicitly opened and included, including dependents whose validation must be invalidated. Refuses unsupported dependency files, dirty/conflicting Owners and graph windows. Uses existing session guards, staged validation and compensating disk/memory rollback. Not crash-atomic; retains recovery backups. Success consumes drafts; reopen before editing again.")]
public static class HaruGraphTransaction
{
    public sealed class Parameters
    {
        [ToolParameter("dependencies, preview, commit")] public string action { get; set; }
        [ToolParameter("1..16 unique {draft_id,generation} objects; required for dependencies/preview.", Required = false)] public JArray drafts { get; set; }
        [ToolParameter("Single-use preview token; required for commit.", Required = false)] public string preview_token { get; set; }
    }
    public static object HandleCommand(JObject parameters) => HaruGraphDraft.TransactionCommand(parameters);
}

public static partial class HaruGraphDraft
{
    private sealed class TransactionPlan
    {
        internal DateTime Expires;
        internal string[] Ids;
        internal string Fingerprint;
    }
    private static readonly Dictionary<string, TransactionPlan> transactionPlans = new();

    private static string[] AffectedFiles(IEnumerable<Draft> selected)
    {
        var source = new HashSet<string>(selected.Select(draft => AssetDatabase.GetAssetPath(draft.Session.Owner)), StringComparer.Ordinal);
        return AssetDatabase.GetAllAssetPaths().Where(path => !AssetDatabase.IsValidFolder(path)
            && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
            && (source.Contains(path) || AssetDatabase.GetDependencies(path, true).Any(source.Contains)))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
    private static JArray DependencyDocuments(string[] paths)
    {
        var entries = new JArray();
        foreach (string path in paths)
        {
            var documents = path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? HaruGraphInspect.DescribeDocuments(path) : new List<object>();
            entries.Add(JObject.FromObject(new { asset_path = path, supported = path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && documents.Count > 0, documents }));
        }
        return entries;
    }
    private static string TransactionFingerprint(string[] ids, string[] paths)
        => Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", ids.Select(id =>
        {
            var draft = drafts[id];
            return id + ":" + draft.Session.Generation + ":" + Fingerprint(draft.Session.Document)
                + ":" + AssetRevision(draft.Session.Owner) + ":" + DiskRevision(draft.Session.Owner);
        })) + "\n" + string.Join("\n", paths)));

    internal static object TransactionCommand(JObject parameters)
    {
        try
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return Error("editor_busy", "Use transactions in idle Edit Mode.");
            if (parameters == null) return Error("invalid_request", "A transaction request is required.");
            var input = new ToolParams(parameters);
            string action = input.Get("action");
            if (!new[] { "dependencies", "preview", "commit" }.Contains(action)) return Error("unknown_action", "Use dependencies, preview or commit.");
            foreach (string expired in transactionPlans.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray()) transactionPlans.Remove(expired);
            TransactionPlan plan = null;
            string[] ids;
            if (action == "commit")
            {
                string token = input.Get("preview_token");
                if (token == null || !transactionPlans.TryGetValue(token, out plan)) return Error("preview_required", "Preview the full transaction first.");
                transactionPlans.Remove(token);
                ids = plan.Ids;
            }
            else
            {
                if (input.GetRaw("drafts") is not JArray requested || requested.Count < 1 || requested.Count > 16)
                    return Error("invalid_transaction", "Provide 1..16 draft_id/generation pairs.");
                ids = requested.Select(item => (string)item["draft_id"]).ToArray();
                if (ids.Any(string.IsNullOrEmpty) || ids.Distinct().Count() != ids.Length) return Error("invalid_transaction", "Draft IDs must be nonempty and unique.");
                foreach (var item in requested)
                    if (!drafts.TryGetValue((string)item["draft_id"], out var draft) || (int?)item["generation"] != draft.Session.Generation)
                        return Error("stale_generation", "Read current draft status before planning.");
            }
            foreach (string id in ids)
                if (!drafts.TryGetValue(id, out var draft) || draft.Expires <= DateTime.UtcNow || draft.RequiresReopen || draft.OwnsOwner || draft.Session.Owner == null)
                    return Error("draft_unavailable", "Every transaction member must be a current persistent draft.");
            var selected = ids.Select(id => drafts[id]).ToArray();
            if (selected.Select(draft => draft.Session.DocumentId).Distinct().Count() != selected.Length)
                return Error("invalid_transaction", "Do not submit two drafts of the same document.");
            string[] paths = AffectedFiles(selected);
            var dependencies = DependencyDocuments(paths);
            if (dependencies.ToString(Formatting.None).Length > 1048576) return Error("result_limit", "Dependency report exceeds 1 MiB.");
            if (action == "dependencies") return new SuccessResponse("All affected files and discovered documents; explicitly include them in a preview. No drafts were opened automatically.", new { files = dependencies });
            var included = new HashSet<string>(selected.Select(draft => draft.Session.DocumentId));
            if (dependencies.Any(file => !(bool)file["supported"] || !file["documents"].Any()
                || file["documents"].Any(document => !included.Contains((string)document["document_id"]))))
                return Error("dependencies_required", "Include every document returned by dependencies. Files without editable bindings (including scenes/packages) block the transaction.");
            foreach (var draft in selected)
            {
                string blocker = CommitBlocker(draft, dependencyTransaction: true);
                if (blocker != null) return Error("commit_conflict", blocker);
            }
            string fingerprint = TransactionFingerprint(ids, paths);
            if (action == "preview")
            {
                if (transactionPlans.Count >= 4) return Error("preview_limit", "At most four transaction previews may be retained for ten minutes.");
                string token = Guid.NewGuid().ToString("N");
                transactionPlans.Add(token, new TransactionPlan { Ids = ids, Fingerprint = fingerprint, Expires = DateTime.UtcNow.AddMinutes(10) });
                return new SuccessResponse("Review every draft capture/history and affected file before commit. All member validation flags will be invalidated, even for unchanged dependents.", new {
                    preview_token = token, files = dependencies, members = ids.Select(id => new { draft_id = id,
                        generation = drafts[id].Session.Generation, before_hash = drafts[id].BaselineHash,
                        after_hash = Fingerprint(drafts[id].Session.Document) }).ToArray() });
            }
            if (plan.Fingerprint != fingerprint) return Error("transaction_changed", "Draft, source file or dependency closure changed after preview.");
            var result = HGDocumentSession<IGraphDocument>.CommitAssetsPreservingOwners(selected.Select(draft => draft.Session).ToArray());
            if (result.Success) foreach (string id in ids) ReleaseDraft(id);
            else if (result.RecoveryDirectory != null) foreach (var draft in selected) draft.RequiresReopen = true;
            return result.Success ? new SuccessResponse("Committed selected files and invalidated dependent documents. Drafts consumed; reopen before further editing.", result)
                : new ErrorResponse("harugraph.draft.transaction_failed", result);
        }
        catch (Exception exception) { return Error("transaction_failed", exception.Message); }
    }
}
}
