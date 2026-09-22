using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("HaruFamily.DependencyCore.GraphKit.Editor.Tests")]

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
            hash = hash * 31 + (OwnerId?.GetHashCode() ?? 0);
            hash = hash * 31 + (Path?.GetHashCode() ?? 0);
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

/// <summary>Action 的寫入輸出；持久化關係由目標 Slot 保存，不是 Action Header 的讀取來源。</summary>
internal sealed class HGPropertyWriteSource : IHGPortSource
{
    public PropertySlotBase Slot { get; }
    public GraphNode OutputNode { get; }
    public object CycleRoot => null;
    public HGPropertyWriteSource(PropertySlotBase slot, GraphNode owner)
    { Slot = slot; OutputNode = owner; }
    public bool Accepts(GraphSlotBase input) => false;
    internal HGPortConnectionResult CheckTarget(GraphNode target)
    {
        if (target?.Kind != NodeKind.Property) return HGPortConnectionResult.IncompatibleType;
        if (!target.IsProtoProperty) return HGPortConnectionResult.Allowed;
        if (target.Property == null) return HGPortConnectionResult.MissingBinding;
        return Slot.AcceptsProperty(target.Property)
            ? HGPortConnectionResult.Allowed : HGPortConnectionResult.IncompatibleFamily;
    }
}

/// <summary>Optional detailed source compatibility contract shared by Port linking and direct source drops.</summary>
public interface IHGPortSourceAcceptance : IHGPortSource
{
    HGPortConnectionResult CheckAcceptance(GraphSlotBase input);
}

public interface IHGPortPolicy
{
    bool CanStart { get; }
    bool CanAccept(IHGPortSource source);
}

/// <summary>Optional detailed acceptance contract for Tools that need to explain a rejected connection.</summary>
public interface IHGPortConnectionPolicy : IHGPortPolicy
{
    HGPortConnectionResult Check(IHGPortSource source);
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

/// <summary>Optional stable location for a Port presentation that does not retain Editor view objects.</summary>
public interface IHGPortPresentationLocator
{
    string NodeId { get; }
    string FieldPath { get; }
}

/// <summary>Generation-local geometry and identity that a Tool may use to anchor an extension Port.</summary>
public readonly struct HGPortAnchor
{
    public string NodeId { get; }
    public string FieldPath { get; }
    public Vector2 Position { get; }
    public Rect HitRect { get; }

    public HGPortAnchor(string nodeId, string fieldPath, Vector2 position, Rect hitRect)
    {
        NodeId = nodeId ?? "";
        FieldPath = fieldPath ?? "";
        Position = position;
        HitRect = hitRect;
    }
}

/// <summary>Read-only Port snapshot exposed to extension providers during the current build generation.</summary>
public readonly struct HGPortDescriptor
{
    public HGPortKey Key { get; }
    public HGPortAnchor Anchor { get; }
    public bool IsVisible { get; }
    public bool IsLocked { get; }
    public bool CanStart { get; }
    public bool IsPrimaryOutput { get; }

    internal HGPortDescriptor(HGPort port, bool? primaryOutput = null)
    {
        Key = port.Key;
        string nodeId = port.Presentation is IHGPortPresentationLocator locator
            ? locator.NodeId
            : port.Key.OwnerId;
        string fieldPath = port.Presentation is IHGPortPresentationLocator located
            ? located.FieldPath
            : port.Key.Path;
        Anchor = new HGPortAnchor(nodeId, fieldPath, port.Presentation.Position, port.Presentation.HitRect);
        IsVisible = port.Presentation.Visible;
        IsLocked = port.Presentation.Locked;
        CanStart = port.CanStart;
        IsPrimaryOutput = primaryOutput ?? port.IsPrimaryOutput;
    }
}

/// <summary>Read-only node snapshot available to an extension provider without exposing HGNodeView.</summary>
public readonly struct HGNodeViewInfo
{
    public string Id { get; }
    public Rect Rect { get; }
    public bool IsRoot { get; }
    public bool IsHidden { get; }

    internal HGNodeViewInfo(HGNodeView node)
    {
        Id = node.Id ?? "";
        Rect = node.Rect;
        IsRoot = node.IsRoot;
        IsHidden = node.Hidden;
    }
}

/// <summary>Read-only field snapshot available to an extension provider without exposing HGRow.</summary>
public readonly struct HGFieldViewInfo
{
    public string NodeId { get; }
    public string Path { get; }
    public HGRowKind Kind { get; }
    public Type ResultType { get; }
    public bool HasInputPort { get; }
    public bool IsVisible { get; }
    public bool IsLocked { get; }
    public HGPortAnchor InputAnchor { get; }

    internal HGFieldViewInfo(HGRow row, HGNodeView node)
    {
        NodeId = node.Id ?? "";
        Path = row.Path ?? "";
        Kind = row.Kind;
        ResultType = row.ResultType;
        HasInputPort = row.HasInputPort;
        IsVisible = !node.Hidden && row.IsInputPortVisible;
        IsLocked = row.Locked || node.InLockedSubtree;
        InputAnchor = new HGPortAnchor(NodeId, Path, row.InputPortPosition,
            new Rect(row.InputPortPosition - Vector2.one * HGGraph.PortRadius,
                Vector2.one * HGGraph.PortDiameter));
    }
}

/// <summary>Editor-only endpoint assembled from independent binding, policy and presentation adapters.</summary>
public sealed class HGPort
{
    public HGPortKey Key { get; }
    public IHGPortBinding Binding { get; }
    public IHGPortPolicy Policy { get; }
    public IHGPortPresentation Presentation { get; }
    public int Generation { get; }
    public bool IsPrimaryOutput { get; }

