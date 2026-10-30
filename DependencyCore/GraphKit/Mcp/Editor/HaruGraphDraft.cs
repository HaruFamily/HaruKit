namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>Bounded Agent authoring transactions independent of graph windows.</summary>
[McpForUnityTool("haru_graph_draft", Description = "Domain-independent GraphKit draft authoring. Discover registered bindings, direct IGraphDocument fields and editable graph assets through haru_graph_inspect. Use open/new_asset/new_owner, then status for supported actions. Edit nodes, Action/Formula slots, Token/Property libraries, assets, nested fields, groups and layout through isolated sessions. endpoints keys expire after each mutation. apply_batch supports 1..32 atomic edits with prior-step $ref data and one Undo; check asserts read-only query results. validate reports structural/document diagnostics, not execution. preview/commit and preview_save_as/save_as enforce generation and source conflicts. No gameplay services, execution contexts or domain-specific test actions. Custom execution integrations own their context and isolation. Supports scalar and common Unity values; slot_asset_choices/slot_asset_reference select compatible persistent ScriptableObjects by exact path and GUID/localFileId. Use haru_graph_transaction for multi-document and dependent-asset commits; haru_graph_execute discovers registered execution providers. status lists limits and capabilities. No graph window is required.")]
public static partial class HaruGraphDraft
{
    public sealed class Parameters
    {
        [ToolParameter("Structured field path from structured_fields: an array of exact field_id strings and zero-based list indices, max depth 16. Omit or [] to query the inline node body. Requery paths after list edits; nested slot keys expire after every mutation.", Required = false)]
        public JArray value_path { get; set; }
        [ToolParameter("metadata_fields, set_metadata_value, structured_types, replace_structured_value, structured_fields, set_structured_value, initialize_structured_value, insert_value_item, remove_value_item, move_value_item, asset_types, new_asset, owner_types, new_owner, apply_batch, check, asset_parameters, configure_asset_parameter, open, status, capture, value_fields, set_value, endpoints, check_connection, connect, disconnect, undo, redo, preview, validate, history, commit, preview_save_as, save_as, discard, catalog_fields, field_options, set_field_options, set_field_count, layout_nodes, configure_group, group_tabs, add_group_tab, rename_group_tab, remove_group_tab, activate_group_tab, group_member_tab, duplicate_group, nodes, configure_node, duplicate_nodes, set_group_rect, node_candidates, create_node, replace_node, delete_node, library, create_definition, rename_definition, duplicate_definition, delete_definition, configure_property, slot_lists, slot_items, insert_slot, remove_slot, move_slot, roots, root_options, add_root, remove_root, asset_choices, slot_value, slot_asset_choices, slot_asset_reference, group_scopes, groups, group_members, create_group, edit_group, delete_group, group_member")]
        public string action { get; set; }
        [ToolParameter("Exact asset path from search_documents; required for open.", Required = false)]
        public string asset_path { get; set; }
        [ToolParameter("Exact document_id from search_documents/list_documents; required for open.", Required = false)]
        public string document_id { get; set; }
        [ToolParameter("Draft ID returned by open/new_asset/new_owner; required for draft operations. Discovery actions asset_types/owner_types and creation/open do not require a draft.", Required = false)]
        public string draft_id { get; set; }
        [ToolParameter("Current generation from status; required for all command actions except open/new_asset/asset_types/status/capture/history/discard.", Required = false)]
        public int? generation { get; set; }
        [ToolParameter("Exact node StableId from this draft's captured records. Required for value_fields/set_value.", Required = false)]
        public string node_id { get; set; }
        [ToolParameter("1..64 distinct node IDs from nodes for duplicate_nodes. Only this selection is copied; outside slot connections are cleared.", Required = false)]
        public JArray node_ids { get; set; }
        [ToolParameter("Optional finite {x,y} offset for duplicate_nodes, default {x:40,y:40}. Applied only to nodes with manual positions.", Required = false)]
        public JObject position_offset { get; set; }
        [ToolParameter("Carrier disabled flag for configure_node; affects all references to this shared carrier.", Required = false)]
        public bool? disabled { get; set; }
        [ToolParameter("Node note for configure_node, max 16384 characters; explicit null clears, omitted preserves.", Required = false)]
        public JToken note { get; set; }
        [ToolParameter("Manual node position {x,y}, finite float32. Mutually exclusive with clear_position=true.", Required = false)]
        public JObject position { get; set; }
        [ToolParameter("Clear manual position for configure_node so canvas layout may place it.", Required = false)]
        public bool? clear_position { get; set; }
        [ToolParameter("Group rectangle {x,y,width,height} for set_group_rect. Finite float32, nonnegative dimensions; does not move member nodes.", Required = false)]
        public JObject rect { get; set; }
        [ToolParameter("layout_nodes mode: translate, align_x, align_y, distribute_x, distribute_y, grid, clear. Uses position as delta/axis target/grid origin. Except grid/clear, all nodes need manual positions.", Required = false)]
        public string layout_mode { get; set; }
        [ToolParameter("Grid anchor spacing {x,y}, positive finite values; default {x:320,y:200}. No node size measurement.", Required = false)]
        public JObject spacing { get; set; }
        [ToolParameter("Grid columns 1..64, default 4. Input node_ids order determines grid order.", Required = false)]
        public int? columns { get; set; }
        [ToolParameter("Group comment, max 16384 characters; explicit null clears. Distinct from tab name.", Required = false)]
        public JToken comment { get; set; }
        [ToolParameter("Group custom color {r,g,b,a}; finite values, alpha stored as 1 by existing group semantics.", Required = false)]
        public JObject custom_color { get; set; }
        [ToolParameter("Restore the group's existing theme palette color; mutually exclusive with custom_color.", Required = false)]
        public bool? reset_color { get; set; }
        [ToolParameter("Generation-local tab index from group_tabs or preceding tab command. Last tab cannot be removed; removed members return to first tab.", Required = false)]
        public int? tab_index { get; set; }
        [ToolParameter("Exact field_id returned by value_fields.", Required = false)]
        public string field_id { get; set; }
        [ToolParameter("JSON scalar or exact component object from value_format. Supports Vector2/3/4, Vector2Int/3Int, Color/Color32, Rect/RectInt. Enum values use an exact declared name. Required for set_value. slot_asset_reference uses null or exactly {asset_path,asset_reference_id}; omit to read.", Required = false)]
        public JToken value { get; set; }
        [ToolParameter("Generation-local slot key from endpoints.", Required = false)]
        public string slot_key { get; set; }
        [ToolParameter("Generation-local source key from endpoints.", Required = false)]
        public string source_key { get; set; }
        [ToolParameter("Exact token returned by preview for commit, or preview_save_as for save_as. Tokens cannot be mixed.", Required = false)]
        public string preview_token { get; set; }
        [ToolParameter("Exact new Assets/.../*.asset path in an existing folder for preview_save_as/save_as. Standalone $asset or single-document ScriptableObject Owner drafts; never overwrites, creates folders or replaces references. save_as requires the preview_save_as token.", Required = false)]
        public string destination_path { get; set; }
        [ToolParameter("Scope from group_scopes; groups affect layout only.", Required = false)]
        public string scope_id { get; set; }
        [ToolParameter("Exact group_id from groups.", Required = false)]
        public string group_id { get; set; }
        [ToolParameter("Whether the node belongs to the selected group.", Required = false)]
        public bool? member { get; set; }
        [ToolParameter("Group collapsed state for edit_group.", Required = false)]
        public bool? collapsed { get; set; }
        [ToolParameter("Exact assembly-qualified type from asset_types (new_asset), owner_types (new_owner), catalog, node_candidates or library types.", Required = false)]
        public string type { get; set; }
        [ToolParameter("inline, token, property or asset for create_node/replace_node.", Required = false)]
        public string source_kind { get; set; }
        [ToolParameter("token, property or types for library; token/property for definition edits.", Required = false)]
        public string library { get; set; }
        [ToolParameter("Exact definition_id from library.", Required = false)]
        public string definition_id { get; set; }
        [ToolParameter("Non-empty definition name, maximum 256 characters.", Required = false)]
        public string name { get; set; }
        [ToolParameter("Exact asset_reference_id from asset_choices; combine with asset_path.", Required = false)]
        public string asset_reference_id { get; set; }
        [ToolParameter("Path substring for asset_choices, or type-name substring for asset_types/owner_types.", Required = false)]
        public string search { get; set; }
        [ToolParameter("Generation-local list_key from slot_lists.", Required = false)]
        public string list_key { get; set; }
        [ToolParameter("Zero-based item index; insert permits Count for append.", Required = false)]
        public int? index { get; set; }
        [ToolParameter("Final zero-based index for move_slot.", Required = false)]
        public int? destination_index { get; set; }
        [ToolParameter("Generation-local root_key from root_options for add_root.", Required = false)]
        public string root_key { get; set; }
        [ToolParameter("Generation-local root_index from roots for remove_root.", Required = false)]
        public int? root_index { get; set; }
        [ToolParameter("Exact parameter name from asset_parameters.", Required = false)]
        public string parameter_name { get; set; }
        [ToolParameter("Exact assembly-qualified parameter family from asset_parameters; never inferred from result type.", Required = false)]
        public string parameter_family { get; set; }
        [ToolParameter("Referenced asset revision returned by asset_parameters, required for configure_asset_parameter.", Required = false)]
        public string schema_revision { get; set; }
        [ToolParameter("Enable/disable this caller's parameter override; disabled bindings retain values and connections.", Required = false)]
        public bool? override_enabled { get; set; }
        [ToolParameter("1..32 atomic graph query/edit steps for apply_batch. Each object has action and optional unique id; omit draft_id/generation. Use {\"$ref\":\"priorId.details.node_id\"} to reference prior step data. No persistence or nested batches.", Required = false)]
        public JArray commands { get; set; }
        [ToolParameter("1..32 checks: {name?, query:{action,...}, path: dot path in query data, op: equals|exists|count, expected: JSON}. Queries are read-only; omit draft_id/generation/value. Missing differs from null; count applies to arrays on the requested page. Failed checks return checks_failed and roll back enclosing apply_batch.", Required = false)]
        public JArray checks { get; set; }
        [ToolParameter("Catalog snapshot token from field_options; required for set_field_options/set_field_count.", Required = false)]
        public string catalog_revision { get; set; }
        [ToolParameter("Generation returned by field_options; required for writes, including within apply_batch.", Required = false)]
        public int? options_generation { get; set; }
        [ToolParameter("Distinct option_index values for HGOptions selection. Empty clears a list; scalar clear requires AllowEmpty and a reference type.", Required = false)]
        public JArray option_indices { get; set; }
        [ToolParameter("Option index from field_options for set_field_count.", Required = false)]
        public int? option_index { get; set; }
        [ToolParameter("Desired count for one HGSpinner choice. Respects declared CountMin/CountMax; other values retain order and counts.", Required = false)]
        public int? count { get; set; }
        [ToolParameter("Property retention; configure_property can also set supported initial value via value.", Required = false)]
        public bool? retain_value { get; set; }
        [ToolParameter("Zero-based offset for endpoints/history/validate.", Required = false)]
        public int offset { get; set; }
        [ToolParameter("Page size 1..100 for endpoints/history/validate, default 25; validate caps each page at 20.", Required = false)]
        public int page_size { get; set; } = 25;
    }

