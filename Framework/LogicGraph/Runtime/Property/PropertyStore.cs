namespace HaruFamily.Framework.LogicGraph
{
using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>一個 LogicGraph runtime instance 的 Property 儲存；不寫回圖或共用 Asset 定義。</summary>
internal sealed class PropertyStore
{
    private PropertyScope root;

    internal PropertyScope Root(IEnumerable<GraphProperty> properties) => root ??= new PropertyScope(properties, null);

    /// <summary>把所有 scope 的目前值退回未寫入狀態。</summary>
    // 不丟掉 scope 物件：已經拿到 scope 的 TokenTable 會繼續用手上那一份，換新的只會讓明確初始化對它們無效。
    internal void Initialize() => root?.Initialize();
}

internal sealed class PropertyScope
{
    private sealed class Value
    {
        public object Current;
        public bool Written;
    }

    private sealed class PropertyReferenceComparer : IEqualityComparer<GraphProperty>
    {
        internal static readonly PropertyReferenceComparer Instance = new();
        public bool Equals(GraphProperty x, GraphProperty y) => ReferenceEquals(x, y);
        public int GetHashCode(GraphProperty value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    private readonly Dictionary<GraphProperty, Value> values = new(PropertyReferenceComparer.Instance);
    private readonly Dictionary<(ScriptableObject asset, GraphNode caller), PropertyScope> assets = new();
    private readonly PropertyScope parent;

    internal PropertyScope(IEnumerable<GraphProperty> properties, PropertyScope parent)
    {
        this.parent = parent;
        foreach (GraphProperty property in properties ?? Array.Empty<GraphProperty>())
            if (property != null) values[property] = new Value();
    }

    internal PropertyScope Asset(ScriptableObject asset, GraphNode caller, IEnumerable<GraphProperty> properties)
    {
        var key = (asset, caller);
        if (!assets.TryGetValue(key, out PropertyScope scope))
        {
            scope = new PropertyScope(properties, this);
            assets.Add(key, scope);
        }
        return scope;
    }

    internal T Read<T>(GraphProperty property)
    {
        if (!TryFind(property, out Value value)) return default;
        object current = value.Written ? value.Current : property.InitialValue;
        return current is T typed ? typed : default;
    }

    internal bool Write<T>(GraphProperty property, T value)
    {
        if (!TryFind(property, out Value entry)) return false;
        entry.Current = value;
        entry.Written = true;
        return true;
    }

    internal bool IsWritten(GraphProperty property)
        => TryFind(property, out Value value) && value.Written;

    internal void Initialize()
    {
        foreach (Value value in values.Values)
        {
            value.Current = null;
            value.Written = false;
        }
        foreach (PropertyScope scope in assets.Values) scope.Initialize();
    }

    private bool TryFind(GraphProperty property, out Value value)
    {
        value = null;
        if (property == null) return false;
        if (values.TryGetValue(property, out value)) return true;
        // LocalProperty 住在 GraphNode，不在文件層的 ProtoProperty 清單裡；第一次執行時登記進當前 scope，
        // 之後的讀寫便會共用同一份 runtime 值。
        if (!property.Proto) { value = Register(property); return true; }
        if (parent != null) return parent.TryFind(property, out value);
        // root 建立之後才加進圖層清單的 ProtoProperty：就地登記，否則寫入會無聲失敗而讀取永遠只拿得到初始內容。
        value = Register(property);
        return true;
    }

    private Value Register(GraphProperty property)
    {
        var value = new Value();
        values.Add(property, value);
        return value;
    }
}
}
