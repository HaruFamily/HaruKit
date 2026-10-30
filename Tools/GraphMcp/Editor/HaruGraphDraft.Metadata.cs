namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;

public static partial class HaruGraphDraft
{
    private static object MetadataCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var body = draft.Session.FindInlineBody(input.Get("node_id"));
        if (body == null) return Error("node_unavailable", "Select an inline node.");
        if (draft.Context.Metadata == null || !draft.Context.Metadata.TryGetNodeDescriptor(body.GetType(), out var descriptor))
            return Error("metadata_unavailable", "This document binding supplies no descriptor for the selected node. Use reflected field commands.");
        var fields = descriptor.Fields.Where(field => field.TryGetVisibility(body, out bool visible, out var error)
            ? visible : throw new InvalidOperationException(error?.Message ?? "Metadata visibility failed.")).ToArray();
        if (action == "metadata_fields") return new SuccessResponse("Visible consumer metadata. Scalar writes use the same session transaction; slot/group/list fields remain typed contracts.", new {
            generation = draft.Session.Generation, total = fields.Length,
            next_offset = (long)offset + pageSize < fields.Length ? (int?)(offset + pageSize) : null,
            entries = fields.Skip(offset).Take(pageSize).Select(field => new { field_id = field.Id, label = field.Label,
                type = field.ValueType.AssemblyQualifiedName, role = field.Role.ToString(), managed_reference = field.ManagedReference,
                value_path = new[] { field.Id },
                editable = field.Role == HGFieldRole.Value && !field.ReadOnly && SupportedValue(field.ValueType),
                value = SupportedValue(field.ValueType) ? WireValue(field.Read(body)) : null }).ToArray() });
        var selected = fields.SingleOrDefault(field => field.Id == input.Get("field_id"));
        if (selected == null || selected.ReadOnly || selected.Role != HGFieldRole.Value || !SupportedValue(selected.ValueType))
            return Error("field_unavailable", "Select a visible writable scalar descriptor.");
        if (!TryValue(input.GetRaw("value"), selected.ValueType, out var value)) return Error("invalid_value", "Value does not match the descriptor type.");
        return CommandResult(draft, draft.Session.EditValue(body, selected, value), action, new { field_id = selected.Id });
    }
}
}