    public HGPort(HGPortKey key, IHGPortBinding binding, IHGPortPolicy policy,
        IHGPortPresentation presentation, int generation, bool isPrimaryOutput = false)
    {
        Key = key;
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Generation = generation;
        IsPrimaryOutput = isPrimaryOutput;
    }

    public GraphSlotBase InputSlot => (Binding as IHGInputPortBinding)?.InputSlot;
    public IHGPortSource Source => (Binding as IHGOutputPortBinding)?.Source;
    public bool IsInput => Binding is IHGInputPortBinding;
    public bool IsOutput => Binding is IHGOutputPortBinding;
    public bool CanStart => Binding is not IHGAggregatePortBinding && Presentation.Visible && !Presentation.Locked && Policy.CanStart;
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
    IncompatibleFamily,
    IncompatibleType,
    DomainRejected,
    WouldCreateCycle,
    UnsupportedOperation,
    Rejected,
    ProviderFailed,
}

/// <summary>Non-UI connection coordinator shared by dragging, snapping and direct tests.</summary>
public static class HGPortConnection
{
    public static HGPortConnectionResult Check(HGPort first, HGPort second, int generation)
    {
        try { return CheckPair(first, second, generation); }
        catch (Exception) { return HGPortConnectionResult.ProviderFailed; }
    }

    private static HGPortConnectionResult CheckPair(HGPort first, HGPort second, int generation)
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
        try { return CheckInput(input, source, generation); }
        catch (Exception) { return HGPortConnectionResult.ProviderFailed; }
    }

    private static HGPortConnectionResult CheckInput(HGPort input, IHGPortSource source, int generation)
    {
        if (input == null || !input.IsInput) return HGPortConnectionResult.InvalidEndpoint;
        if (input.Generation != generation) return HGPortConnectionResult.StaleGeneration;
        if (input.InputSlot == null || source == null) return HGPortConnectionResult.MissingBinding;
        if (!input.Presentation.Visible) return HGPortConnectionResult.Hidden;
        if (input.Presentation.Locked) return HGPortConnectionResult.Locked;
        return input.Policy is IHGPortConnectionPolicy detailed
            ? detailed.Check(source)
            : input.Policy.CanAccept(source) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.Rejected;
    }

