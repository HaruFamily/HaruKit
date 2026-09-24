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
        => new HGPortKey(row?.OwnerNodeId, row?.Path,
            row?.InputSlot is PropertySlotBase ? HGPortRole.Output : HGPortRole.Input);

    private static HGPortKey OutputKey(HGNodeView node)
        => new HGPortKey(node?.Id, "", HGPortRole.Output);

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

                if (row.Kind == HGRowKind.List)
                {
                    AddAggregatePort(context, node, row);
                    AddListAppendPort(context, node, row);
                }
            }

            if (!node.IsRoot && node.Carrier != null)
                AddNodeOutputPort(context, node);

            if (node.IsPropertyNode && node.PropertyInput != null)
                AddPropertyInputPort(context, node);
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

        // ○／◎ 要問「這顆接點有沒有線」：連線兩端解析完才知道，所以跟著這一代的接點一起重算。
        linkedPorts.Clear();
        foreach (var link in graph.Links)
        {
            if (link.InputPort != null) linkedPorts.Add(link.InputPort);
            if (link.OutputPort != null) linkedPorts.Add(link.OutputPort);
        }
    }

    private void AddInputPort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.InputPortPosition, () => PortRect(row.InputPortPosition),
            () => !node.Hidden && row.IsInputPortVisible, () => row.Locked || node.InLockedSubtree);
        if (row.InputSlot is PropertySlotBase writer)
        {
            context.AddOutput(InputKey(row), new HGPropertyWriteSource(writer, node.Carrier),
                new HGDelegatePortPolicy(() => true), presentation);
            return;
        }
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

    private void AddPropertyInputPort(HGPortBuildContext context, HGNodeView node)
    {
        var slot = node.PropertyInput;
        var presentation = new HGDelegatePortPresentation(slot, node, node.PropertyInputRow,
            () => node.PropertyInputPortPosition, () => PortRect(node.PropertyInputPortPosition),
            () => !node.Hidden, () => node.InLockedSubtree);
        var policy = new HGDelegatePortPolicy(() => true, checkAcceptance: source =>
        {
            if (source is not HGPropertyWriteSource writer) return HGPortConnectionResult.IncompatibleType;
            return writer.CheckTarget(node.Carrier);
        });
        context.AddInput(new HGPortKey(node.Id, "/property/input", HGPortRole.Input), slot, policy, presentation);
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

    private void AddAggregatePort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.InputPortPosition,
            () => Rect.zero,
            () => !node.Hidden && row.Collapsed && HasConnectedElement(row),
            () => false);
        context.AddAggregate(new HGPortKey(row.OwnerNodeId, row.Path, HGPortRole.Aggregate), presentation);
    }

    /// <summary>
    /// 元素是 Slot、可增刪的清單在標題列掛新增接點（位置與 Aggregate 相同，Aggregate 契約不動）。
    /// PropertySlot 清單方向相反：新增接點是寫入端，寫入來源要有擁有者載體，根上的清單（沒有載體）不掛。
    /// </summary>
    private void AddListAppendPort(HGPortBuildContext context, HGNodeView node, HGRow row)
    {
        if (row.Items is not HGListItemSource items || !items.CanEditStructure) return;
        if (items.ElementType == null || !HGReflect.IsSlotType(items.ElementType)) return;
        if (!items.TryCreateElement(out object created) || created is not GraphSlotBase prototype) return;

        var presentation = new HGDelegatePortPresentation(row, node, row,
            () => row.InputPortPosition, () => PortRect(row.InputPortPosition),
            () => !node.Hidden && !row.Hidden, () => row.Locked || node.InLockedSubtree);
        if (prototype is PropertySlotBase writer)
        {
            if (node.Carrier == null) return;
            context.AddListAppend(InputKey(row), new HGListAppendWriteBinding(writer, node.Carrier, items),
                new HGDelegatePortPolicy(() => true), presentation);
            return;
        }
        context.AddListAppend(InputKey(row), new HGListAppendPortBinding(prototype, items),
            HGListAppend.DefaultPolicy(prototype, node.Carrier), presentation);
    }

    /// <summary>清單列的新增接點（取值清單是輸入角色、PropertySlot 清單是輸出角色）；沒有就回 null。</summary>
    private HGPort ListAppendPortOf(HGRow row)
    {
        if (graph == null || row == null) return null;
        foreach (HGPortRole role in new[] { HGPortRole.Input, HGPortRole.Output })
            if (graph.PortsByKey.TryGetValue(new HGPortKey(row.OwnerNodeId, row.Path, role), out var port)
                && port.Binding is IHGListAppendBinding) return port;
        return null;
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

            GraphNode source = link.OutputOwner?.Carrier;
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
        // Property 不是求值節點：它沒有往下的求值子樹，
        // 所以「讀 Property → 算 → 寫回同一顆」在圖上成一圈但不是遞迴求值，不可當環根。
        if (node == null || node.IsPropertyNode) return null;
        return node.IsTokenNode ? node.Token?.Slot : node.Carrier;
    }

    private bool SourceAccepts(HGNodeView source, GraphSlotBase input)
        => SourceAcceptance(source, input) == HGPortConnectionResult.Allowed;

    private HGPortConnectionResult SourceAcceptance(HGNodeView source, GraphSlotBase input)
    {
        if (source?.Carrier == null || input == null) return HGPortConnectionResult.MissingBinding;
        if (input is PropertySlotBase) return HGPortConnectionResult.IncompatibleType;
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
        // 讀取欄位與寫入目標欄位共用這一條：兩者的差別是欄位型別（PropertySlotBase），不是節點種類，
        // 而兩邊的 AcceptsProperty 都是比族。
        if (source.IsPropertyNode)
        {
            if (source.Property == null) return HGPortConnectionResult.MissingBinding;
            return input.AcceptsProperty(source.Property)
                ? HGPortConnectionResult.Allowed
                : HGPortConnectionResult.IncompatibleFamily;
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
        if (row?.InputSlot is PropertySlotBase)
            return HGPortConnection.CheckSourceAcceptance(source, row.InputSlot) == HGPortConnectionResult.Allowed;
        var input = PortFor(row);
        return HGPortConnection.CheckInputSource(input, source, graphGeneration)
            == HGPortConnectionResult.Allowed;
    }

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

    internal HGNodeView NodeOfId(string nodeId)
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
            if (port.Presentation.HitRect.Contains(graphPoint) && IsPortOnTop(port, graphPoint)) return port;
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
            if (port.Presentation.HitRect.Contains(graphPoint) && IsPortOnTop(port, graphPoint)) return port;
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
            // 清單標題的新增接點只收拉線；資產／Token 直接落在列上的路徑要的是有欄位的列。
            if (port.Binding is IHGListAppendBinding) continue;
            if (!row.ScreenRect.Contains(graphPoint)) continue;
            owner = OwnerNodeOfPort(port);
            return row;
        }
        return null;
    }

    /// <summary>點線剪斷的命中：與繪製用同一份折線路徑比距離，畫在哪裡就點得到哪裡。</summary>
    private HGLink LinkAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        RebuildFoldFan();
        foreach (var link in graph.Links)
        {
            if (!IsLinkVisible(link) || link.InputPort == null || link.OutputPort == null) continue;
            BuildLinkPathOf(link, linkPath);
            for (int i = 1; i < linkPath.Count; i++)
                if (PointToSegmentSqrDistance(graphPoint, linkPath[i - 1], linkPath[i]) < 36f) return link;
        }
        return null;
    }

    /// <summary>
    /// 接點朝外的水平方向：位在節點左半邊朝左（-1），右半邊朝右（+1）。
    /// 不看輸入／輸出：寫入 Property 的欄位是輸出卻在右緣，Property 節點的寫入接點是輸入卻在左緣。
    /// </summary>
    private float PortDirection(HGPort port)
    {
        var owner = OwnerNodeOfPort(port);
        if (owner == null) return port != null && port.IsOutput ? -1f : 1f;
        return port.Presentation.Position.x < owner.Pos.x + owner.Width * 0.5f ? -1f : 1f;
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

        if (linkPort.IsOutput && linkPort.Source is not HGPropertyWriteSource)
        {
            ShowNotification(new GUIContent("請拖到相容的參數接點"));
            return;
        }

        // 放在群組標題列的代表接點上＝在群組內建立，和拉到空白處同一條路徑，只是位置與歸屬不同。
        GraphNodeGroup joinGroup = NodeGroupHeaderPortAt(graphMouse);
        Vector2 createPos = joinGroup != null ? NodeGroupSpawnPosition(joinGroup) : graphMouse;
        if (joinGroup == null && NodeAt(graphMouse) != null)
        {
            ShowNotification(new GUIContent("請拖到相容的節點、群組標題列或畫布空白處"));
            return;
        }

        var slot = (linkPort.Source as HGPropertyWriteSource)?.Slot ?? linkPort.InputSlot;
        if (slot == null) return;
        if (slot is GraphPropertyInputSlot)
        {
            ShowNotification(new GUIContent("請連到 Action 的寫入 OutputSlot"));
            return;
        }
        var append = linkPort.Binding as IHGListAppendBinding;
        string createdId = null;
        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            // 從清單新增接點拉到空白處＝新增一項並接上一顆空節點；取消或落在不相容處不會走到這裡，不留空項。
            if (append != null && !append.Commit()) throw new InvalidOperationException("這個清單無法新增項目。");
            GraphNode carrier = NewSource(slot);
            if (slot is PropertySlotBase propertySlot)
            {
                // 從寫入 Slot 拉到空白處就是建立新的普通 Property：定義一建立，
                // 這顆 Property 節點的 Header Output 便可立即作為後續讀取來源。
                GraphProperty property = model.CreateLocalProperty(propertySlot.FamilyType, out string propertyError);
                if (property == null) throw new InvalidOperationException(propertyError ?? "無法建立一般 Property。");
                carrier.SetLocalProperty(property);
            }
            else if ((slot as FormulaSlotBase)?.CreateDefaultBody() is GraphNodeContent body) carrier.SetBody(body);
            carrier.Pos = SnapToGrid(createPos);
            carrier.EnsureId();
            createdId = carrier.Id;
        }, out var error))
        {
            ShowNotification(new GUIContent(error));
            return;
        }
        Invalidate();
        if (joinGroup != null && createdId != null) JoinNodeGroupAndReveal(createdId, joinGroup);
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
        if (output.Source is HGPropertyWriteSource writer)
        {
            var target = OwnerNodeOfPort(input);
            if (target?.Carrier == null || !target.IsPropertyNode) return PortCommandResult.Rejected;
            if (ReferenceEquals(writer.Slot.Node, target.Carrier)
                && writer.Slot.AcceptsProperty(target.Property)) return PortCommandResult.NoChange;
            var appendWriter = output.Binding as IHGListAppendBinding;
            if (!TryMutateContent(() =>
            {
                PreserveVisibleNodePositions();
                // PropertySlot 清單的新增接點：先把預備的寫入欄位放進清單，再照一般寫入接線。
                if (appendWriter != null && !appendWriter.Commit()) throw new InvalidOperationException("這個清單無法新增項目。");
                if (!target.Carrier.IsProtoProperty && target.Property?.FamilyType != writer.Slot.FamilyType)
                {
                    var local = model.CreateLocalProperty(writer.Slot.FamilyType, out string typeError);
                    if (local == null) throw new InvalidOperationException(typeError);
                    target.Carrier.SetLocalProperty(local);
                    BreakIncompatiblePropertyLinks(target.Carrier, local);
                }
                AttachSource(writer.Slot, target.Carrier);
            }, out string writeError))
            {
                ShowNotification(new GUIContent(writeError));
                return PortCommandResult.Rejected;
            }
            Invalidate();
            return PortCommandResult.Changed;
        }
        if (input.InputSlot == null || output.Source?.OutputNode == null) return PortCommandResult.Rejected;
        if (!graph.PrimaryInputs.ContainsKey(input.InputSlot) || !graph.ByCarrier.ContainsKey(output.Source.OutputNode))
            return PortCommandResult.Rejected;
        if (ReferenceEquals(input.InputSlot.Node, output.Source.OutputNode)) return PortCommandResult.NoChange;

        var append = input.Binding as IHGListAppendBinding;
        if (!TryMutateContent(() =>
        {
            PreserveVisibleNodePositions();
            // 清單新增接點：先把預備元素放進清單，再照一般欄位接上；兩件事在同一個復原步驟裡。
            if (append != null && !append.Commit()) throw new InvalidOperationException("這個清單無法新增項目。");
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
    private PortCommandResult CutLink(HGLink link) => CutLink(link?.ParentRow?.InputSlot);

    private PortCommandResult CutLink(GraphSlotBase slot)
    {
        if (slot is GraphPropertyInputSlot)
        {
            var writers = new List<GraphSlotBase>();
            foreach (var link in graph.Links)
                if (ReferenceEquals(link.InputPort?.InputSlot, slot) && link.ParentRow?.InputSlot is PropertySlotBase)
                    writers.Add(link.ParentRow.InputSlot);
            if (writers.Count == 0) return PortCommandResult.NoChange;
            if (!TryMutateContent(() =>
            {
                PreserveVisibleNodePositions();
                foreach (var writer in writers) AttachSource(writer, null);
            }, out var message))
            {
                ShowNotification(new GUIContent(message));
                return PortCommandResult.Rejected;
            }
            Invalidate();
            return PortCommandResult.Changed;
        }
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
