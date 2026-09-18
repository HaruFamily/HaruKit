namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Port construction, hit testing, compatibility, connection and disconnection.</summary>
public partial class HaruGraphWindow
{
    private enum PortCommandResult
    {
        Changed,
        NoChange,
        Rejected,
    }

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
        ClearPortInteractionState();
        graphGeneration++;
        graph.Ports.Clear();
        graph.PortsByKey.Clear();
        graph.PrimaryOutputs.Clear();
        graph.PrimaryInputs.Clear();

        foreach (var node in graph.Nodes) UpdateRowGeometry(node, node.Rows);

        var context = new HGPortBuildContext(graph, graphGeneration, graph.Ports, graph.PortsByKey, graph.PrimaryOutputs);
        foreach (var node in graph.Nodes)
        {
            foreach (var row in HGGraph.AllRows(node.Rows))
            {
                if (row.HasInputPort && row.InputSlot != null)
                    AddInputPort(context, node, row);

                if (row.OutputNode != null)
                    AddCellOutputPort(context, node, row);

                if (row.Kind == HGRowKind.List)
                    AddAggregatePort(context, node, row);
            }

            if (!node.IsRoot && node.Carrier != null)
                AddNodeOutputPort(context, node);
        }

        try
        {
            activeContext.Provider.AddPorts(context);
            graph.Diagnostics.AddRange(context.Diagnostics);
        }
        catch (Exception exception)
        {
            graph.Ports.Clear();
            graph.PortsByKey.Clear();
            graph.PrimaryInputs.Clear();
            graph.PrimaryOutputs.Clear();
            graph.Diagnostics.Add(new GraphDiagnostic("graphkit.provider.ports-failed", GraphDiagnosticSeverity.Error,
                exception.Message, new GraphDiagnosticLocation(model.DocumentId, focus?.Id)));
        }
        ResolveLinkPorts();
    }

    private void AddInputPort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.InputPortPosition,
            () => PortRect(row.InputPortPosition),
            () => !node.Hidden && row.IsInputPortVisible,
            () => row.Locked || node.InLockedSubtree);
        var policy = new HGDelegatePortPolicy(
            () => true,
            checkAcceptance: source =>
            {
                if (source == null) return HGPortConnectionResult.MissingBinding;
                HGPortConnectionResult sourceResult = HGPortConnection.CheckSourceAcceptance(source, row.InputSlot);
                if (sourceResult != HGPortConnectionResult.Allowed) return sourceResult;
                return WouldCreateCycle(row.InputSlot, source.CycleRoot)
                    ? HGPortConnectionResult.WouldCreateCycle
                    : HGPortConnectionResult.Allowed;
            });
        context.AddInput(InputKey(row), row.InputSlot, policy, presentation);
    }

    private void AddNodeOutputPort(HGPortBuildContext context, HGNodeView node)
    {
        var presentation = new HGDelegatePortPresentation(node, node, null,
            () => node.OutputPortPosition,
            () => PortRect(node.OutputPortPosition),
            () => !node.IsRoot && !node.Hidden && node.HasOutputPort,
            () => node.InLockedSubtree);
        var source = new HGDelegatePortSource(node.Carrier, CycleRoot(node), input => SourceAccepts(node, input),
            input => SourceAcceptance(node, input));
        context.AddOutput(OutputKey(node), source, new HGDelegatePortPolicy(() => true), presentation, true);
    }

    private void AddCellOutputPort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.OutputPortPosition,
            () => PortRect(row.OutputPortPosition),
            () => !node.Hidden && !row.Hidden,
            () => row.Locked || node.InLockedSubtree);
        var source = new HGDelegatePortSource(row.OutputNode, row.OutputNode,
            input => input.AcceptsBody(row.OutputNode?.BodyObject),
            input => row.OutputNode?.BodyObject is GraphNodeContent body
                ? input.AcceptsBody(body) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.IncompatibleType
                : HGPortConnectionResult.MissingBinding);
        context.AddOutput(OutputKey(row), source, new HGDelegatePortPolicy(() => true), presentation, true);
    }

    private void AddAggregatePort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.InputPortPosition,
            () => Rect.zero,
            () => !node.Hidden && row.Collapsed && HasConnectedElement(row),
            () => false);
        context.AddAggregate(new HGPortKey(row.OwnerNodeId, row.Path, HGPortRole.Aggregate), presentation);
    }

    private void ResolveLinkPorts()
    {
        graph.Diagnostics.RemoveAll(diagnostic => diagnostic.Code.StartsWith("graphkit.port-resolution.", StringComparison.Ordinal));
        foreach (var link in graph.Links)
        {
            HGLinkPortResolution resolution = HGLinkPortResolver.Resolve(link, graph.PortsByKey, graph.PrimaryOutputs,
                graphGeneration, graph.PrimaryInputs);
            if (resolution == HGLinkPortResolution.InputUnresolved)
            {
                AddPortResolutionDiagnostic("input-unresolved", "連線的輸入接點無法在目前圖形中定位。",
                    link.ParentRow?.OwnerNodeId, link.ParentRow?.Path);
                continue;
            }

            GraphNode source = link.TargetRow?.OutputNode ?? link.OutputOwner?.Carrier;
            if (resolution == HGLinkPortResolution.OutputUnresolved)
            {
                AddPortResolutionDiagnostic("output-unresolved", "連線的來源載體無法在目前圖形中定位。",
                    link.ParentRow.OwnerNodeId, link.ParentRow.Path);
                continue;
            }

            if (resolution == HGLinkPortResolution.PrimaryOutputMissing)
            {
                AddPortResolutionDiagnostic("primary-output-missing", "連線來源沒有可用的主要輸出接點。",
                    source.Id, null);
                continue;
            }
        }
    }

    private void AddPortResolutionDiagnostic(string kind, string message, string nodeId, string fieldPath)
    {
        string code = "graphkit.port-resolution." + kind;
        var location = new GraphDiagnosticLocation(model.DocumentId, focus?.Id, nodeId: nodeId, fieldPath: fieldPath);
        foreach (var diagnostic in graph.Diagnostics)
        {
            if (diagnostic.Code != code || diagnostic.Severity != GraphDiagnosticSeverity.Error) continue;
            GraphDiagnosticLocation existing = diagnostic.Location;
            if (existing.DocumentId == location.DocumentId && existing.FocusId == location.FocusId
                && existing.NodeId == location.NodeId && existing.TokenId == location.TokenId
                && existing.FieldPath == location.FieldPath) return;
        }
        graph.Diagnostics.Add(new GraphDiagnostic(code, GraphDiagnosticSeverity.Error, message, location,
            "重建圖形或確認擴充接點已註冊主要輸出。"));
    }

    private static object CycleRoot(HGNodeView node)
    {
        if (node == null || node.IsCatalogNode) return null;
        return node.IsTokenNode ? node.Token?.Slot : node.Carrier;
    }

    private bool SourceAccepts(HGNodeView source, GraphSlotBase input)
        => SourceAcceptance(source, input) == HGPortConnectionResult.Allowed;

    private HGPortConnectionResult SourceAcceptance(HGNodeView source, GraphSlotBase input)
    {
        if (source?.Carrier == null || input == null) return HGPortConnectionResult.MissingBinding;
        if (source.IsAssetNode)
        {
            if (source.Asset == null) return HGPortConnectionResult.MissingBinding;
            return CanAssignAsset(input, source.Asset)
                ? HGPortConnectionResult.Allowed
                : HGPortConnectionResult.IncompatibleType;
        }
        if (source.IsTokenNode)
        {
            if (source.Token == null) return HGPortConnectionResult.MissingBinding;
            return input.AcceptsToken(source.Token)
                ? HGPortConnectionResult.Allowed
                : HGPortConnectionResult.IncompatibleFamily;
        }
        if (source.IsCatalogNode)
        {
            if (source.Carrier.CatalogObject == null) return HGPortConnectionResult.MissingBinding;
            if (input is not CatalogSlotBase catalogSlot) return HGPortConnectionResult.IncompatibleFamily;
            return catalogSlot.AcceptsCatalogObject(source.Carrier.CatalogObject)
                ? HGPortConnectionResult.Allowed
                : HGPortConnectionResult.DomainRejected;
        }

        if (source.IsPlaceholder)
        {
            Type sourceKind = RepresentativeSlotType(source);
            return sourceKind == null || sourceKind == input.GetType()
                ? HGPortConnectionResult.Allowed
                : HGPortConnectionResult.IncompatibleFamily;
        }

        if (source.Obj is not GraphNodeContent body) return HGPortConnectionResult.MissingBinding;
        return input.AcceptsBody(body) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.IncompatibleType;
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

    /// <summary>Invalidates generation-local Port references before the graph, focus, or working copy changes.</summary>
    private void ClearPortInteractionState()
    {
        EndLink();
        inputPortClickPort = null;
        inputPortClickStart = Vector2.zero;
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
        return HGPortConnection.CheckInputSource(input, source, graphGeneration)
            == HGPortConnectionResult.Allowed;
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
        if (port?.Presentation is IHGPortPresentationLocator locator && !string.IsNullOrEmpty(locator.NodeId))
            return NodeOfId(locator.NodeId);
        if (port?.Presentation is IHGPortPresentationAnchor anchor) return anchor.Node;
        if (port?.Presentation.Owner is HGNodeView node) return node;
        return port?.Presentation.Owner is HGRow row ? OwnerOfRow(row) : null;
    }

    private HGRow OwnerRowOfPort(HGPort port)
    {
        if (port?.Presentation is IHGPortPresentationLocator locator
            && !string.IsNullOrEmpty(locator.NodeId) && !string.IsNullOrEmpty(locator.FieldPath))
            return RowOf(locator.NodeId, locator.FieldPath);
        if (port?.Presentation is IHGPortPresentationAnchor anchor) return anchor.Row;
        return port?.Presentation.Owner as HGRow;
    }

    private HGNodeView NodeOfId(string nodeId)
    {
        if (graph == null || string.IsNullOrEmpty(nodeId)) return null;
        foreach (var node in graph.Nodes)
            if (node.Id == nodeId) return node;
        return null;
    }

    private HGRow RowOf(string nodeId, string path)
    {
        HGNodeView node = NodeOfId(nodeId);
        if (node == null) return null;
        foreach (var row in HGGraph.AllRows(node.Rows))
            if (row.Path == path) return row;
        return null;
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
            HGRow row = OwnerRowOfPort(port);
            if (!port.IsInput || !port.Presentation.Visible || row == null) continue;
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
        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            GraphNode carrier = NewSource(slot);
            if ((slot as CatalogSlotBase)?.CreateDefaultCatalog() is GraphNodeContent pack) carrier.SetCatalog(pack);
            else if ((slot as FormulaSlotBase)?.CreateDefaultBody() is GraphNodeContent body) carrier.SetBody(body);
            carrier.Pos = SnapToGrid(graphMouse);
        }, out var error))
        {
            ShowNotification(new GUIContent(error));
            return;
        }
        Invalidate();
        Repaint();
    }

    private PortCommandResult TryConnectPorts(HGPort first, HGPort second)
    {
        var acceptance = HGPortConnection.Check(first, second, graphGeneration);
        if (acceptance != HGPortConnectionResult.Allowed)
        {
            ShowNotification(new GUIContent("無法接線：" + acceptance));
            return PortCommandResult.Rejected;
        }
        HGPort input = first.IsInput ? first : second;
        HGPort output = first.IsOutput ? first : second;
        if (input.InputSlot == null || output.Source?.OutputNode == null) return PortCommandResult.Rejected;
        if (!graph.PrimaryInputs.ContainsKey(input.InputSlot)
            || !graph.ByCarrier.ContainsKey(output.Source.OutputNode) && !graph.CellRows.ContainsKey(output.Source.OutputNode))
            return PortCommandResult.Rejected;
        if (ReferenceEquals(input.InputSlot.Node, output.Source.OutputNode)) return PortCommandResult.NoChange;

        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            AttachSource(input.InputSlot, output.Source.OutputNode);
        }, out var error))
        {
            ShowNotification(new GUIContent(error));
            return PortCommandResult.Rejected;
        }
        Invalidate();
        return PortCommandResult.Changed;
    }

    // 線的輸入端一律走 Port：命中測試（LinkAt）已經要求兩端都解析得到，走不到沒有 Port 的線。
    private PortCommandResult CutLink(HGLink link) => CutLink(link?.InputPort?.InputSlot);

    private PortCommandResult CutLink(GraphSlotBase slot)
    {
        if (slot?.Node == null) return PortCommandResult.NoChange;
        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            AttachSource(slot, null);
        }, out var error))
        {
            ShowNotification(new GUIContent(error));
            return PortCommandResult.Rejected;
        }
        Invalidate();
        return PortCommandResult.Changed;
    }

    private bool AttachSource(GraphSlotBase slot, GraphNode next)
    {
        if (slot == null) return false;
        var old = slot.Node;
        if (ReferenceEquals(old, next)) return false;

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
        return true;
    }

    private bool IsCarrierUsed(GraphNode carrier)
    {
        if (carrier == null) return false;
        foreach (var slot in SlotsInCurrentGraph())
            if (ReferenceEquals(slot?.Node, carrier)) return true;
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
