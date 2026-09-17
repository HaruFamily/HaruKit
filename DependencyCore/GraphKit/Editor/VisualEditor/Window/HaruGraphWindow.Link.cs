namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Port construction, hit testing, compatibility, connection and disconnection.</summary>
public partial class HaruGraphWindow
{
    private static HGPortKey InputKey(HGRow row)
        => new HGPortKey(row?.OwnerNodeId, row?.Path, HGPortRole.Input);

    private static HGPortKey OutputKey(HGNodeView node)
        => new HGPortKey(node?.Id, "", HGPortRole.Output);

    private static HGPortKey OutputKey(HGRow row)
        => new HGPortKey(row?.OutputNode?.EnsureId(), "", HGPortRole.Output);

    private static Rect PortRect(Vector2 position)
        => new Rect(position - Vector2.one * HGGraph.PortRadius, Vector2.one * HGGraph.PortDiameter);

    /// <summary>Rebuild all generation-local Port adapters and discard every old hit/compatibility reference.</summary>
    private void RebuildPorts()
    {
        EndLink();
        inputPortClickPort = null;
        graphGeneration++;
        graph.Generation = graphGeneration;
        graph.Ports.Clear();
        graph.PortsByKey.Clear();

        var context = new HGPortBuildContext(graph, graphGeneration, graph.Ports, graph.PortsByKey);
        foreach (var node in graph.Nodes)
        {
            foreach (var row in HGGraph.AllRows(node.Rows))
            {
                if (row.HasInputPort && row.InputSlot != null)
                    context.Add(CreateInputPort(node, row));

                if (row.OutputNode != null)
                    context.Add(CreateCellOutputPort(node, row));

                if (row.Kind == HGRowKind.List)
                    context.Add(CreateAggregatePort(node, row));
            }

            if (!node.IsRoot && node.Carrier != null)
                context.Add(CreateNodeOutputPort(node));
        }

        activeContext.Provider.AddPorts(context);
        ResolveLinkPorts();
    }

