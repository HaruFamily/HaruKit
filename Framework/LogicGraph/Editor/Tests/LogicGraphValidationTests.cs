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
    private sealed class OtherIntSlot : FormulaSlot<int, IntAsset, IntFormula, Pack> { }

    [Serializable]
    private sealed class IntPropertySlot : SetPropertySlot<int, IntSlot> { }

    [Serializable]
    private sealed class ReadWriteValue : ActionBase<Pack>
    {
        public IntSlot Input = new();
        public IntPropertySlot Output = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            Output.Write(await Input.Evaluate(pack, tokens), tokens);
        }
    }

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
    public void PropertyReadWriteValidationRejectsMissingAndIncompatibleTargetsWithoutRejectingWriteBack()
    {
        var property = new GraphProperty(null, new IntSlot());
        var target = new GraphNode();
        target.SetLocalProperty(property);
        var body = new ReadWriteValue();
        body.Input.SetNode(target);
        body.Output.SetNode(target);
        var action = new ActionSlot<Pack>(body);
        var graph = Graph(action);

        Assert.That(graph.CollectDiagnostics(), Is.Empty, "合法讀取後寫回不是求值循環。");
        property.SetSlot(new OtherIntSlot());
        var errors = graph.CollectDiagnostics().Where(d => d.Code == "logicgraph.node.property-incompatible").ToList();
        Assert.That(errors, Has.Count.EqualTo(2), "讀取端與寫入端都必須逐 Slot 驗族。");
        Assert.That(errors.All(d => d.Severity == GraphDiagnosticSeverity.Error), Is.True);

        target.SetProtoProperty(null);
        Assert.That(graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.property-missing").Severity,
            Is.EqualTo(GraphDiagnosticSeverity.Error));
        action.Disabled = true;
        Assert.That(graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.property-missing").Severity,
            Is.EqualTo(GraphDiagnosticSeverity.Warning));

        action.Disabled = false;
        body.Input.SetNode(null);
        body.Output.SetNode(new GraphNode(new ReadValue()));
        Assert.That(graph.CollectDiagnostics().Single(d => d.Code == "logicgraph.node.body-incompatible").Location.FieldPath,
            Does.EndWith(".Output"));
    }

    [Test]
    public void PropertyLibrariesValidateNamesAndTypesInTheGraphAndReferencedAssets()
    {
        var asset = ScriptableObject.CreateInstance<TestActionAsset>();
        try
        {
            asset.SetTarget(new ReadValue());
            asset.Properties.Add(new GraphProperty("Same", new IntSlot(), true));
            asset.Properties.Add(new GraphProperty("Same", new OtherIntSlot(), true));
            var node = new GraphNode();
            node.SetAsset(asset);
            var slot = new ActionSlot<Pack>();
            slot.SetNode(node);
            var graph = Graph(slot);
            graph.Properties.Add(new GraphProperty(null, null, true));

            var errors = graph.CollectDiagnostics();
            Assert.That(errors.Count(d => d.Code == "logicgraph.property.name-missing"), Is.EqualTo(1));
            Assert.That(errors.Count(d => d.Code == "logicgraph.property.slot-missing"), Is.EqualTo(1));
            Assert.That(errors.Single(d => d.Code == "logicgraph.property.duplicate").Location.FieldPath,
                Does.Contain(".Asset.Properties[1]"));
            var propertyRoot = new GraphNode();
            propertyRoot.SetProtoProperty(asset.Properties[0]);
            asset.SetRoot(propertyRoot);
            Assert.That(graph.CollectDiagnostics().Any(d => d.Code == "logicgraph.asset.property-root"), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
    }

    [Test]
    public void AssetPropertyEditingAndHistoryKeepDefinitionsWithTheirOwnWorkingGraph()
    {
        var owner = ScriptableObject.CreateInstance<Owner>();
        var asset = ScriptableObject.CreateInstance<TestActionAsset>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        var selection = UnityEditor.Selection.activeObject;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            var property = new GraphProperty("AssetValue", new IntSlot(), true);
            property.Slot.DefaultObject = 7;
            property.EnsureId();
            asset.Properties.Add(property);
            var target = new GraphNode();
            target.SetProtoProperty(property);
            var body = new ReadValue();
            body.Input.SetNode(target);
            asset.SetTarget(body);
            owner.Second.Properties.Add(new GraphProperty("OwnerValue", new IntSlot(), true));

            var binding = LogicGraphEditor.CreateBinding(typeof(Owner).GetField(nameof(Owner.Second)));
            Assert.That(window.BindDocument(owner, binding, LogicGraphEditor.Context), Is.True);
            // 只呼叫既有私有 UI 命令；測試不存檔，也不以另一份模擬快照取代真正的視窗歷程。
            typeof(HaruGraphWindow).GetMethod("EnterAsset", flags, null,
                new[] { typeof(UnityEngine.Object), typeof(Type) }, null).Invoke(window, new object[] { asset, typeof(ActionSlot<Pack>) });
            HGFocus Focus() => (HGFocus)typeof(HaruGraphWindow).GetField("focus", flags).GetValue(window);
            var focus = Focus();
            var working = focus.AssetProperties.Single();
            Assert.That(working, Is.Not.SameAs(property));
            Assert.That(((ReadValue)focus.AssetHostSlot.Node.BodyObject).Input.Node.Property, Is.SameAs(working));
            Assert.That(typeof(HaruGraphWindow).GetMethod("CurrentProperties", flags).Invoke(window, null),
                Is.SameAs(focus.AssetProperties));
            Assert.That(window.GetDocumentCommands().Validate().Any(d => d.Severity == GraphDiagnosticSeverity.Error), Is.False);

            var originalRoot = focus.AssetHostSlot.Node;
            var propertyRoot = new GraphNode();
            propertyRoot.SetProtoProperty(working);
            focus.AssetHostSlot.SetNode(propertyRoot);
            var model = (HGModel)typeof(HaruGraphWindow).GetField("model", flags).GetValue(window);
            Assert.That(HGValidator.RunSubtree(model, focus, focus.AssetHostSlot, "Asset").Issues
                .Any(issue => issue.Diagnostic.Code == "graphkit.asset.property-root"), Is.True);
            focus.AssetHostSlot.SetNode(originalRoot);

            typeof(HaruGraphWindow).GetMethod("SetPropertyInitialValue", flags).Invoke(window, new object[] { working, 11 });
            Assert.That(property.InitialValue, Is.EqualTo(7), "編輯不能改到原資產。");
            Assert.That(owner.Second.Properties.Single().Name, Is.EqualTo("OwnerValue"));
            Assert.That(window.GetDocumentCommands().Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(Focus().AssetProperties.Single().InitialValue, Is.EqualTo(7));
            Assert.That(((ReadValue)Focus().AssetHostSlot.Node.BodyObject).Input.Node.Property,
                Is.SameAs(Focus().AssetProperties.Single()));
            Assert.That(window.GetDocumentCommands().Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(Focus().AssetProperties.Single().InitialValue, Is.EqualTo(11));
            Assert.That(((ReadValue)Focus().AssetHostSlot.Node.BodyObject).Input.Node.Property,
                Is.SameAs(Focus().AssetProperties.Single()));
        }
        finally
        {
            typeof(HaruGraphWindow).GetMethod("ExitAsset", flags).Invoke(window, null);
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(asset);
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEditor.Selection.activeObject = selection;
        }
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
