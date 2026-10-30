namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only transport adapter for detached GraphKit inspection snapshots.</summary>
[McpForUnityTool("haru_graph_inspect", Description = "Inspect HaruGraph without opening an edit session. catalog discovers types; candidates checks NEW default Slot inline candidates; sequence reads ActionSlot list order; references traces serialized links. Use list_documents with asset_path, capture with its document_id, then records and fields with snapshot_id. Includes Action, Formula, Token, Asset references, Property, ordered lists and layout regardless of canvas visibility. capture reads Editor owner memory; list_windows/capture_window reads an existing bound document draft, excluding shared-asset focus drafts. Neither is disk-only. Does not execute, validate, clean or save graphs. Referenced assets must be captured separately. Snapshots expire after 10 minutes or domain reload.")]
public static class HaruGraphInspect
{
    public sealed class Parameters
    {
        [ToolParameter("search_documents, list_documents, capture, list_windows, capture_window, records, fields, references, sequence, catalog, candidates, or release")]
        public string action { get; set; }
        [ToolParameter("Exact project-relative asset path returned by Unity asset search; required for list_documents/capture.", Required = false)]
        public string asset_path { get; set; }
        [ToolParameter("Exact document_id returned by list_documents; required for capture.", Required = false)]
        public string document_id { get; set; }
        [ToolParameter("Exact window_id from list_windows; required for capture_window. IDs expire when a window closes.", Required = false)]
        public int window_id { get; set; }
        [ToolParameter("Snapshot ID returned by capture.", Required = false)]
        public string snapshot_id { get; set; }
        [ToolParameter("Record Key returned by records; required for fields.", Required = false)]
        public string record_key { get; set; }
        [ToolParameter("Optional record kind filter; catalog accepts action, formula, token, asset or family. Empty means all.", Required = false)]
        public string kind { get; set; }
        [ToolParameter("Optional case-insensitive text search. search_documents matches asset paths; records/catalog/candidates match labels and type metadata.", Required = false)]
        public string search { get; set; }
        [ToolParameter("Exact assembly-qualified Slot type from records/catalog. Closed generic types require a retained snapshot containing that Slot. candidates evaluates a NEW default Slot, not the captured configuration.", Required = false)]
        public string slot_type { get; set; }
        [ToolParameter("Zero-based page offset.", Required = false, DefaultValue = "0")]
        public int offset { get; set; }
        [ToolParameter("Page size, 1 to 100.", Required = false, DefaultValue = "50")]
        public int page_size { get; set; } = 50;
    }

    private sealed class StoredSnapshot
    {
        public HGDocumentSnapshot Data;
        public DateTime Expires;
    }

    private static readonly Dictionary<string, StoredSnapshot> snapshots = new();

    public static object HandleCommand(JObject parameters)
    {
        try
        {
            if (parameters == null) return Error("invalid_request", "Provide action and its required parameters.");
            // BatchExecute normalizes keys to camelCase; use the bridge's shared parameter reader.
            var input = new ToolParams(parameters);
            var request = new Parameters
            {
                action = input.Get("action"), asset_path = input.Get("asset_path"), document_id = input.Get("document_id"),
                snapshot_id = input.Get("snapshot_id"), record_key = input.Get("record_key"),
                kind = input.Get("kind"), search = input.Get("search"),
                slot_type = input.Get("slot_type"),
                window_id = input.GetRaw("window_id")?.ToObject<int>() ?? 0,
                offset = input.GetRaw("offset")?.ToObject<int>() ?? 0,
                page_size = input.GetRaw("page_size")?.ToObject<int>() ?? 50,
            };
            if (request.offset < 0 || request.page_size < 1 || request.page_size > 100)
                return Error("invalid_page", "offset must be non-negative; page_size must be 1..100.");
            foreach (string expired in snapshots.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
                snapshots.Remove(expired);
            switch (request.action)
            {
                case "search_documents": return SearchDocuments(request);
                case "list_documents": return ListDocuments(request);
                case "capture": return Capture(request);
                case "list_windows": return ListWindows(request);
                case "capture_window": return CaptureWindow(request);
                case "records": return Read(request, false);
                case "fields": return Read(request, true);
                case "references": return References(request);
                case "catalog": return Catalog(request);
                case "sequence": return Sequence(request);
                case "candidates": return Candidates(request);
                case "release":
                    return new SuccessResponse("Snapshot released.", new { removed = request.snapshot_id != null && snapshots.Remove(request.snapshot_id) });
                default: return Error("unknown_action", "Use search_documents, list_documents, capture, list_windows, capture_window, records, fields, references, sequence, catalog, candidates, or release.");
            }
        }
        catch (Exception exception)
        {
            return Error("inspection_failed", exception.Message);
        }
    }

