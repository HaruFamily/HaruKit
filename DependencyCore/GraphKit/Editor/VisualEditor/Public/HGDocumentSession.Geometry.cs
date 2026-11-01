namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Copied background canvas geometry, with no window, live model or writable carrier references.</summary>
public sealed class HGLayoutSnapshot
{
    public IReadOnlyList<HGLayoutNode> Nodes { get; internal set; }
    public IReadOnlyList<HGLayoutEdge> Edges { get; internal set; }
    public IReadOnlyList<HGLayoutPort> Ports { get; internal set; }
    public IReadOnlyList<string> Diagnostics { get; internal set; }
    public int BoundaryEdges { get; internal set; }
}

public sealed class HGLayoutNode
{
    public string Id { get; internal set; }
    public string ParentNodeId { get; internal set; }
    public string Title { get; internal set; }
    public Rect Rect { get; internal set; }
    public bool SyntheticId { get; internal set; }
    public bool Disabled { get; internal set; }
}

public sealed class HGLayoutEdge
{
    public string InputNodeId { get; internal set; }
    public string OutputNodeId { get; internal set; }
    public string FieldPath { get; internal set; }
    public Vector2 Input { get; internal set; }
    public Vector2 Output { get; internal set; }
}

public sealed class HGLayoutPort
{
    public string NodeId { get; internal set; }
    public string FieldPath { get; internal set; }
    public bool Input { get; internal set; }
    public Vector2 Position { get; internal set; }
}

