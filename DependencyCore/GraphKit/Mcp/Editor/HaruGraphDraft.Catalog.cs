namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private sealed class CatalogRevision { public readonly string Id = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<object, CatalogRevision> catalogRevisions = new();

    private static bool DirectEditableField(FieldInfo field, object body)
        => (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false)) && !field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized
            && !HGReflect.IsHidden(field) && HGReflect.TryIsShown(body, field, out var error) && string.IsNullOrEmpty(error);

    private static bool NumericBounds(FieldInfo field, out double min, out double max)
    {
        min = double.NegativeInfinity; max = double.PositiveInfinity;
        var range = field.GetCustomAttribute<RangeAttribute>();
        var lower = field.GetCustomAttribute<MinAttribute>();
        if (range == null && lower == null) return true;
        if (field.FieldType != typeof(int) && field.FieldType != typeof(float)) return false;
        if (range != null)
        {
            if (float.IsNaN(range.min) || float.IsInfinity(range.min) || float.IsNaN(range.max) || float.IsInfinity(range.max)) return false;
            min = range.min; max = range.max;
        }
        if (lower != null)
        {
            if (float.IsNaN(lower.min) || float.IsInfinity(lower.min)) return false;
            min = Math.Max(min, lower.min);
        }
        if (field.FieldType == typeof(int)) { min = Math.Max(int.MinValue, Math.Ceiling(min)); max = Math.Min(int.MaxValue, Math.Floor(max)); }
        return min <= max;
    }

    private static object FieldConstraints(FieldInfo field)
    {
        if (!field.IsDefined(typeof(RangeAttribute), false) && !field.IsDefined(typeof(MinAttribute), false)) return null;
        NumericBounds(field, out double min, out double max);
        return new { minimum = double.IsNegativeInfinity(min) ? (double?)null : min,
            maximum = double.IsPositiveInfinity(max) ? (double?)null : max, rejects_out_of_range = true };
    }

    private static bool WithinFieldBounds(FieldInfo field, object value)
    {
        if (!NumericBounds(field, out double min, out double max)) return false;
        if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max)) return true;
        double number = Convert.ToDouble(value);
        return number >= min && number <= max;
    }

    private static bool CatalogShape(FieldInfo field, out Type element, out bool multiple, out HGOptionsAttribute options, out HGSpinnerAttribute spinner)
    {
        options = field.GetCustomAttribute<HGOptionsAttribute>(); spinner = field.GetCustomAttribute<HGSpinnerAttribute>();
        element = null; multiple = false;
        if (options == null && spinner == null || options != null && spinner != null
            || field.IsDefined(typeof(RangeAttribute), false) || field.IsDefined(typeof(MinAttribute), false)) return false;
        Type type = field.FieldType;
        if (spinner != null)
        {
            element = type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
                : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : null;
            multiple = true;
            if (spinner.CountMin < 0 || spinner.CountMax < spinner.CountMin) return false;
        }
        else if (!HGOptionsCatalog.TryValueType(type, out element, out multiple)) return false;
        return element != null && (SupportedValue(element) || typeof(UnityEngine.Object).IsAssignableFrom(element))
            && !string.IsNullOrWhiteSpace(options?.Library ?? spinner.Library);
    }

    private static object CatalogWire(object value)
    {
        if (value is UnityEngine.Object asset)
            return asset == null ? null : new { asset_path = AssetDatabase.GetAssetPath(asset), object_id = EditorUtility.IsPersistent(asset) ? HaruGraphInspect.OwnerKey(asset) : null, persistent = EditorUtility.IsPersistent(asset), type = asset.GetType().FullName };
        return WireValue(value);
    }

    private static object CatalogCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var body = draft.Session.FindInlineBody(input.Get("node_id"));
        if (body == null) return Error("node_unavailable", "Select a unique inline node ID from nodes.");
        var fields = HGReflect.Fields(body.GetType()).Where(field => DirectEditableField(field, body)
            && CatalogShape(field, out _, out _, out _, out _)).ToArray();
        if (action == "catalog_fields") return new SuccessResponse("Direct visible serialized HGOptions/HGSpinner fields; catalogs are read only by field_options. Unsupported/conflicting metadata is excluded.", new
        {
            generation = draft.Session.Generation, total = fields.Length,
            next_offset = (long)offset + pageSize < fields.Length ? (int?)(offset + pageSize) : null,
            entries = fields.Skip(offset).Take(pageSize).Select(field =>
            {
                CatalogShape(field, out var element, out bool multiple, out var options, out var spinner);
                return new { field_id = HGDocumentInspection.FieldKey(field), type = field.FieldType.FullName,
                    element_type = element.FullName, kind = spinner == null ? "options" : "spinner", multiple,
                    library = options?.Library ?? spinner.Library, allow_empty = options?.AllowEmpty == true && !element.IsValueType,
                    count_min = spinner?.CountMin, count_max = spinner?.CountMax };
            }).ToArray(),
        });
        var selected = fields.SingleOrDefault(field => HGDocumentInspection.FieldKey(field) == input.Get("field_id"));
        if (selected == null) return Error("field_unavailable", "Select field_id from catalog_fields at this generation.");
        CatalogShape(selected, out var valueType, out bool isMultiple, out var settings, out var countSettings);
        string library = settings?.Library ?? countSettings.Library;
        if (!HGOptionsCatalog.TryGet(library, valueType, out var catalog, out string error)) return Error("catalog_unavailable", error);
        if (catalog.Count > 4096) return Error("catalog_limit", "Catalog exceeds 4096 entries.");
        object original = selected.GetValue(body);
        var current = isMultiple ? (original as IList)?.Cast<object>().ToList() ?? new List<object>() : new List<object> { original };
        if (current.Count > 4096) return Error("catalog_limit", "Current collection exceeds 4096 values.");
        var choices = catalog.Select(entry => entry.Value).ToList();
        foreach (var value in current)
            if (value != null && !choices.Any(choice => Equals(choice, value))) choices.Add(value);
        string revision = Hash(Encoding.UTF8.GetBytes(catalogRevisions.GetValue(catalog, _ => new CatalogRevision()).Id
            + "/" + catalogRevisions.GetValue(draft, _ => new CatalogRevision()).Id + "/" + input.Get("node_id") + "/" + HGDocumentInspection.FieldKey(selected)));
        string Clip(string text) => text?.Length > 512 ? text.Substring(0, 512) : text;
        if (action == "field_options")
        {
            var response = new SuccessResponse("Option indices are local to this field, generation and catalog_revision. Known choices come from the editor catalog; unknown current values may be retained or removed. Spinner updates one count, preserving all other values. Catalog sources must obey their declared revision contract.", new
            {
                generation = draft.Session.Generation, catalog_revision = revision, kind = countSettings == null ? "options" : "spinner",
                multiple = isMultiple, allow_empty = settings?.AllowEmpty == true && !valueType.IsValueType,
                count_min = countSettings?.CountMin, count_max = countSettings?.CountMax,
                current_is_null = original == null, current = current.Select(CatalogWire).ToArray(), total = choices.Count,
                next_offset = (long)offset + pageSize < choices.Count ? (int?)(offset + pageSize) : null,
                entries = choices.Select((value, index) => new
                {
                    option_index = index, known = index < catalog.Count, value = CatalogWire(value),
                    label = Clip(index < catalog.Count ? catalog[index].Label : HGOptionsCatalog.Display(value)),
                    group = index < catalog.Count ? Clip(catalog[index].Group) : null,
                    tooltip = index < catalog.Count ? Clip(catalog[index].Tooltip) : null,
                    selected_count = current.Count(item => Equals(item, value)),
                    minimum_count = countSettings == null ? (int?)null : index < catalog.Count ? countSettings.CountMin : 0,
                    maximum_count = countSettings == null ? (int?)null : index < catalog.Count ? countSettings.CountMax : current.Count(item => Equals(item, value)),
                    selectable = value is not UnityEngine.Object asset || asset != null && EditorUtility.IsPersistent(asset),
                }).Skip(offset).Take(pageSize).ToArray(),
            });
            return JObject.FromObject(response).ToString(Formatting.None).Length <= 1048576 ? response : Error("result_limit", "Field options response exceeds 1048576 characters.");
        }
        if (input.GetRaw("options_generation")?.Type != JTokenType.Integer
            || input.GetRaw("options_generation").ToObject<int>() != draft.Session.Generation || input.Get("catalog_revision") != revision)
            return Error("stale_options", "Read field_options again; both options_generation and catalog_revision must match.");
        object next;
        if (action == "set_field_options" && settings != null)
        {
            if (input.GetRaw("option_indices") is not JArray indices || indices.Count > 4096)
                return Error("invalid_selection", "Provide option_indices (maximum 4096).");
            var values = new List<object>(); var seen = new HashSet<int>();
            foreach (var item in indices)
            {
                if (item.Type != JTokenType.Integer || !int.TryParse(item.ToString(), out int index) || index < 0 || index >= choices.Count || !seen.Add(index))
                    return Error("invalid_selection", "Indices must be distinct values from field_options.");
                values.Add(choices[index]);
            }
            if (!isMultiple && (values.Count > 1 || values.Count == 0 && (!settings.AllowEmpty || valueType.IsValueType)))
                return Error("invalid_selection", "Single selection requires one index; empty requires AllowEmpty and a reference type.");
            next = isMultiple ? MaterializeCatalog(selected.FieldType, valueType, values)
                : values.Count == 0 ? valueType == typeof(string) ? "" : null : values[0];
        }
        else if (action == "set_field_count" && countSettings != null)
        {
            var rawIndex = input.GetRaw("option_index"); var rawCount = input.GetRaw("count");
            if (rawIndex?.Type != JTokenType.Integer || !int.TryParse(rawIndex.ToString(), out int index) || index < 0 || index >= choices.Count
                || rawCount?.Type != JTokenType.Integer || !int.TryParse(rawCount.ToString(), out int count)
                || count < 0 || count > 4096)
                return Error("invalid_value", "Select an option_index and integer count within its declared bounds and the 4096-value limit.");
            var value = choices[index]; int existing = current.Count(item => Equals(item, value));
            if (index < catalog.Count ? count < countSettings.CountMin || count > countSettings.CountMax : count > existing)
                return Error("invalid_value", "Known counts must satisfy declared limits; unknown current values can only decrease or be removed.");
            for (int i = current.Count - 1; i >= 0 && existing > count; i--)
                if (Equals(current[i], value)) { current.RemoveAt(i); existing--; }
            while (existing < count) { current.Add(value); existing++; }
            if (current.Count > 4096) return Error("catalog_limit", "Result exceeds 4096 values.");
            next = MaterializeCatalog(selected.FieldType, valueType, current);
        }
        else return Error("wrong_field_kind", "Use set_field_options for HGOptions or set_field_count for HGSpinner.");
        var nextValues = isMultiple ? ((IList)next).Cast<object>() : new[] { next };
        if (nextValues.Any(value => value is UnityEngine.Object asset && (asset == null || !EditorUtility.IsPersistent(asset))))
            return Error("nonpersistent_reference", "Only live persistent Unity asset candidates can be written.");
        if (!HGOptionsCatalog.TryGet(library, valueType, out var latest, out error) || !ReferenceEquals(catalog, latest))
            return Error("stale_options", error ?? "Catalog changed during this request.");
        bool same = isMultiple ? original is IList old && old.Cast<object>().SequenceEqual(((IList)next).Cast<object>()) : Equals(original, next);
        var descriptor = HGFieldDescriptor.Create<object, object>(HGDocumentInspection.FieldKey(selected),
            target => selected.GetValue(target), (target, value) => selected.SetValue(target, value));
        return CommandResult(draft, same ? HGSessionCommandResult.NoChange : draft.Session.EditValue(body, descriptor, next), action,
            new { node_id = input.Get("node_id"), field_id = input.Get("field_id"), library, catalog_revision = revision,
                selected_count = isMultiple ? ((IList)next).Count : next == null ? 0 : 1 });
    }

    private static IList MaterializeCatalog(Type type, Type element, List<object> values)
    {
        if (type.IsArray)
        {
            var array = Array.CreateInstance(element, values.Count);
            for (int i = 0; i < values.Count; i++) array.SetValue(values[i], i);
            return array;
        }
        var list = (IList)Activator.CreateInstance(type);
        foreach (var value in values) list.Add(value);
        return list;
    }
}
}
