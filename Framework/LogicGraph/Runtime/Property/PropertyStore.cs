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

    internal PropertyScope Root(IEnumerable<GraphProperty> properties)
    {
        root ??= new PropertyScope(properties, null);
        root.RegisterDefinitions(properties);
        return root;
    }

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
        public object Initial;
        public bool HasInitial;
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
    private readonly PropertyScope persistent;
    private bool ended;
    private int generation;

    internal object Identity => persistent ?? this;

    private PropertyScope(PropertyScope persistent, PropertyScope parent)
    {
        this.persistent = persistent;
        this.parent = parent;
        generation = persistent.generation;
    }

    // 圖外寫入的暫存值交給下一次執行；獨立求值只看快照，不消耗待執行的輸入。
    internal PropertyScope BeginExecution(bool consumeExternalWrites = false)
    {
        var scope = new PropertyScope(this, null);
        foreach (var pair in values)
        {
            if (pair.Key.RetainValue || !pair.Value.Written) continue;
            scope.values.Add(pair.Key, new Value { Current = pair.Value.Current, Written = true });
            if (!consumeExternalWrites) continue;
            pair.Value.Current = null;
            pair.Value.Written = false;
            pair.Value.Initial = null;
            pair.Value.HasInitial = false;
        }
        return scope;
    }

    internal void EndExecution()
    {
        ended = true;
        values.Clear();
        foreach (PropertyScope scope in assets.Values) scope.EndExecution();
    }

    internal PropertyScope(IEnumerable<GraphProperty> properties, PropertyScope parent)
    {
        this.parent = parent;
        RegisterDefinitions(properties);
    }

    internal void RegisterDefinitions(IEnumerable<GraphProperty> properties)
    {
        foreach (GraphProperty property in properties ?? Array.Empty<GraphProperty>())
            if (property != null && !values.ContainsKey(property)) Register(property);
    }

    internal PropertyScope Asset(ScriptableObject asset, GraphNode caller, IEnumerable<GraphProperty> properties)
    {
        var key = (asset, caller);
        if (!assets.TryGetValue(key, out PropertyScope scope))
        {
            scope = persistent == null
                ? new PropertyScope(properties, this)
                : new PropertyScope(persistent.Asset(asset, caller, properties), this);
            assets.Add(key, scope);
        }
        // 共用資產可在 scope 建立後新增定義；必須先在該呼叫位置登記，不能落到父層而合併不同呼叫者。
        (scope.persistent ?? scope).RegisterDefinitions(properties);
        return scope;
    }

    internal T Read<T>(GraphProperty property)
    {
        if (!TryFind(property, out Value value)) return default;
        if (!property.RetainValue && !value.Written && !value.HasInitial)
        {
            value.Initial = GraphDeepCopy.Copy(property.InitialValue);
            value.HasInitial = true;
        }
        object current = value.Written ? value.Current : property.RetainValue ? property.InitialValue : value.Initial;
        return current is T typed ? typed : default;
    }

    internal bool Write<T>(GraphProperty property, T value)
    {
        if (property != null && !property.RetainValue && IsEnded) return false;
        if (!TryFind(property, out Value entry)) return false;
        entry.Current = value;
        entry.Written = true;
        return true;
    }

    internal bool IsWritten(GraphProperty property)
        => TryFind(property, out Value value) && value.Written;

    internal void Initialize()
    {
        generation++;
        foreach (Value value in values.Values)
        {
            value.Current = null;
            value.Written = false;
            value.Initial = null;
            value.HasInitial = false;
        }
        foreach (PropertyScope scope in assets.Values) scope.Initialize();
    }

    private bool TryFind(GraphProperty property, out Value value)
    {
        value = null;
        if (property == null) return false;
        if (persistent != null)
        {
            if (property.RetainValue) return persistent.TryFind(property, out value);
            // 已結束的執行表可能仍被延後求值持有；不讓暫存值延長生命週期。
            if (IsEnded)
            {
                if (!persistent.TryFind(property, out _)) return false;
                value = new Value();
                return true;
            }
            if (generation != persistent.generation)
            {
                values.Clear();
                generation = persistent.generation;
            }
            if (!persistent.values.ContainsKey(property))
                return parent != null && parent.TryFind(property, out value);
            if (!values.TryGetValue(property, out value)) value = Register(property);
            return true;
        }
        if (values.TryGetValue(property, out value)) return true;
        return parent != null && parent.TryFind(property, out value);
    }

    private Value Register(GraphProperty property)
    {
        var value = new Value();
        values.Add(property, value);
        return value;
    }

    private bool IsEnded => ended || (parent?.IsEnded ?? false);
}
}