    private HGPort CreateInputPort(HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row,
            () => row.InputPortPos,
            () => PortRect(row.InputPortPos),
            () => !node.Hidden && row.IsLinkable,
            () => row.Locked || node.InLockedSubtree);
        var policy = new HGDelegatePortPolicy(
            () => true,
            source => source != null && source.Accepts(row.InputSlot)
                && !WouldCreateCycle(row.InputSlot, source.CycleRoot));
        return new HGPort(InputKey(row), new HGInputPortBinding(row.InputSlot), policy, presentation, graphGeneration);
    }

    private HGPort CreateNodeOutputPort(HGNodeView node)
    {
        var presentation = new HGDelegatePortPresentation(node,
            () => node.OutputPort,
            () => PortRect(node.OutputPort),
            () => !node.IsRoot && !node.Hidden && node.HasOutputPort,
            () => node.InLockedSubtree);
        var source = new HGDelegatePortSource(node.Carrier, CycleRoot(node), input => SourceAccepts(node, input));
        return new HGPort(OutputKey(node), new HGOutputPortBinding(source),
            new HGDelegatePortPolicy(() => true), presentation, graphGeneration);
    }

    private HGPort CreateCellOutputPort(HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row,
            () => row.OutputPortPos,
            () => PortRect(row.OutputPortPos),
            () => !node.Hidden && !row.Hidden,
            () => row.Locked || node.InLockedSubtree);
        var source = new HGDelegatePortSource(row.OutputNode, row.OutputNode,
            input => input.AcceptsBody(row.OutputNode?.BodyObject));
        return new HGPort(OutputKey(row), new HGOutputPortBinding(source),
            new HGDelegatePortPolicy(() => true), presentation, graphGeneration);
    }

    private HGPort CreateAggregatePort(HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row,
            () => row.InputPortPos,
            () => Rect.zero,
            () => !node.Hidden && row.Collapsed && HasConnectedElement(row),
            () => true);
        return new HGPort(new HGPortKey(row.OwnerNodeId, row.Path, HGPortRole.Aggregate),
            HGAggregatePortBinding.Instance, new HGDelegatePortPolicy(() => false), presentation, graphGeneration);
    }

    private void ResolveLinkPorts()
    {
        foreach (var link in graph.Links)
        {
            graph.PortsByKey.TryGetValue(InputKey(link.ParentRow), out link.InputPort);
            HGPortKey outputKey = link.TargetRow != null ? OutputKey(link.TargetRow) : OutputKey(link.Target);
            graph.PortsByKey.TryGetValue(outputKey, out link.OutputPort);
        }
    }

    private static object CycleRoot(HGNodeView node)
    {
        if (node == null || node.IsCatalogNode) return null;
        return node.IsTokenNode ? node.Token?.Slot : node.Carrier;
    }

    private bool SourceAccepts(HGNodeView source, GraphSlotBase input)
    {
        if (source?.Carrier == null || input == null) return false;
        if (source.IsAssetNode) return CanAssignAsset(input, source.Asset);
        if (source.IsTokenNode) return input.AcceptsToken(source.Token);
        if (source.IsCatalogNode)
            return (input as CatalogSlotBase)?.AcceptsCatalogObject(source.Carrier.CatalogObject) == true;

        if (source.IsPlaceholder)
        {
            Type sourceKind = RepresentativeSlotType(source);
            return sourceKind == null || sourceKind == input.GetType();
        }

        return source.Obj is GraphNodeContent body && input.AcceptsBody(body);
    }

    private HGPort PortFor(HGRow row) => graph != null && graph.PortsByKey.TryGetValue(InputKey(row), out var port) ? port : null;
    private HGPort PortFor(HGNodeView node) => graph != null && graph.PortsByKey.TryGetValue(OutputKey(node), out var port) ? port : null;

    private void BeginLink(HGPort port)
    {
        if (port == null || port.Generation != graphGeneration || !port.CanStart) return;
        linking = true;
        linkPort = port;
        RebuildLinkCompatibility();
    }

    private void EndLink()
    {
        linking = false;
        linkPort = null;
        linkCompatiblePorts.Clear();
    }

    private void RebuildLinkCompatibility()
    {
        linkCompatiblePorts.Clear();
        if (graph == null || linkPort == null) return;
        foreach (var candidate in graph.Ports)
            if (CanConnectPorts(linkPort, candidate)) linkCompatiblePorts.Add(candidate.Key);
    }

    private bool CanConnectPorts(HGPort first, HGPort second)
        => HGPortConnection.CanConnect(first, second, graphGeneration);

    private bool IsCompatible(HGPort port)
        => port != null && linking && linkCompatiblePorts.Contains(port.Key);

    private bool CanAcceptExternal(HGRow row, IHGPortSource source)
    {
        var input = PortFor(row);
        return input != null && source != null && input.Presentation.Visible && !input.Presentation.Locked
            && input.Policy.CanAccept(source);
    }

    private HGNodeView LinkTargetNode(Vector2 graphMouse)
        => OwnerNodeOfPort(SnappedCompatiblePort(graphMouse));

    private Vector2 LinkPreviewEnd(Vector2 graphMouse)
    {
        var snapped = SnappedCompatiblePort(graphMouse);
        return snapped?.Presentation.Position ?? graphMouse;
    }

    private HGPort SnappedCompatiblePort(Vector2 graphMouse)
    {
        if (!linking || graph == null || linkPort == null) return null;

        // Inside a node, use the nearest compatible endpoint in that node before global distance snapping.
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (node.Hidden || !node.Rect.Contains(graphMouse)) continue;
            HGPort nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var port in graph.Ports)
            {
                if (!IsCompatible(port) || !ReferenceEquals(OwnerNodeOfPort(port), node)) continue;
                float distance = Mathf.Abs(port.Presentation.Position.y - graphMouse.y);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = port;
            }
            if (nearest != null) return nearest;
        }

        float maxDistanceSqr = LinkSnapDistance * LinkSnapDistance / (zoom * zoom);
        float nearestDistanceSqr = maxDistanceSqr;
        HGPort result = null;
        foreach (var port in graph.Ports)
        {
            if (!IsCompatible(port)) continue;
            float distanceSqr = (port.Presentation.Position - graphMouse).sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr) continue;
            nearestDistanceSqr = distanceSqr;
            result = port;
        }
        return result;
    }

    private HGNodeView OwnerNodeOfPort(HGPort port)
    {
        if (port?.Presentation.Owner is HGNodeView node) return node;
        return port?.Presentation.Owner is HGRow row ? OwnerOfRow(row) : null;
    }

    private HGNodeView OwnerOfRow(HGRow target)
    {
        if (graph == null || target == null) return null;
        foreach (var node in graph.Nodes)
            foreach (var row in HGGraph.AllRows(node.Rows))
                if (ReferenceEquals(row, target)) return node;
        return null;
    }

    private HGPort OutputPortAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Ports.Count - 1; i >= 0; i--)
        {
            var port = graph.Ports[i];
            if (!port.IsOutput || !port.CanStart) continue;
            if (port.Presentation.HitRect.Contains(graphPoint)) return port;
        }
        return null;
    }

    private HGPort InputPortAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        for (int i = graph.Ports.Count - 1; i >= 0; i--)
        {
            var port = graph.Ports[i];
            if (!port.IsInput || !port.Presentation.Visible) continue;
            if (port.Presentation.HitRect.Contains(graphPoint)) return port;
        }
        return null;
    }

    private HGRow RowAt(Vector2 graphPoint, out HGNodeView owner)
    {
        owner = null;
        if (graph == null) return null;
        for (int i = graph.Ports.Count - 1; i >= 0; i--)
        {
            var port = graph.Ports[i];
            if (!port.IsInput || !port.Presentation.Visible || port.Presentation.Owner is not HGRow row) continue;
            if (!row.ScreenRect.Contains(graphPoint)) continue;
            owner = OwnerNodeOfPort(port);
            return row;
        }
        return null;
    }

    private HGLink LinkAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        foreach (var link in graph.Links)
        {
            if (!IsLinkVisible(link) || link.InputPort == null || link.OutputPort == null) continue;
            if (PointToSegmentSqrDistance(graphPoint, link.InputPort.Presentation.Position,
                    link.OutputPort.Presentation.Position) < 36f) return link;
        }
        return null;
    }

    private static float PointToSegmentSqrDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.01f) return (point - from).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSqr);
        return (point - (from + segment * t)).sqrMagnitude;
    }

    private void ResolveLink(Vector2 graphMouse)
    {
        if (linkPort == null) return;
        var target = SnappedCompatiblePort(graphMouse);
        if (target != null)
        {
            TryConnectPorts(linkPort, target);
            return;
        }

        if (linkPort.IsOutput)
        {
            ShowNotification(new GUIContent("請拖到相容的參數接點"));
            return;
        }

        if (NodeAt(graphMouse) != null)
        {
            ShowNotification(new GUIContent("請拖到相容的節點或畫布空白處"));
            return;
        }

        var slot = linkPort.InputSlot;
        if (slot == null) return;
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        GraphNode carrier = NewSource(slot);
        if ((slot as CatalogSlotBase)?.CreateDefaultCatalog() is GraphNodeContent pack) carrier.SetCatalog(pack);
        else if ((slot as FormulaSlotBase)?.CreateDefaultBody() is GraphNodeContent body) carrier.SetBody(body);
        carrier.Pos = SnapToGrid(graphMouse);
        Invalidate();
        Repaint();
    }

    private bool TryConnectPorts(HGPort first, HGPort second)
    {
        if (!CanConnectPorts(first, second)) return false;
        HGPort input = first.IsInput ? first : second;
        HGPort output = first.IsOutput ? first : second;
        if (input.InputSlot == null || output.Source?.OutputNode == null) return false;

        BreakUndoMerge();
        PreserveVisibleNodePositions();
        AttachSource(input.InputSlot, output.Source.OutputNode);
        Invalidate();
        return true;
    }

    private void CutLink(HGLink link) => CutLink(link?.InputPort?.InputSlot ?? link?.ParentRow?.InputSlot);

    private void CutLink(GraphSlotBase slot)
    {
        if (slot == null) return;
        PreserveVisibleNodePositions();
        AttachSource(slot, null);
        Invalidate();
    }

    private void AttachSource(GraphSlotBase slot, GraphNode next)
    {
        if (slot == null) return;
        var old = slot.Node;
        if (ReferenceEquals(old, next)) return;

        slot.SetNode(next);
        if (old != null && !IsCarrierUsed(old))
        {
            model.AddOrphan(old);
            RememberOrphanKind(old, slot.GetType());
        }
        if (next != null)
        {
            next.EnsureId();
            model.RemoveOrphan(next);
        }
    }

    private bool IsCarrierUsed(GraphNode carrier)
    {
        if (carrier == null) return false;
        foreach (var slot in SlotsInCurrentGraph())
            if (slot is GraphSlotBase graphSlot && ReferenceEquals(graphSlot.Node, carrier)) return true;
        return focus.Kind == HGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(focus.AssetHostSlot.Node, carrier);
    }

    private GraphNode NewSource(GraphSlotBase slot)
    {
        var carrier = new GraphNode();
        carrier.EnsureId();
        AttachSource(slot, carrier);
        return carrier;
    }

    /// <summary>把已經失效的包連線就地斷開，回傳斷了幾條。</summary>
    private int BreakInvalidPackLinks()
    {
        var stale = new List<CatalogSlotBase>();
        foreach (var slot in SlotsInCurrentGraph())
        {
            if (slot is not CatalogSlotBase catalogSlot) continue;
            GraphNodeContent pack = catalogSlot.Node?.CatalogObject;
            if (pack == null || catalogSlot.AcceptsCatalogObject(pack)) continue;
            stale.Add(catalogSlot);
        }
        if (stale.Count == 0) return 0;

        PreserveVisibleNodePositions();
        foreach (var catalogSlot in stale)
        {
            GraphNode carrier = catalogSlot.Node;
            catalogSlot.SetNode(null);
            model.AddOrphan(carrier);
        }
        return stale.Count;
    }

    private void MarkCatalogPorts()
    {
        foreach (var node in graph.Nodes)
        {
            if (!node.IsCatalogNode) continue;
            GraphNodeContent pack = node.Carrier?.CatalogObject;
            node.HasOutputPort = pack == null || AnySlotTakesPack(pack, node.Carrier);
        }
    }

    private bool AnySlotTakesPack(GraphNodeContent pack, GraphNode carrier)
    {
        foreach (var slot in SlotsInCurrentGraph())
        {
            if (slot is not CatalogSlotBase catalogSlot) continue;
            if (catalogSlot.AcceptsCatalogObject(pack) || ReferenceEquals(catalogSlot.Node, carrier)) return true;
        }
        return false;
    }

    private static bool WouldCreateCycle(GraphSlotBase slot, object node)
    {
        if (slot == null || node == null) return false;
        foreach (var childSlot in HGModel.WalkSlots(node, new HashSet<object>(HGRefComparer.Instance)))
            if (ReferenceEquals(childSlot, slot)) return true;
        return false;
    }

    private void Connect(GraphSlotBase slot, object node)
    {
        if (slot == null || node is not GraphNodeContent body) return;
        PreserveVisibleNodePositions();
        NewSource(slot).SetBody(body);
        Invalidate();
    }
}
}
