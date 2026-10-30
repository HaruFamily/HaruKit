namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static bool AssetReferenceType(Type type) => type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)
        && (type.IsAssignableFrom(typeof(ScriptableObject)) || typeof(ScriptableObject).IsAssignableFrom(type));

    private static bool ProjectAssetPath(string path) => !string.IsNullOrWhiteSpace(path)
        && path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains("\\")
        && !path.Split('/').Any(part => part == ".." || part == "." || part.Length == 0)
        && !AssetDatabase.IsValidFolder(path);

    private static object AssetReferenceValue(UnityEngine.Object asset)
        => asset == null || !EditorUtility.IsPersistent(asset) ? null : new
        {
            asset_path = AssetDatabase.GetAssetPath(asset), asset_reference_id = HaruGraphInspect.OwnerKey(asset),
        };

    private static object SlotAssetCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var session = draft.Session;
        var map = new HaruGraphDraftConnections(session);
        string key = input.Get("slot_key");
        if (!map.Slots.TryGetValue(key ?? "", out var target) || target is not FormulaSlotBase slot)
            return Error("slot_unavailable", "Select a Formula/Token input slot from endpoints; Property declarations are not supported by this action.");
        Type type = slot.DefaultEditType;
        if (!AssetReferenceType(type)) return Error("reference_type_unsupported", "This slot must accept ScriptableObject references. Scene objects, components and imported media references are not supported.");
        if (action == "slot_asset_choices")
        {
            string search = input.Get("search") ?? "";
            var paths = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Distinct().Where(path => path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var entries = new List<object>();
            var failures = new List<object>();
            foreach (string path in paths.Skip(offset).Take(pageSize))
            {
                try
                {
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>()
                        .Where(asset => asset != null && type.IsInstanceOfType(asset) && EditorUtility.IsPersistent(asset))
                        .OrderBy(HaruGraphInspect.OwnerKey, StringComparer.Ordinal))
                        entries.Add(new { asset_path = path, asset_reference_id = HaruGraphInspect.OwnerKey(asset), name = asset.name,
                            type = asset.GetType().AssemblyQualifiedName, is_subasset = AssetDatabase.IsSubAsset(asset) });
                }
                catch (Exception error) { failures.Add(new { asset_path = path, message = error.Message }); }
            }
            return new SuccessResponse("Compatible persistent ScriptableObjects, including subassets. Pages scan files, not matching objects; follow next_offset even on an empty page. Select by exact path AND identity; choices are revalidated on assignment.", new
            {
                generation = session.Generation, edit_type = type.AssemblyQualifiedName, entries, failures,
                scanned_files = Math.Min(pageSize, Math.Max(0, paths.Length - offset)), total_files = paths.Length,
                next_offset = (long)offset + pageSize < paths.Length ? (int?)(offset + pageSize) : null,
            });
        }
        JToken token = input.GetRaw("value");
        if (token == null)
        {
            var current = slot.DefaultObject as UnityEngine.Object;
            return new SuccessResponse("Slot asset fallback, not the connected node source. Omit value to read; explicit null clears; otherwise provide only asset_path and asset_reference_id from slot_asset_choices. Source assets are never edited or copied by assignment.", new
            {
                generation = session.Generation, edit_type = type.AssemblyQualifiedName, connected = slot.Node != null,
                value = AssetReferenceValue(current), reference_state = current == null ? ReferenceEquals(current, null) || current.GetInstanceID() == 0 ? "null" : "missing"
                    : EditorUtility.IsPersistent(current) ? "persistent" : "transient",
                writable = !session.InspectObjects().OfType<NamedFormulaSlot>().Any(binding => ReferenceEquals(binding.Slot, slot) && !binding.OverrideEnabled),
            });
        }
        if (session.InspectObjects().OfType<NamedFormulaSlot>().Any(binding => ReferenceEquals(binding.Slot, slot) && !binding.OverrideEnabled))
            return Error("parameter_disabled", "Enable the asset parameter override before editing its fallback.");
        ScriptableObject next = null;
        if (token.Type != JTokenType.Null)
        {
            if (token is not JObject reference || reference.Count != 2 || reference["asset_path"]?.Type != JTokenType.String
                || reference["asset_reference_id"]?.Type != JTokenType.String)
                return Error("invalid_reference", "value must be null or exactly {asset_path:string, asset_reference_id:string} from slot_asset_choices.");
            string path = (string)reference["asset_path"], id = (string)reference["asset_reference_id"];
            if (!ProjectAssetPath(path) || string.IsNullOrWhiteSpace(id))
                return Error("invalid_reference", "Select an exact Assets/... file path and persistent object identity; folders, traversal and Packages are not supported.");
            next = AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>().SingleOrDefault(asset => asset != null
                && EditorUtility.IsPersistent(asset) && HaruGraphInspect.OwnerKey(asset) == id);
            if (next == null) return Error("asset_unavailable", "The exact asset is missing or moved. Refresh slot_asset_choices; names and paths alone are insufficient.");
            if (!type.IsInstanceOfType(next)) return Error("reference_type_mismatch", "The selected asset is incompatible with this slot's edit_type.");
        }
        return CommandResult(draft, session.SetSlotDefault(slot, next), action, new { slot_key = key, value = AssetReferenceValue(next) });
    }
}
}
