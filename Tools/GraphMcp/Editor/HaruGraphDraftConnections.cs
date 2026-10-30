namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using UnityEngine;

/// <summary>Generation-local endpoints for serialized document data, independent of canvas visibility.</summary>
internal sealed class HaruGraphDraftConnections
{
    internal readonly HGPortRegistry Registry;
    internal readonly Dictionary<string, GraphSlotBase> Slots = new();
    internal readonly Dictionary<string, GraphNode> Nodes = new();
    internal readonly Dictionary<string, IHGPortSource> Sources = new();

    internal HaruGraphDraftConnections(HGDocumentSession<IGraphDocument> session)
    {
        Registry = session.CreatePortRegistry();
        var declarations = new HashSet<GraphSlotBase>(((session.Document as IPropertyOwner)?.Properties ?? new List<GraphProperty>())
            .Where(property => property?.Slot != null).Select(property => (GraphSlotBase)property.Slot));
        foreach (object value in session.InspectObjects())
        {
            if (value is GraphNode node) Nodes.Add("n" + Nodes.Count, node);
            if (value is GraphSlotBase slot && (slot is ActionSlotBase || slot is FormulaSlotBase || slot is PropertySlotBase)
                && slot is not GraphPropertyInputSlot && !declarations.Contains(slot))
                Slots.Add("s" + Slots.Count, slot);
        }
        foreach (var pair in Nodes)
        {
            GraphNode node = pair.Value;
            object cycleRoot = node.Kind == NodeKind.Property ? null : node.Kind == NodeKind.Token ? node.Token?.Slot : node;
            var source = new HGDelegatePortSource(node, cycleRoot, input => Accepts(input, node));
            Sources.Add(pair.Key, source);
            Registry.AddOutput(OutputKey(pair.Key), source, new HGDelegatePortPolicy(() => true), Presentation(node));
        }
        var disabledParameters = new HashSet<GraphSlotBase>(session.InspectObjects().OfType<NamedFormulaSlot>()
            .Where(binding => !binding.OverrideEnabled && binding.Slot != null).Select(binding => (GraphSlotBase)binding.Slot));
        foreach (var pair in Slots)
        {
            GraphSlotBase slot = pair.Value;
            var policy = new HGDelegatePortPolicy(() => !disabledParameters.Contains(slot), checkAcceptance: source =>
            {
                var acceptance = HGPortConnection.CheckSourceAcceptance(source, slot);
                if (acceptance != HGPortConnectionResult.Allowed) return acceptance;
                if (source.CycleRoot != null)
                    foreach (var child in HGModel.WalkSlots(source.CycleRoot, new HashSet<object>(ReferenceComparer.Instance)))
                        if (ReferenceEquals(child, slot)) return HGPortConnectionResult.WouldCreateCycle;
                return HGPortConnectionResult.Allowed;
            });
            Registry.AddInput(InputKey(pair.Key), slot, policy, Presentation(slot));
        }
    }

    private static bool Accepts(GraphSlotBase slot, GraphNode node)
    {
        if (slot is PropertySlotBase) return node.Kind == NodeKind.Property && node.Property != null && slot.AcceptsProperty(node.Property);
        return node.Kind switch
        {
            NodeKind.Inline => node.BodyObject != null && slot.AcceptsBody(node.BodyObject),
            NodeKind.Token => node.Token != null && slot.AcceptsToken(node.Token),
            NodeKind.Property => node.Property != null && slot.AcceptsProperty(node.Property),
            NodeKind.Asset => node.AssetObject != null && slot.AcceptsAsset(node.AssetObject),
            _ => false,
        };
    }

    internal HGPortConnectionResult Check(string slotKey, string nodeKey)
    {
        if (slotKey == null || nodeKey == null || !Slots.ContainsKey(slotKey) || !Nodes.ContainsKey(nodeKey))
            return HGPortConnectionResult.InvalidEndpoint;
        Registry.TryGet(InputKey(slotKey), out var input);
        Registry.TryGet(OutputKey(nodeKey), out var output);
        return HGPortConnection.Check(input, output, Registry.Generation);
    }

    internal string SourceKey(GraphSlotBase slot) => Nodes.FirstOrDefault(pair => ReferenceEquals(pair.Value, slot.Node)).Key;
    internal static HGPortKey InputKey(string key) => new(key, "/slot", HGPortRole.Input);
    internal static HGPortKey OutputKey(string key) => new(key, "/node", HGPortRole.Output);
    private static HGDelegatePortPresentation Presentation(object target)
        => new(target, () => Vector2.zero, () => new Rect(), () => true, () => false);
}
}