    internal static UnityEngine.Object[] Owners(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("\\") || path.Split('/').Any(part => part == ".." || part == ".")
            || (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)))
            throw new ArgumentException("Provide an exact Assets/... or Packages/... asset path, without traversal.");
        if (AssetDatabase.IsValidFolder(path)) throw new ArgumentException("Select an asset file, not a folder.");
        var owners = AssetDatabase.LoadAllAssetsAtPath(path).Where(owner => owner != null).ToArray();
        if (owners.Length == 0) throw new ArgumentException("Asset not found: " + path);
        return owners;
    }

    public static string OwnerKey(UnityEngine.Object owner)
    {
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(owner, out string guid, out long localId))
            throw new InvalidOperationException("Owner has no persistent asset identity.");
        return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
    }

    private static object ListDocuments(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Discover documents in Edit Mode after compilation and asset updates finish.");
        var result = DescribeDocuments(request.asset_path);
        return new SuccessResponse("Direct document fields, graph assets and explicitly registered custom bindings. Scene components are not searched.",
            new { total = result.Count, documents = result.Skip(request.offset).Take(request.page_size).ToArray(),
                next_offset = Next(request, result.Count) });
    }

    internal static List<object> DescribeDocuments(string assetPath)
    {
        var result = new List<object>();
        foreach (var owner in Owners(assetPath))
        {
            string prefix = OwnerKey(owner) + "/";
            if (owner is IGraphAsset)
                result.Add(new { document_id = prefix + "$asset", owner = owner.name, type = owner.GetType().AssemblyQualifiedName, is_null = false });
            foreach (var field in HGDocumentInspection.DocumentFields(owner.GetType()))
                result.Add(new { document_id = prefix + HGDocumentInspection.FieldKey(field), owner = owner.name,
                    type = field.FieldType.AssemblyQualifiedName, is_null = field.GetValue(owner) == null });
            foreach (var custom in HaruGraphExtensions.Bindings(owner))
            {
                custom.entry.Binding.TryRead(owner, out var document);
                result.Add(new { document_id = custom.id, owner = owner.name, type = document?.GetType().AssemblyQualifiedName,
                    is_null = document == null, binding_kind = "registered" });
            }
        }
        return result;
    }

    private static object SearchDocuments(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Search documents in idle Edit Mode.");
        var paths = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct()
            .Where(path => Matches(request.search, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var entries = new List<object>();
        var failures = new List<object>();
        foreach (string path in paths.Skip(request.offset).Take(request.page_size))
        {
            try
            {
                var documents = DescribeDocuments(path);
                if (documents.Count > 0)
                    entries.Add(new { asset_path = path, document_count = documents.Count,
                        documents = documents.Take(100).ToArray(), documents_truncated = documents.Count > 100 });
            }
            catch (Exception exception) { failures.Add(new { asset_path = path, message = exception.Message }); }
        }
        return new SuccessResponse("Searches ScriptableObject asset paths under Assets and inspects one bounded page, including subassets. Does not open graph windows. offset/page_size count candidate files, not matching documents: follow next_offset even when entries is empty. Asset changes between pages can change ordering; restart for a fresh scan. Scenes and Packages are not searched; registered bindings on discovered Owners are included. Loading assets can invoke Unity OnEnable.",
            new { candidate_count = paths.Length, scanned_count = Math.Min(request.page_size, Math.Max(0, paths.Length - request.offset)),
                next_offset = Next(request, paths.Length), entries, failures });
    }

    private static object Capture(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Capture in Edit Mode after compilation and asset updates finish.");
        if (string.IsNullOrEmpty(request.document_id)) return Error("document_required", "Select an exact document_id from list_documents.");
        if (snapshots.Count >= 8) return Error("snapshot_limit", "Release a previous snapshot before capturing another (maximum 8).");
        foreach (var owner in Owners(request.asset_path))
        {
            string prefix = OwnerKey(owner) + "/";
            object document = null;
            if (request.document_id == prefix + "$asset" && owner is IGraphAsset) document = owner;
            else
            {
                var field = HGDocumentInspection.DocumentFields(owner.GetType())
                    .FirstOrDefault(candidate => request.document_id == prefix + HGDocumentInspection.FieldKey(candidate));
                if (field != null) document = field.GetValue(owner);
            }
            if (document == null)
                foreach (var custom in HaruGraphExtensions.Bindings(owner))
                    if (custom.id == request.document_id && custom.entry.Binding.TryRead(owner, out var bound)) document = bound;
            if (document == null) continue;
            HGDocumentSnapshot data = HGDocumentInspection.Capture(document, "owner_memory");
            return StoreSnapshot(data, owner, request.document_id);
        }
        return Error("document_unavailable", "Document not found or null. Run list_documents again; no document was created.");
    }

    private static object ListWindows(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Inspect windows in idle Edit Mode.");
        var entries = new List<object>();
        foreach (var window in Resources.FindObjectsOfTypeAll<HaruGraphWindow>().OrderBy(value => value.GetInstanceID()))
            if (window.TryGetDocumentInspectionInfo(out var owner, out string documentId, out bool dirty))
                entries.Add(new { window_id = window.GetInstanceID(), title = window.titleContent.text,
                    document_id = documentId, owner_instance_id = owner.GetInstanceID(), owner_name = owner.name,
                    asset_path = AssetDatabase.GetAssetPath(owner), working_copy_dirty = dirty });
        return new SuccessResponse("Existing bound document windows only. Does not open, focus or rebuild windows. document_id is the window binding ID, not the ID used by asset capture.",
            new { total = entries.Count, next_offset = Next(request, entries.Count), entries = entries.Skip(request.offset).Take(request.page_size).ToArray() });
    }

    private static object CaptureWindow(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Capture windows in idle Edit Mode.");
        if (snapshots.Count >= 8) return Error("snapshot_limit", "Release a previous snapshot before capturing another (maximum 8).");
        var window = Resources.FindObjectsOfTypeAll<HaruGraphWindow>().FirstOrDefault(value => value.GetInstanceID() == request.window_id);
        if (window == null || !window.TryGetDocumentInspectionInfo(out var owner, out string documentId, out bool dirty))
            return Error("window_unavailable", "Select a bound window from list_windows. No window was opened.");
        var data = window.CaptureDocumentSnapshot();
        if (data == null) return Error("document_unavailable", "The window no longer has a bound document draft.");
        return StoreSnapshot(data, owner, documentId, request.window_id, dirty);
    }

    internal static object StoreSnapshot(HGDocumentSnapshot data, UnityEngine.Object owner, string documentId,
        int? windowId = null, bool? workingCopyDirty = null, Action<string> onStored = null)
    {
        foreach (string expired in snapshots.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            snapshots.Remove(expired);
        if (snapshots.Count >= 8) return Error("snapshot_limit", "Release a previous snapshot before capturing another (maximum 8).");
        if (UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
        {
            data.Complete = false;
            data.Diagnostics.Add("Owner has missing managed-reference types; unavailable content was not cleaned or reconstructed. This is an owner-wide warning.");
        }
        string json = JsonUtility.ToJson(data);
        if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024)
            return Error("snapshot_too_large", "Snapshot exceeds 8 MiB. No snapshot retained; inspect a smaller asset.");
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        string id = Guid.NewGuid().ToString("N");
        var stored = new StoredSnapshot { Data = data, Expires = DateTime.UtcNow.AddMinutes(10) };
        snapshots.Add(id, stored);
        onStored?.Invoke(id);
        return new SuccessResponse("Detached snapshot captured; no graph validation or execution performed.", new
        {
            snapshot_id = id, schema = data.Schema, source = data.Source, document_id = documentId,
            asset_path = AssetDatabase.GetAssetPath(owner), window_id = windowId, working_copy_dirty = workingCopyDirty, owner_dirty = EditorUtility.IsDirty(owner), root = data.Root,
            complete = data.Complete, record_count = data.Records.Count, content_hash = hash,
            expires_utc = stored.Expires.ToString("O"), diagnostics = data.Diagnostics,
            kinds = data.Records.GroupBy(record => record.Kind).ToDictionary(group => group.Key, group => group.Count()),
            limitations = new[] { data.Source == "agent_working_copy" || data.Source == "agent_baseline" || data.Source == "agent_save_as_preview"
                    ? "Agent draft or its captured baseline; independent of current Owner memory and window drafts."
                    : data.Source == "window_working_copy"
                    ? "Bound document draft only; shared-asset focus drafts are excluded. Binding IDs are not asset document IDs."
                    : "Owner memory may differ from disk and HaruGraph window drafts.",
                "Content hash is a snapshot fingerprint, not a commit conflict token.",
                "External Unity assets remain references; capture each referenced graph separately.",
                "Diagnostics describe capture completeness, not structural or gameplay validity." }
        });
    }

    internal static void ReleaseSnapshot(string id)
    {
        if (id != null) snapshots.Remove(id);
    }

    private static object Read(Parameters request, bool fields)
    {
        if (request.snapshot_id == null || !snapshots.TryGetValue(request.snapshot_id, out var stored))
            return Error("snapshot_unavailable", "Snapshot expired, was released, or domain reloaded. Capture again.");
        if (!fields)
        {
            var matches = stored.Data.Records.Where(record => (string.IsNullOrEmpty(request.kind) || record.Kind == request.kind)
                && Matches(request.search, record.Label, record.Name, record.Description, record.Type)).ToArray();
            return new SuccessResponse("Snapshot record page.", new
            {
                total = matches.Length, next_offset = Next(request, matches.Length),
                records = matches.Skip(request.offset).Take(request.page_size)
                    .Select(record => new { key = record.Key, type = record.Type, kind = record.Kind,
                        stable_id = record.StableId, name = record.Name, label = record.Label,
                        description = record.Description, field_count = record.Fields.Count }).ToArray()
            });
        }
        var selected = stored.Data.Records.FirstOrDefault(record => record.Key == request.record_key);
        if (selected == null) return Error("record_unavailable", "Use a record_key from records in the same snapshot.");
        var page = new List<HGSnapshotField>();
        int characters = 0;
        foreach (var field in selected.Fields.Skip(request.offset).Take(request.page_size))
        {
            int size = (field.Name?.Length ?? 0) + (field.Type?.Length ?? 0) + (field.Value?.Length ?? 0) + 128;
            if (page.Count > 0 && characters + size > 60000) break;
            characters += size;
            page.Add(field);
        }
        return new SuccessResponse("Fields retain serialized declaration names and list order; reference values point to record keys.", new
        {
            record_key = selected.Key, total = selected.Fields.Count,
            next_offset = (long)request.offset + page.Count < selected.Fields.Count ? (int?)(request.offset + page.Count) : null,
            fields = page.ToArray()
        });
    }

    private static bool Matches(string search, params string[] values) => string.IsNullOrWhiteSpace(search)
        || values.Any(value => value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

    private static object References(Parameters request)
    {
        if (request.snapshot_id == null || !snapshots.TryGetValue(request.snapshot_id, out var stored))
            return Error("snapshot_unavailable", "Capture again.");
        if (!stored.Data.Records.Any(record => record.Key == request.record_key))
            return Error("record_unavailable", "Select a record_key from this snapshot.");
        var links = stored.Data.Records.SelectMany(record => record.Fields
            .Where(field => field.Kind == "reference" && (record.Key == request.record_key || field.Value == request.record_key))
            .Select(field => new { source = record.Key, field = field.Name, target = field.Value })).ToArray();
        return new SuccessResponse("Serialized references in both directions; these include ownership and library references, not only executable connections.",
            new { total = links.Length, next_offset = Next(request, links.Length), references = links.Skip(request.offset).Take(request.page_size).ToArray() });
    }

    private static object Catalog(Parameters request)
    {
        if (!string.IsNullOrEmpty(request.kind) && !new[] { "action", "formula", "token", "asset", "family" }.Contains(request.kind))
            return Error("invalid_kind", "Catalog kind must be action, formula, token, asset or family.");
        var entries = HGTypeIndex.Concrete(typeof(GraphNodeContent))
            .Concat(HGTypeIndex.Concrete(typeof(GraphToken)))
            .Concat(HGTypeIndex.Concrete(typeof(ScriptableObject)).Where(type => typeof(IGraphAsset).IsAssignableFrom(type)))
            .Concat(HGTypeIndex.Concrete(typeof(FormulaSlotBase)).Where(type => !typeof(UnionSlotBase).IsAssignableFrom(type)))
            .Distinct().Select(type => new
            {
                type = type.AssemblyQualifiedName, label = HGReflect.TypeName(type), description = HGReflect.TypeDescription(type),
                group = HGReflect.TypeCategory(type),
                kind = typeof(GraphToken).IsAssignableFrom(type) ? "token" : typeof(IGraphAsset).IsAssignableFrom(type) ? "asset"
                    : typeof(FormulaSlotBase).IsAssignableFrom(type) ? "family" : HGReflect.FormulaPackType(type) != null ? "formula" : "action",
                formula_pack = HGReflect.FormulaPackType(type)?.AssemblyQualifiedName,
                formula_result = HGReflect.FormulaResultType(type)?.AssemblyQualifiedName,
            }).Where(entry => (string.IsNullOrEmpty(request.kind) || entry.kind == request.kind)
                && Matches(request.search, entry.label, entry.description, entry.type))
            .OrderBy(entry => entry.kind, StringComparer.Ordinal).ThenBy(entry => entry.group, StringComparer.Ordinal)
            .ThenBy(entry => entry.label, StringComparer.Ordinal).ThenBy(entry => entry.type, StringComparer.Ordinal).ToArray();
        return new SuccessResponse("Loaded concrete type catalog, excluding test assemblies. No constructors or graph execution invoked. Entries are not proof of compatibility with a particular graph or Slot; generic definitions are omitted.",
            new { total = entries.Length, next_offset = Next(request, entries.Length), entries = entries.Skip(request.offset).Take(request.page_size).ToArray() });
    }

    private static object Sequence(Parameters request)
    {
        if (request.snapshot_id == null || !snapshots.TryGetValue(request.snapshot_id, out var stored))
            return Error("snapshot_unavailable", "Capture again.");
        var records = stored.Data.Records.ToDictionary(record => record.Key);
        if (request.record_key == null || !records.TryGetValue(request.record_key, out var list) || list.Kind != "list")
            return Error("list_required", "Select the record_key of an ActionSlot list from fields/references.");
        foreach (var field in list.Fields)
            if (field.Kind != "null" && (field.Kind != "reference" || !records.TryGetValue(field.Value, out var target) || target.Kind != "action-slot"))
                return Error("action_list_required", "This list contains non-ActionSlot or unavailable entries; use fields to inspect it.");
        var entries = list.Fields.Skip(request.offset).Take(request.page_size).Select(field =>
        {
            var slot = field.Kind == "reference" ? records[field.Value] : null;
            var nodes = slot?.Fields.Where(value => value.Kind == "reference" && records.TryGetValue(value.Value, out var target) && target.Kind == "node")
                .Select(value => new { field = value.Name, node_key = value.Value, stable_id = records[value.Value].StableId,
                    sources = records[value.Value].Fields.Where(source => source.Kind == "reference" || source.Kind == "unity-object")
                        .Select(source => new { field = source.Name, kind = source.Kind, target = source.Value }).ToArray() }).ToArray();
            return new { index = field.Name, slot_key = slot?.Key, slot_type = slot?.Type, is_null = slot == null,
                settings = slot?.Fields.Where(value => value.Kind == "scalar" || value.Kind == "enum").ToArray(), nodes };
        }).ToArray();
        return new SuccessResponse("Serialized ActionSlot list order, including null and disabled entries. This is not an execution trace; conditions, disabled flags and container semantics determine actual execution.",
            new { snapshot_complete = stored.Data.Complete, total = list.Fields.Count, next_offset = Next(request, list.Fields.Count), entries });
    }

    private static object Candidates(Parameters request)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Query candidates in idle Edit Mode.");
        var type = HGTypeIndex.Concrete(typeof(GraphSlotBase)).FirstOrDefault(candidate => candidate.AssemblyQualifiedName == request.slot_type);
        // TypeCache lists declared types, not closed generic Slots such as ActionSlot<TPack>.
        // Only resolve names already observed in our own retained snapshots, never arbitrary input.
        if (type == null && !string.IsNullOrEmpty(request.slot_type)
            && snapshots.Values.Any(snapshot => snapshot.Data.Records.Any(record => record.Type == request.slot_type
                && (record.Kind == "action-slot" || record.Kind == "formula-slot"))))
            type = Type.GetType(request.slot_type, false);
        if (type == null || type.IsAbstract || type.ContainsGenericParameters || !typeof(GraphSlotBase).IsAssignableFrom(type))
            return Error("slot_type_required", "Use an exact concrete Slot type from catalog or a retained snapshot. For closed generic Slots, capture the graph again after snapshot expiry/release or domain reload.");
        if (HGReflect.CreateInstance(type) is not GraphSlotBase slot)
            return Error("slot_creation_failed", "The default Slot could not be constructed.");
        List<Type> types;
        if (slot is FormulaSlotBase formula) types = HGTypeIndex.FormulasFor(formula);
        else if (slot is ActionSlotBase action)
        {
            types = new List<Type>();
            foreach (var candidate in HGTypeIndex.Concrete(action.BodyBaseType))
                if (HGReflect.CreateInstance(candidate) is GraphNodeContent body && action.AcceptsBody(body)) types.Add(candidate);
        }
        else return Error("unsupported_slot", "Only Action and Formula inline-body candidates are supported; Property writes select definitions, not inline bodies.");
        var entries = types.Where(candidate => Matches(request.search, candidate.AssemblyQualifiedName, HGReflect.TypeName(candidate), HGReflect.TypeDescription(candidate)))
            .OrderBy(candidate => candidate.AssemblyQualifiedName, StringComparer.Ordinal)
            .Select(candidate => new { type = candidate.AssemblyQualifiedName, label = HGReflect.TypeName(candidate), description = HGReflect.TypeDescription(candidate) }).ToArray();
        return new SuccessResponse("Inline-body candidates accepted by a NEW default Slot, using the editor's AcceptsBody rules. Constructs temporary Slot/body instances; does not Execute/Evaluate. Captured settings, existing Token/Asset/Property choices and cycle checks are NOT covered.",
            new { scope = "default_slot_type", slot_type = request.slot_type, total = entries.Length,
                next_offset = Next(request, entries.Length), entries = entries.Skip(request.offset).Take(request.page_size).ToArray() });
    }

    private static int? Next(Parameters request, int count) => (long)request.offset + request.page_size < count
        ? request.offset + request.page_size : (int?)null;
    private static object Error(string code, string message) => new ErrorResponse("harugraph." + code, new { message });
}
}
