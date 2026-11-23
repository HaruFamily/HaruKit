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
    // Stable aliases survive mutations: "@<node_id>" for a source, "@<node_id>.<field>" / "@<node_id>.<field>[i]" for a
    // slot declared directly on an inline node, "@use:<node_id>" for the single read slot that holds a node.
    private readonly Dictionary<string, string> stableSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> stableSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> stableBySlotKey = new(StringComparer.Ordinal);

    internal string ResolveStableSlot(string alias) => alias != null && stableSlots.TryGetValue(alias, out string key) ? key : null;
    internal string ResolveStableSource(string alias) => alias != null && stableSources.TryGetValue(alias, out string key) ? key : null;
    internal string StableSlotKey(string key) => key != null && stableBySlotKey.TryGetValue(key, out string alias) ? alias : null;
    internal string StableSourceKey(string key)
        => key != null && Nodes.TryGetValue(key, out var node) && !string.IsNullOrEmpty(node.Id)
            && stableSources.TryGetValue("@" + node.Id, out string owner) && owner == key ? "@" + node.Id : null;

    private void BuildStableKeys()
    {
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in Nodes.Values)
            if (!string.IsNullOrEmpty(node.Id)) idCounts[node.Id] = idCounts.TryGetValue(node.Id, out int count) ? count + 1 : 1;
        var keyBySlot = new Dictionary<object, string>(ReferenceComparer.Instance);
        foreach (var pair in Slots) keyBySlot[pair.Value] = pair.Key;
        void Alias(string alias, GraphSlotBase slot)
        {
            if (slot == null || !keyBySlot.TryGetValue(slot, out string key) || stableSlots.ContainsKey(alias)) return;
            stableSlots.Add(alias, key);
            if (!stableBySlotKey.ContainsKey(key)) stableBySlotKey.Add(key, alias);
        }
        foreach (var pair in Nodes)
        {
            GraphNode node = pair.Value;
            if (string.IsNullOrEmpty(node.Id) || idCounts[node.Id] != 1) continue;
            stableSources["@" + node.Id] = pair.Key;
            object body = node.Kind == NodeKind.Inline ? node.BodyObject : null;
            if (body == null) continue;
            foreach (var field in HGReflect.Fields(body.GetType()))
            {
                if (field.IsStatic || field.IsNotSerialized) continue;
                object value = field.GetValue(body);
                if (value is GraphSlotBase direct) Alias("@" + node.Id + "." + field.Name, direct);
                else if (value is System.Collections.IList list)
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] is GraphSlotBase item) Alias("@" + node.Id + "." + field.Name + "[" + i + "]", item);
            }
        }
        // "@use:<node_id>": the one read slot that currently holds this node (for example its action item).
        var readers = new Dictionary<object, List<GraphSlotBase>>(ReferenceComparer.Instance);
        foreach (var slot in Slots.Values)
        {
            if (slot is PropertySlotBase || slot.Node == null) continue;
            if (!readers.TryGetValue(slot.Node, out var users)) readers.Add(slot.Node, users = new List<GraphSlotBase>());
            users.Add(slot);
        }
        foreach (var pair in Nodes)
        {
            GraphNode node = pair.Value;
            if (string.IsNullOrEmpty(node.Id) || idCounts[node.Id] != 1) continue;
            if (readers.TryGetValue(node, out var users) && users.Count == 1) Alias("@use:" + node.Id, users[0]);
        }
    }

    internal HaruGraphDraftConnections(HGDocumentSession<IGraphDocument> session)
    {
        Registry = session.CreatePortRegistry();
        var declarations = new HashSet<GraphSlotBase>(((session.Document as IPropertyOwner)?.Properties ?? new List<GraphProperty>())
            .Where(property => property?.Slot != null).Select(property => (GraphSlotBase)property.Slot));
        foreach (object value in session.InspectObjects())
        {
            if (value is GraphNode node) Nodes.Add("n" + Nodes.Count, node);
            if (value is GraphSlotBase slot && (slot is ActionSlotBase || slot is FormulaSlotBase || slot is PropertySlotBase)
                && !declarations.Contains(slot))
                Slots.Add("s" + Slots.Count, slot);
        }
        foreach (var pair in Nodes)
        {
            GraphNode node = pair.Value;
            var source = SourceFor(node);
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
        BuildStableKeys();
    }

    /// <summary>Wraps one carrier with the acceptance rule and cycle root shared by every draft connection check.</summary>
    internal static IHGPortSource SourceFor(GraphNode node)
    {
        object cycleRoot = node.Kind == NodeKind.Property ? null : node.Kind == NodeKind.Token ? node.Token?.Slot : node;
        return new HGDelegatePortSource(node, cycleRoot, input => Accepts(input, node));
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
        if (Slots[slotKey] is PropertySlotBase writer)
            return Accepts(writer, Nodes[nodeKey]) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.IncompatibleFamily;
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
