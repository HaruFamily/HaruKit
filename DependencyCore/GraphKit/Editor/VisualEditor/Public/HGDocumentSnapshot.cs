namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Detached inspection data. References point to Keys in Records, never live graph objects.</summary>
[Serializable]
public sealed class HGDocumentSnapshot
{
    public string Schema = "harugraph.document/1";
    public string Source;
    public string Root;
    public bool Complete = true;
    public List<HGSnapshotRecord> Records = new();
    public List<string> Diagnostics = new();
}

[Serializable]
public sealed class HGSnapshotRecord
{
    public string Key;
    public string Type;
    public string Kind;
    public string StableId;
    public string Name;
    public string Label;
    public string Description;
    public List<HGSnapshotField> Fields = new();
}

/// <summary>Kind is null, scalar, enum, reference, unity-object, native-json, or unavailable.</summary>
[Serializable]
public sealed class HGSnapshotField
{
    public string Name;
    public string Type;
    public string Kind;
    public string Value;
}

/// <summary>
/// Reads serialized fields without opening a session, invoking graph getters, cleaning data or assigning IDs.
/// Includes hidden fields, library definitions, disconnected nodes and layout. External assets remain references.
/// </summary>
public static class HGDocumentInspection
{
    // JsonUtility on a boxed Rect can produce {}. Preserve signed width/height, not normalized bounds.
    [Serializable]
    private struct RectValue
    {
        public float x, y, width, height;
        public RectValue(Rect rect) { x = rect.x; y = rect.y; width = rect.width; height = rect.height; }
    }

    [Serializable]
    private struct Vector2IntValue
    {
        public int x, y;
        public Vector2IntValue(Vector2Int value) { x = value.x; y = value.y; }
    }

    [Serializable]
    private struct Vector3IntValue
    {
        public int x, y, z;
        public Vector3IntValue(Vector3Int value) { x = value.x; y = value.y; z = value.z; }
    }

    [Serializable]
    private struct RectIntValue
    {
        public int x, y, width, height;
        public RectIntValue(RectInt value) { x = value.x; y = value.y; width = value.width; height = value.height; }
    }

    public static HGDocumentSnapshot Capture(object document, string source, int maxRecords = 20000)
    {
        if (document is not IGraphDocument && document is not IGraphAsset)
            throw new ArgumentException("Expected IGraphDocument or IGraphAsset.", nameof(document));
        if (maxRecords < 1 || maxRecords > 100000) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        return new Reader(source, maxRecords).Capture(document);
    }

    /// <summary>Document fields use declaring type plus field name to distinguish hidden base fields.</summary>
    public static IReadOnlyList<FieldInfo> DocumentFields(Type ownerType)
    {
        var result = new List<FieldInfo>();
        foreach (var field in HGReflect.Fields(ownerType))
            if (Serialized(field) && typeof(IGraphDocument).IsAssignableFrom(field.FieldType)) result.Add(field);
        return result;
    }

    public static string FieldKey(FieldInfo field) => field.DeclaringType.FullName + "/" + field.Name;

