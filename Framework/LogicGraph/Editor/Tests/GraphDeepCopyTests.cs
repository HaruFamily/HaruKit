namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

public class GraphDeepCopyTests
{
    [Test]
    public void Copy_ClonesGraphAndPreservesReferences()
    {
        var asset = ScriptableObject.CreateInstance<CloneAsset>();
        var source = new CloneNode { Asset = asset };
        source.Children.Add(source);
        source.Shared = new CloneLeaf { Value = 7 };
        source.Other = source.Shared;

        var copy = GraphDeepCopy.Copy(source);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy, Is.Not.SameAs(source));
        Assert.That(copy.Asset, Is.SameAs(asset));
        Assert.That(copy.Children[0], Is.SameAs(copy));
        Assert.That(copy.Shared, Is.Not.SameAs(source.Shared));
        Assert.That(copy.Other, Is.SameAs(copy.Shared));
        Assert.That(copy.Shared.Value, Is.EqualTo(7));

        UnityEngine.Object.DestroyImmediate(asset);
    }

    [Test]
    public void CollectDiagnostics_ValidatesWithoutEmittingSummary()
    {
        var graph = new LogicGraph<TestTiming, TestPack>();
        graph.MarkValidated();
        graph.Tokens.Add(null);

        IReadOnlyList<GraphDiagnostic> diagnostics = graph.CollectDiagnostics();

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Code, Is.EqualTo("logicgraph.token.missing"));
        Assert.That(diagnostics[0].Location.FieldPath, Is.EqualTo("Tokens[0]"));
        Assert.That(diagnostics[0].Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        Assert.That(graph.IsValidated, Is.True, "Live diagnostics must not replace Owner validation state.");
    }

    /// <summary>共用來源：兩個欄位指到同一個載體，複製後必須還是同一個，否則存檔會裂成兩個節點。</summary>
    [Test]
    public void Copy_KeepsSharedCarrierShared()
    {
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.Pos = new Vector2(120f, 40f);
        var holder = new CarrierHolder { A = carrier, B = carrier };

        var copy = GraphDeepCopy.Copy(holder);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.A, Is.Not.SameAs(carrier));
        Assert.That(copy.B, Is.SameAs(copy.A));
        Assert.That(copy.A.Id, Is.EqualTo(carrier.Id));
        Assert.That(copy.A.Pos, Is.EqualTo(carrier.Pos));
    }

    /// <summary>載體內容繞回自己：複製不可無限遞迴，且環要保留。</summary>
    [Test]
    public void Copy_KeepsCarrierCycle()
    {
        var body = new CarrierNode();
        var carrier = new GraphNode(body);
        carrier.EnsureId();
        body.Child = carrier;

        var copy = GraphDeepCopy.Copy(carrier);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy, Is.Not.SameAs(carrier));
        Assert.That(copy.Kind, Is.EqualTo(NodeKind.Inline));
        var copiedBody = copy.GetBody<CarrierNode>();
        Assert.That(copiedBody, Is.Not.Null);
        Assert.That(copiedBody, Is.Not.SameAs(body));
        Assert.That(copiedBody.Child, Is.SameAs(copy));
    }

    /// <summary>載體指到的資產是 Unity Object，複製後必須是同一個資產而不是新複本。</summary>
    [Test]
    public void Copy_KeepsCarrierAssetReference()
    {
        var asset = ScriptableObject.CreateInstance<CloneAsset>();
        var carrier = new GraphNode();
        carrier.SetAsset(asset);

        var copy = GraphDeepCopy.Copy(carrier);

        Assert.That(copy.Kind, Is.EqualTo(NodeKind.Asset));
        Assert.That(copy.AssetObject, Is.SameAs(asset));

        UnityEngine.Object.DestroyImmediate(asset);
    }

    /// <summary>
    /// 複製圖裡的一小塊（例：複製一個Token）：`shared` 裡的具名Token必須原樣沿用。
    /// 跟著抄一份就變成不在清單裡的孤兒端點——參照得到、卻永遠查不到值。
    /// </summary>
    [Test]
    public void Copy_WithShared_KeepsSharedObjectsUncopied()
    {
        var otherSlot = new TestSlot(5);
        var other = new GraphToken("other", otherSlot);
        other.EnsureId();

        var reference = new GraphNode();
        reference.SetToken(other);
        var sourceSlot = new TestSlot();
        sourceSlot.SetNode(reference);
        var source = new GraphToken("source", sourceSlot);
        source.EnsureId();

        var copy = GraphDeepCopy.Copy(source, new object[] { other });

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy, Is.Not.SameAs(source));
        Assert.That(copy.Slot, Is.Not.SameAs(sourceSlot));
        Assert.That(copy.Slot.Node, Is.Not.SameAs(reference), "載體本身仍要複製");
        Assert.That(copy.Slot.Node.Token, Is.SameAs(other), "共用Token不可跟著複製");

        // 沒給 shared 就是整棵抄：這正是複製單一子圖不能用它的原因。
        var plain = GraphDeepCopy.Copy(source);
        Assert.That(plain.Slot.Node.Token, Is.Not.SameAs(other));
    }

    [Test]
    public void Copy_ClonesAssetBindingsAndTheirGraphs()
    {
        var asset = ScriptableObject.CreateInstance<TestFormulaAsset>();
        var source = new GraphNode();
        source.SetAsset(asset);
        var child = new GraphNode(new TestFormula());
        var slot = new TestSlot(3);
        slot.SetNode(child);
        source.Bindings.Add(new NamedFormulaSlot("amount", slot) { OverrideEnabled = true });

        var copy = GraphDeepCopy.Copy(source);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.AssetObject, Is.SameAs(asset));
        Assert.That(copy.Bindings, Has.Count.EqualTo(1));
        Assert.That(copy.Bindings[0], Is.Not.SameAs(source.Bindings[0]));
        Assert.That(copy.Bindings[0].Slot, Is.Not.SameAs(slot));
        Assert.That(copy.Bindings[0].Slot.Node, Is.Not.SameAs(child));
        Assert.That(copy.Bindings[0].OverrideEnabled, Is.True);

        UnityEngine.Object.DestroyImmediate(asset);
    }

    [Test]
    public async Task TokenTable_Resolve_ReevaluatesEveryTime()
    {
        var slot = new TestSlot();
        slot.SetNode(new GraphNode(new CountingFormula()));
        var table = new TokenTable<TestPack>();
        table.Register(new GraphToken("value", slot));

        int first = await table.Resolve<int>(typeof(TestSlot), "value", default).AsTask();
        int second = await table.Resolve<int>(typeof(TestSlot), "value", default).AsTask();

        Assert.That(first, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(2));
    }

    [Test]
    public async Task DisabledFormulaAssetRootUsesEachCallingSlotsFallbackWithoutEvaluating()
    {
        var asset = ScriptableObject.CreateInstance<TestFormulaAsset>();
        var formula = new CountingFormula();
        var root = new GraphNode(formula) { Disabled = true };
        asset.SetRoot(root);
        var carrier = new GraphNode();
        carrier.SetAsset(asset);
        var first = new TestSlot(7);
        var second = new TestSlot(13);
        first.SetNode(carrier);
        second.SetNode(carrier);
        try
        {
            Assert.That(await first.Evaluate(default, null), Is.EqualTo(7));
            Assert.That(await second.Evaluate(default, null), Is.EqualTo(13));
            Assert.That(await asset.Evaluate(default, null), Is.Zero, "資產直接求值沒有呼叫欄位，回 default(T)。");
            Assert.That(formula.Calls, Is.Zero);
            root.Disabled = false;
            Assert.That(await first.Evaluate(default, null), Is.EqualTo(1));
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
    }

    [Test]
    public async Task AssetBinding_UsesExplicitOverrideAndIsolatesCallerTable()
    {
        var asset = ScriptableObject.CreateInstance<TestFormulaAsset>();
        var internalSlot = new TestSlot();
        internalSlot.SetNode(new GraphNode(new TestFormula()));
        var parameter = new GraphToken("amount", internalSlot);
        asset.Tokens.Add(parameter);

        var proxy = new GraphNode();
        proxy.SetToken(parameter);
        var target = new PassFormula();
        target.Value.SetNode(proxy);
        asset.SetTarget(target);
        AssetGraphSchema.InvalidateCache();

        var assetCarrier = new GraphNode();
        assetCarrier.SetAsset(asset);
        var bindingSlot = new TestSlot(7);
        var binding = new NamedFormulaSlot("amount", bindingSlot);
        assetCarrier.Bindings.Add(binding);
        var call = new TestSlot();
        call.SetNode(assetCarrier);
        var caller = new TokenTable<TestPack>();

        int internalValue = await call.Evaluate(default, caller).AsTask();
        binding.OverrideEnabled = true;
        int overrideValue = await call.Evaluate(default, caller).AsTask();

        Assert.That(internalValue, Is.EqualTo(1));
        Assert.That(overrideValue, Is.EqualTo(7));

        asset.SetTarget(new ResolveFormula());
        var ownerSlot = new TestSlot();
        ownerSlot.SetNode(new GraphNode(new TestFormula()));
        caller.Register(new GraphToken("owner", ownerSlot));
        int leakedValue = await call.Evaluate(default, caller).AsTask();
        Assert.That(leakedValue, Is.EqualTo(0), "資產內容不可直接解析 caller token");

        UnityEngine.Object.DestroyImmediate(asset);
    }

    [Serializable]
    private sealed class CarrierHolder
    {
        public GraphNode A;
        public GraphNode B;
    }

    [Serializable]
    private sealed class CarrierNode : GraphNodeContent
    {
        public GraphNode Child;
    }

    [Serializable]
    private sealed class CloneNode
    {
        public CloneAsset Asset;
        public List<CloneNode> Children = new();
        public CloneLeaf Shared;
        public CloneLeaf Other;
    }

    [Serializable]
    private sealed class CloneLeaf
    {
        public int Value;
    }

    private sealed class CloneAsset : ScriptableObject { }

    [Test]
    public async Task ExecutionObservesFormulaAndTokenVisitsInTheSameChain()
    {
        var formula = new GraphNode(new CountingFormula());
        formula.EnsureId();
        var tokenSlot = new TestSlot();
        tokenSlot.SetNode(formula);
        var token = new GraphToken("value", tokenSlot);
        var reference = new GraphNode();
        reference.SetToken(token);
        reference.EnsureId();
        var input = new TestSlot();
        input.SetNode(reference);
        var graph = ExecutionGraph(input, out var action);
        graph.Tokens.Add(token);
        using var observation = graph.ExecutionSource.Observe();
        var key = new GraphExecutionNodeKey(formula.Id);
        graph.ExecutionSource.SetHoldForNextExecution(key, graph.ExecutionRevision, true);
        var running = graph.TriggerAction(TestTiming.Start, default).AsTask();
        try
        {
            var session = graph.ExecutionSource.Sessions[0];
            Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Holding));
            Assert.That(session.Query(new GraphExecutionNodeKey(action.Id)).State, Is.EqualTo(GraphExecutionState.Running));
            Assert.That(session.Query(new GraphExecutionNodeKey(reference.Id)).State, Is.EqualTo(GraphExecutionState.Running));
            session.SetHold(key, false);
            await running;
            Assert.That(session.Query(key).Completed, Is.EqualTo(2));
            Assert.That(session.Query(new GraphExecutionNodeKey(reference.Id)).Completed, Is.EqualTo(2));
            Assert.That(session.Query(new GraphExecutionNodeKey(action.Id)).Completed, Is.EqualTo(1));
        }
        finally { graph.CancelObservedExecutions(); }
    }

    [Test]
    public async Task ExecutionPropagatesThroughAssetRootsWithAQualifiedScope()
    {
        var asset = ScriptableObject.CreateInstance<TestFormulaAsset>();
        var root = new GraphNode(new CountingFormula());
        root.EnsureId();
        asset.SetRoot(root);
        var reference = new GraphNode();
        reference.SetAsset(asset);
        reference.EnsureId();
        var input = new TestSlot();
        input.SetNode(reference);
        var graph = ExecutionGraph(input, out _);
        using var observation = graph.ExecutionSource.Observe();
        var key = new GraphExecutionNodeKey(root.Id, "asset:" + asset.GetInstanceID());
        graph.ExecutionSource.SetHoldForNextExecution(key, graph.ExecutionRevision, true);
        var running = graph.TriggerAction(TestTiming.Start, default).AsTask();
        try
        {
            var session = graph.ExecutionSource.Sessions[0];
            Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Holding));
            Assert.That(session.Query(new GraphExecutionNodeKey(root.Id)).State, Is.EqualTo(GraphExecutionState.NotVisited));
            session.SetHold(key, false);
            await running;
            Assert.That(session.Query(key).Completed, Is.EqualTo(2));
            Assert.That(session.Query(new GraphExecutionNodeKey(reference.Id)).Completed, Is.EqualTo(2));
        }
        finally { graph.CancelObservedExecutions(); UnityEngine.Object.DestroyImmediate(asset); }
    }

    private static LogicGraph<TestTiming, TestPack> ExecutionGraph(TestSlot input, out GraphNode action)
    {
        action = new GraphNode(new EvaluateTwice { Value = input });
        action.EnsureId();
        var slot = new ActionSlot<TestPack>();
        slot.SetNode(action);
        var graph = new LogicGraph<TestTiming, TestPack>();
        graph.ActionGroups.Add(new ActionTimingGroup<TestTiming, TestPack> { Timing = TestTiming.Start, Actions = new() { slot } });
        graph.MarkValidated();
        return graph;
    }

    [Serializable]
    private sealed class EvaluateTwice : ActionBase<TestPack>
    {
        public TestSlot Value;
        protected override async UniTask OnExecute(TestPack pack, TokenTable<TestPack> tokens)
        {
            await Value.Evaluate(pack, tokens);
            await Value.Evaluate(pack, tokens);
        }
    }

    private enum TestTiming { Start }

    private struct TestPack { }

    [Serializable]
    private class TestFormula : FormulaBase<int, TestPack>
    {
        protected override UniTask<int> OnEvaluate(TestPack pack, TokenTable<TestPack> tokens)
            => UniTask.FromResult(1);
    }

    private sealed class TestFormulaAsset : FormulaAsset<int, TestPack> { }

    [Serializable]
    private sealed class CountingFormula : TestFormula
    {
        public int Calls;

        protected override UniTask<int> OnEvaluate(TestPack pack, TokenTable<TestPack> tokens)
            => UniTask.FromResult(++Calls);
    }

    [Serializable]
    private sealed class PassFormula : FormulaBase<int, TestPack>
    {
        public TestSlot Value = new();

        protected override async UniTask<int> OnEvaluate(TestPack pack, TokenTable<TestPack> tokens)
            => await Value.Evaluate(pack, tokens);
    }

    [Serializable]
    private sealed class ResolveFormula : FormulaBase<int, TestPack>
    {
        protected override async UniTask<int> OnEvaluate(TestPack pack, TokenTable<TestPack> tokens)
            => await tokens.Resolve<int>(typeof(TestSlot), "owner", pack);
    }

    [Serializable]
    private sealed class TestSlot : FormulaSlot<int, TestFormulaAsset, TestFormula, TestPack>
    {
        public TestSlot() { }
        public TestSlot(int value) : base(value) { }
    }
}
}