    /// <summary>Checks source compatibility without erasing a Tool-provided rejection reason.</summary>
    public static HGPortConnectionResult CheckSourceAcceptance(IHGPortSource source, GraphSlotBase input)
    {
        if (source == null || input == null) return HGPortConnectionResult.MissingBinding;
        return source is IHGPortSourceAcceptance detailed
            ? detailed.CheckAcceptance(input)
            : source.Accepts(input) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.Rejected;
    }

    public static bool CanConnect(HGPort first, HGPort second, int generation)
        => Check(first, second, generation) == HGPortConnectionResult.Allowed;
}

internal enum HGLinkPortResolution
{
    Resolved,
    InputUnresolved,
    OutputUnresolved,
    PrimaryOutputMissing,
}

/// <summary>Resolves generation-local link endpoints after built-in and Tool-owned Ports have registered.</summary>
internal static class HGLinkPortResolver
{
    public static HGLinkPortResolution Resolve(HGLink link, IReadOnlyDictionary<HGPortKey, HGPort> portsByKey,
        IReadOnlyDictionary<GraphNode, HGPort> primaryOutputs, int generation,
        IReadOnlyDictionary<GraphSlotBase, HGPort> primaryInputs = null)
    {
        if (link == null) return HGLinkPortResolution.InputUnresolved;
        link.InputPort = null;
        link.OutputPort = null;
        if (link.ParentRow?.InputSlot is PropertySlotBase)
        {
            var writerKey = new HGPortKey(link.ParentRow.OwnerNodeId, link.ParentRow.Path, HGPortRole.Output);
            var targetKey = new HGPortKey(link.OutputOwner?.Id, "/property/input", HGPortRole.Input);
            if (portsByKey == null || !portsByKey.TryGetValue(writerKey, out var writer)
                || writer.Generation != generation) return HGLinkPortResolution.OutputUnresolved;
            if (!portsByKey.TryGetValue(targetKey, out var target)
                || target.Generation != generation) return HGLinkPortResolution.InputUnresolved;
            link.OutputPort = writer;
            link.InputPort = target;
            return HGLinkPortResolution.Resolved;
        }
        HGPort input = null;
        if (link.ParentRow?.InputSlot != null && primaryInputs != null)
            primaryInputs.TryGetValue(link.ParentRow.InputSlot, out input);
        if (input == null && link.ParentRow != null && portsByKey != null)
            portsByKey.TryGetValue(InputKey(link.ParentRow), out input);
        if (input == null || input.Generation != generation)
            return HGLinkPortResolution.InputUnresolved;

        link.InputPort = input;
        GraphNode source = link.OutputOwner?.Carrier;
        if (source == null) return HGLinkPortResolution.OutputUnresolved;
        if (primaryOutputs == null || !primaryOutputs.TryGetValue(source, out HGPort output) || output.Generation != generation)
            return HGLinkPortResolution.PrimaryOutputMissing;

        link.OutputPort = output;
        return HGLinkPortResolution.Resolved;
    }

    private static HGPortKey InputKey(HGRow row)
        => new HGPortKey(row?.OwnerNodeId, row?.Path, HGPortRole.Input);
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
public sealed class HGDelegatePortSource : IHGPortSourceAcceptance
{
    private readonly Func<GraphSlotBase, bool> accepts;
    private readonly Func<GraphSlotBase, HGPortConnectionResult> checkAcceptance;

    public GraphNode OutputNode { get; }
    public object CycleRoot { get; }

    public HGDelegatePortSource(GraphNode outputNode, object cycleRoot, Func<GraphSlotBase, bool> accepts,
        Func<GraphSlotBase, HGPortConnectionResult> checkAcceptance = null)
    {
        OutputNode = outputNode;
        CycleRoot = cycleRoot;
        this.accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
        this.checkAcceptance = checkAcceptance;
    }

    public bool Accepts(GraphSlotBase input) => CheckAcceptance(input) == HGPortConnectionResult.Allowed;