    private sealed class Draft
    {
        public HGDocumentSession<IGraphDocument> Session;
        public HGDocumentBinding<IGraphDocument> Binding;
        public HGEditorExtensionContext Context = HGEditorExtensionContext.Default;
        public DateTime Expires;
        public Func<IGraphDocument> ReadDocument;
        public HGDocumentSnapshot Baseline;
        public IGraphDocument BaselineDocument;
        public string BaselineHash;
        public string AssetRevision;
        public string DiskRevision;
        public bool InitiallyDirty;
        public string PreviewToken;
        public string PreviewHash;
        public int PreviewGeneration = -1;
        public bool RequiresReopen;
        public bool OwnsOwner;
        public FieldInfo DocumentField;
        public string SaveAsToken;
        public string SaveAsHash;
        public string SaveAsPath;
        public string SaveAsFolderGuid;
        public int SaveAsGeneration = -1;
        public readonly List<object> History = new();
        public int HistoryDropped;
        public readonly Dictionary<UnityEngine.ScriptableObject, string> ParameterAssets = new();
    }

    private static readonly Dictionary<string, Draft> drafts = new();

    static HaruGraphDraft()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseDrafts;
        EditorApplication.quitting += ReleaseDrafts;
    }

    private static bool ReleaseDraft(string id)
    {
        if (id == null || !drafts.TryGetValue(id, out var draft)) return false;
        drafts.Remove(id);
        if (draft.OwnsOwner && draft.Session.Owner != null && !EditorUtility.IsPersistent(draft.Session.Owner))
            UnityEngine.Object.DestroyImmediate(draft.Session.Owner);
        return true;
    }

    private static void ReleaseDrafts()
    {
        foreach (string id in drafts.Keys.ToArray()) ReleaseDraft(id);
    }

    public static object HandleCommand(JObject parameters)
    {
        try
        {
            if (parameters == null) return Error("invalid_request", "Provide action and its required parameters.");
            var input = new ToolParams(parameters);
            string action = input.Get("action");
            string id = input.Get("draft_id");
            foreach (string expired in drafts.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
                ReleaseDraft(expired);
            if (action == "discard")
                return new SuccessResponse("Draft discarded without writing Owner or changing a window.", new { removed = ReleaseDraft(id) });
            if (!new[] { "open", "status", "capture", "value_fields", "set_value", "undo", "redo", "endpoints",
                "check_connection", "connect", "disconnect", "preview", "validate", "history", "commit", "preview_save_as", "save_as", "new_asset", "asset_types", "owner_types", "new_owner", "apply_batch", "check" }.Contains(action) && !authoringActions.Contains(action))
                return Error("unknown_action", "Use an action listed in the tool schema. Read status for supported authoring actions.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return Error("editor_busy", "Use drafts in idle Edit Mode.");
            if (action == "open") return Open(input.Get("asset_path"), input.Get("document_id"));
            if (action == "owner_types") return OwnerTypes(input);
            if (action == "new_owner") return NewOwner(input.Get("type"));
            if (action == "new_asset") return NewAsset(input.Get("type"));
            if (action == "asset_types") return AssetTypes(input);
            if (id == null || !drafts.TryGetValue(id, out var draft) || draft.Session.Owner == null)
                return Error("draft_unavailable", "Draft expired, was discarded, its Owner was destroyed, or domain reloaded. Open again.");
            draft.Expires = DateTime.UtcNow.AddMinutes(30);
            if (action == "status") return Status(id, draft);
            int offset = input.GetRaw("offset")?.ToObject<int>() ?? 0;
            int pageSize = input.GetRaw("page_size")?.ToObject<int>() ?? 25;
            if (offset < 0 || pageSize < 1 || pageSize > 100) return Error("invalid_page", "offset >= 0 and page_size 1..100 are required.");
            if (action == "history") return new SuccessResponse("Chronological command history, not a final-state diff. Preview snapshots are authoritative.",
                new { total = draft.History.Count, older_entries_dropped = draft.HistoryDropped, next_offset = (long)offset + pageSize < draft.History.Count ? (int?)(offset + pageSize) : null,
                    entries = draft.History.Skip(offset).Take(pageSize).ToArray() });
            if (action != "capture")
            {
                if (input.GetRaw("generation")?.ToObject<int>() != draft.Session.Generation)
                    return Error("stale_generation", "Read status and resolve targets again before editing. No change applied.");
                if (draft.RequiresReopen) return Error("reopen_required", "A previous commit could not confirm persistence. Inspect Owner and open a fresh draft; automatic retry is disabled.");
                if (action == "apply_batch") return ApplyBatch(id, draft, input);
                if (action == "check") return CheckDraft(id, draft, input);
                if (authoringActions.Contains(action)) return AuthoringCommand(draft, input, action, offset, pageSize);
                if (action == "validate") return Validate(draft, offset, pageSize);
                if (action == "preview") return Preview(draft);
                if (action == "commit") return Commit(draft, input.Get("preview_token"));
                if (action == "preview_save_as") return PreviewSaveAs(draft, input.Get("destination_path"));
                if (action == "save_as") return SaveAs(draft, input.Get("destination_path"), input.Get("preview_token"));
                if (action == "endpoints" || action == "check_connection" || action == "connect" || action == "disconnect")
                    return ConnectionCommand(draft, input, action, offset, pageSize);
                if (action == "undo" || action == "redo")
                    return CommandResult(draft, action == "undo" ? draft.Session.Undo() : draft.Session.Redo(), action);
                return ValueCommand(draft, input, action == "set_value");
            }
            return HaruGraphInspect.StoreSnapshot(HGDocumentInspection.Capture(draft.Session.Document, "agent_working_copy"),
                draft.Session.Owner, draft.Session.DocumentId, workingCopyDirty: draft.Session.IsDirty);
        }
        catch (Exception exception) { return Error("draft_failed", exception.Message); }
    }

    private static object Open(string assetPath, string documentId)
    {
        if (drafts.Count >= 16) return Error("draft_limit", "Discard a previous draft before opening another (maximum 16).");
        if (string.IsNullOrEmpty(documentId)) return Error("document_required", "Select a document_id from search_documents/list_documents.");
        foreach (var owner in HaruGraphInspect.Owners(assetPath))
        {
            string prefix = HaruGraphInspect.OwnerKey(owner) + "/";
            var field = HGDocumentInspection.DocumentFields(owner.GetType())
                .FirstOrDefault(candidate => prefix + HGDocumentInspection.FieldKey(candidate) == documentId);
            bool assetDocument = documentId == prefix + "$asset" && owner is IGraphAsset;
            var custom = HaruGraphExtensions.Bindings(owner).FirstOrDefault(item => item.id == documentId).entry;
            if (field == null && !assetDocument && custom == null) continue;
            if (SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                return Error("missing_types", "Owner contains missing managed-reference types. Draft opening refuses automatic Owner cleanup.");
            string assetRevision = AssetRevision(owner);
            string diskRevision = DiskRevision(owner);
            bool initiallyDirty = AssetIsDirty(owner);
            Func<IGraphDocument> read;
            HGDocumentBinding<IGraphDocument> binding;
            if (custom != null)
            {
                read = () => { custom.Binding.TryRead(owner, out var value); return value; };
                binding = new HGDocumentBinding<IGraphDocument>(documentId,
                    _ => read(), (target, document) =>
                    { if (!custom.Binding.TryWrite(target, document)) throw new InvalidOperationException("Registered binding rejected the write."); },
                    create: null, readRevision: target => AssetRevision(target) + ":" + custom.Binding.ReadRevision(target));
            }
            else if (assetDocument)
            {
                if (owner is not IEditableGraphAsset || owner is not UnityEngine.ScriptableObject asset)
                    return Error("unsupported_asset", "This asset has no explicit isolated editing contract (IEditableGraphAsset).");
                var adapter = new HGAssetDocumentBinding(asset, documentId,
                    "ast:" + AssetDatabase.AssetPathToGUID(assetPath), target => AssetRevision(target));
                binding = adapter.Binding;
                read = adapter.ReadDocument;
            }
            else
            {
                read = () => field.GetValue(owner) as IGraphDocument;
                binding = new HGDocumentBinding<IGraphDocument>(documentId,
                    target => field.GetValue(target) as IGraphDocument,
                    (target, document) => field.SetValue(target, document), create: null,
                    readRevision: target => AssetRevision(target));
            }
            var live = read();
            if (live == null) return Error("document_unavailable", "Document is null; no document was created.");
            var baseline = HGDocumentInspection.Capture(live, "agent_baseline");
            if (!baseline.Complete) return Error("incomplete_document", "Draft requires a complete baseline; inspect capture diagnostics first.");
            string baselineHash = Fingerprint(live);
            if (!HGDocumentSession<IGraphDocument>.TryOpenPreservingOwner(owner, binding, custom?.Context ?? HGEditorExtensionContext.Default, out var session, out var diagnostic))
                return new ErrorResponse("harugraph.draft.open_failed", new { diagnostic });
            if (AssetRevision(owner) != assetRevision || AssetIsDirty(owner) != initiallyDirty)
                return Error("source_changed_during_open", "An editing provider changed Owner while opening. No draft was retained; inspect the source.");
            string id = Guid.NewGuid().ToString("N");
            var draft = new Draft { Session = session, Binding = binding, Context = custom?.Context ?? HGEditorExtensionContext.Default, Expires = DateTime.UtcNow.AddMinutes(30), ReadDocument = read, DocumentField = field,
                Baseline = baseline, BaselineDocument = live,
                BaselineHash = baselineHash, AssetRevision = assetRevision, DiskRevision = diskRevision, InitiallyDirty = initiallyDirty };
            drafts.Add(id, draft);
            return Status(id, draft);
        }
        return Error("unsupported_document", "Select a direct IGraphDocument field or a $asset document implementing IEditableGraphAsset.");
    }

    private static object Status(string id, Draft draft) => new SuccessResponse("Isolated Agent draft. Preview before commit; commit checks current Owner, disk and open-window state.", new
    {
        draft_id = id, source = "agent_working_copy", document_id = draft.Session.DocumentId,
        asset_path = AssetDatabase.GetAssetPath(draft.Session.Owner), generation = draft.Session.Generation,
        document_kind = draft.Session.Document is HGAssetDocument ? "asset" : draft.Session.DocumentId.Contains("/$binding/") ? "registered" : "field",
        document_field = draft.DocumentField == null ? null : HGDocumentInspection.FieldKey(draft.DocumentField),
        is_new_asset = draft.OwnsOwner,
        working_copy_dirty = draft.Session.IsDirty, owner_dirty = EditorUtility.IsDirty(draft.Session.Owner),
        expires_utc = draft.Expires.ToString("O"), commit_supported = !draft.OwnsOwner, requires_reopen = draft.RequiresReopen,
        can_undo = draft.Session.CanUndo, can_redo = draft.Session.CanRedo,
        authoring_actions = authoringActions,
        batch_action = "apply_batch", execution_tool = "haru_graph_execute", transaction_tool = "haru_graph_transaction", max_open_drafts = 16,
        check_action = "check", check_queries = checkQueries,
        creation_actions = new[] { "asset_types", "new_asset", "owner_types", "new_owner", "preview_save_as", "save_as" },
        limitations = new[] { draft.OwnsOwner ? "Unsaved new asset; use preview_save_as/save_as to create a file. Discard destroys its temporary Owner." : "Opened from Owner memory, not an existing window draft.",
            "Opening clones the document and may clean obsolete Tokens in the clone.",
            "Validation is explicit via validate; status does not attest validation. Execution is supplied by separate consumer integrations, not this tool.",
            "Generation is local to this draft, not an Owner conflict token." }
    });

    private static bool EditableField(FieldInfo field, object body)
    {
        Type type = field.FieldType;
        return DirectEditableField(field, body)
            && !field.IsDefined(typeof(HGOptionsAttribute), false) && !field.IsDefined(typeof(HGSpinnerAttribute), false)
            && NumericBounds(field, out _, out _) && SupportedValue(type);
    }

    private static object ValueCommand(Draft draft, ToolParams input, bool write)
    {
        var body = draft.Session.FindInlineBody(input.Get("node_id"));
        if (body == null) return Error("node_unavailable", "Select a unique existing inline node StableId from this draft's snapshot. IDs are not assigned automatically.");
        if (draft.Context.Metadata != null && draft.Context.Metadata.TryGetNodeDescriptor(body.GetType(), out _))
            return MetadataCommand(draft, input, write ? "set_metadata_value" : "metadata_fields", 0, 100);
        var fields = HGReflect.Fields(body.GetType()).Where(field => EditableField(field, body)).ToArray();
        if (!write)
            return new SuccessResponse("Direct public scalar and supported Unity value fields on the inline body. Use exact value_format components. Range/Min constraints are reported and enforced without clamping. Catalog fields use catalog_fields/field_options. Arbitrary nested objects and custom metadata are not supported.",
                new { generation = draft.Session.Generation, fields = fields.Select(field => new
                {
                    field_id = HGDocumentInspection.FieldKey(field), type = field.FieldType.FullName,
                    value = WireValue(field.GetValue(body)), value_format = ValueFormat(field.FieldType), constraints = FieldConstraints(field),
                    enum_names = field.FieldType.IsEnum ? Enum.GetNames(field.FieldType) : null,
                }).ToArray() });
        var selected = fields.FirstOrDefault(field => HGDocumentInspection.FieldKey(field) == input.Get("field_id"));
        if (selected == null) return Error("field_unavailable", "Select an editable field_id from value_fields at the current generation.");
        JToken token = input.GetRaw("value");
        if (token == null) return Error("value_required", "Provide value matching value_format from value_fields.");
        if (!TryValue(token, selected.FieldType, out object value) || !WithinFieldBounds(selected, value))
            return Error("invalid_value", "Value must match the field type and value_format; all object components are required, numeric components must be finite and in range.");
        var descriptor = HGFieldDescriptor.Create<object, object>(HGDocumentInspection.FieldKey(selected),
            target => selected.GetValue(target), (target, next) => selected.SetValue(target, next));
        object before = selected.GetValue(body);
        return CommandResult(draft, draft.Session.EditValue(body, descriptor, value), "set_value",
            new { node_id = input.Get("node_id"), field_id = input.Get("field_id"), before = WireValue(before), after = WireValue(value) });
    }

    private static object CommandResult(Draft draft, HGSessionCommandResult result, string operation = null, object details = null)
    {
        if (result == HGSessionCommandResult.Changed && operation != null)
        {
            if (draft.History.Count == 256) { draft.History.RemoveAt(0); draft.HistoryDropped++; }
            draft.History.Add(new { operation, generation = draft.Session.Generation, details });
        }
        var data = new { result = result.ToString(), generation = draft.Session.Generation,
            working_copy_dirty = draft.Session.IsDirty, can_undo = draft.Session.CanUndo, can_redo = draft.Session.CanRedo,
            diagnostic = draft.Session.LastDiagnostic, details };
        return result == HGSessionCommandResult.Changed || result == HGSessionCommandResult.NoChange
            ? new SuccessResponse("Draft command completed; Owner and window drafts were not written.", data)
            : new ErrorResponse("harugraph.draft.command_rejected", data);
    }

    private static object Error(string code, string message) => new ErrorResponse("harugraph.draft." + code, new { message });
}
}
