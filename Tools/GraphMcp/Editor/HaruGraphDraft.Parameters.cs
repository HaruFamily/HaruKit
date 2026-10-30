namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

public static partial class HaruGraphDraft
{
    private static string ParameterRevision(UnityEngine.ScriptableObject asset) => AssetRevision(asset) + ":" + DiskRevision(asset);

    private static string ParameterAssetBlocker(Draft draft)
    {
        var references = draft.Session.InspectObjects().OfType<GraphNode>().Where(node => node.Kind == NodeKind.Asset).ToArray();
        foreach (var pair in draft.ParameterAssets)
            if (references.Any(node => ReferenceEquals(node.AssetObject, pair.Key))
                && (pair.Key == null || ParameterRevision(pair.Key) != pair.Value))
                return "A referenced asset used for parameter overrides changed. Read its parameters and review/configure the overrides again before saving.";
        return null;
    }

    private static object AssetParameterCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var node = FindNode(draft, input.Get("node_id"));
        if (node?.Kind != NodeKind.Asset || node.AssetObject == null || !EditorUtility.IsPersistent(node.AssetObject))
            return Error("asset_node_required", "Select an Asset node referencing a persistent graph asset.");
        var parameters = AssetGraphSchema.Read(node.AssetObject, out var duplicates);
        if (duplicates.Count != 0) return Error("ambiguous_parameters", "Asset schema contains duplicate family/name identities; fix the asset separately.");
        string revision = ParameterRevision(node.AssetObject);
        var map = new HaruGraphDraftConnections(draft.Session);
        if (action == "asset_parameters")
            return new SuccessResponse("Read-only asset parameter schema and caller overrides. Query does not create bindings. Configure requires this schema_revision and exact parameter_family/name. Disabled overrides retain their values and connections. Enable before editing or connecting the parameter slot.", new
            {
                generation = draft.Session.Generation, schema_revision = revision, asset_path = AssetDatabase.GetAssetPath(node.AssetObject),
                total = parameters.Count, next_offset = offset + pageSize < parameters.Count ? (int?)(offset + pageSize) : null,
                entries = parameters.Skip(offset).Take(pageSize).Select(parameter =>
                {
                    var bindings = node.Bindings.Where(binding => binding?.Name == parameter.Name && binding.Slot?.FamilyType == parameter.Slot?.FamilyType).ToArray();
                    var binding = bindings.Length == 1 ? bindings[0] : null;
                    var type = binding?.Slot.DefaultEditType ?? parameter.Slot?.DefaultEditType;
                    return new
                    {
                        parameter_name = parameter.Name, parameter_family = parameter.Slot?.FamilyType?.AssemblyQualifiedName,
                        result_type = parameter.ResultType?.AssemblyQualifiedName, edit_type = type?.AssemblyQualifiedName,
                        binding_count = bindings.Length, override_enabled = binding?.OverrideEnabled ?? false,
                        slot_key = binding == null ? null : map.Slots.FirstOrDefault(pair => ReferenceEquals(pair.Value, binding.Slot)).Key,
                        source_node_id = binding?.Slot.Node?.Id, value_format = ValueFormat(type),
                        reference_action = AssetReferenceType(type) ? "slot_asset_reference" : null,
                        value = binding != null && SupportedValue(type) ? WireValue(binding.Slot.DefaultObject) : null,
                    };
                }).ToArray(),
            });
        if (input.Get("schema_revision") != revision) return Error("stale_asset_schema", "Read asset_parameters again; the referenced asset changed or no schema_revision was supplied.");
        var selected = parameters.SingleOrDefault(item => item.Name == input.Get("parameter_name")
            && item.Slot?.FamilyType?.AssemblyQualifiedName == input.Get("parameter_family"));
        if (selected?.Slot == null) return Error("parameter_unavailable", "Select exact parameter_name and parameter_family from asset_parameters.");
        var enabled = input.GetRaw("override_enabled");
        if (enabled?.Type != JTokenType.Boolean) return Error("invalid_value", "override_enabled must be a JSON boolean.");
        var token = input.GetRaw("value");
        object value = null;
        if (token != null && (!(bool)enabled || !TryValue(token, selected.Slot.DefaultEditType, out value)))
            return Error("invalid_value", "Enable the override and use its value_format to set a fallback; a connected source still takes precedence.");
        var result = draft.Session.ConfigureAssetParameter(node, selected.Slot.FamilyType, selected.Name,
            (bool)enabled, token != null, value);
        if (result == HaruFamily.DependencyCore.GraphKit.Editor.HGSessionCommandResult.Changed
            || result == HaruFamily.DependencyCore.GraphKit.Editor.HGSessionCommandResult.NoChange)
            draft.ParameterAssets[node.AssetObject] = revision;
        return CommandResult(draft, result, action, new
            {
                node_id = node.Id, parameter_name = selected.Name, parameter_family = selected.Slot.FamilyType.AssemblyQualifiedName,
                override_enabled = (bool)enabled, value_supplied = token != null, value = WireValue(value),
            });
    }
}
}
