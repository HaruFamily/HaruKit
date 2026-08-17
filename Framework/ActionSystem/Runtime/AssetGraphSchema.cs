namespace PinPlugin.ActionSystem
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>公式與動作資產共同提供的內部圖。資產標註由這個邊界掃描，不會外洩到 Owner 圖。</summary>
public interface IActionSystemAssetGraph
{
    object ContentObject { get; }
    List<GraphNode> Orphans { get; }
}

public sealed class AssetParameterDefinition
{
    public string Name;
    public Type ResultType;
    public Type PackType;
    public GraphNode Node;
}

/// <summary>從資產內容與被標註候選建立穩定的參數 schema。</summary>
public static class AssetGraphSchema
{
    public static List<AssetParameterDefinition> Read(ScriptableObject asset, out List<string> duplicates)
    {
        duplicates = new List<string>();
        var result = new List<AssetParameterDefinition>();
        if (asset is not IActionSystemAssetGraph graph) return result;

        var nodes = new List<GraphNode>();
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        Collect(graph.ContentObject, visited, nodes);
        if (graph.Orphans != null)
        {
            foreach (var orphan in graph.Orphans)
                if (orphan?.IsToken == true) Collect(orphan, visited, nodes);
        }

        var names = new HashSet<string>();
        foreach (var node in nodes)
        {
            if (!node.IsToken) continue;
            if (!TryFormulaTypes(node, out var resultType, out var packType)) continue;
            if (!names.Add(node.TokenName)) duplicates.Add(node.TokenName);
            result.Add(new AssetParameterDefinition
            {
                Name = node.TokenName,
                ResultType = resultType,
                PackType = packType,
                Node = node,
            });
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    public static bool TryFormulaTypes(GraphNode node, out Type resultType, out Type packType)
    {
        resultType = null;
        packType = null;
        if (node == null) return false;
        Type type = node.Kind switch
        {
            NodeKind.Inline => node.BodyObject?.GetType(),
            NodeKind.Asset => node.AssetObject?.GetType(),
            _ => null,
        };
        if (type == null) return false;

        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            if (!current.IsGenericType) continue;
            Type definition = current.GetGenericTypeDefinition();
            if (definition != typeof(FormulaBase<,>) && definition != typeof(FormulaAsset<,>)) continue;
            var args = current.GetGenericArguments();
            resultType = args[0];
            packType = args[1];
            return true;
        }
        return false;
    }

    private static void Collect(object value, HashSet<object> visited, List<GraphNode> nodes)
    {
        if (value == null || !visited.Add(value)) return;
        if (value is FormulaSlotBase formulaSlot)
        {
            Collect(formulaSlot.Node, visited, nodes);
            return;
        }
        if (IsActionSlot(value.GetType()))
        {
            Collect(value.GetType().GetProperty("Node")?.GetValue(value), visited, nodes);
            return;
        }
        if (value is GraphNode node)
        {
            nodes.Add(node);
            if (node.Kind == NodeKind.Inline) Collect(node.BodyObject, visited, nodes);
            foreach (var binding in node.Bindings)
                if (binding?.Slot != null) Collect(binding.Slot, visited, nodes);
            return;
        }
        if (value is UnityEngine.Object) return;

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string) return;
        if (value is IList list)
        {
            foreach (var item in list) Collect(item, visited, nodes);
            return;
        }
        foreach (var field in Fields(type)) Collect(field.GetValue(value), visited, nodes);
    }

    private static bool IsActionSlot(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ActionSlot<>)) return true;
        return false;
    }

    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized) yield return field;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object left, object right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
}
