namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static readonly string[] authoringActions = { "metadata_fields", "set_metadata_value", "structured_types", "replace_structured_value", "structured_fields", "set_structured_value", "initialize_structured_value", "insert_value_item", "remove_value_item", "move_value_item", "catalog_fields", "field_options", "set_field_options", "set_field_count", "layout_nodes", "configure_group", "group_tabs", "add_group_tab", "rename_group_tab", "remove_group_tab", "activate_group_tab", "group_member_tab", "duplicate_group", "nodes", "configure_node", "duplicate_nodes", "set_group_rect", "node_candidates", "create_node", "replace_node", "delete_node", "asset_parameters", "configure_asset_parameter",
        "library", "create_definition", "rename_definition", "duplicate_definition", "delete_definition", "configure_property",
        "slot_lists", "slot_items", "insert_slot", "remove_slot", "move_slot", "roots", "root_options", "add_root", "remove_root", "asset_choices", "slot_value", "slot_asset_choices", "slot_asset_reference",
        "group_scopes", "groups", "group_members", "create_group", "edit_group", "delete_group", "group_member" };

    // Resolve exact names from the editor catalog or current serialized objects; never load arbitrary input types.
    private static IEnumerable<Type> AuthoringTypes(Draft draft, Type baseType)
        => HGTypeIndex.Concrete(baseType).Concat(draft.Session.InspectObjects().Select(value => value.GetType())
            .Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters
                && type.GetConstructor(Type.EmptyTypes) != null)).Distinct();

    private static GraphNode FindNode(Draft draft, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return draft.Session.InspectObjects().OfType<GraphNode>().SingleOrDefault(node => node.Id == id);
    }

    private static IEnumerable<Type> DefinitionTypes(Draft draft)
    {
        foreach (Type type in AuthoringTypes(draft, typeof(FormulaSlotBase)))
            if (!typeof(UnionSlotBase).IsAssignableFrom(type) && HGReflect.CreateInstance(type) is FormulaSlotBase slot
                && slot.PackType == draft.Session.Document.PackType) yield return type;
        foreach (Type type in AuthoringTypes(draft, typeof(GraphToken)))
            if (HGReflect.CreateInstance(type) is GraphToken token && token.PackType == draft.Session.Document.PackType
                && token.ConfigurationError == null && !token.IsObsolete) yield return type;
    }

    private static object FindDefinition(Draft draft, string library, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (library == "token") return (draft.Session.Document as ITokenOwner)?.Tokens.SingleOrDefault(item => item?.Id == id);
        if (library == "property") return (draft.Session.Document as IPropertyOwner)?.Properties.SingleOrDefault(item => item?.Id == id);
        return null;
    }

    private static object AuthoringCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        if (action == "slot_asset_choices" || action == "slot_asset_reference") return SlotAssetCommand(draft, input, action, offset, pageSize);
        if (action == "metadata_fields" || action == "set_metadata_value") return MetadataCommand(draft, input, action, offset, pageSize);
        if (structuredActions.Contains(action)) return StructuredCommand(draft, input, action, offset, pageSize);
        if (action == "asset_parameters" || action == "configure_asset_parameter")
            return AssetParameterCommand(draft, input, action, offset, pageSize);
        if (new[] { "catalog_fields", "field_options", "set_field_options", "set_field_count" }.Contains(action)) return CatalogCommand(draft, input, action, offset, pageSize);
        if (groupActions.Contains(action)) return GroupCommand(draft, input, action, offset, pageSize);
        if (action == "layout_nodes") return LayoutSelection(draft, input);
        if (action == "duplicate_nodes") return DuplicateNodes(draft, input);
        if (action == "nodes" || action == "configure_node") return NodeLayoutCommand(draft, input, action, offset, pageSize);
        var session = draft.Session;
        string library = input.Get("library");
        string name = input.Get("name");
        if (name?.Length > 256) return Error("invalid_name", "Names are limited to 256 characters.");
        if (action == "slot_value")
        {
            var map = new HaruGraphDraftConnections(session);
            if (!map.Slots.TryGetValue(input.Get("slot_key") ?? "", out var targetSlot) || targetSlot is not FormulaSlotBase slot)
                return Error("slot_unavailable", "Select a Formula/Token input slot from endpoints; Property declarations use configure_property. Union edits change only the outer fallback, not branch settings.");
            JToken token = input.GetRaw("value");
            if (token == null)
            {
                Type type = slot.DefaultEditType;
                bool supported = SupportedValue(type);
                return new SuccessResponse("Slot fallback value; a connected source takes precedence. Omit value to read, include a value matching value_format to write.", new
                {
                    generation = session.Generation, result_type = slot.ResultType?.AssemblyQualifiedName, edit_type = type?.AssemblyQualifiedName, editable = supported,
                    value = supported ? WireValue(slot.DefaultObject) : null, value_format = ValueFormat(type),
                    reference_action = AssetReferenceType(type) ? "slot_asset_reference" : null,
                    enum_names = type?.IsEnum == true ? Enum.GetNames(type) : null,
                });
            }
            if (session.InspectObjects().OfType<NamedFormulaSlot>().Any(binding => ReferenceEquals(binding.Slot, slot) && !binding.OverrideEnabled))
                return Error("parameter_disabled", "Enable the asset parameter override before editing its fallback.");
            if (!TryValue(token, slot.DefaultEditType, out object next)) return Error("invalid_value", "Use a value matching edit_type and value_format; all object components are required, numeric components must be finite and in range.");
            return CommandResult(draft, session.SetSlotDefault(slot, next), action, new { slot_key = input.Get("slot_key"), value = WireValue(next) });
        }
        var lists = session.InspectObjects().OfType<IList>().Where(list => HGDocumentSession<IGraphDocument>.SlotListElementType(list) != null).ToArray();
        object Page(IEnumerable<object> values)
        {
            var all = values.ToArray();
            return new SuccessResponse("Generation-local authoring choices. Resolve again after any command.", new
            {
                generation = session.Generation, total = all.Length,
                next_offset = (long)offset + pageSize < all.Length ? (int?)(offset + pageSize) : null,
                entries = all.Skip(offset).Take(pageSize).ToArray(),
            });
        }
        if (action == "group_scopes") return Page(session.GroupScopes().Select(pair => (object)new { scope_id = pair.Key, label = pair.Value }));
        if (action == "create_group")
        {
            string scope = input.Get("scope_id");
            if (scope == null) return Error("scope_required", "Select scope_id from group_scopes.");
            var result = session.CreateGroup(scope, name, out string id);
            return CommandResult(draft, result, action, new { group_id = id });
        }
        if (action == "set_group_rect" || action == "groups" || action == "group_members" || action == "edit_group" || action == "delete_group" || action == "group_member")
        {
            if (session.Document is not IGraphViewStateOwner) return Error("groups_unsupported", "This document does not expose graph view state.");
            // Inspect existing serialized state rather than invoking a getter which may initialize it.
            var state = session.InspectObjects().OfType<GraphViewState>().SingleOrDefault();
            var groups = state?.NodeGroups ?? Array.Empty<GraphNodeGroup>();
            if (action == "groups") return Page(groups.Where(group => group != null).Select(group => (object)new
            {
                group_id = group.Id, scope_id = group.Scope, name = group.Tabs.FirstOrDefault()?.Name,
                collapsed = group.Collapsed, member_count = group.Members.Count, rect = WireValue(group.Rect),
                comment = group.Title, use_custom_color = group.UseCustomColor, custom_color = WireValue(group.CustomColor),
                palette_index = group.ColorIndex, tab_count = group.Tabs.Count, active_tab = group.ActiveTab,
            }));
            var selected = groups.SingleOrDefault(group => group?.Id == input.Get("group_id"));
            if (selected == null) return Error("group_unavailable", "Select group_id from groups.");
            if (action == "set_group_rect")
            {
                if (input.GetRaw("rect") is not JToken rawRect || !TryValue(rawRect, typeof(Rect), out var value))
                    return Error("invalid_value", "rect requires exact finite x/y/width/height components.");
                var rect = (Rect)value;
                if (rect.width < 0 || rect.height < 0 || float.IsInfinity(rect.xMax) || float.IsInfinity(rect.yMax))
                    return Error("invalid_value", "Group width/height must be nonnegative and rectangle bounds finite.");
                var before = WireValue(selected.Rect);
                return CommandResult(draft, session.SetGroupRect(selected, rect), action,
                    new { group_id = selected.Id, before, after = WireValue(rect) });
            }
            if (action == "group_members") return Page(selected.Members.Select(id => (object)new { node_id = id, tab_index = selected.TabOf(id), on_active_tab = selected.IsOnActiveTab(id) }));
            if (action == "group_member")
            {
                JToken member = input.GetRaw("member");
                if (member?.Type != JTokenType.Boolean) return Error("invalid_value", "member must be a JSON boolean.");
                return CommandResult(draft, session.SetGroupMember(selected, FindNode(draft, input.Get("node_id")), (bool)member), action,
                    new { group_id = selected.Id, node_id = input.Get("node_id"), member = (bool)member });
            }
            JToken collapsed = input.GetRaw("collapsed");
            if (collapsed != null && collapsed.Type != JTokenType.Boolean) return Error("invalid_value", "collapsed must be a JSON boolean.");
            return CommandResult(draft, session.EditGroup(selected, name, collapsed?.ToObject<bool>(), action == "delete_group"), action, new { group_id = selected.Id });
        }
        if (action == "slot_lists") return Page(lists.Select((list, i) => (object)new
        {
            list_key = "l" + i, count = list.Count, element_type = HGDocumentSession<IGraphDocument>.SlotListElementType(list).AssemblyQualifiedName,
        }));
        if (action == "slot_items")
        {
            int listIndex = Enumerable.Range(0, lists.Length).Where(i => "l" + i == input.Get("list_key")).DefaultIfEmpty(-1).First();
            if (listIndex < 0) return Error("list_unavailable", "Select list_key from slot_lists.");
            return Page(lists[listIndex].Cast<GraphSlotBase>().Select((slot, i) => (object)new { index = i, node_id = slot?.Node?.Id }));
        }
        if (action == "roots") return Page(session.ReadRootGroups().Select((root, i) => (object)new
        {
            root_index = i, label = root.RootKey?.ToString(),
            list_key = Array.FindIndex(lists, list => ReferenceEquals(list, root.Items)) is int index && index >= 0 ? "l" + index : null,
        }));
        if (action == "root_options") return Page(session.AvailableRootKeys().Select((key, i) => (object)new
        {
            root_key = "k" + i, label = key?.ToString(), exists = session.ReadRootGroups().Any(root => Equals(root.RootKey, key)),
        }));
        if (action == "add_root")
        {
            var keys = session.AvailableRootKeys();
            string key = input.Get("root_key");
            int index = Enumerable.Range(0, keys.Count).Where(i => "k" + i == key).DefaultIfEmpty(-1).First();
            if (index < 0) return Error("root_unavailable", "Select root_key from root_options.");
            return CommandResult(draft, session.AddRoot(keys[index]), action, new { root_key = key });
        }
        if (action == "remove_root")
        {
            int index = input.GetRaw("root_index")?.ToObject<int>() ?? -1;
            var roots = session.ReadRootGroups();
            if (index < 0 || index >= roots.Count) return Error("root_unavailable", "Select root_index from roots.");
            return CommandResult(draft, session.RemoveRoot(roots[index].Root), action, new { root_index = index });
        }
        if (action == "insert_slot" || action == "remove_slot" || action == "move_slot")
        {
            string key = input.Get("list_key");
            int listIndex = Enumerable.Range(0, lists.Length).Where(i => "l" + i == key).DefaultIfEmpty(-1).First();
            if (listIndex < 0) return Error("list_unavailable", "Select list_key from slot_lists at the current generation.");
            int index = input.GetRaw("index")?.ToObject<int>() ?? -1;
            int destination = input.GetRaw("destination_index")?.ToObject<int>() ?? -1;
            var result = action == "insert_slot" ? session.InsertSlot(lists[listIndex], index)
                : action == "remove_slot" ? session.RemoveSlot(lists[listIndex], index) : session.MoveSlot(lists[listIndex], index, destination);
            return CommandResult(draft, result, action, new { list_key = key, index, destination_index = destination });
        }
        if (action == "library")
        {
            if (library == "token") return Page(((session.Document as ITokenOwner)?.Tokens ?? new List<GraphToken>())
                .Where(item => item != null).Select(item => (object)new { definition_id = item.Id, name = item.Name, family = item.FamilyType?.AssemblyQualifiedName }));
            if (library == "property") return Page(((session.Document as IPropertyOwner)?.Properties ?? new List<GraphProperty>())
                .Where(item => item != null).Select(item => (object)new { definition_id = item.Id, name = item.Name,
                    family = item.FamilyType?.AssemblyQualifiedName, result_type = item.ResultType?.AssemblyQualifiedName,
                    value_format = ValueFormat(item.ResultType), retain_value = item.RetainValue, initial_is_null = item.InitialValueIsNull }));
            if (library == "types") return Page(DefinitionTypes(draft).Select(type => (object)new { type = type.AssemblyQualifiedName,
                    label = HGReflect.TypeName(type), kind = typeof(GraphToken).IsAssignableFrom(type) ? "token" : "family" }));
            return Error("invalid_library", "library must be token, property or types.");
        }
        if (action == "create_definition")
        {
            if (library != "token" && library != "property") return Error("invalid_library", "Select token or property.");
            var type = DefinitionTypes(draft)
                .FirstOrDefault(item => item.AssemblyQualifiedName == input.Get("type"));
            if (type == null || typeof(UnionSlotBase).IsAssignableFrom(type)) return Error("type_unavailable", "Select an exact type from library types.");
            var result = session.CreateDefinition(library == "property", type, name, out string id);
            return CommandResult(draft, result, action, new { library, definition_id = id, name });
        }
        if (action.EndsWith("_definition", StringComparison.Ordinal) || action == "configure_property")
        {
            object definition = FindDefinition(draft, library, input.Get("definition_id"));
            if (definition == null) return Error("definition_unavailable", "Select a unique definition_id from the chosen library.");
            string id = input.Get("definition_id");
            HGSessionCommandResult result;
            if (action == "rename_definition") result = session.RenameDefinition(definition, name);
            else if (action == "duplicate_definition") result = session.DuplicateDefinition(definition, out id);
            else if (action == "delete_definition") result = session.DeleteDefinition(definition);
            else
            {
                if (definition is not GraphProperty property) return Error("invalid_library", "configure_property requires library property.");
                JToken retain = input.GetRaw("retain_value"), initial = input.GetRaw("value");
                if (retain != null && retain.Type != JTokenType.Boolean) return Error("invalid_value", "retain_value must be a JSON boolean.");
                object value = null;
                if (initial != null && !TryValue(initial, property.ResultType, out value)) return Error("invalid_value", "Initial value must match the declared result type and value_format; all object components are required and numeric components must be finite and in range.");
                result = session.ConfigureProperty(property, retain?.ToObject<bool>(), initial != null, value);
            }
            return CommandResult(draft, result, action, new { library, definition_id = id, name });
        }
        if (action == "asset_choices")
        {
            string search = input.Get("search") ?? "";
            var paths = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Distinct().Where(path => path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var entries = new List<object>();
            var failures = new List<object>();
            foreach (string path in paths.Skip(offset).Take(pageSize))
            {
                try
                {
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>().Where(asset => asset is IGraphAsset))
                        entries.Add(new { asset_path = path, asset_reference_id = HaruGraphInspect.OwnerKey(asset), name = asset.name, type = asset.GetType().AssemblyQualifiedName });
                }
                catch (Exception error) { failures.Add(new { asset_path = path, message = error.Message }); }
            }
            return new SuccessResponse("Pages scan candidate files; follow next_offset even when entries are empty. Existing graph assets only; creation/extraction is not performed.",
                new { entries, failures, next_offset = (long)offset + pageSize < paths.Length ? (int?)(offset + pageSize) : null });
        }
        if (action == "node_candidates")
        {
            var map = new HaruGraphDraftConnections(session);
            if (!map.Slots.TryGetValue(input.Get("slot_key") ?? "", out var slot)) return Error("slot_unavailable", "Select slot_key from endpoints.");
            return Page(AuthoringTypes(draft, typeof(GraphNodeContent)).Where(type =>
                HGReflect.CreateInstance(type) is GraphNodeContent body && slot.AcceptsBody(body))
                .Select(type => (object)new { type = type.AssemblyQualifiedName, label = HGReflect.TypeName(type) }));
        }
        var target = FindNode(draft, input.Get("node_id"));
        if (action == "delete_node") return CommandResult(draft, session.DeleteNode(target), action, new { node_id = input.Get("node_id") });
        HGCarrierSource source;
        switch (input.Get("source_kind"))
        {
            case "inline":
                var type = AuthoringTypes(draft, typeof(GraphNodeContent)).FirstOrDefault(item => item.AssemblyQualifiedName == input.Get("type"));
                if (type == null || HGReflect.CreateInstance(type) is not GraphNodeContent body) return Error("type_unavailable", "Select an exact inline type from catalog or node_candidates.");
                source = HGCarrierSource.Body(body);
                break;
            case "token":
                if (FindDefinition(draft, "token", input.Get("definition_id")) is not GraphToken token) return Error("definition_unavailable", "Select a Token definition.");
                source = HGCarrierSource.NamedToken(token);
                break;
            case "property":
                if (FindDefinition(draft, "property", input.Get("definition_id")) is not GraphProperty property) return Error("definition_unavailable", "Select a Property definition.");
                source = HGCarrierSource.NamedProperty(property);
                break;
            case "asset":
                var asset = HaruGraphInspect.Owners(input.Get("asset_path")).OfType<ScriptableObject>()
                    .SingleOrDefault(item => item is IGraphAsset && HaruGraphInspect.OwnerKey(item) == input.Get("asset_reference_id"));
                if (asset == null) return Error("asset_unavailable", "Select exact asset_path and asset_reference_id from asset_choices.");
                source = HGCarrierSource.Asset(asset);
                break;
            default: return Error("source_required", "source_kind must be inline, token, property or asset.");
        }
        string nodeId = target?.Id;
        var command = action == "create_node" ? session.CreateNode(source, out nodeId) : session.ReplaceSource(target, source);
        return CommandResult(draft, command, action, new { node_id = nodeId });
    }
}
}
