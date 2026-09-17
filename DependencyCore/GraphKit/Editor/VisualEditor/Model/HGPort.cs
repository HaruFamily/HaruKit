namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEngine;

public enum HGPortRole
{
    Input,
    Output,
    Aggregate,
}

/// <summary>Stable endpoint identity within one focus. HGPort instances themselves are generation-local.</summary>
public readonly struct HGPortKey : IEquatable<HGPortKey>
{
    public string OwnerId { get; }
    public string Path { get; }
    public HGPortRole Role { get; }

    public HGPortKey(string ownerId, string path, HGPortRole role)
    {
        OwnerId = ownerId ?? "";
        Path = path ?? "";
        Role = role;
    }

    public bool Equals(HGPortKey other)
        => OwnerId == other.OwnerId && Path == other.Path && Role == other.Role;

    public override bool Equals(object obj) => obj is HGPortKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + OwnerId.GetHashCode();
            hash = hash * 31 + Path.GetHashCode();
            return hash * 31 + (int)Role;
        }
    }
    public override string ToString() => $"{OwnerId}#{Path}:{Role}";
}

public interface IHGPortBinding { }

public interface IHGInputPortBinding : IHGPortBinding
{
    GraphSlotBase InputSlot { get; }
}

public interface IHGOutputPortBinding : IHGPortBinding
{
    IHGPortSource Source { get; }
}

public interface IHGAggregatePortBinding : IHGPortBinding { }

/// <summary>A source that can be tested by an Input Port without knowing its concrete adapter type.</summary>
public interface IHGPortSource
{
    GraphNode OutputNode { get; }
    object CycleRoot { get; }
    bool Accepts(GraphSlotBase input);
}

public interface IHGPortPolicy
{
    bool CanStart { get; }
    bool CanAccept(IHGPortSource source);
}

public interface IHGPortPresentation
{
    object Owner { get; }
    Vector2 Position { get; }
    Rect HitRect { get; }
    bool Visible { get; }
    bool Locked { get; }
}

/// <summary>Editor-only endpoint assembled from independent binding, policy and presentation adapters.</summary>
public sealed class HGPort
{
    public HGPortKey Key { get; }
    public IHGPortBinding Binding { get; }
    public IHGPortPolicy Policy { get; }
    public IHGPortPresentation Presentation { get; }
    public int Generation { get; }

    public HGPort(HGPortKey key, IHGPortBinding binding, IHGPortPolicy policy,
        IHGPortPresentation presentation, int generation)
    {
        Key = key;
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Generation = generation;
    }

    public GraphSlotBase InputSlot => (Binding as IHGInputPortBinding)?.InputSlot;
    public IHGPortSource Source => (Binding as IHGOutputPortBinding)?.Source;
    public bool IsInput => Binding is IHGInputPortBinding;
    public bool IsOutput => Binding is IHGOutputPortBinding;
    public bool CanStart => Presentation.Visible && !Presentation.Locked && Policy.CanStart;
}

/// <summary>Non-UI connection coordinator shared by dragging, snapping and direct tests.</summary>
public static class HGPortConnection
{
    public static bool CanConnect(HGPort first, HGPort second, int generation)
    {
        if (first == null || second == null || first.Generation != generation || second.Generation != generation)
            return false;

        HGPort input = first.IsInput ? first : second.IsInput ? second : null;
        HGPort output = first.IsOutput ? first : second.IsOutput ? second : null;
        if (input == null || output == null || ReferenceEquals(input, output)) return false;
        if (!input.Presentation.Visible || input.Presentation.Locked
            || !output.Presentation.Visible || output.Presentation.Locked) return false;
        return input.Policy.CanAccept(output.Source);
    }
}

public sealed class HGInputPortBinding : IHGInputPortBinding
{
    public GraphSlotBase InputSlot { get; }
    public HGInputPortBinding(GraphSlotBase inputSlot)
        => InputSlot = inputSlot ?? throw new ArgumentNullException(nameof(inputSlot));
}

public sealed class HGOutputPortBinding : IHGOutputPortBinding
{
    public IHGPortSource Source { get; }
    public HGOutputPortBinding(IHGPortSource source)
        => Source = source ?? throw new ArgumentNullException(nameof(source));
}

public sealed class HGAggregatePortBinding : IHGAggregatePortBinding
{
    public static HGAggregatePortBinding Instance { get; } = new HGAggregatePortBinding();
    private HGAggregatePortBinding() { }
}

/// <summary>Composable source adapter used by built-in and Tool-specific Output Ports.</summary>
public sealed class HGDelegatePortSource : IHGPortSource
{
    private readonly Func<GraphSlotBase, bool> accepts;

    public GraphNode OutputNode { get; }
    public object CycleRoot { get; }

    public HGDelegatePortSource(GraphNode outputNode, object cycleRoot, Func<GraphSlotBase, bool> accepts)
    {
        OutputNode = outputNode;
        CycleRoot = cycleRoot;
        this.accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
    }

    public bool Accepts(GraphSlotBase input) => input != null && accepts(input);
}

public sealed class HGDelegatePortPolicy : IHGPortPolicy
{
    private readonly Func<bool> canStart;
    private readonly Func<IHGPortSource, bool> canAccept;

    public bool CanStart => canStart?.Invoke() == true;

    public HGDelegatePortPolicy(Func<bool> canStart, Func<IHGPortSource, bool> canAccept = null)
    {
        this.canStart = canStart;
        this.canAccept = canAccept;
    }

    public bool CanAccept(IHGPortSource source) => canAccept?.Invoke(source) == true;
}

public sealed class HGDelegatePortPresentation : IHGPortPresentation
{
    private readonly Func<Vector2> position;
    private readonly Func<Rect> hitRect;
    private readonly Func<bool> visible;
    private readonly Func<bool> locked;

    public object Owner { get; }
    public Vector2 Position => position();
    public Rect HitRect => hitRect();
    public bool Visible => visible();
    public bool Locked => locked();

    public HGDelegatePortPresentation(object owner, Func<Vector2> position, Func<Rect> hitRect,
        Func<bool> visible, Func<bool> locked)
    {
        Owner = owner;
        this.position = position ?? throw new ArgumentNullException(nameof(position));
        this.hitRect = hitRect ?? throw new ArgumentNullException(nameof(hitRect));
        this.visible = visible ?? throw new ArgumentNullException(nameof(visible));
        this.locked = locked ?? throw new ArgumentNullException(nameof(locked));
    }
}

/// <summary>Bounded Port adapter seam exposed to an explicitly supplied extension provider.</summary>
public sealed class HGPortBuildContext
{
    private readonly List<HGPort> ports;
    private readonly Dictionary<HGPortKey, HGPort> byKey;

    public HGGraphView Graph { get; }
    public int Generation { get; }

    public HGPortBuildContext(HGGraphView graph, int generation)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Generation = generation;
        ports = graph.Ports;
        byKey = graph.PortsByKey;
    }

    internal HGPortBuildContext(HGGraphView graph, int generation, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey)
    {
        Graph = graph;
        Generation = generation;
        this.ports = ports;
        this.byKey = byKey;
    }

    /// <summary>Add one adapter. Duplicate stable keys are rejected so hit testing stays deterministic.</summary>
    public bool Add(HGPort port)
    {
        if (port == null || port.Generation != Generation || byKey.ContainsKey(port.Key)) return false;
        ports.Add(port);
        byKey.Add(port.Key, port);
        return true;
    }
}
}
