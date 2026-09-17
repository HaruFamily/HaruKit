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

/// <summary>Optional built-in anchor for ports that participate in node-local snapping and row interaction.</summary>
public interface IHGPortPresentationAnchor
{
    HGNodeView Node { get; }
    HGRow Row { get; }
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

/// <summary>Outcome shared by link highlighting, snapping and the final connection attempt.</summary>
public enum HGPortConnectionResult
{
    Allowed,
    InvalidEndpoint,
    StaleGeneration,
    SameRole,
    AggregateOnly,
    MissingBinding,
    Hidden,
    Locked,
    Rejected,
}

/// <summary>Non-UI connection coordinator shared by dragging, snapping and direct tests.</summary>
public static class HGPortConnection
{
    public static HGPortConnectionResult Check(HGPort first, HGPort second, int generation)
    {
        if (first == null || second == null) return HGPortConnectionResult.InvalidEndpoint;
        if (first.Generation != generation || second.Generation != generation)
            return HGPortConnectionResult.StaleGeneration;

        HGPort input = first.IsInput ? first : second.IsInput ? second : null;
        HGPort output = first.IsOutput ? first : second.IsOutput ? second : null;
        if (input == null || output == null)
        {
            if (first.Binding is IHGAggregatePortBinding || second.Binding is IHGAggregatePortBinding)
                return HGPortConnectionResult.AggregateOnly;
            return HGPortConnectionResult.SameRole;
        }

        if (ReferenceEquals(input, output)) return HGPortConnectionResult.InvalidEndpoint;
        if (output.Source?.OutputNode == null) return HGPortConnectionResult.MissingBinding;
        if (!output.Presentation.Visible || !input.Presentation.Visible) return HGPortConnectionResult.Hidden;
        if (output.Presentation.Locked || input.Presentation.Locked) return HGPortConnectionResult.Locked;
        return CheckInputSource(input, output.Source, generation);
    }

    /// <summary>Checks an external source with the same acceptance path used by a rendered Output Port.</summary>
    public static HGPortConnectionResult CheckInputSource(HGPort input, IHGPortSource source, int generation)
    {
        if (input == null || !input.IsInput) return HGPortConnectionResult.InvalidEndpoint;
        if (input.Generation != generation) return HGPortConnectionResult.StaleGeneration;
        if (input.InputSlot == null || source == null) return HGPortConnectionResult.MissingBinding;
        if (!input.Presentation.Visible) return HGPortConnectionResult.Hidden;
        if (input.Presentation.Locked) return HGPortConnectionResult.Locked;
        return input.Policy.CanAccept(source) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.Rejected;
    }

    public static bool CanConnect(HGPort first, HGPort second, int generation)
        => Check(first, second, generation) == HGPortConnectionResult.Allowed;
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

public sealed class HGDelegatePortPresentation : IHGPortPresentation, IHGPortPresentationAnchor
{
    private readonly Func<Vector2> position;
    private readonly Func<Rect> hitRect;
    private readonly Func<bool> visible;
    private readonly Func<bool> locked;

    public object Owner { get; }
    public HGNodeView Node { get; }
    public HGRow Row { get; }
    public Vector2 Position => position();
    public Rect HitRect => hitRect();
    public bool Visible => visible();
    public bool Locked => locked();

    public HGDelegatePortPresentation(object owner, Func<Vector2> position, Func<Rect> hitRect,
        Func<bool> visible, Func<bool> locked)
        : this(owner, owner as HGNodeView, owner as HGRow, position, hitRect, visible, locked)
    {
    }

    public HGDelegatePortPresentation(object owner, HGNodeView node, HGRow row, Func<Vector2> position,
        Func<Rect> hitRect, Func<bool> visible, Func<bool> locked)
    {
        Owner = owner;
        Node = node;
        Row = row;
        this.position = position ?? throw new ArgumentNullException(nameof(position));
        this.hitRect = hitRect ?? throw new ArgumentNullException(nameof(hitRect));
        this.visible = visible ?? throw new ArgumentNullException(nameof(visible));
        this.locked = locked ?? throw new ArgumentNullException(nameof(locked));
    }
}

/// <summary>Public, bounded registry for a Tool-owned set of generation-local Ports.</summary>
public sealed class HGPortRegistry
{
    private readonly List<HGPort> ports;
    private readonly Dictionary<HGPortKey, HGPort> byKey;
    public int Generation { get; }
    public HGPortBuildResult LastResult { get; private set; } = HGPortBuildResult.None;
    public IReadOnlyList<HGPort> Ports => ports;

    public HGPortRegistry(int generation)
        : this(generation, new List<HGPort>(), new Dictionary<HGPortKey, HGPort>())
    {
    }

    internal HGPortRegistry(int generation, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey)
    {
        Generation = generation;
        this.ports = ports ?? throw new ArgumentNullException(nameof(ports));
        this.byKey = byKey ?? throw new ArgumentNullException(nameof(byKey));
    }

