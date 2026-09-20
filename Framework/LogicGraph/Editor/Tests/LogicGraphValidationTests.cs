namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class LogicGraphValidationTests
{
    private enum Timing { Start, End }
    private sealed class Pack { }
    private abstract class IntFormula : FormulaBase<int, Pack> { }
    private sealed class IntAsset : FormulaAsset<int, Pack> { }
    private sealed class TestActionAsset : ActionAssetBase<Pack> { }

    [Serializable]
    private sealed class IntSlot : FormulaSlot<int, IntAsset, IntFormula, Pack> { }

    [Serializable]
    private sealed class ReadValue : ActionBase<Pack>
    {
        public IntSlot Input = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            await Input.Evaluate(pack, tokens);
        }
    }

    private static LogicGraph<Timing, Pack> Graph(ActionSlot<Pack> slot)
    {
        var graph = new LogicGraph<Timing, Pack>();
        graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { slot } });
        return graph;
    }

    [Test]
    public void NestedInputDiagnosticsRetainCodeNodeAndFieldLocation()
    {
        var empty = new GraphNode();
        empty.EnsureId();
        var body = new ReadValue();
        body.Input.SetNode(empty);
        var graph = Graph(new ActionSlot<Pack>(body));
        graph.MarkValidated();

        var diagnostic = graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.empty");

        Assert.That(diagnostic.Location.NodeId, Is.EqualTo(empty.Id));
        Assert.That(diagnostic.Location.FieldPath, Is.EqualTo("ActionGroups[0].Actions[0].Input"));
        Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        Assert.That(graph.IsValidated, Is.True);
    }

    [Test]
    public void SharedEnabledPathWinsOverDisabledPathWithoutDuplicateWarnings()
    {
        var empty = new GraphNode();
        var body = new ReadValue();
        body.Input.SetNode(empty);
        var carrier = new GraphNode(body);
        var disabled = new ActionSlot<Pack> { Disabled = true };
        var enabled = new ActionSlot<Pack>();
        disabled.SetNode(carrier);
        enabled.SetNode(carrier);
        var graph = Graph(disabled);
        graph.ActionGroups[0].Actions.Add(enabled);

        var diagnostic = graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.empty");
        Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        Assert.That(diagnostic.Location.FieldPath, Is.EqualTo("ActionGroups[0].Actions[1].Input"));

        enabled.Disabled = true;
        diagnostic = graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.empty");
        Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Warning));
    }

    [Test]
    public void DisabledAssetRootDowngradesItsUnreachableInputProblems()
    {
        var asset = ScriptableObject.CreateInstance<TestActionAsset>();
        var body = new ReadValue();
        body.Input.SetNode(new GraphNode());
        asset.SetTarget(body);
        var carrier = new GraphNode();
        carrier.SetAsset(asset);
        var slot = new ActionSlot<Pack>();
        slot.SetNode(carrier);
        var graph = Graph(slot);
        try
        {
            Assert.That(graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.empty").Severity,
                Is.EqualTo(GraphDiagnosticSeverity.Error));
            asset.Root.Disabled = true;
            Assert.That(graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.empty").Severity,
                Is.EqualTo(GraphDiagnosticSeverity.Warning));
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
    }

    private sealed class Owner : ScriptableObject, ILogicGraphTimingOwner, IGraphDomainDiagnostics
    {
        public LogicGraph<Timing, Pack> First = new();
        public LogicGraph<Timing, Pack> Second = new();
        public bool Reject;
        public bool Throw;
        [NonSerialized] public IGraphDocument Inspected;
        public IReadOnlyList<Enum> AllowedTimings => new Enum[] { Timing.Start };
        public void MarkGraphDirty() => Second.MarkDirty();
        public bool IsGraphValidated() => Second.IsValidated;
        public void VerifyGraph() => Second.Verify(domain: this);

        public void CollectDiagnostics(IGraphDocument document, List<GraphDiagnostic> diagnostics)
        {
            Inspected = document;
            if (Throw) throw new InvalidOperationException("expected domain failure");
            if (Reject) diagnostics.Add(new GraphDiagnostic("test.domain.rejected", GraphDiagnosticSeverity.Error,
                "領域規則拒絕。", new GraphDiagnosticLocation(fieldPath: "Description")));
        }
    }

    [Test]
    public void OwnerAndLiveValidationUseTheSameDomainReportWithoutChangingLiveValidationState()
    {
        var owner = ScriptableObject.CreateInstance<Owner>();
        try
        {
            owner.Second.MarkValidated();
            owner.Reject = true;
            var diagnostics = owner.Second.CollectDiagnostics(domain: owner);
            Assert.That(diagnostics.Single().Code, Is.EqualTo("test.domain.rejected"));
            Assert.That(owner.Second.IsValidated, Is.True);
            string revision = owner.Second.ExecutionRevision;
            LogAssert.Expect(LogType.Error, new Regex("test.domain.rejected"));
            owner.VerifyGraph();
            Assert.That(owner.Second.IsValidated, Is.False);
            Assert.That(owner.Second.Diagnostics.Single().Code, Is.EqualTo("test.domain.rejected"));
            Assert.That(owner.Second.ExecutionRevision, Is.EqualTo(revision), "驗證結果不可冒充圖內容修改。");

            owner.Throw = true;
            var failure = owner.Second.CollectDiagnostics(domain: owner).Single();
            Assert.That(failure.Code, Is.EqualTo("logicgraph.domain.validation-failed"));
            Assert.That(failure.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void ExplicitEntryBindsOnlyTheRequestedFieldAndAppliesTimingAndDomainRules()
    {
        var owner = ScriptableObject.CreateInstance<Owner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var first = owner.First;
            var second = owner.Second;
            var binding = LogicGraphEditor.CreateBinding(typeof(Owner).GetField(nameof(Owner.Second)));
            Assert.That(window.BindDocument(owner, binding, LogicGraphEditor.Context), Is.True);
            Assert.That(window.IsBoundToDocument(owner, binding.DocumentId), Is.True);
            Assert.That(LogicGraphEditor.Context.Profile.RootAdapter.RootKeys(second, owner),
                Is.EqualTo(new object[] { Timing.Start }));

            var commands = window.GetDocumentCommands();
            owner.Reject = true;
            Assert.That(commands.Validate().Count(d => d.Code == "test.domain.rejected"), Is.EqualTo(1));
            Assert.That(owner.Inspected, Is.Not.SameAs(second), "診斷必須使用工作副本。");
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(owner.Second, Is.SameAs(second));
            owner.Reject = false;
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second, Is.Not.SameAs(second));
            Assert.That(owner.First, Is.SameAs(first));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void NonWindowConsumersCanReuseTheOfficialContextAndDomainCommitGate()
    {
        var owner = ScriptableObject.CreateInstance<Owner>();
        try
        {
            var binding = new HGDocumentBinding<LogicGraph<Timing, Pack>>("test.second",
                value => ((Owner)value).Second, (value, graph) => ((Owner)value).Second = graph);
            Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, binding,
                LogicGraphEditor.Context, out var session, out var error), Is.True, error?.Message);
            owner.Reject = true;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(owner.Second.IsValidated, Is.False);
            owner.Reject = false;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second.IsValidated, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }
}
}
