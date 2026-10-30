namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static object Validate(Draft draft, int offset, int pageSize)
    {
        // No validation flags are persisted: even legacy validators receive a disposable document copy.
        var diagnostics = draft.Session.ValidateStructure();
        int limit = Math.Min(pageSize, 20);
        int errors = diagnostics.Count(item => item.Severity == GraphDiagnosticSeverity.Error);
        int warnings = diagnostics.Count(item => item.Severity == GraphDiagnosticSeverity.Warning);
        string Text(string value) => value?.Length > 512 ? value.Substring(0, 512) : value;
        bool Truncated(string value) => value?.Length > 512;
        return new SuccessResponse("Structural/domain diagnostics only. No Verify flag, gameplay execution, cleanup or save. Each page reruns validation; referenced assets may change independently.", new
        {
            generation = draft.Session.Generation, scope = "structure_and_document_diagnostics",
            passed = errors == 0, errors, warnings, total = diagnostics.Count,
            next_offset = (long)offset + limit < diagnostics.Count ? (int?)(offset + limit) : null,
            diagnostics = diagnostics.Skip(offset).Take(limit).Select(item => new
            {
                code = Text(item.Code), severity = item.Severity.ToString(), message = Text(item.Message), fix = Text(item.Fix),
                location = new { document_id = Text(item.Location.DocumentId ?? draft.Session.DocumentId),
                    focus_id = Text(item.Location.FocusId), node_id = Text(item.Location.NodeId),
                    token_id = Text(item.Location.TokenId), field_path = Text(item.Location.FieldPath) },
                text_truncated = Truncated(item.Code) || Truncated(item.Message) || Truncated(item.Fix)
                    || Truncated(item.Location.DocumentId ?? draft.Session.DocumentId) || Truncated(item.Location.FocusId)
                    || Truncated(item.Location.NodeId) || Truncated(item.Location.TokenId) || Truncated(item.Location.FieldPath),
            }).ToArray(),
        });
    }

    private static object ConnectionCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var map = new HaruGraphDraftConnections(draft.Session);
        string slotKey = input.Get("slot_key");
        string sourceKey = input.Get("source_key");
        if (action == "endpoints")
        {
            var objects = draft.Session.InspectObjects();
            var locations = new System.Collections.Generic.Dictionary<GraphSlotBase, System.Collections.Generic.List<object>>(ReferenceComparer.Instance);
            foreach (var target in objects)
            {
                if (target is System.Collections.IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] is GraphSlotBase listSlot) AddLocation(listSlot, new { owner_type = target.GetType().FullName, list_index = i });
                    continue;
                }
                foreach (var field in HGReflect.Fields(target.GetType()))
                    if (!field.IsStatic && !field.IsNotSerialized && typeof(GraphSlotBase).IsAssignableFrom(field.FieldType)
                        && field.GetValue(target) is GraphSlotBase fieldSlot)
                        AddLocation(fieldSlot, new { owner_type = target.GetType().FullName, field_id = HGDocumentInspection.FieldKey(field) });
            }
            void AddLocation(GraphSlotBase slot, object location)
            {
                if (!locations.TryGetValue(slot, out var hints)) locations.Add(slot, hints = new System.Collections.Generic.List<object>());
                if (hints.Count < 8) hints.Add(location);
            }
            var entries = map.Slots.Select(pair => (object)new
            {
                key = pair.Key, kind = "slot", type = pair.Value.GetType().AssemblyQualifiedName,
                source_key = map.SourceKey(pair.Value), relation = pair.Value is PropertySlotBase ? "property_write_target" : "input",
                locations = locations.TryGetValue(pair.Value, out var hints) ? hints.ToArray() : Array.Empty<object>(),
            }).Concat(map.Nodes.Select(pair => (object)new
            {
                key = pair.Key, kind = "source", node_kind = pair.Value.Kind.ToString(), node_id = pair.Value.Id,
                body_type = pair.Value.BodyObject?.GetType().AssemblyQualifiedName,
                token_name = pair.Value.Token?.Name, property_name = pair.Value.Property?.Name,
                asset_path = pair.Value.AssetObject != null ? AssetDatabase.GetAssetPath(pair.Value.AssetObject) : null,
            }));
            int total = map.Slots.Count + map.Nodes.Count;
            return new SuccessResponse("Generation-local serialized endpoints, independent of canvas visibility. Keys expire after ANY mutation. Slot locations are hints (up to 8 direct fields); root/list entries may have none. Property writers target Property sources. No new nodes or list entries are created.",
                new { generation = draft.Session.Generation, total,
                    next_offset = (long)offset + pageSize < total ? (int?)(offset + pageSize) : null,
                    entries = entries.Skip(offset).Take(pageSize).ToArray() });
        }
        if (slotKey == null || !map.Slots.ContainsKey(slotKey)) return Error("slot_unavailable", "Select a slot key from endpoints at this generation.");
        if (action == "disconnect")
        {
            string previousSource = map.SourceKey(map.Slots[slotKey]);
            string previousNodeId = map.Slots[slotKey].Node?.Id;
            return CommandResult(draft, draft.Session.Disconnect(map.Registry, HaruGraphDraftConnections.InputKey(slotKey)),
                "disconnect", new { slot_key = slotKey, slot_type = map.Slots[slotKey].GetType().FullName,
                    previous_source_key = previousSource, previous_node_id = previousNodeId });
        }
        var check = map.Check(slotKey, sourceKey);
        if (action == "check_connection") return new SuccessResponse("Connection compatibility check; no change applied.",
            new { generation = draft.Session.Generation, result = check.ToString(), allowed = check == HGPortConnectionResult.Allowed });
        if (check != HGPortConnectionResult.Allowed) return Error("connection_rejected", check.ToString());
        string previousNode = map.Slots[slotKey].Node?.Id;
        return CommandResult(draft, draft.Session.Connect(map.Registry, HaruGraphDraftConnections.InputKey(slotKey), HaruGraphDraftConnections.OutputKey(sourceKey)),
            "connect", new { slot_key = slotKey, slot_type = map.Slots[slotKey].GetType().FullName,
                source_key = sourceKey, source_node_id = map.Nodes[sourceKey].Id, previous_node_id = previousNode });
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static string Fingerprint(IGraphDocument document, bool persistedStrings = false)
    {
        var snapshot = HGDocumentInspection.Capture(document, "revision");
        if (!snapshot.Complete)
            throw new InvalidOperationException("Cannot fingerprint incomplete graph data: " + string.Join(" | ",
                snapshot.Diagnostics.Take(3).Select(message => message?.Length > 512 ? message.Substring(0, 512) : message)));
        // Unity persists null serialized strings as empty strings. Only disk verification uses
        // this equivalence; memory revisions and preview tokens must still detect null edits.
        if (persistedStrings)
            foreach (var record in snapshot.Records)
                foreach (var field in record.Fields)
                    if (field.Kind == "null" && field.Type == typeof(string).AssemblyQualifiedName)
                    {
                        field.Kind = "scalar";
                        field.Value = string.Empty;
                    }
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(snapshot));
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Graph fingerprint exceeds 8 MiB.");
        return Hash(bytes);
    }

    // Full asset memory protects unrelated fields and subassets, not just the selected document.
    private static string AssetRevision(UnityEngine.Object owner)
    {
        var text = new StringBuilder();
        foreach (var item in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(owner))
            .Where(value => value != null).OrderBy(HaruGraphInspect.OwnerKey, StringComparer.Ordinal))
        {
            string json = EditorJsonUtility.ToJson(item);
            text.Append(HaruGraphInspect.OwnerKey(item)).Append(':').Append(json.Length).Append(':').Append(json);
            if (text.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Asset memory exceeds the revision size limit.");
        }
        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static string DiskRevision(UnityEngine.Object owner)
    {
        string assetPath = AssetDatabase.GetAssetPath(owner);
        if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("Agent drafts currently require an Assets/ file.");
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Asset file exceeds the revision size limit.");
        return Hash(File.ReadAllBytes(path));
    }

    private static bool AssetIsDirty(UnityEngine.Object owner)
        => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(owner)).Any(item => item != null && EditorUtility.IsDirty(item));

    private static string CommitBlocker(Draft draft, bool saveAs = false, bool dependencyTransaction = false)
    {
        string parameterBlocker = ParameterAssetBlocker(draft);
        if (parameterBlocker != null) return parameterBlocker;
        if (draft.OwnsOwner) return saveAs ? NewAssetBlocker(draft) : "New assets have no source file. Use preview_save_as/save_as with an exact destination.";
        if (!ReferenceEquals(draft.ReadDocument(), draft.BaselineDocument)) return "Owner document was replaced since this draft opened/last committed.";
        if (draft.InitiallyDirty) return "The asset already had unsaved changes when this draft opened. Save those deliberately, then open a new draft.";
        if (AssetIsDirty(draft.Session.Owner)) return "The asset or a subasset has unsaved changes outside this draft.";
        if (SerializationUtility.HasManagedReferencesWithMissingTypes(draft.Session.Owner)) return "Owner contains missing managed-reference types.";
        if (AssetRevision(draft.Session.Owner) != draft.AssetRevision || DiskRevision(draft.Session.Owner) != draft.DiskRevision)
            return "Owner memory or the asset file changed since opening/last commit. Open a new draft after reviewing the conflict.";
        string path = AssetDatabase.GetAssetPath(draft.Session.Owner);
        foreach (var window in Resources.FindObjectsOfTypeAll<HaruGraphWindow>())
            if (window.TryGetDocumentInspectionInfo(out var owner, out _, out bool dirty)
                && AssetDatabase.GetAssetPath(owner) == path && dirty)
                return "A graph window has unsaved edits for this asset. Resolve that draft before committing this independent draft.";
        foreach (var window in Resources.FindObjectsOfTypeAll<HaruGraphWindow>())
            if (window.TryGetAssetInspectionInfo(out var asset, out bool assetDirty) && assetDirty && AssetDatabase.GetAssetPath(asset) == path)
                return "A graph window has unsaved shared-asset edits for this file.";
        if (draft.Session.Document is HGAssetDocument assetDocument)
        {
            if (draft.Session.Owner is IActionGraphAsset && assetDocument.State.Input.Node is GraphNode root
                && root.Kind != NodeKind.Empty && root.Kind != NodeKind.Inline)
                return "Action asset roots must be inline Action content; referenced Asset/Token/Property roots cannot be stored.";
            if (!AssetDatabase.IsMainAsset(draft.Session.Owner)
                || AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>().Count() != 1)
                return "Asset-state commits currently require a standalone main asset without other ScriptableObject subassets.";
            // Shared edits require a separately designed multi-file invalidation transaction. Do not silently
            // save dependent documents, leave stale validation flags, or rely on the cached graph index.
            string dependent = saveAs || dependencyTransaction ? null : FirstDependentFile(path);
            if (dependent != null)
                return "Asset is referenced by " + dependent + ". Use haru_graph_transaction to include and invalidate dependent documents.";
        }
        if (!saveAs && !AssetDatabase.IsOpenForEdit(path)) return "Asset is not open for editing in version control.";
        return null;
    }

    private static string FirstDependentFile(string path)
    {
        var candidates = AssetDatabase.GetAllAssetPaths().Where(candidate => candidate != path
            && (candidate.StartsWith("Assets/", StringComparison.Ordinal) || candidate.StartsWith("Packages/", StringComparison.Ordinal))
            && !AssetDatabase.IsValidFolder(candidate)).OrderBy(candidate => candidate, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0 || !AssetDatabase.GetDependencies(candidates, true).Contains(path)) return null;
        // Bulk queries avoid crossing the Unity API once per file on large projects.
        while (candidates.Length > 1)
        {
            int middle = candidates.Length / 2;
            var first = candidates.Take(middle).ToArray();
            candidates = AssetDatabase.GetDependencies(first, true).Contains(path) ? first : candidates.Skip(middle).ToArray();
        }
        return candidates[0];
    }

    private static object Preview(Draft draft)
    {
        var after = HGDocumentInspection.Capture(draft.Session.Document, "agent_working_copy");
        if (!after.Complete) return Error("incomplete_preview", "Current draft cannot be captured completely; inspect capture diagnostics.");
        string beforeId = null, afterId = null;
        var first = HaruGraphInspect.StoreSnapshot(draft.Baseline, draft.Session.Owner, draft.Session.DocumentId,
            onStored: id => beforeId = id);
        if (beforeId == null) return first;
        var second = HaruGraphInspect.StoreSnapshot(after, draft.Session.Owner, draft.Session.DocumentId,
            workingCopyDirty: draft.Session.IsDirty, onStored: id => afterId = id);
        if (afterId == null) { HaruGraphInspect.ReleaseSnapshot(beforeId); return second; }
        draft.PreviewHash = Fingerprint(draft.Session.Document);
        draft.PreviewGeneration = draft.Session.Generation;
        draft.PreviewToken = Guid.NewGuid().ToString("N");
        string blocker = CommitBlocker(draft);
        return new SuccessResponse("Review BOTH snapshots through haru_graph_inspect records/fields. They are complete before/after states, not a semantic diff; record keys must not be matched across snapshots. history describes commands including undone commands. Commit invalidates validation state and saves only the selected asset file, including its subassets. No gameplay test was run.", new
        {
            generation = draft.Session.Generation, preview_token = draft.PreviewToken,
            before_snapshot_id = beforeId, after_snapshot_id = afterId,
            before_hash = draft.BaselineHash, after_hash = draft.PreviewHash,
            changed = draft.BaselineHash != draft.PreviewHash, can_commit = blocker == null, commit_blocker = blocker,
            before_records = draft.Baseline.Records.Count, after_records = after.Records.Count,
        });
    }

    private static object Commit(Draft draft, string token)
    {
        if (token == null || token != draft.PreviewToken || draft.PreviewGeneration != draft.Session.Generation
            || draft.PreviewHash != Fingerprint(draft.Session.Document))
            return Error("preview_required", "Preview the current generation and pass its exact token before commit.");
        string blocker = CommitBlocker(draft);
        if (blocker != null) return Error("commit_conflict", blocker);
        draft.PreviewToken = null; // A commit attempt is never blindly retried with an old token.
        if (draft.PreviewHash == draft.BaselineHash)
            return new SuccessResponse("No content changes; no asset was saved.", new { result = "NoChange", generation = draft.Session.Generation });
        var result = draft.Session.CommitAssetPreservingOwner();
        if (result == HGSessionCommandResult.Changed)
        {
            try
            {
                var live = draft.ReadDocument();
                draft.BaselineDocument = live;
                draft.Baseline = HGDocumentInspection.Capture(live, "agent_baseline");
                draft.BaselineHash = Fingerprint(live);
                draft.AssetRevision = AssetRevision(draft.Session.Owner);
                draft.DiskRevision = DiskRevision(draft.Session.Owner);
                if (AssetIsDirty(draft.Session.Owner)) throw new InvalidOperationException("Owner remains dirty after saving.");
            }
            catch (Exception exception)
            {
                draft.RequiresReopen = true;
                return Error("commit_followup_failed", "Commit returned Changed, but post-save verification failed. Inspect Owner before retrying: " + exception.Message);
            }
            draft.History.Clear();
            draft.HistoryDropped = 0;
            return new SuccessResponse("Committed the selected document through the shared session transaction and saved only its asset file. Validation state was invalidated. Existing clean graph windows are not reloaded; reopen them before further editing.",
                new { result = result.ToString(), generation = draft.Session.Generation, owner_dirty = EditorUtility.IsDirty(draft.Session.Owner) });
        }
        if (result == HGSessionCommandResult.WriteFailed) draft.RequiresReopen = true;
        return CommandResult(draft, result);
    }
}
}