    public HGPortConnectionResult CheckAcceptance(GraphSlotBase input)
    {
        if (input == null) return HGPortConnectionResult.MissingBinding;
        return checkAcceptance?.Invoke(input) ?? (accepts(input)
            ? HGPortConnectionResult.Allowed
            : HGPortConnectionResult.Rejected);
    }
}

public sealed class HGDelegatePortPolicy : IHGPortConnectionPolicy
{
    private readonly Func<bool> canStart;
    private readonly Func<IHGPortSource, bool> canAccept;
    private readonly Func<IHGPortSource, HGPortConnectionResult> checkAcceptance;

    public bool CanStart => canStart?.Invoke() == true;

    public HGDelegatePortPolicy(Func<bool> canStart, Func<IHGPortSource, bool> canAccept = null,
        Func<IHGPortSource, HGPortConnectionResult> checkAcceptance = null)
    {
        this.canStart = canStart;
        this.canAccept = canAccept;
        this.checkAcceptance = checkAcceptance;
    }

    public bool CanAccept(IHGPortSource source) => Check(source) == HGPortConnectionResult.Allowed;

    public HGPortConnectionResult Check(IHGPortSource source)
        => checkAcceptance?.Invoke(source) ?? (canAccept?.Invoke(source) == true
            ? HGPortConnectionResult.Allowed
            : HGPortConnectionResult.Rejected);
}

public sealed class HGDelegatePortPresentation : IHGPortPresentation, IHGPortPresentationAnchor, IHGPortPresentationLocator
{
    private readonly Func<Vector2> position;
    private readonly Func<Rect> hitRect;
    private readonly Func<bool> visible;
    private readonly Func<bool> locked;

    public object Owner { get; }
    public HGNodeView Node { get; }
    public HGRow Row { get; }
    public string NodeId { get; }
    public string FieldPath { get; }
    public Vector2 Position => position();
    public Rect HitRect => hitRect();
    public bool Visible => visible();
    public bool Locked => locked();

    public HGDelegatePortPresentation(object owner, Func<Vector2> position, Func<Rect> hitRect,
        Func<bool> visible, Func<bool> locked)
        : this(owner, owner as HGNodeView, owner as HGRow,
            (owner as HGNodeView)?.Id ?? (owner as HGRow)?.OwnerNodeId,
            (owner as HGRow)?.Path, position, hitRect, visible, locked)
    {
    }

    public HGDelegatePortPresentation(object owner, HGNodeView node, HGRow row, Func<Vector2> position,
        Func<Rect> hitRect, Func<bool> visible, Func<bool> locked)
        : this(owner, node, row, node?.Id ?? row?.OwnerNodeId, row?.Path, position, hitRect, visible, locked)
    {
    }

    /// <summary>Creates a Tool-facing presentation anchored by a build-context snapshot rather than a View reference.</summary>
    public HGDelegatePortPresentation(object owner, HGPortAnchor anchor, Func<Vector2> position, Func<Rect> hitRect,
        Func<bool> visible, Func<bool> locked)
        : this(owner, null, null, anchor.NodeId, anchor.FieldPath, position, hitRect, visible, locked)
    {
    }