    public bool AddInput(HGPortKey key, GraphSlotBase inputSlot, IHGPortPolicy policy,
        IHGPortPresentation presentation)
    {
        if (inputSlot == null || policy == null || presentation == null)
        {
            LastResult = HGPortBuildResult.InvalidBinding;
            return false;
        }
        return Add(new HGPort(WithRole(key, HGPortRole.Input), new HGInputPortBinding(inputSlot), policy,
            presentation, Generation));
    }

    public bool AddOutput(HGPortKey key, IHGPortSource source, IHGPortPolicy policy,
        IHGPortPresentation presentation)
    {
        if (source == null || policy == null || presentation == null)
        {
            LastResult = HGPortBuildResult.InvalidBinding;
            return false;
        }
        return Add(new HGPort(WithRole(key, HGPortRole.Output), new HGOutputPortBinding(source), policy,
            presentation, Generation));
    }

    public bool AddAggregate(HGPortKey key, IHGPortPresentation presentation, IHGPortPolicy policy = null)
    {
        if (presentation == null)
        {
            LastResult = HGPortBuildResult.InvalidBinding;
            return false;
        }
        return Add(new HGPort(WithRole(key, HGPortRole.Aggregate), HGAggregatePortBinding.Instance,
            policy ?? new HGDelegatePortPolicy(() => false), presentation, Generation));
    }

    public bool TryGet(HGPortKey key, out HGPort port) => byKey.TryGetValue(key, out port);

    /// <summary>Add one legacy adapter. Duplicate and incomplete ports are rejected without mutating the view.</summary>
    public bool Add(HGPort port)
    {
        LastResult = Validate(port);
        if (LastResult != HGPortBuildResult.Added) return false;
        ports.Add(port);
        byKey.Add(port.Key, port);
        return true;
    }

    private static HGPortKey WithRole(HGPortKey key, HGPortRole role)
        => new HGPortKey(key.OwnerId, key.Path, role);

    private HGPortBuildResult Validate(HGPort port)
    {
        if (port == null) return HGPortBuildResult.NullPort;
        if (port.Generation != Generation) return HGPortBuildResult.StaleGeneration;
        if (string.IsNullOrEmpty(port.Key.OwnerId) || port.Presentation.Owner == null)
            return HGPortBuildResult.MissingOwner;
        if (byKey.ContainsKey(port.Key)) return HGPortBuildResult.DuplicateKey;

        return port.Key.Role switch
        {
            HGPortRole.Input when port.Binding is IHGInputPortBinding && port.InputSlot != null
                => HGPortBuildResult.Added,
            HGPortRole.Output when port.Binding is IHGOutputPortBinding && port.Source?.OutputNode != null
                => HGPortBuildResult.Added,
            HGPortRole.Aggregate when port.Binding is IHGAggregatePortBinding
                => HGPortBuildResult.Added,
            _ => HGPortBuildResult.InvalidBinding,
        };
    }
}

/// <summary>Bounded Port adapter seam exposed to an explicitly supplied extension provider.</summary>
public sealed class HGPortBuildContext
{
    private readonly HGGraphView graph;
    private readonly HGPortRegistry registry;

    /// <summary>Legacy mutable view access. New providers must use role-specific registration and query methods.</summary>
    [Obsolete("Use the role-specific Add methods and query methods instead of mutating the graph view.")]
    public HGGraphView Graph => graph;
    public int Generation => registry.Generation;
    public HGPortBuildResult LastResult => registry.LastResult;

    public HGPortBuildContext(HGGraphView graph, int generation)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        registry = new HGPortRegistry(generation, graph.Ports, graph.PortsByKey);
    }

    internal HGPortBuildContext(HGGraphView graph, int generation, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey)
    {
        this.graph = graph;
        registry = new HGPortRegistry(generation, ports, byKey);
    }

    public bool AddInput(HGPortKey key, GraphSlotBase inputSlot, IHGPortPolicy policy,
        IHGPortPresentation presentation)
        => registry.AddInput(key, inputSlot, policy, presentation);

    public bool AddOutput(HGPortKey key, IHGPortSource source, IHGPortPolicy policy,
        IHGPortPresentation presentation)
        => registry.AddOutput(key, source, policy, presentation);

    public bool AddAggregate(HGPortKey key, IHGPortPresentation presentation, IHGPortPolicy policy = null)
        => registry.AddAggregate(key, presentation, policy);

    public bool TryGet(HGPortKey key, out HGPort port) => registry.TryGet(key, out port);

    /// <summary>Add one legacy adapter. Duplicate and incomplete ports are rejected without mutating the view.</summary>
    public bool Add(HGPort port) => registry.Add(port);
}

/// <summary>Reason a Port registration was not accepted by the current build scope.</summary>
public enum HGPortBuildResult
{
    None,
    Added,
    NullPort,
    StaleGeneration,
    DuplicateKey,
    MissingOwner,
    InvalidBinding,
}
}
