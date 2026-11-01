namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class HGNestedSlotTests
{
    [Test]
    public void CompositeListItemCollapseHidesNestedNodesAndRestoresThemOnExpansion()
    {
        var formula = new GraphNode(new Formula()); formula.EnsureId();
        var entry = new CompositeEntry(); entry.Condition.SetNode(formula);
        var action = new CompositeAction(); action.Cases.Add(entry);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var root = new ActionSlot(); root.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(root);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        var state = new Dictionary<string, bool>();
        HGGraphView Build() => HGGraph.Build(model, roots, null, "root", "Root", state, nestSlots: true);
        var view = Build();
        var item = HGGraph.AllRows(view.ByCarrier[carrier].Rows).Single(row => row.IsItem && row.Kind == HGRowKind.Foldout);
        Assert.That(item.Collapsed, Is.False);
        string key = HGGraph.CollapseKey(carrier.Id, item);
        state[key] = true;
        view = Build();
        Assert.That(view.ByCarrier[formula].Hidden, Is.True);
        Assert.That(view.ByCarrier[formula].NestedParent, Is.SameAs(view.ByCarrier[carrier]));
        state[key] = false;
        view = Build();
        Assert.That(view.ByCarrier[formula].Hidden, Is.False);
        Assert.That(entry.Condition.Node, Is.SameAs(formula));
    }

    [Serializable] private sealed class CompositeAction : Action
    {
        public List<CompositeEntry> Cases = new();
    }

    [Serializable] private sealed class CompositeEntry
    {
        public ValueSlot Condition = new();
        public List<ActionSlot> Actions = new();
    }

    [TestCase(GraphNodeMode.Composition)]
    [TestCase(GraphNodeMode.Connection)]
    public void EmptyNodesNestInRootListsAndValueSlotsOnlyInCompositionMode(GraphNodeMode mode)
    {
        var emptyAction = new GraphNode(); emptyAction.EnsureId(); emptyAction.Mode = mode;
        var emptyValue = new GraphNode(); emptyValue.EnsureId(); emptyValue.Mode = mode;
        var action = new Action(); action.Value.SetNode(emptyValue);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var first = new ActionSlot(); first.SetNode(emptyAction);
        var second = new ActionSlot(); second.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(first); document.Root.Actions.Add(second);
        string before = JsonUtility.ToJson(document);
        var view = HGGraph.Build(HGModel.CreateDetachedView(null, document, null),
            new object[] { document.Root }, null, "empty", "Empty", nestSlots: true);
        foreach (var empty in new[] { emptyAction, emptyValue })
        {
            var node = view.ByCarrier[empty];
            Assert.That(node.IsPlaceholder, Is.True);
            if (mode == GraphNodeMode.Composition)
            {
                Assert.That(node.NestedParent, Is.Not.Null);
                Assert.That(node.NestedSlot.InputSlot.Node, Is.SameAs(empty));
                Assert.That(node.NestedSlot.NestedNode, Is.SameAs(node));
            }
            else Assert.That(node.NestedParent, Is.Null);
        }
        Assert.That(action.Value.Node, Is.SameAs(emptyValue));
        Assert.That(document.Root.Actions[0], Is.SameAs(first));
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SingleSlotNestingUsesOneViewAndKeepsStoredPositionsAndSharedIdentity(bool shared)
    {
        var formula = new GraphNode(new Formula()); formula.EnsureId(); formula.Pos = new Vector2(900, 300);
        var action = new Action(); action.Value.SetNode(formula);
        if (shared) action.Other.SetNode(formula);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var slot = new ActionSlot(); slot.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(slot);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        HGGraph.Build(model, roots, null, "root", "Root");
        string before = JsonUtility.ToJson(document);
        var nested = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        Assert.That(nested.Diagnostics, Is.Empty);
        Assert.That(nested.Nodes.Count(node => ReferenceEquals(node.Carrier, formula)), Is.EqualTo(1));
        var child = nested.ByCarrier[formula];
        if (shared)
        {
            Assert.That(child.NestedParent, Is.Null);
            Assert.That(child.Pos, Is.EqualTo(formula.Pos));
            Assert.That(action.Value.Node, Is.SameAs(action.Other.Node));
        }
        else
        {
            Assert.That(child.NestedParent, Is.SameAs(nested.ByCarrier[carrier]));
            Assert.That(child.NestedSlot.NestedNode, Is.SameAs(child));
            Assert.That(child.Pos.x - child.NestedParent.Pos.x,
                Is.GreaterThanOrEqualTo(HGGraph.LabelWidthOf(HGGraph.NodeWidth, 0, 0)),
                "Slot Label must remain outside the nested Node.");
            Assert.That(child.NestedParent.Rect.Contains(child.Rect.min), Is.True);
            Assert.That(child.NestedParent.Width, Is.EqualTo(HGGraph.NodeWidth));
            Assert.That(child.Rect.xMax, Is.GreaterThan(child.NestedParent.Rect.xMax));
            Assert.That(child.Rect.yMax, Is.LessThan(child.NestedParent.Rect.yMax));
            Assert.That(child.Pos, Is.Not.EqualTo(formula.Pos));
            var start = child.Pos;
            var root = child.NestedParent;
            while (root.NestedParent != null) root = root.NestedParent;
            root.Pos += new Vector2(100, 40);
            HGGraph.PositionNestedSlots(nested);
            Assert.That(child.Pos, Is.EqualTo(start + new Vector2(100, 40)));
        }
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
        Assert.That(formula.Pos, Is.EqualTo(new Vector2(900, 300)));
    }

    [Test]
    public void ActionListsAndFormulaChainsShareOneNestedTreeWithoutChangingSlotIdentity()
    {
        var leaf = new GraphNode(new Formula()); leaf.EnsureId();
        var expression = new Formula(); expression.Input.SetNode(leaf);
        var expressionNode = new GraphNode(expression); expressionNode.EnsureId();
        var action = new Action(); action.Value.SetNode(expressionNode);
        var actionNode = new GraphNode(action); actionNode.EnsureId();
        var parent = new Action();
        var condition = new GraphNode(new Formula()); condition.EnsureId(); parent.Value.SetNode(condition);
        var step = new ActionSlot(); step.SetNode(actionNode); parent.Steps.Add(step);
        var parentNode = new GraphNode(parent); parentNode.EnsureId();
        var first = new ActionSlot(); first.SetNode(parentNode);
        var sibling = new GraphNode(new Action()); sibling.EnsureId();
        var second = new ActionSlot(); second.SetNode(sibling);
        var document = new Document(); document.Root.Actions.Add(first); document.Root.Actions.Add(second);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        HGGraph.Build(model, roots, null, "root", "Root");
        string before = JsonUtility.ToJson(document);
        var view = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(view.ByCarrier[actionNode].NestedParent, Is.SameAs(view.ByCarrier[parentNode]));
        Assert.That(view.ByCarrier[condition].Rect.xMin, Is.EqualTo(view.ByCarrier[actionNode].Rect.xMin),
            "Single slots and action lists must share the same content column.");
        Assert.That(view.ByCarrier[condition].Rect.xMax, Is.EqualTo(view.ByCarrier[actionNode].Rect.xMax),
            "Sibling Nodes retain their own declared width.");
        Assert.That(view.ByCarrier[expressionNode].NestedParent, Is.SameAs(view.ByCarrier[actionNode]));
        Assert.That(view.ByCarrier[leaf].NestedParent, Is.SameAs(view.ByCarrier[expressionNode]));
        foreach (var node in view.Nodes.Where(node => node.NestedParent != null))
        {
            Assert.That(node.NestedParent.Rect.Contains(node.Rect.min), Is.True);
            Assert.That(node.NestedParent.Width, Is.EqualTo(HGGraph.NodeWidth));
            Assert.That(node.Width, Is.EqualTo(HGGraph.NodeWidth));
            Assert.That(node.Rect.xMax, Is.GreaterThan(node.NestedParent.Rect.xMax));
            Assert.That(node.Rect.yMax, Is.LessThan(node.NestedParent.Rect.yMax));
            Assert.That(view.Nodes.IndexOf(node), Is.GreaterThan(view.Nodes.IndexOf(node.NestedParent)));
        }
        Assert.That(view.ByCarrier[sibling].Rect.yMin, Is.GreaterThan(view.ByCarrier[parentNode].Rect.yMax));
        Assert.That(parent.Steps[0], Is.SameAs(step));
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CollapsingActionListHidesOnlyItsNestedSubtreeAndPreservesOwnership(bool shared)
    {
        var value = new GraphNode(new Formula()); value.EnsureId();
        var action = new Action(); action.Value.SetNode(value);
        var child = new GraphNode(action); child.EnsureId();
        var parent = new Action();
        var step = new ActionSlot(); step.SetNode(child); parent.Steps.Add(step);
        var parentNode = new GraphNode(parent); parentNode.EnsureId();
        var root = new ActionSlot(); root.SetNode(parentNode);
        var document = new Document(); document.Root.Actions.Add(root);
        if (shared)
        {
            var other = new ActionSlot(); other.SetNode(child); document.Root.Actions.Add(other);
        }
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        var expanded = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        var list = HGGraph.AllRows(expanded.ByCarrier[parentNode].Rows).Single(row => row.ActionSequence);
        string before = JsonUtility.ToJson(document);
        var folded = new Dictionary<string, bool> { [HGGraph.CollapseKey(parentNode.Id, list)] = true };
        var collapsed = HGGraph.Build(model, roots, null, "root", "Root", listCollapse: folded, nestSlots: true);
        Assert.That(collapsed.Diagnostics, Is.Empty);
        if (shared) Assert.That(collapsed.ByCarrier[child].NestedParent, Is.Null);
        else Assert.That(collapsed.ByCarrier[child].NestedParent, Is.SameAs(collapsed.ByCarrier[parentNode]));
        Assert.That(collapsed.ByCarrier[child].Hidden, Is.EqualTo(!shared));
        Assert.That(collapsed.ByCarrier[value].Hidden, Is.EqualTo(!shared));
        var reopened = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        Assert.That(reopened.ByCarrier[child].Hidden, Is.False);
        Assert.That(reopened.ByCarrier[value].Hidden, Is.False);
        Assert.That(parent.Steps[0], Is.SameAs(step));
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HeaderCollapsePersistsAndKeepsWidthReferencesAndExternalSources(bool collapseParent)
    {
        var token = new GraphToken("external", new ValueSlot());
        var tokenNode = new GraphNode(); tokenNode.SetToken(token); tokenNode.EnsureId();
        var formula = new Formula(); formula.Input.SetNode(tokenNode);
        var formulaNode = new GraphNode(formula); formulaNode.EnsureId();
        var action = new Action(); action.Value.SetNode(formulaNode);
        var actionNode = new GraphNode(action); actionNode.EnsureId();
        var root = new ActionSlot(); root.SetNode(actionNode);
        var document = new Document(); document.Root.Actions.Add(root); document.Tokens.Add(token);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        var expanded = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        var target = collapseParent ? actionNode : formulaNode;
        float width = expanded.ByCarrier[target].Width;
        string key = target.Id + "#@node";
        document.Layout.SetFolded(key, true);
        string stored = JsonUtility.ToJson(document);
        var restored = JsonUtility.FromJson<Document>(stored);
        var reopened = HGGraph.Build(HGModel.CreateDetachedView(null, restored, null),
            new object[] { restored.Root }, null, "root", "Root", nestSlots: true);
        var collapsed = reopened.Nodes.Single(node => node.Id == target.Id);
        Assert.That(reopened.Diagnostics, Is.Empty);
        Assert.That(collapsed.Collapsed, Is.True);
        Assert.That(collapsed.Height, Is.EqualTo(HGGraph.HeaderHeight));
        Assert.That(collapsed.Width, Is.EqualTo(width));
        Assert.That(reopened.Nodes.Single(node => node.Id == tokenNode.Id).Hidden, Is.True,
            "A nested Token follows its collapsed ancestors.");
        Assert.That(reopened.Links.Count, Is.EqualTo(expanded.Links.Count));
        if (collapseParent) Assert.That(reopened.Nodes.Single(node => node.Id == formulaNode.Id).Hidden, Is.True);
        Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(stored));
        restored.Layout.SetFolded(key, false);
        var shown = HGGraph.Build(HGModel.CreateDetachedView(null, restored, null),
            new object[] { restored.Root }, null, "root", "Root", nestSlots: true);
        Assert.That(shown.Nodes.Single(node => node.Id == target.Id).Collapsed, Is.False);
        Assert.That(shown.Nodes.Single(node => node.Id == target.Id).Height, Is.GreaterThan(HGGraph.HeaderHeight));
        Assert.That(shown.Nodes.Single(node => node.Id == formulaNode.Id).Hidden, Is.False);
    }

    [Test]
    public void FormulaSlotListSupportsConstantsAndNestedNodesWithoutChangingStoredValues()
    {
        var action = new Action();
        var constant = new ValueSlot { DefaultObject = 5 };
        var source = new GraphNode(new Formula()); source.EnsureId();
        var expression = new ValueSlot { DefaultObject = 10 }; expression.SetNode(source);
        action.Values.Add(constant); action.Values.Add(expression);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var root = new ActionSlot(); root.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(root);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        string before = JsonUtility.ToJson(document);
        var view = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        var row = HGGraph.AllRows(view.ByCarrier[carrier].Rows)
            .Single(item => item.Items is HGListItemSource list && ReferenceEquals(list.List, action.Values));
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(row.NodeList, Is.True);
        Assert.That(row.ActionSequence, Is.False);
        Assert.That(view.ByCarrier[source].NestedParent, Is.SameAs(view.ByCarrier[carrier]));
        Assert.That(constant.Node, Is.Null);
        Assert.That(constant.DefaultObject, Is.EqualTo(5));
        Assert.That(expression.DefaultObject, Is.EqualTo(10));
        Assert.That(action.Values[1], Is.SameAs(expression));
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [Test]
    public void ExcessiveNestingReturnsToExternalNodesAtTheDepthBoundary()
    {
        GraphNode chain = null;
        for (int i = 0; i < HGGraph.MaxNestedDepth + 5; i++)
        {
            var body = new Formula(); body.Input.SetNode(chain);
            chain = new GraphNode(body); chain.EnsureId();
        }
        var action = new Action(); action.Value.SetNode(chain);
        var node = new GraphNode(action); node.EnsureId();
        var slot = new ActionSlot(); slot.SetNode(node);
        var document = new Document(); document.Root.Actions.Add(slot);
        var model = HGModel.CreateDetachedView(null, document, null);
        var roots = new object[] { document.Root };
        HGGraph.Build(model, roots, null, "root", "Root");
        string before = JsonUtility.ToJson(document);
        var view = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(view.Nodes.Count(item => item.NestedParent == null), Is.GreaterThan(1));
        foreach (var item in view.Nodes)
        {
            int depth = 0;
            for (var parent = item.NestedParent; parent != null; parent = parent.NestedParent) depth++;
            Assert.That(depth, Is.LessThanOrEqualTo(HGGraph.MaxNestedDepth));
        }
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [TestCase(GraphNodeMode.Composition)]
    [TestCase(GraphNodeMode.Connection)]
    public void NodeModePersistsAndControlsNestingWithoutChangingSources(GraphNodeMode mode)
    {
        var source = new GraphNode(new Formula()); source.EnsureId();
        Assert.That(source.Mode, Is.EqualTo(GraphNodeMode.Composition));
        source.Mode = mode;
        var action = new Action(); action.Value.SetNode(source);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var root = new ActionSlot(); root.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(root);
        string json = JsonUtility.ToJson(document);
        var restored = JsonUtility.FromJson<Document>(json);
        var restoredAction = (Action)restored.Root.Actions[0].Node.BodyObject;
        Assert.That(restoredAction.Value.Node.Mode, Is.EqualTo(mode));
        var view = HGGraph.Build(HGModel.CreateDetachedView(null, restored, null),
            new object[] { restored.Root }, null, "mode", "Mode", nestSlots: true);
        var node = view.ByCarrier[restoredAction.Value.Node];
        if (mode == GraphNodeMode.Connection) Assert.That(node.NestedParent, Is.Null);
        else Assert.That(node.NestedParent, Is.Not.Null);
        Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(json));
    }

    [Test]
    public void OnlyConnectionModeAllowsAnotherSlotToReuseTheSameNode()
    {
        var property = new GraphProperty("Value", new ValueSlot());
        var source = new GraphNode(); source.SetProperty(property); source.EnsureId();
        var first = new ValueSlot(); first.SetNode(source);
        var second = new ValueSlot();
        var slots = new GraphSlotBase[] { first, second };
        Assert.That(HGModel.CanReferenceNode(source, first, slots), Is.True);
        Assert.That(HGModel.CanReferenceNode(source, second, slots), Is.False);
        source.Mode = GraphNodeMode.Connection;
        Assert.That(HGModel.CanReferenceNode(source, second, slots), Is.True);
        Assert.That(HGModel.PropertyReferenceForSlot(second, source, slots), Is.SameAs(source),
            "Connection mode must share the carrier instead of silently cloning Property references.");
        second.SetNode(source);
        Assert.That(HGModel.NodeUsers(source, slots).Count, Is.EqualTo(2));
        Assert.That(HGModel.SeparatePropertyReferences(slots), Is.Zero,
            "Explicitly connected Property nodes must remain shared.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DetachedFocusCountsItsOwnSlotsAndPreservesActuallySharedSources(bool shared)
    {
        var document = new Document();
        var token = new GraphToken("Local", new ValueSlot());
        var source = new GraphNode(); source.SetToken(token); source.EnsureId();
        var formula = new Formula(); formula.Input.SetNode(source);
        var carrier = new GraphNode(formula); carrier.EnsureId();
        var action = new Action(); action.Value.SetNode(carrier);
        if (shared) action.Other.SetNode(source);
        var actionCarrier = new GraphNode(action); actionCarrier.EnsureId();
        var root = new ActionSlot(); root.SetNode(actionCarrier);
        var model = HGModel.CreateDetachedView(null, document, null);
        string before = JsonUtility.ToJson(document);
        var view = HGGraph.Build(model, new object[] { root }, null, "detached", "Detached", nestSlots: true);
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(view.ByCarrier[carrier].NestedParent, Is.Not.Null,
            "Focus roots outside model.Data must still nest.");
        if (shared) Assert.That(view.ByCarrier[source].NestedParent, Is.Null);
        else Assert.That(view.ByCarrier[source].NestedParent, Is.SameAs(view.ByCarrier[carrier]));
        Assert.That(formula.Input.Node, Is.SameAs(source));
        Assert.That(root.Node, Is.SameAs(actionCarrier));
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    [Test]
    public void PropertyReferenceSeparationPreservesDefinitionAndLayoutAndIsIdempotent()
    {
        var property = new GraphProperty("Shared", new ValueSlot { DefaultObject = 9 });
        var source = new GraphNode(); source.SetProperty(property); source.EnsureId();
        source.Pos = new Vector2(80, 120); source.Note = "reference"; source.Disabled = true;
        var first = new ValueSlot(); first.SetNode(source);
        var second = new ValueSlot(); second.SetNode(source);
        var third = new ValueSlot(); third.SetNode(source);
        var slots = new GraphSlotBase[] { first, second, third, second };
        string definition = JsonUtility.ToJson(property);
        Assert.That(HGModel.SeparatePropertyReferences(slots), Is.EqualTo(2));
        Assert.That(first.Node, Is.SameAs(source));
        Assert.That(second.Node, Is.Not.SameAs(source).And.Not.SameAs(third.Node));
        Assert.That(second.Node.Property, Is.SameAs(property));
        Assert.That(third.Node.Property, Is.SameAs(property));
        Assert.That(second.Node.Id, Is.Not.EqualTo(source.Id).And.Not.EqualTo(third.Node.Id));
        Assert.That(second.Node.Pos, Is.EqualTo(source.Pos));
        Assert.That(second.Node.Note, Is.EqualTo(source.Note));
        Assert.That(second.Node.Disabled, Is.True);
        Assert.That(HGModel.SeparatePropertyReferences(slots), Is.Zero);
        Assert.That(JsonUtility.ToJson(property), Is.EqualTo(definition));
    }

    [Test]
    public void ConnectingPropertyCopiesOnlyWhenAnotherSlotUsesTheCarrier()
    {
        var property = new GraphProperty("Shared", new ValueSlot());
        var source = new GraphNode(); source.SetProperty(property); source.EnsureId();
        var first = new ValueSlot(); first.SetNode(source);
        var second = new ValueSlot();
        var slots = new GraphSlotBase[] { first, second };
        Assert.That(HGModel.PropertyReferenceForSlot(first, source, slots), Is.SameAs(source));
        var next = HGModel.PropertyReferenceForSlot(second, source, slots);
        Assert.That(next, Is.Not.SameAs(source));
        Assert.That(next.Property, Is.SameAs(property));
        Assert.That(first.Node, Is.SameAs(source));
        var inline = new GraphNode(new Formula());
        Assert.That(HGModel.PropertyReferenceForSlot(second, inline, slots), Is.SameAs(inline));
    }

    [TestCase(NodeKind.Token, false)]
    [TestCase(NodeKind.Token, true)]
    [TestCase(NodeKind.Asset, false)]
    [TestCase(NodeKind.Asset, true)]
    [TestCase(NodeKind.Property, false)]
    [TestCase(NodeKind.Property, true)]
    public void ReferenceNodesNestWithoutCopyingSourcesAndSharedCarriersStayExternal(NodeKind kind, bool shared)
    {
        var document = new Document();
        var source = new GraphNode(); source.EnsureId();
        ReferenceAsset asset = null;
        var token = new GraphToken("Token", new ValueSlot());
        var property = new GraphProperty("Property", new ValueSlot());
        try
        {
            if (kind == NodeKind.Token) { source.SetToken(token); document.Tokens.Add(token); }
            else if (kind == NodeKind.Property) source.SetProperty(property);
            else { asset = ScriptableObject.CreateInstance<ReferenceAsset>(); source.SetAsset(asset); }
            var action = new Action(); action.Value.SetNode(source);
            if (shared) action.Other.SetNode(source);
            var carrier = new GraphNode(action); carrier.EnsureId();
            var root = new ActionSlot(); root.SetNode(carrier); document.Root.Actions.Add(root);
            var model = HGModel.CreateDetachedView(null, document, null);
            var roots = new object[] { document.Root };
            HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
            string before = JsonUtility.ToJson(document);
            var view = HGGraph.Build(model, roots, null, "root", "Root", nestSlots: true);
            var node = view.ByCarrier[source];
            Assert.That(view.Nodes.Count(item => ReferenceEquals(item.Carrier, source)), Is.EqualTo(1));
            if (shared) Assert.That(node.NestedParent, Is.Null);
            else Assert.That(node.NestedParent, Is.SameAs(view.ByCarrier[carrier]));
            Assert.That(source.Kind, Is.EqualTo(kind));
            if (kind == NodeKind.Token) Assert.That(source.Token, Is.SameAs(token));
            if (kind == NodeKind.Property) Assert.That(source.Property, Is.SameAs(property));
            if (kind == NodeKind.Asset) Assert.That(source.AssetObject, Is.SameAs(asset));
            Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
        }
        finally { if (asset != null) UnityEngine.Object.DestroyImmediate(asset); }
    }

    private sealed class ReferenceAsset : ScriptableObject { }

    [Test]
    public void LabelLayoutsStackOnlyNestedContentAndPreserveHiddenCompatibility()
    {
        var action = new LayoutAction();
        var formula = new GraphNode(new Formula()); formula.EnsureId();
        var hidden = new GraphNode(new Formula()); hidden.EnsureId();
        var step = new GraphNode(new Formula()); step.EnsureId();
        action.Stacked.SetNode(formula); action.Hidden.SetNode(hidden);
        var listSlot = new ValueSlot(); listSlot.SetNode(step); action.StackedValues.Add(listSlot);
        var carrier = new GraphNode(action); carrier.EnsureId();
        var root = new ActionSlot(); root.SetNode(carrier);
        var document = new Document(); document.Root.Actions.Add(root);
        var model = HGModel.CreateDetachedView(null, document, null);
        var view = HGGraph.Build(model, new object[] { document.Root }, null, "root", "Root", nestSlots: true);
        var owner = view.ByCarrier[carrier];
        var rows = owner.Rows;
        var stacked = rows.Single(row => row.Field?.Name == nameof(LayoutAction.Stacked));
        var constant = rows.Single(row => row.Field?.Name == nameof(LayoutAction.Constant));
        var hiddenRow = rows.Single(row => row.Field?.Name == nameof(LayoutAction.Hidden));
        var legacy = rows.Single(row => row.Field?.Name == nameof(LayoutAction.Legacy));
        var list = rows.Single(row => row.Field?.Name == nameof(LayoutAction.StackedValues));
        Assert.That(stacked.LabelAbove, Is.True);
        Assert.That(stacked.SocketContentY - stacked.LocalY, Is.EqualTo(HGGraph.RowHeight));
        Assert.That(constant.LabelAbove, Is.False, "Unconnected values keep horizontal layout.");
        Assert.That(hiddenRow.HideLabel, Is.True);
        Assert.That(hiddenRow.SocketContentY, Is.EqualTo(hiddenRow.LocalY));
        Assert.That(legacy.HideLabel, Is.True, "Legacy hide wins over Vertical.");
        Assert.That(list.LabelAbove, Is.True);
        Assert.That(view.ByCarrier[step].Pos.x, Is.GreaterThan(view.ByCarrier[formula].Pos.x),
            "List items reserve the same label column as horizontal single slots.");
        Assert.That(view.ByCarrier[step].Pos.y, Is.GreaterThanOrEqualTo(owner.Pos.y + list.LocalY + HGGraph.RowHeight));
        Assert.That(action.Stacked.Node, Is.SameAs(formula));
    }

    [Serializable] private sealed class LayoutAction : Action
    {
        [HGLabel("Stacked", Layout = HGLabelLayout.Vertical)] public ValueSlot Stacked = new();
        [HGLabel(Layout = HGLabelLayout.Vertical)] public ValueSlot Constant = new();
        [HGLabel(Layout = HGLabelLayout.Hidden)] public ValueSlot Hidden = new();
        [HGLabel(Layout = HGLabelLayout.Hidden)] public ValueSlot Legacy = new();
        [HGLabel("StackedValues", Layout = HGLabelLayout.Vertical)] public List<ValueSlot> StackedValues = new();
    }

    [Serializable] private sealed class Formula : FormulaNodeShape<int, NullPack> { public ValueSlot Input = new(); }
    [Serializable] private class Action : ActionNodeShape
    {
        public ValueSlot Value = new();
        public ValueSlot Other = new();
        public List<ActionSlot> Steps = new();
        public List<ValueSlot> Values = new();
    }

    [Serializable] private sealed class ValueSlot : FormulaSlotBase
    {
        public ValueSlot() { }
        [SerializeReference] private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override Type ResultType => typeof(int);
        public override Type PackType => typeof(NullPack);
        public override Type BodyBaseType => typeof(Formula);
        public override Type AssetBaseType => typeof(ScriptableObject);
        [SerializeField] private int constant;
        public override object DefaultObject { get => constant; set => constant = (int)value; }
        public override bool AcceptsBody(GraphNodeContent value) => value is Formula;
        public override bool AcceptsToken(GraphToken value) => value?.Slot is ValueSlot;
    }

    [Serializable] private sealed class ActionSlot : ActionSlotBase
    {
        public ActionSlot() { }
        [SerializeReference] private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override bool Disabled { get; set; }
        public override string Label { get; set; }
        public override string Id => null;
        public override string EnsureId() => "test-slot";
        public override void ResetId() { }
        public override Vector2 Pos { get; set; }
        public override bool HasPos => false;
        public override void ClearPos() { }
        public override List<GraphNode> Orphans { get; } = new();
        public override Type PackType => typeof(NullPack);
        public override Type BodyBaseType => typeof(Action);
        public override Type AssetBaseType => typeof(ScriptableObject);
        public override bool AcceptsBody(GraphNodeContent value) => value is Action;
    }

    [Serializable] private sealed class Root : ISequentialActionContainer
    {
        public List<ActionSlot> Actions = new();
        public IReadOnlyList<ActionSlotBase> SequentialActions => Actions;
    }

    [Serializable] private sealed class Document : IGraphDocument, ITokenOwner, IGraphViewStateOwner
    {
        public GraphViewState Layout = new();
        public GraphViewState ViewState => Layout;
        public Root Root = new();
        [SerializeReference] private List<GraphToken> tokens = new();
        [SerializeReference] private List<GraphNode> orphans = new();
        public List<GraphToken> Tokens => tokens;
        public List<GraphNode> Orphans => orphans;
        List<GraphToken> ITokenOwner.Tokens => Tokens;
        public IList Roots => new object[] { Root };
        public bool IsValidated => false;
        public Type PackType => typeof(NullPack);
        public Type ItemSlotType => typeof(ActionSlot);
        public string RootChip => "Test";
        public string RootNoun => "Test";
        public string WindowTitle => "Test";
        public HGCapabilities Capabilities => HGCapabilities.Tokens;
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { "root" };
        public object KeyOf(object root) => "root";
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => Root.Actions;
        public void InvalidateValidation() => throw new InvalidOperationException("Read-only preview must not invalidate.");
        public void Verify() => throw new InvalidOperationException("Read-only preview must not verify.");
        public object DeepCopy() => GraphDeepCopy.Copy(this);
        public object AddRoot(object key) => throw new NotSupportedException();
    }
}
}