    private HGDelegatePortPresentation(object owner, HGNodeView node, HGRow row, string nodeId, string fieldPath,
        Func<Vector2> position, Func<Rect> hitRect, Func<bool> visible, Func<bool> locked)
    {
        Owner = owner;
        Node = node;
        Row = row;
        NodeId = nodeId ?? "";
        FieldPath = fieldPath ?? "";
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
    private readonly Dictionary<GraphNode, HGPort> primaryOutputs;
    private readonly Dictionary<GraphSlotBase, HGPort> primaryInputs;
    private readonly HashSet<GraphNode> selectedOutputs = new();
    private readonly HashSet<GraphSlotBase> selectedInputs = new();
    private readonly List<HGPortDescriptor> descriptors = new();
    private readonly Dictionary<HGPortKey, HGPortDescriptor> descriptorsByKey = new();
    public int Generation { get; }
    internal object Scope { get; }
    public HGPortBuildResult LastResult { get; private set; } = HGPortBuildResult.None;
    internal bool Reject(HGPortBuildResult result) { LastResult = result; return false; }
    public IReadOnlyList<HGPort> Ports => ports.AsReadOnly();
    public IReadOnlyList<HGPortDescriptor> Descriptors => descriptors.AsReadOnly();

    public HGPortRegistry(int generation)
        : this(generation, null, new List<HGPort>(), new Dictionary<HGPortKey, HGPort>(), new Dictionary<GraphNode, HGPort>())
    {
    }

    internal HGPortRegistry(int generation, object scope)
        : this(generation, scope, new List<HGPort>(), new Dictionary<HGPortKey, HGPort>(), new Dictionary<GraphNode, HGPort>())
    {
    }

    internal HGPortRegistry(int generation, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey, Dictionary<GraphNode, HGPort> primaryOutputs,
        Dictionary<GraphSlotBase, HGPort> primaryInputs = null)
        : this(generation, null, ports, byKey, primaryOutputs, primaryInputs)
    {
    }

    private HGPortRegistry(int generation, object scope, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey, Dictionary<GraphNode, HGPort> primaryOutputs,
        Dictionary<GraphSlotBase, HGPort> primaryInputs = null)
    {
        Generation = generation;
        Scope = scope;
        this.ports = ports ?? throw new ArgumentNullException(nameof(ports));
        this.byKey = byKey ?? throw new ArgumentNullException(nameof(byKey));
        this.primaryOutputs = primaryOutputs ?? throw new ArgumentNullException(nameof(primaryOutputs));
        this.primaryInputs = primaryInputs ?? new Dictionary<GraphSlotBase, HGPort>();
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
        IHGPortPresentation presentation, bool isPrimaryOutput = false)
    {
        if (source == null || policy == null || presentation == null)
        {
            LastResult = HGPortBuildResult.InvalidBinding;
            return false;
        }
        return Add(new HGPort(WithRole(key, HGPortRole.Output), new HGOutputPortBinding(source), policy,
            presentation, Generation, isPrimaryOutput));
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
    public bool TryGetDescriptor(HGPortKey key, out HGPortDescriptor descriptor)
        => descriptorsByKey.TryGetValue(key, out descriptor);

    /// <summary>Finds the explicit primary presentation for a persisted source without exposing its mutable Port.</summary>
    public bool TryGetPrimaryOutputDescriptor(GraphNode source, out HGPortDescriptor descriptor)
    {
        if (source != null && primaryOutputs.TryGetValue(source, out HGPort port))
            return TryGetDescriptor(port.Key, out descriptor);
        descriptor = default;
        return false;
    }

    internal bool TryGetPrimaryOutput(GraphNode source, out HGPort port)
    {
        if (source != null) return primaryOutputs.TryGetValue(source, out port);
        port = null;
        return false;
    }

    /// <summary>Registers an explicitly constructed Port after validation.</summary>
    public bool Add(HGPort port)
    {
        HGPortDescriptor descriptor;
        try
        {
            LastResult = Validate(port);
            if (LastResult != HGPortBuildResult.Added) return false;
            // Evaluate Tool getters before publishing any part of the registration.
            descriptor = new HGPortDescriptor(port);
        }
        catch (Exception)
        {
            LastResult = HGPortBuildResult.InvalidBinding;
            return false;
        }
        ports.Add(port);
        byKey.Add(port.Key, port);
        if (port.IsPrimaryOutput) primaryOutputs.Add(port.Source.OutputNode, port);
        if (port.IsInput && !primaryInputs.ContainsKey(port.InputSlot)) primaryInputs.Add(port.InputSlot, port);
        descriptors.Add(descriptor);
        descriptorsByKey.Add(port.Key, descriptor);
        return true;
    }

    /// <summary>Selects one explicit input presentation, replacing the built-in default.</summary>
    public bool SelectPrimaryInput(HGPortKey key)
    {
        if (!byKey.TryGetValue(key, out var port) || !port.IsInput) return false;
        if (!selectedInputs.Add(port.InputSlot)) { LastResult = HGPortBuildResult.DuplicatePrimaryInput; return false; }
        primaryInputs[port.InputSlot] = port;
        return true;
    }

    /// <summary>Selects one explicit output presentation, replacing the built-in default.</summary>
    public bool SelectPrimaryOutput(HGPortKey key)
    {
        if (!byKey.TryGetValue(key, out var port) || !port.IsOutput) return false;
        if (selectedOutputs.Contains(port.Source.OutputNode)) { LastResult = HGPortBuildResult.DuplicatePrimaryOutput; return false; }
        var updates = new List<(int index, HGPortDescriptor descriptor)>();
        try
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                var candidate = byKey[descriptors[i].Key];
                if (!candidate.IsOutput || !ReferenceEquals(candidate.Source.OutputNode, port.Source.OutputNode)) continue;
                updates.Add((i, new HGPortDescriptor(candidate, ReferenceEquals(candidate, port))));
            }
        }
        catch (Exception) { LastResult = HGPortBuildResult.InvalidBinding; return false; }
        selectedOutputs.Add(port.Source.OutputNode);
        primaryOutputs[port.Source.OutputNode] = port;
        foreach (var update in updates)
        {
            descriptors[update.index] = update.descriptor;
            descriptorsByKey[update.descriptor.Key] = update.descriptor;
        }
        LastResult = HGPortBuildResult.Added;
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

        if (port.IsPrimaryOutput)
        {
            if (!port.IsOutput || port.Source?.OutputNode == null) return HGPortBuildResult.InvalidBinding;
            if (primaryOutputs.ContainsKey(port.Source.OutputNode)) return HGPortBuildResult.DuplicatePrimaryOutput;
        }

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
    private readonly HGPortRegistry registry;
    private readonly List<HGNodeViewInfo> nodes;
    private readonly List<HGFieldViewInfo> fields;
    private readonly HashSet<GraphSlotBase> allowedInputs;
    private readonly HashSet<GraphNode> allowedOutputs;
    private readonly List<GraphDiagnostic> diagnostics = new();
    internal IReadOnlyList<GraphDiagnostic> Diagnostics => diagnostics;

    public int Generation => registry.Generation;
    public HGPortBuildResult LastResult => registry.LastResult;
    public IReadOnlyList<HGNodeViewInfo> Nodes => nodes.AsReadOnly();
    public IReadOnlyList<HGFieldViewInfo> Fields => fields.AsReadOnly();
    public IReadOnlyList<HGPortDescriptor> Ports => registry.Descriptors;

    public HGPortBuildContext(HGGraphView graph, int generation)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        registry = new HGPortRegistry(generation, graph.Ports, graph.PortsByKey, graph.PrimaryOutputs, graph.PrimaryInputs);
        (nodes, fields) = BuildViewInfo(graph);
    }