    internal static bool Serialized(FieldInfo field) => !field.IsStatic && !field.IsInitOnly
        && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), false)
            || field.IsDefined(typeof(SerializeReference), false));

    private sealed class Reader
    {
        private readonly HGDocumentSnapshot snapshot;
        private readonly int maxRecords;
        private readonly Dictionary<object, string> references = new(ReferenceComparer.Instance);
        private readonly Queue<(object value, HGSnapshotRecord record)> pending = new();
        private int fieldCount;

        public Reader(string source, int maxRecords)
        {
            snapshot = new HGDocumentSnapshot { Source = source };
            this.maxRecords = maxRecords;
        }

        public HGDocumentSnapshot Capture(object root)
        {
            snapshot.Root = Register(root);
            while (pending.Count > 0)
            {
                var (value, record) = pending.Dequeue();
                if (value is IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!Budget(record.Key)) break;
                        record.Fields.Add(Read(i.ToString(CultureInfo.InvariantCulture), list[i]));
                    }
                    continue;
                }
                foreach (var field in HGReflect.Fields(value.GetType()))
                {
                    if (!Serialized(field)) continue;
                    if (!Budget(record.Key)) break;
                    string key = FieldKey(field);
                    try { record.Fields.Add(Read(key, field.GetValue(value), field.FieldType)); }
                    catch (Exception exception)
                    {
                        Incomplete(record.Key + "/" + key + ": " + exception.Message);
                        record.Fields.Add(new HGSnapshotField { Name = key, Kind = "unavailable" });
                    }
                }
            }
            return snapshot;
        }

        private bool Budget(string path)
        {
            if (++fieldCount <= 200000) return true;
            Incomplete(path + ": field limit reached.");
            return false;
        }

        private string Register(object value)
        {
            if (references.TryGetValue(value, out string existing)) return existing;
            if (snapshot.Records.Count >= maxRecords)
            {
                Incomplete("Record limit reached; capture a smaller document.");
                return null;
            }
            var record = new HGSnapshotRecord
            {
                Key = "r" + snapshot.Records.Count.ToString(CultureInfo.InvariantCulture),
                Type = TypeName(value.GetType()), Kind = Kind(value),
                Label = HGReflect.TypeName(value.GetType()), Description = HGReflect.TypeDescription(value.GetType()),
            };
            // These concrete getters only read fields; avoid lazy collection or family getters.
            if (value is GraphNode node) { record.StableId = node.Id; record.Name = node.Kind.ToString(); }
            if (value is GraphToken token) { record.StableId = token.Id; record.Name = token.Name; }
            if (value is GraphProperty property) { record.StableId = property.Id; record.Name = property.Name; }
            references.Add(value, record.Key);
            snapshot.Records.Add(record);
            pending.Enqueue((value, record));
            return record.Key;
        }

        private HGSnapshotField Read(string name, object value, Type declared = null)
        {
            var field = new HGSnapshotField { Name = name, Type = TypeName(value?.GetType() ?? declared) };
            if (value == null) { field.Kind = "null"; return field; }
            Type type = value.GetType();
            if (value is Object unityObject)
            {
                field.Kind = "unity-object";
                if (unityObject == null)
                {
                    // Unity Instantiate can materialize an empty serialized reference as a managed shell with ID 0.
                    // Destroyed/missing references with a nonzero instance ID must remain distinguishable from empty.
                    if (unityObject.GetInstanceID() == 0) field.Kind = "null";
                    else field.Value = "missing";
                    return field;
                }
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unityObject, out string guid, out long localId))
                    field.Value = guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + ":" + AssetDatabase.GetAssetPath(unityObject);
                else field.Value = "instance:" + unityObject.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                return field;
            }
            if (type.IsEnum || type.IsPrimitive || value is string || value is decimal)
            {
                if (value is string text && text.Length > 16384)
                {
                    field.Kind = "unavailable";
                    Incomplete(name + ": string exceeds 16384 characters.");
                    return field;
                }
                field.Kind = type.IsEnum ? "enum" : "scalar";
                field.Value = type.IsEnum
                    ? Enum.Format(type, value, "D") + ":" + value
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                return field;
            }
            if (type.Namespace == "UnityEngine" || (type.Namespace?.StartsWith("UnityEngine.", StringComparison.Ordinal) ?? false))
            {
                field.Kind = "native-json";
                field.Value = value switch
                {
                    Rect rect => JsonUtility.ToJson(new RectValue(rect)),
                    Vector2Int vector => JsonUtility.ToJson(new Vector2IntValue(vector)),
                    Vector3Int vector => JsonUtility.ToJson(new Vector3IntValue(vector)),
                    RectInt rect => JsonUtility.ToJson(new RectIntValue(rect)),
                    _ => JsonUtility.ToJson(value),
                };
                if (field.Value == "{}") Incomplete(name + ": native value could not be represented: " + type.FullName);
                if (field.Value.Length > 16384)
                {
                    field.Kind = "unavailable";
                    field.Value = null;
                    Incomplete(name + ": native value exceeds 16384 characters.");
                }
                return field;
            }
            if (value is IDictionary || value is Delegate || value is Type || (!type.IsSerializable && value is not IList))
            {
                field.Kind = "unavailable";
                Incomplete(name + ": unsupported serialized value: " + type.FullName);
                return field;
            }
            field.Value = Register(value);
            field.Kind = field.Value == null ? "unavailable" : "reference";
            return field;
        }

        private void Incomplete(string message)
        {
            snapshot.Complete = false;
            if (snapshot.Diagnostics.Count < 100 && !snapshot.Diagnostics.Contains(message)) snapshot.Diagnostics.Add(message);
        }

        private static string Kind(object value)
        {
            if (value is IGraphDocument) return "document";
            if (value is IGraphAsset) return "asset";
            if (value is GraphNode) return "node";
            if (value is GraphToken) return "token";
            if (value is GraphProperty) return "property";
            if (value is GraphNodeGroup) return "group";
            if (value is PropertySlotBase) return "property-write-slot";
            if (value is ActionSlotBase) return "action-slot";
            if (value is FormulaSlotBase) return "formula-slot";
            if (value is GraphSlotBase) return "slot";
            if (value is IList) return "list";
            for (Type type = value.GetType(); type != null; type = type.BaseType)
            {
                if (!type.IsGenericType) continue;
                if (type.GetGenericTypeDefinition() == typeof(ActionNodeShape<>)) return "action";
                if (type.GetGenericTypeDefinition() == typeof(FormulaNodeShape<,>)) return "formula";
            }
            return "object";
        }

        private static string TypeName(Type type) => type?.AssemblyQualifiedName;
    }
}
}