public sealed partial class HGDocumentSession<TDocument> where TDocument : class, IGraphDocument
{
    /// <summary>Measures an isolated graph copy using the canvas builder; tab selection never touches a window.</summary>
    public HGLayoutSnapshot CaptureLayout(string scope, string groupId = null, int? tabIndex = null)
    {
        var copy = GraphDeepCopy.Copy(Document);
        HGGraphSnapshotSafety.RequireDetachedGraph(Document, copy);
        var ids = new HashSet<string>(InspectObjects().OfType<GraphNode>().Select(node => node.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
        var model = HGModel.CreateDetachedView(null, copy, context.Profile?.RootAdapter);
        var roots = new List<object>();
        System.Collections.IList orphans;
        if (scope == RootGroupScope)
        {
            roots.AddRange(model.ReadRootGroups().Where(root => root?.Root != null).Select(root => root.Root));
            orphans = copy.Orphans;
        }
        else
        {
            var token = (copy as ITokenOwner)?.Tokens.SingleOrDefault(item => item != null && TokenGroupScope(item) == scope);
            if (token == null) throw new ArgumentException("Select a current scope from group_scopes.");
            roots.Add(token.Slot);
            orphans = token.Orphans;
        }
        var graph = HGGraph.Build(model, roots, orphans, scope, "背景幾何", metadata: context.Metadata, nestSlots: true);
        var diagnostics = graph.Diagnostics.Select(issue => issue.Code + ": " + issue.Message).ToList();
        if (graph.Diagnostics.Any(issue => issue.Severity == GraphDiagnosticSeverity.Error))
            throw new InvalidOperationException("Cannot measure a failed graph build: " + string.Join(" | ", diagnostics.Take(3)));
        var off = new HashSet<string>();
        var groups = (copy as IGraphViewStateOwner)?.ViewState?.NodeGroups.Where(group => group.Scope == scope).ToArray()
            ?? Array.Empty<GraphNodeGroup>();
        var selected = groupId == null ? null : groups.SingleOrDefault(group => group.Id == groupId);
        if (groupId != null && selected == null || tabIndex.HasValue && (selected == null || tabIndex < 0 || tabIndex >= selected.Tabs.Count))
            throw new ArgumentException("Select a group and valid tab_index from group_tabs.");
        foreach (var group in groups)
            foreach (string member in group.Members)
            {
                bool hide = group == selected ? group.TabOf(member) != (tabIndex ?? group.ActiveTab)
                    : group.Collapsed || !group.IsOnActiveTab(member);
                if (hide) off.Add(member);
            }
        var visible = graph.Nodes.Where(node => !node.Hidden && !off.Contains(node.Id)).ToArray();
        if (visible.Length > 512 || graph.Links.Count > 4096) throw new InvalidOperationException("Geometry exceeds 512 nodes / 4096 links; use a smaller document or scope.");
        foreach (var node in graph.Nodes) if (off.Contains(node.Id)) node.Hidden = true;
        BuildGeometryPorts(graph);
        var visibleNodes = new HashSet<HGNodeView>(visible);
        var edges = new List<HGLayoutEdge>();
        int boundaries = 0;
        foreach (var link in graph.Links)
        {
            if (link.OutputOwner?.NestedSlot != null && ReferenceEquals(link.ParentRow, link.OutputOwner.NestedSlot)) continue;
            if (!visibleNodes.Contains(link.InputOwner) || !visibleNodes.Contains(link.OutputOwner))
            { if (visibleNodes.Contains(link.InputOwner) || visibleNodes.Contains(link.OutputOwner)) boundaries++; continue; }
            if (link.InputPort == null || link.OutputPort == null || !link.InputPort.Presentation.Visible || !link.OutputPort.Presentation.Visible)
            { boundaries++; continue; }
            edges.Add(new HGLayoutEdge { InputNodeId = (link.InputPort.Presentation as IHGPortPresentationLocator)?.NodeId ?? link.InputOwner.Id,
                OutputNodeId = (link.OutputPort.Presentation as IHGPortPresentationLocator)?.NodeId ?? link.OutputOwner.Id,
                FieldPath = link.ParentRow?.Path, Input = link.InputPort.Presentation.Position, Output = link.OutputPort.Presentation.Position });
        }
        diagnostics.Add("Measured canvas bounds/ports; schematic preview omits custom controls, group frames, hidden-edge ghosts and text clipping. Not a Unity screenshot or visual acceptance.");
        return new HGLayoutSnapshot { Nodes = Array.AsReadOnly(visible.Select(node => new HGLayoutNode
            { Id = node.Id, ParentNodeId = node.NestedParent?.Id, Title = node.Title, Rect = node.Rect,
                SyntheticId = !ids.Contains(node.Id), Disabled = node.InDisabledSubtree }).ToArray()),
            Ports = Array.AsReadOnly(graph.Ports.Where(port => port.Presentation.Visible).Select(port => new HGLayoutPort
                { NodeId = (port.Presentation as IHGPortPresentationLocator)?.NodeId ?? port.Key.OwnerId,
                    FieldPath = (port.Presentation as IHGPortPresentationLocator)?.FieldPath, Input = port.IsInput, Position = port.Presentation.Position }).ToArray()),
            Edges = edges.AsReadOnly(), BoundaryEdges = boundaries, Diagnostics = diagnostics.AsReadOnly() };
    }

    private void BuildGeometryPorts(HGGraphView graph)
    {
        var build = new HGPortBuildContext(graph, Generation, graph.Ports, graph.PortsByKey, graph.PrimaryOutputs);
        var policy = new HGDelegatePortPolicy(() => false);
        IHGPortPresentation Presentation(HGNodeView node, HGRow row, Func<Vector2> position, Func<bool> visible)
            => new HGDelegatePortPresentation(row ?? (object)node, node, row, position, () => Rect.zero, visible, () => true);
        foreach (var node in graph.Nodes)
        {
            foreach (var row in HGGraph.AllRows(node.Rows))
            {
                row.ScreenRect = new Rect(node.Pos.x, node.Pos.y + row.LocalY, node.Width, row.Height);
                row.InputPortPosition = new Vector2(node.Pos.x + node.Width - HGGraph.PortRadius,
                    node.Pos.y + row.LocalY + (row.NodeSocket ? row.SocketHeaderHeight : row.Height) * 0.5f);
                if (!row.HasInputPort || row.InputSlot == null || row.InputSlot is PropertySlotBase) continue;
                var presentation = Presentation(node, row, () => row.InputPortPosition, () => !node.Hidden && row.IsInputPortVisible && !row.IsTabHidden);
                var key = new HGPortKey(row.OwnerNodeId, row.Path, HGPortRole.Input);
                build.AddInput(key, row.InputSlot, policy, presentation);
            }
            if (!node.IsRoot && node.Carrier != null)
                build.AddOutput(new HGPortKey(node.Id, "", HGPortRole.Output), new HGDelegatePortSource(node.Carrier, node.Carrier, _ => false), policy,
                    Presentation(node, null, () => node.OutputPortPosition, () => !node.Hidden && node.HasOutputPort), true);
        }
        context.Provider.AddPorts(build);
        if (graph.Ports.Count > 8192) throw new InvalidOperationException("Background geometry exceeds 8192 ports.");
        if (build.Diagnostics.Any(issue => issue.Severity == GraphDiagnosticSeverity.Error))
            throw new InvalidOperationException("Background port registration failed.");
        foreach (var link in graph.Links)
            HGLinkPortResolver.Resolve(link, graph.PortsByKey, graph.PrimaryOutputs, Generation, graph.PrimaryInputs);
    }
}
}