    internal HGPortBuildContext(HGGraphView graph, int generation, List<HGPort> ports,
        Dictionary<HGPortKey, HGPort> byKey, Dictionary<GraphNode, HGPort> primaryOutputs)
    {
        registry = new HGPortRegistry(generation, ports, byKey, primaryOutputs, graph.PrimaryInputs);
        (nodes, fields) = BuildViewInfo(graph);
        allowedInputs = new HashSet<GraphSlotBase>();
        allowedOutputs = new HashSet<GraphNode>();
        foreach (var node in graph.Nodes)
        {
            if (node.Carrier != null) allowedOutputs.Add(node.Carrier);
            if (node.PropertyInput != null) allowedInputs.Add(node.PropertyInput);
            foreach (var row in HGGraph.AllRows(node.Rows))
            {
                if (row.InputSlot != null) allowedInputs.Add(row.InputSlot);
            }
        }
    }

    public bool AddInput(HGPortKey key, GraphSlotBase inputSlot, IHGPortPolicy policy,
        IHGPortPresentation presentation)
    {
        if (allowedInputs != null && !allowedInputs.Contains(inputSlot))
            return Record(key, registry.Reject(HGPortBuildResult.ForeignBinding));
        return Record(key, registry.AddInput(key, inputSlot, policy, presentation));
    }

