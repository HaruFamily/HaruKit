namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static readonly string[] structuredActions = { "structured_fields", "set_structured_value", "initialize_structured_value",
        "insert_value_item", "remove_value_item", "move_value_item", "structured_types", "replace_structured_value" };

    private sealed class StructuredValue
    {
        internal Type Type;
        internal bool ManagedReference;
        internal bool ReadOnly;
        internal FieldInfo Field;
        internal Func<object> Read;
        internal Action<object> Write;
        internal JArray Path;
    }

    private static bool StructuredRecord(Type type) => type != null && type.IsClass && type.IsSerializable && !type.IsAbstract
        && type.Assembly != typeof(GraphNode).Assembly
        && !type.IsGenericType && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type)
        && !typeof(UnityEngine.Object).IsAssignableFrom(type) && !typeof(GraphNodeContent).IsAssignableFrom(type)
        && !typeof(GraphNode).IsAssignableFrom(type) && !typeof(GraphSlotBase).IsAssignableFrom(type)
        && !typeof(GraphToken).IsAssignableFrom(type) && !typeof(GraphProperty).IsAssignableFrom(type)
        && !typeof(Delegate).IsAssignableFrom(type) && !typeof(IGraphDocument).IsAssignableFrom(type)
        && type.GetConstructor(Type.EmptyTypes) != null;

    private static Type StructuredElement(Type type, bool managed = false) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
        && (SupportedValue(type.GetGenericArguments()[0]) || StructuredRecord(type.GetGenericArguments()[0]) || managed && ManagedType(type.GetGenericArguments()[0])) ? type.GetGenericArguments()[0] : null;

    private static bool ManagedType(Type type) => type != null && (type.IsClass || type.IsInterface)
        && type != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type)
        && !typeof(GraphNodeContent).IsAssignableFrom(type) && !typeof(GraphSlotBase).IsAssignableFrom(type)
        && !typeof(GraphNode).IsAssignableFrom(type) && !typeof(GraphToken).IsAssignableFrom(type)
        && !typeof(GraphProperty).IsAssignableFrom(type) && !typeof(IGraphDocument).IsAssignableFrom(type)
        && !typeof(Delegate).IsAssignableFrom(type) && !typeof(IEnumerable).IsAssignableFrom(type);

    private static Type[] StructuredTypes(Type type) => UnityEditor.TypeCache.GetTypesDerivedFrom(type).Concat(new[] { type }).Distinct()
        .Where(candidate => type.IsAssignableFrom(candidate) && StructuredRecord(candidate)).OrderBy(candidate => candidate.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();

    private static bool StructuredField(FieldInfo field, object owner) => DirectEditableField(field, owner)
        && !field.IsDefined(typeof(HGOptionsAttribute), false) && !field.IsDefined(typeof(HGSpinnerAttribute), false)
        && NumericBounds(field, out _, out _)
        && (SupportedValue(field.FieldType) || StructuredRecord(field.FieldType) || StructuredElement(field.FieldType, field.IsDefined(typeof(SerializeReference), false)) != null
            || field.IsDefined(typeof(SerializeReference), false) && ManagedType(field.FieldType)
            || typeof(GraphSlotBase).IsAssignableFrom(field.FieldType))
        && (owner is not GraphSlotBase || field.IsDefined(typeof(HGSlotSettingAttribute), false));

    private static JArray AppendValuePath(JArray path, JToken segment)
    { var next = (JArray)path.DeepClone(); next.Add(segment); return next; }

    private static IEnumerable<StructuredValue> StructuredChildren(StructuredValue parent, bool root = false, HGEditorExtensionContext context = null)
    {
        object owner = parent.Read();
        if (owner == null) yield break;
        if (!parent.ManagedReference && owner.GetType() != parent.Type) throw new ArgumentException("Polymorphic structured values are not supported.");
        Type element = StructuredElement(parent.Type, parent.ManagedReference);
        if (element != null)
        {
            var list = (IList)owner;
            if (list.Count > 1024) throw new ArgumentException("Structured list exceeds 1024 items.");
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                yield return new StructuredValue { Type = element, ManagedReference = parent.ManagedReference, ReadOnly = parent.ReadOnly, Read = () => list[index], Write = parent.ReadOnly ? null : value => list[index] = value,
                    Path = AppendValuePath(parent.Path, new JValue(index)) };
            }
            yield break;
        }
        if (!root && !StructuredRecord(owner.GetType()) && owner is not GraphSlotBase) yield break;
        if (context?.Metadata != null && context.Metadata.TryGetNodeDescriptor(owner.GetType(), out var descriptor))
        {
            foreach (var field in descriptor.Fields)
            {
                if (!field.TryGetVisibility(owner, out bool visible, out var error)) throw new ArgumentException(error?.Message ?? "Metadata visibility failed.");
                if (!visible) continue;
                bool readOnly = parent.ReadOnly || field.ReadOnly;
                yield return new StructuredValue { Type = field.ValueType, ReadOnly = readOnly, ManagedReference = field.ManagedReference, Read = () => field.Read(owner),
                    Write = readOnly || field.Write == null ? null : value => field.Write(owner, value),
                    Path = AppendValuePath(parent.Path, new JValue(field.Id)) };
            }
            yield break;
        }
        foreach (var field in HGReflect.Fields(owner.GetType()).Where(field => StructuredField(field, owner)))
            yield return new StructuredValue { Type = field.FieldType, Field = field, ReadOnly = parent.ReadOnly, ManagedReference = field.IsDefined(typeof(SerializeReference), false), Read = () => field.GetValue(owner),
                Write = parent.ReadOnly ? null : value => field.SetValue(owner, value), Path = AppendValuePath(parent.Path, new JValue(HGDocumentInspection.FieldKey(field))) };
    }

    private static StructuredValue ResolveStructured(object body, JArray path, HGEditorExtensionContext context)
    {
        if (path.Count > 16 || path.ToString(Formatting.None).Length > 8192) throw new ArgumentException("value_path exceeds depth 16 or 8192 characters.");
        var current = new StructuredValue { Type = body.GetType(), Read = () => body, Path = new JArray() };
        var ancestors = new HashSet<object>(ReferenceComparer.Instance);
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].Type != JTokenType.String && path[i].Type != JTokenType.Integer) throw new ArgumentException("Path segments must be exact field IDs or integer indices.");
            object value = current.Read();
            if (value == null || !ancestors.Add(value)) throw new ArgumentException("Path crosses null or a cycle.");
            current = StructuredChildren(current, i == 0, context).SingleOrDefault(child => JToken.DeepEquals(child.Path.Last, path[i]))
                ?? throw new ArgumentException("Path is unavailable, hidden, unsupported or stale; query structured_fields again.");
        }
        return current;
    }

    private static object StructuredWire(StructuredValue item, HaruGraphDraftConnections map)
    {
        object value = item.Read();
        Type element = StructuredElement(item.Type, item.ManagedReference);
        string kind = SupportedValue(item.Type) ? "value" : element != null ? "list" : typeof(GraphSlotBase).IsAssignableFrom(item.Type) ? "slot" : "object";
        bool exact = value == null || value.GetType() == item.Type;
        return new { value_path = item.Path, type = item.Type.AssemblyQualifiedName, runtime_type = value?.GetType().AssemblyQualifiedName, managed_reference = item.ManagedReference, kind, is_null = value == null,
            editable = exact && kind == "value" && item.Write != null,
            can_initialize = value == null && item.Write != null && (element != null || !item.ManagedReference && StructuredRecord(item.Type)),
            can_replace = item.ManagedReference && item.Write != null && element == null,
            can_expand = (exact || item.ManagedReference) && value != null && kind != "value", count = value is IList list ? (int?)list.Count : null,
            element_type = element?.AssemblyQualifiedName, value = kind == "value" ? WireValue(value) : null,
            value_format = kind == "value" ? ValueFormat(item.Type) : null,
            constraints = item.Field == null ? null : FieldConstraints(item.Field),
            enum_names = item.Type.IsEnum ? Enum.GetNames(item.Type) : null,
            slot_key = value is GraphSlotBase ? map.Slots.FirstOrDefault(pair => ReferenceEquals(pair.Value, value)).Key : null };
    }

    private static bool StructuredHasLinks(object value, HashSet<object> seen, ref int visits, int depth = 0)
    {
        if (++visits > 4096 || depth > 32) throw new ArgumentException("Removal inspection exceeded its budget; disconnect and simplify the item first.");
        if (value == null || value is string || value is UnityEngine.Object || value.GetType().IsPrimitive
            || value.GetType().IsEnum || SupportedValue(value.GetType()) || !seen.Add(value)) return false;
        if (value is GraphSlotBase slot) return slot.Node != null;
        if (value is GraphNode || value is GraphToken || value is GraphProperty) return true;
        if (value is IList list)
        {
            foreach (object child in list) if (StructuredHasLinks(child, seen, ref visits, depth + 1)) return true;
        }
        else foreach (var field in HGReflect.Fields(value.GetType()))
            if (!field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized
                && (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false))
                && StructuredHasLinks(field.GetValue(value), seen, ref visits, depth + 1)) return true;
        return false;
    }

    private static object StructuredCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var body = draft.Session.FindInlineBody(input.Get("node_id"));
        if (body == null) return Error("node_unavailable", "Select a unique inline node_id from nodes.");
        JToken rawPath = input.GetRaw("value_path");
        if (rawPath != null && rawPath is not JArray) return Error("invalid_value_path", "value_path must be an array from structured_fields.");
        var path = rawPath as JArray ?? new JArray();
        try
        {
            var target = ResolveStructured(body, path, draft.Context);
            if (action == "structured_fields")
            {
                var map = new HaruGraphDraftConnections(draft.Session);
                var children = StructuredChildren(target, path.Count == 0, draft.Context).ToArray();
                var response = new SuccessResponse("Direct children of the selected visible serialized value. Requery affected paths after list edits and slot keys after every mutation. Catalog fields and graph internals are excluded; managed references expose structured_types and explicit replacement; slots expose only HGSlotSetting fields and a key for existing slot commands.", new
                {
                    generation = draft.Session.Generation, target = StructuredWire(target, map), total = children.Length,
                    next_offset = (long)offset + pageSize < children.Length ? (int?)(offset + pageSize) : null,
                    entries = children.Skip(offset).Take(pageSize).Select(child => StructuredWire(child, map)).ToArray(),
                });
                return JObject.FromObject(response).ToString(Formatting.None).Length <= 1048576 ? response : Error("result_limit", "Structured response exceeds 1048576 characters.");
            }
            if (action == "structured_types")
            {
                if (!target.ManagedReference) return Error("unsupported_value", "Select a SerializeReference field or list item.");
                var types = StructuredTypes(StructuredElement(target.Type, true) ?? target.Type);
                return new SuccessResponse("Assignable serialized data types; constructors run only on explicit replacement/insertion.", new {
                    generation = draft.Session.Generation, total = types.Length,
                    next_offset = (long)offset + pageSize < types.Length ? (int?)(offset + pageSize) : null,
                    entries = types.Skip(offset).Take(pageSize).Select(type => new { type = type.AssemblyQualifiedName }).ToArray() });
            }
            if (target.Write == null) return Error("invalid_value_path", "Select a field or list item, not the root body.");
            object current = target.Read(), next;
            if (!target.ManagedReference && current != null && current.GetType() != target.Type) return Error("unsupported_value", "Polymorphic values cannot be edited here.");
            if (action == "replace_structured_value")
            {
                if (!target.ManagedReference || StructuredElement(target.Type, true) != null)
                    return Error("unsupported_value", "Select a managed-reference field or list item, not its list.");
                int visits = 0;
                if (StructuredHasLinks(current, new HashSet<object>(ReferenceComparer.Instance), ref visits))
                    return Error("item_connected", "Disconnect embedded graph links before replacing a managed reference.");
                string typeId = input.Get("type");
                if (typeId == null && input.GetRaw("value")?.Type == JTokenType.Null) next = null;
                else
                {
                    Type selected = StructuredTypes(target.Type).SingleOrDefault(type => type.AssemblyQualifiedName == typeId);
                    if (selected == null || input.GetRaw("value") != null) return Error("invalid_type", "Select an exact type from structured_types; omit value. To clear, omit type and send value:null.");
                    next = Activator.CreateInstance(selected);
                }
            }
            else if (action == "set_structured_value")
            {
                if (!TryValue(input.GetRaw("value"), target.Type, out next) || target.Field != null && !WithinFieldBounds(target.Field, next))
                    return Error("invalid_value", "Provide a complete typed value within the declared bounds; records and slots cannot be replaced as JSON.");
            }
            else if (action == "initialize_structured_value")
            {
                if (current != null || target.ManagedReference && StructuredElement(target.Type, true) == null || !StructuredRecord(target.Type) && StructuredElement(target.Type, target.ManagedReference) == null || input.GetRaw("value") != null)
                    return Error("invalid_value", "Only a null declared record or List can be initialized, without a value payload.");
                next = Activator.CreateInstance(target.Type);
            }
            else
            {
                Type element = StructuredElement(target.Type, target.ManagedReference);
                if (element == null || current is not IList list || list.Count > 1024) return Error("list_unavailable", "Select an initialized supported List with at most 1024 items.");
                if (input.GetRaw("index")?.Type != JTokenType.Integer || !int.TryParse(input.GetRaw("index").ToString(), out int index)
                    || index < 0 || index > list.Count || action != "insert_value_item" && index == list.Count)
                    return Error("invalid_index", "Use an in-range index; insertion permits Count.");
                var copy = (IList)Activator.CreateInstance(target.Type);
                foreach (var item in list) copy.Add(item);
                if (action == "insert_value_item")
                {
                    if (list.Count == 1024) return Error("list_limit", "Structured lists are limited to 1024 items.");
                    object item;
                    if (SupportedValue(element))
                    {
                        if (!TryValue(input.GetRaw("value"), element, out item)) return Error("invalid_value", "Scalar insertion requires a typed value.");
                    }
                    else
                    {
                        if (input.GetRaw("value") != null) return Error("invalid_value", "Record insertion constructs the declared default type; omit value and edit its fields afterwards.");
                        Type concrete = target.ManagedReference ? StructuredTypes(element).SingleOrDefault(type => type.AssemblyQualifiedName == input.Get("type")) : element;
                        if (concrete == null) return Error("invalid_type", "Managed-reference insertion requires a type from structured_types.");
                        item = Activator.CreateInstance(concrete);
                    }
                    copy.Insert(index, item);
                }
                else if (action == "remove_value_item")
                {
                    int visits = 0;
                    if (StructuredHasLinks(copy[index], new HashSet<object>(ReferenceComparer.Instance), ref visits))
                        return Error("item_connected", "Disconnect this item's slots first; existing disconnect commands preserve source nodes in their candidate pool.");
                    copy.RemoveAt(index);
                }
                else
                {
                    if (input.GetRaw("destination_index")?.Type != JTokenType.Integer || !int.TryParse(input.GetRaw("destination_index").ToString(), out int destination)
                        || destination < 0 || destination >= copy.Count) return Error("invalid_index", "destination_index is the final index in the current list.");
                    if (destination == index) return CommandResult(draft, HGSessionCommandResult.NoChange, action);
                    object item = copy[index]; copy.RemoveAt(index); copy.Insert(destination, item);
                }
                next = copy;
            }
            var descriptor = HGFieldDescriptor.Create<object, object>(path.ToString(Formatting.None), _ => target.Read(), (_, value) => target.Write(value));
            return CommandResult(draft, draft.Session.EditValue(body, descriptor, next), action,
                new { node_id = input.Get("node_id"), value_path = path, count = next is IList values ? (int?)values.Count : null });
        }
        catch (ArgumentException error) { return Error("invalid_value_path", error.Message); }
    }
}
}