    public bool AddOutput(HGPortKey key, IHGPortSource source, IHGPortPolicy policy,
        IHGPortPresentation presentation, bool isPrimaryOutput = false)
    {
        if (allowedOutputs != null && (source == null || !allowedOutputs.Contains(source.OutputNode)))
            return Record(key, registry.Reject(HGPortBuildResult.ForeignBinding));
        return Record(key, registry.AddOutput(key, source, policy, presentation, isPrimaryOutput));
    }

    public bool AddAggregate(HGPortKey key, IHGPortPresentation presentation, IHGPortPolicy policy = null)
        => Record(key, registry.AddAggregate(key, presentation, policy));

    internal bool TryGet(HGPortKey key, out HGPort port) => registry.TryGet(key, out port);

    /// <summary>Creates a view-free presentation that follows current layout, visibility and locking.</summary>
    public IHGPortPresentation CreatePresentation(HGPortKey existing, Vector2 offset)
    {
        if (!registry.TryGet(existing, out var port)) throw new ArgumentException("Unknown endpoint.", nameof(existing));
        var anchor = new HGPortDescriptor(port).Anchor;
        return new HGDelegatePortPresentation(new object(), anchor,
            () => port.Presentation.Position + offset,
            () => { var rect = port.Presentation.HitRect; rect.position += offset; return rect; },
            () => port.Presentation.Visible, () => port.Presentation.Locked);
    }

    /// <summary>Adds a presentation of an existing input without exposing its view or policy.</summary>
    public bool AddInputAlias(HGPortKey existing, HGPortKey key, IHGPortPresentation presentation)
        => Record(key, registry.TryGet(existing, out var port) && port.IsInput
            && registry.AddInput(key, port.InputSlot, port.Policy, presentation));

    /// <summary>Adds a presentation of an existing source without exposing its view or policy.</summary>
    public bool AddOutputAlias(HGPortKey existing, HGPortKey key, IHGPortPresentation presentation)
        => Record(key, registry.TryGet(existing, out var port) && port.IsOutput
            && registry.AddOutput(key, port.Source, port.Policy, presentation));

    public bool SelectPrimaryInput(HGPortKey key) => Record(key, registry.SelectPrimaryInput(key));
    public bool SelectPrimaryOutput(HGPortKey key) => Record(key, registry.SelectPrimaryOutput(key));

    private bool Record(HGPortKey key, bool accepted)
    {
        if (!accepted) diagnostics.Add(new GraphDiagnostic("graphkit.port.registration-rejected", GraphDiagnosticSeverity.Error,
            "Port " + key + " was rejected: " + registry.LastResult + ". Binding must belong to the current graph.",
            new GraphDiagnosticLocation(nodeId: key.OwnerId, fieldPath: key.Path)));
        return accepted;
    }
    public bool TryGetDescriptor(HGPortKey key, out HGPortDescriptor descriptor)
        => registry.TryGetDescriptor(key, out descriptor);
    public bool TryGetPrimaryOutputDescriptor(GraphNode source, out HGPortDescriptor descriptor)
        => registry.TryGetPrimaryOutputDescriptor(source, out descriptor);

    private static (List<HGNodeViewInfo> nodes, List<HGFieldViewInfo> fields) BuildViewInfo(HGGraphView graph)
    {
        var nodes = new List<HGNodeViewInfo>();
        var fields = new List<HGFieldViewInfo>();
        foreach (var node in graph.Nodes)
        {
            nodes.Add(new HGNodeViewInfo(node));
            foreach (var row in HGGraph.AllRows(node.Rows)) fields.Add(new HGFieldViewInfo(row, node));
        }
        return (nodes, fields);
    }
}

/// <summary>Reason a Port registration was not accepted by the current build scope.</summary>
public enum HGPortBuildResult
{
    None,
    Added,
    NullPort,
    StaleGeneration,
    DuplicateKey,
    DuplicatePrimaryOutput,
    MissingOwner,
    InvalidBinding,
    DuplicatePrimaryInput,
    ForeignBinding,
}
}
