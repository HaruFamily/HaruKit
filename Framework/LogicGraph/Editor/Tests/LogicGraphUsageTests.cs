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

public sealed class LogicGraphUsageTests
{
    private enum Timing { Start, End }
    private enum DisplayTiming
    {
        [HGLabel("初始化")] Start = 100,
        [HGLabel("初始化")] SameLabel = 200,
        End = 300,
        [HGLabel("")] EmptyLabel = 400,
        [HGLabel(" \t")] BlankLabel = 500,
        [HGLabel] UnnamedLabel = 600,
    }
    private sealed class Pack { }

    [Serializable]
    private sealed class FlowAction : ActionBase<Pack>, IGraphFlowContainer
    {
        public GraphFlowKind Kind;
        public IntSlot Input = new();
        public List<ActionSlot<Pack>> Body = new();
        public List<ActionSlot<Pack>> Otherwise = new();

        public GraphFlowDescription DescribeFlow()
        {
            if (Kind == GraphFlowKind.FirstMatch) return new(Kind, new[]
            {
                new GraphFlowSection("case", "條件", GraphFlowSectionRole.Case, Body, Input),
                new GraphFlowSection("default", "預設", GraphFlowSectionRole.Default, Otherwise),
            });
            if (Kind == GraphFlowKind.Conditional) return new(Kind, new[]
            {
                new GraphFlowSection("yes", "成立", GraphFlowSectionRole.WhenTrue, Body),
                new GraphFlowSection("no", "不成立", GraphFlowSectionRole.WhenFalse, Otherwise),
            }, new[] { new GraphFlowInput("condition", "條件", Input) });
            if (Kind == GraphFlowKind.Loop) return new(Kind,
                new[] { new GraphFlowSection("body", "動作", GraphFlowSectionRole.Body, Body) },
                new[] { new GraphFlowInput("source", "來源", Input) });
            return GraphFlowDescription.Sequence(Body);
        }

        protected override UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            => throw new InvalidOperationException("Flow inspection must never execute actions.");
    }

    [Test]
    public void FlowProjectionKeepsBranchOrderAndSharedUsesWithoutChangingSession()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        try
        {
            var shared = new GraphNode(new ReadValue());
            var loop = new FlowAction { Kind = GraphFlowKind.Loop };
            var firstUse = new ActionSlot<Pack>(); firstUse.SetNode(shared);
            firstUse.Label = "第一步";
            loop.Body.Add(firstUse);
            loop.Body.Add(new ActionSlot<Pack>(new ReadValue()) { Label = "第二步" });
            var conditional = new FlowAction { Kind = GraphFlowKind.Conditional };
            conditional.Body.Add(new ActionSlot<Pack>(loop));
            var secondUse = new ActionSlot<Pack>(); secondUse.SetNode(shared);
            conditional.Otherwise.Add(secondUse);
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack>
            { Timing = Timing.Start, Actions = new()
                { new ActionSlot<Pack>(conditional), new ActionSlot<Pack>(new FlowAction { Kind = GraphFlowKind.FirstMatch }) } });
            Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, SecondBinding(), out var session), Is.True);
            string ownerBefore = JsonUtility.ToJson(owner.Second);
            string draftBefore = JsonUtility.ToJson(session.Document);
            int generation = session.Generation;
            var snapshot = session.CaptureFlow();
            Assert.That(snapshot.Complete, Is.True);
            Assert.That(snapshot.Roots[0].FlowKind, Is.EqualTo(GraphFlowKind.Sequence));
            var branch = snapshot.Roots[0].Sections[0].Steps[0];
            Assert.That(branch.FlowKind, Is.EqualTo(GraphFlowKind.Conditional));
            Assert.That(branch.Sections.Select(section => section.Role), Is.EqualTo(new[]
                { GraphFlowSectionRole.WhenTrue, GraphFlowSectionRole.WhenFalse }));
            var repeated = branch.Sections[0].Steps[0];
            Assert.That(repeated.FlowKind, Is.EqualTo(GraphFlowKind.Loop));
            Assert.That(repeated.Sections[0].Steps.Select(step => step.Label), Is.EqualTo(new[] { "第一步", "第二步" }));
            var yes = repeated.Sections[0].Steps[0];
            var no = branch.Sections[1].Steps[0];
            Assert.That(yes.Path, Is.Not.EqualTo(no.Path));
            Assert.That(yes.ReferenceCount, Is.EqualTo(2));
            Assert.That(no.IsShared, Is.True);
            Assert.That(yes.Unknown, Is.True, "Undeclared actions remain opaque, not inferred from fields.");
            var selection = snapshot.Roots[0].Sections[0].Steps[1];
            Assert.That(selection.FlowKind, Is.EqualTo(GraphFlowKind.FirstMatch));
            Assert.That(selection.Sections.Select(section => section.Role), Is.EqualTo(new[]
                { GraphFlowSectionRole.Case, GraphFlowSectionRole.Default }));
            Assert.That(selection.Sections[0].Condition.SlotType, Is.EqualTo(typeof(IntSlot).AssemblyQualifiedName));
            Assert.That(JsonUtility.ToJson(owner.Second), Is.EqualTo(ownerBefore));
            Assert.That(JsonUtility.ToJson(session.Document), Is.EqualTo(draftBefore));
            Assert.That(session.Generation, Is.EqualTo(generation));
            Assert.That(session.IsDirty || session.CanUndo || session.CanRedo, Is.False);
            Assert.That(session.CaptureFlow().Roots[0].Sections[0].Steps[0].Path, Is.EqualTo(branch.Path));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FlowProjectionBoundsCyclesAndKeepsSnapshotsDetached()
    {
        var graph = new LogicGraph<Timing, Pack>();
        var body = new FlowAction { Kind = GraphFlowKind.Sequence };
        var node = new GraphNode(body);
        var recursive = new ActionSlot<Pack>(); recursive.SetNode(node);
        body.Body.Add(recursive);
        var rootSlot = new ActionSlot<Pack>(); rootSlot.SetNode(node);
        graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { rootSlot } });
        var snapshot = HGFlowSnapshot.Capture(graph);
        var projected = snapshot.Roots[0].Sections[0].Steps[0];
        Assert.That(projected.Sections[0].Steps[0].Cycle, Is.True);
        Assert.That(snapshot.Issues.Any(issue => issue.Code == "graphkit.flow.cycle"), Is.True);
        Assert.That(snapshot.Complete, Is.False);
        var bounded = HGFlowSnapshot.Capture(graph, maxSteps: 1);
        Assert.That(bounded.Issues.Any(issue => issue.Code == "graphkit.flow.limit"), Is.True);
        rootSlot.Disabled = true;
        body.Body.Clear();
        Assert.That(projected.Disabled, Is.False);
        Assert.That(projected.Sections[0].Steps.Count, Is.EqualTo(1), "Snapshot contains copied data, not the live list.");
        Assert.That(HGFlowSnapshot.Capture(graph).Roots[0].Sections[0].Steps[0].Disabled, Is.True);
    }

    private sealed class DisplayOwner : ScriptableObject
    {
        public LogicGraph<DisplayTiming, Pack> Graph = new();
    }
    private abstract class IntFormula : FormulaBase<int, Pack> { }
    [Serializable] private sealed class IntToken : FormulaToken<int, IntSlot, Pack> { }
    private sealed class IntAsset : FormulaAsset<int, IntSlot, Pack> { }
    private sealed class TestActionAsset : ActionAssetBase<Pack> { }
    [Serializable]
    private sealed class IntSlot : FormulaSlot<int, IntFormula, IntToken, IntAsset, Pack> { public IntSlot() { } }

    [Serializable]
    private sealed class ReadValue : ActionBase<Pack>
    {
        public ReadValue() { }
        public IntSlot Input = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            await Input.Evaluate(pack, tokens);
        }
    }

    private class PlainOwner : ScriptableObject
    {
        public LogicGraph<Timing, Pack> First = new();
        public LogicGraph<Timing, Pack> Second = new();
        [NonSerialized] public IGraphDocument Scratch;
    }

    private sealed class ConfiguredOwner : PlainOwner, ILogicGraphUsage<Timing, Pack>
    {
        public bool RequireToken;
        public bool Reject;
        public bool ThrowConfiguration;
        public bool ThrowValidation;
        [NonSerialized] public LogicGraph<Timing, Pack> Inspected;

        public void ConfigureGraph(LogicGraphUsage<Timing, Pack> usage)
        {
            if (ThrowConfiguration) throw new InvalidOperationException("expected configuration failure");
            usage.AllowTimings(new[] { Timing.Start });
            if (RequireToken) usage.RequireToken("Required", "Description");
            usage.AddValidation((graph, report) =>
            {
                Inspected = graph;
                if (ThrowValidation) throw new InvalidOperationException("expected validation failure");
                if (Reject) report.Error("consumer.rejected", "內容規則拒絕。", "Description");
            });
        }
    }

    private static HGDocumentBinding<LogicGraph<Timing, Pack>> SecondBinding()
        => new HGDocumentBinding<LogicGraph<Timing, Pack>>("test.second",
            owner => ((PlainOwner)owner).Second, (owner, graph) => ((PlainOwner)owner).Second = graph);

    [Serializable]
    private sealed class FlowConstant : IntFormula
    {
        public int Value;
        protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens) => UniTask.FromResult(Value);
    }

    [Serializable]
    private abstract class WrapAction : ActionBase<Pack>, IGraphFlowContainer
    {
        public IntSlot Input = new();
        public List<ActionSlot<Pack>> Body = new();
        public List<ActionSlot<Pack>> Otherwise = new();
        protected abstract GraphFlowKind Kind { get; }
        public GraphFlowDescription DescribeFlow() => new(Kind, Kind == GraphFlowKind.Conditional ? new[]
        {
            new GraphFlowSection("body", "成立", GraphFlowSectionRole.WhenTrue, Body),
            new GraphFlowSection("otherwise", "不成立", GraphFlowSectionRole.WhenFalse, Otherwise),
        } : new[] { new GraphFlowSection("body", "動作", GraphFlowSectionRole.Body, Body) },
            new[] { new GraphFlowInput("input", "條件或來源", Input) });
        protected override UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            => throw new InvalidOperationException("Structural authoring must never execute a container.");
    }
    [Serializable] private sealed class ConditionalFlowWrapper : WrapAction
    { public ConditionalFlowWrapper() { } protected override GraphFlowKind Kind => GraphFlowKind.Conditional; }
    [Serializable] private sealed class LoopFlowWrapper : WrapAction
    { public LoopFlowWrapper() { } protected override GraphFlowKind Kind => GraphFlowKind.Loop; }
    [Serializable] private sealed class NonemptyFlowWrapper : WrapAction
    {
        public NonemptyFlowWrapper() => Body.Add(new ActionSlot<Pack>(new ReadValue()));
        protected override GraphFlowKind Kind => GraphFlowKind.Loop;
    }

    [Test]
    public void FlowTransferPreservesSlotsSharingAndRejectsDescendantOrStaleTargets()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var shared = new GraphNode(new ReadValue()); shared.EnsureId();
            var first = new ActionSlot<Pack> { Label = "first" }; first.SetNode(shared); first.EnsureId();
            var second = new ActionSlot<Pack> { Label = "second", Disabled = true }; second.SetNode(shared); second.EnsureId();
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new()
                { first, second, new ActionSlot<Pack>(new ConditionalFlowWrapper()) } });
            string original = JsonUtility.ToJson(owner.Second);
            Assert.That(window.BindDocument(owner, SecondBinding(), LogicGraphEditor.Context), Is.True);
            var commands = window.GetDocumentCommands();
            int generation = commands.Query().Generation;
            string beforeHover = JsonUtility.ToJson(window.CaptureDocumentSnapshot());
            Assert.That(commands.CanTransferFlowSteps(generation, "root/0/body", 0, 2, "root/0/body/2/body", 0), Is.True);
            Assert.That(JsonUtility.ToJson(window.CaptureDocumentSnapshot()), Is.EqualTo(beforeHover), "Hover validation must not write.");
            Assert.That(commands.TransferFlowSteps(generation, "root/0/body", 0, 2, "root/0/body/2/body", 0), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(JsonUtility.ToJson(owner.Second), Is.EqualTo(original));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(window.CaptureDocumentSnapshot().Records.Count(record => record.Kind == "action-slot"), Is.EqualTo(3));
            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.TransferFlowSteps(generation, "root/0/body", 0, 1, "root/0/body", 0), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            var wrapper = (ConditionalFlowWrapper)owner.Second.ActionGroups[0].Actions.Single().Node.BodyObject;
            Assert.That(wrapper.Body.Select(slot => slot.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(wrapper.Body[1].Disabled, Is.True);
            Assert.That(wrapper.Body[0].Node, Is.SameAs(wrapper.Body[1].Node));
            Assert.That(owner.Second.Orphans.Any(node => node.Id == shared.Id), Is.False);
            generation = commands.Query().Generation;
            string beforeReject = JsonUtility.ToJson(window.CaptureDocumentSnapshot());
            Assert.That(commands.CanTransferFlowSteps(generation, "root/0/body", 0, 1, "root/0/body/0/body", 0), Is.False);
            Assert.That(commands.TransferFlowSteps(generation, "root/0/body", 0, 1, "root/0/body/0/body", 0), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(JsonUtility.ToJson(window.CaptureDocumentSnapshot()), Is.EqualTo(beforeReject));
            Assert.That(commands.TransferFlowSteps(generation, "root/0/body/0/body", 0, 1, "root/0/body", 1), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second.ActionGroups[0].Actions.Count, Is.EqualTo(1));
            Assert.That(((ConditionalFlowWrapper)owner.Second.ActionGroups[0].Actions[0].Node.BodyObject).Body.Count, Is.EqualTo(2));
        }
        finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FlowWrappingIsAtomicAndKeepsOriginalStepIdentityThroughNestedContainers()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var first = new ActionSlot<Pack>(new ReadValue()); first.EnsureId();
            var second = new ActionSlot<Pack>(new ReadValue()) { Label = "keep", Disabled = true }; second.EnsureId();
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { first, second } });
            Assert.That(window.BindDocument(owner, SecondBinding(), LogicGraphEditor.Context), Is.True);
            var commands = window.GetDocumentCommands();
            int generation = commands.Query().Generation;
            string beforeReject = JsonUtility.ToJson(window.CaptureDocumentSnapshot());
            Assert.That(commands.WrapFlowSteps(generation, "root/0/body", 0, 2, typeof(NonemptyFlowWrapper), "body"), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(commands.WrapFlowSteps(generation, "root/0/body", 0, 2, typeof(ConditionalFlowWrapper), "missing"), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(JsonUtility.ToJson(window.CaptureDocumentSnapshot()), Is.EqualTo(beforeReject));
            Assert.That(commands.WrapFlowSteps(generation, "root/0/body", 0, 2, typeof(ConditionalFlowWrapper), "body"), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(window.CaptureDocumentSnapshot().Records.Count(record => record.Kind == "action-slot"), Is.EqualTo(2), "One Undo must remove the wrapper and restore all original positions.");
            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.WrapFlowSteps(commands.Query().Generation, "root/0/body/0/body", 1, 1, typeof(LoopFlowWrapper), "body"), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            var condition = (ConditionalFlowWrapper)owner.Second.ActionGroups[0].Actions.Single().Node.BodyObject;
            Assert.That(condition.Body[0].Id, Is.EqualTo(first.Id));
            var loop = (LoopFlowWrapper)condition.Body[1].Node.BodyObject;
            Assert.That(loop.Body.Single().Id, Is.EqualTo(second.Id));
            Assert.That(loop.Body[0].Label, Is.EqualTo("keep"));
            Assert.That(loop.Body[0].Disabled, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FlowEditingKeepsSingleUndoSharedCarriersAndSavedRoundTrip()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var shared = new GraphNode(new ReadValue()); shared.EnsureId();
            var first = new ActionSlot<Pack>(); first.SetNode(shared);
            var second = new ActionSlot<Pack>(); second.SetNode(shared);
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { first, second } });
            string before = JsonUtility.ToJson(owner.Second);
            Assert.That(window.BindDocument(owner, SecondBinding(), LogicGraphEditor.Context), Is.True);
            var commands = window.GetDocumentCommands();
            int generation = commands.Query().Generation;
            Assert.That(commands.InsertFlowStep(generation, "root/0/body", 1, typeof(ReadValue)), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(window.CaptureDocumentSnapshot().Records.Count(record => record.Kind == "action-slot"), Is.EqualTo(3));
            Assert.That(commands.RemoveFlowStep(generation, "root/0/body/0"), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
            Assert.That(JsonUtility.ToJson(owner.Second), Is.EqualTo(before));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(window.CaptureDocumentSnapshot().Records.Count(record => record.Kind == "action-slot"), Is.EqualTo(2), "One Undo must undo the complete insertion.");
            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.RemoveFlowStep(commands.Query().Generation, "root/0/body/0"), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.MoveFlowStep(commands.Query().Generation, "root/0/body/1", -1), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second.ActionGroups[0].Actions.Count, Is.EqualTo(2));
            Assert.That(owner.Second.ActionGroups[0].Actions[0].Node.Id, Is.EqualTo(shared.Id));
            Assert.That(owner.Second.Orphans.Any(node => node.Id == shared.Id), Is.False, "Removing one use cannot orphan a still-used carrier.");
            Assert.That(commands.InsertFlowStep(commands.Query().Generation, "root/0/body", 0, typeof(ReadValue)), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(window.CaptureDocumentSnapshot().Records.Count(record => record.Kind == "action-slot"), Is.EqualTo(2));
            Assert.That(commands.RemoveFlowStep(commands.Query().Generation, "root/0/body/0"), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second.Orphans.Any(node => node.Id == shared.Id), Is.True, "Last-use removal preserves the complete source in the candidate pool.");
        }
        finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FlowValueAndSourceEditsRejectStaleOrInvalidWritesAndPersistExplicitly()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var body = new ReadValue(); body.Input.DefaultObject = 5;
            var carrier = new GraphNode(body); carrier.EnsureId();
            var slot = new ActionSlot<Pack>(); slot.SetNode(carrier);
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { slot } });
            Assert.That(window.BindDocument(owner, SecondBinding(), LogicGraphEditor.Context), Is.True);
            var commands = window.GetDocumentCommands();
            var query = commands.Query();
            string path = query.Ports.First(port => port.Anchor.NodeId == carrier.Id && port.Anchor.FieldPath?.EndsWith("Input", StringComparison.Ordinal) == true).Anchor.FieldPath;
            Assert.That(commands.SetFlowValue(query.Generation, carrier.Id, path, 7), Is.EqualTo(HGSessionCommandResult.Changed));
            query = commands.Query();
            string beforeFailure = JsonUtility.ToJson(window.CaptureDocumentSnapshot());
            Assert.That(commands.SetFlowValue(query.Generation, carrier.Id, path, "invalid"), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(commands.SetFlowSource(query.Generation, carrier.Id, path, null, HGCarrierSource.Body(new ReadValue())), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(JsonUtility.ToJson(window.CaptureDocumentSnapshot()), Is.EqualTo(beforeFailure));
            Assert.That(body.Input.DefaultObject, Is.EqualTo(5));
            Assert.That(body.Input.Node, Is.Null);
            Assert.That(commands.SetFlowSource(query.Generation, carrier.Id, path, null,
                HGCarrierSource.Body(new FlowConstant { Value = 11 })), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.SetFlowValue(commands.Query().Generation, carrier.Id, path, 8), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            var saved = (ReadValue)owner.Second.ActionGroups[0].Actions[0].Node.BodyObject;
            Assert.That(saved.Input.DefaultObject, Is.EqualTo(8));
            Assert.That(((FlowConstant)saved.Input.Node.BodyObject).Value, Is.EqualTo(11));
            Assert.That(window.BindDocument(owner, SecondBinding(), LogicGraphEditor.Context), Is.True);
            Assert.That(commands.SetFlowValue(query.Generation, carrier.Id, path, 99), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
            Assert.That(window.GetDocumentCommands().Query().Nodes.Any(node => node.Id == carrier.Id), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(owner); }
    }

    private class PrivateOwner : ScriptableObject
    {
        [SerializeField] private LogicGraph<Timing, Pack> graph = new();
        [NonSerialized] public LogicGraph<Timing, Pack> Scratch = new();
        public LogicGraph<Timing, Pack> Graph => graph;
    }

    private sealed class InheritedOwner : PrivateOwner { }

    [Test]
    public void TimingDisplayLabelsMatchHeadersAndMenuTitlesWithoutChangingRootIdentity()
    {
        var owner = ScriptableObject.CreateInstance<DisplayOwner>();
        try
        {
            var model = new HGModel();
            Assert.That(model.Bind(owner), Is.True);
            Assert.That(model.AvailableRootKeys, Is.EqualTo(new object[]
            {
                DisplayTiming.Start, DisplayTiming.SameLabel, DisplayTiming.End,
                DisplayTiming.EmptyLabel, DisplayTiming.BlankLabel, DisplayTiming.UnnamedLabel,
            }));
            Assert.That(model.RootKeyTitle(DisplayTiming.Start), Is.EqualTo("初始化"));
            Assert.That(model.RootKeyTitle(DisplayTiming.SameLabel), Is.EqualTo("初始化"));
            Assert.That(model.Doc.Roots, Is.Empty, "讀取未建立時機的名稱不能新增 root。");

            var first = model.AddRoot(DisplayTiming.Start);
            var second = model.AddRoot(DisplayTiming.SameLabel);
            Assert.That(model.Doc.TitleOf(first.Root), Is.EqualTo(model.RootKeyTitle(first.RootKey)));
            Assert.That(model.Doc.TitleOf(second.Root), Is.EqualTo(model.RootKeyTitle(second.RootKey)));
            Assert.That(first.RootKey, Is.EqualTo(DisplayTiming.Start));
            Assert.That(second.RootKey, Is.EqualTo(DisplayTiming.SameLabel));
            Assert.That(model.RootId(first.Root), Is.Not.EqualTo(model.RootId(second.Root)));
            Assert.That(model.HasRoot(DisplayTiming.Start), Is.True);
            Assert.That(model.HasRoot(DisplayTiming.SameLabel), Is.True);
            var built = HGGraph.Build(model, new System.Collections.Generic.List<object> { first.Root, second.Root },
                null, "roots", "Roots");
            Assert.That(built.Nodes.Select(node => node.Title), Is.EqualTo(new[] { "初始化", "初始化" }));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void TimingTitlesFallBackToMemberNamesAndUndefinedValues()
    {
        IGraphDocument document = new LogicGraph<DisplayTiming, Pack>();
        var display = (IGraphRootKeyDisplay)document;
        foreach (var timing in new[] { DisplayTiming.End, DisplayTiming.EmptyLabel, DisplayTiming.BlankLabel, DisplayTiming.UnnamedLabel })
        {
            Assert.That(display.TitleOfKey(timing), Is.EqualTo(timing.ToString()));
            Assert.That(document.TitleOf(document.AddRoot(timing)), Is.EqualTo(timing.ToString()));
        }
        Assert.That(display.TitleOfKey((DisplayTiming)999), Is.EqualTo("999"));
        Assert.That(HGEnumLabels.GetName(typeof(DisplayTiming), nameof(DisplayTiming.Start)), Is.EqualTo("初始化"));
    }

    [Test]
    public void InheritedPrivateSerializedFieldIsDiscoveredWithoutRuntimeScratchFields()
    {
        var owner = ScriptableObject.CreateInstance<InheritedOwner>();
        try
        {
            var field = HGModel.FindSystemField(owner);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.Name, Is.EqualTo("graph"));
            Assert.That(LogicGraphEditor.Verify(owner, field), Is.True);
            Assert.That(owner.Graph.IsValidated, Is.True);
            Assert.That(owner.Scratch.IsValidated, Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PlainOwnerNeedsNoInterfacesToVerifyTheSelectedFieldAndAllDocuments()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        try
        {
            Assert.That(HGOwnerValidation.DocumentFields(owner.GetType()).Select(field => field.Name),
                Is.EquivalentTo(new[] { nameof(PlainOwner.First), nameof(PlainOwner.Second) }));
            Assert.That(LogicGraphEditor.HasGraph(owner.GetType()), Is.True);
            Assert.That(LogicGraphEditor.Verify(owner, typeof(PlainOwner).GetField(nameof(PlainOwner.Second))), Is.True);
            Assert.That(owner.Second.IsValidated, Is.True);
            Assert.That(owner.First.IsValidated, Is.False, "指定欄位驗證不能改到另一份圖。");
            Assert.That(((IGraphDocument)owner.Second).RootKeys(owner), Is.EqualTo(new object[] { Timing.Start, Timing.End }));
            Assert.That(LogicGraphEditor.Verify(owner), Is.True);
            Assert.That(HGOwnerValidation.IsValidated(owner), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PlainOwnerCanBindAndCommitThroughTheDefaultGraphKitContext()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var first = owner.First;
            var second = owner.Second;
            var binding = LogicGraphEditor.CreateBinding(typeof(PlainOwner).GetField(nameof(PlainOwner.Second)));
            Assert.That(window.BindDocument(owner, binding, HGEditorExtensionContext.Default), Is.True);
            Assert.That(window.GetDocumentCommands().Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.First, Is.SameAs(first));
            Assert.That(owner.Second, Is.Not.SameAs(second));
            Assert.That(owner.Second.IsValidated, Is.False, "保存不代替明確驗證。");
            Assert.That(LogicGraphEditor.Verify(owner, typeof(PlainOwner).GetField(nameof(PlainOwner.Second))), Is.True);
            Assert.That(owner.Second.IsValidated, Is.True);
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void UsageAppliesTheSameTimingsAndTokenRequirementsToTheWorkingDocument()
    {
        var owner = ScriptableObject.CreateInstance<ConfiguredOwner>();
        try
        {
            owner.RequireToken = true;
            owner.Second.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.End });
            var working = owner.Second.DeepCopy();
            var diagnostics = ((IGraphDocumentValidation)working).CollectDiagnostics(owner);
            Assert.That(diagnostics.Select(item => item.Code), Is.EquivalentTo(new[]
                { "logicgraph.timing.disallowed", "logicgraph.external-token.missing" }));
            Assert.That(diagnostics.Single(item => item.Code == "logicgraph.external-token.missing").Location.FieldPath,
                Is.EqualTo("Description"));
            Assert.That(((IGraphDocument)working).RootKeys(owner), Is.EqualTo(new object[] { Timing.Start }));

            working.ActionGroups[0].Timing = Timing.Start;
            working.Tokens.Add(GraphTestTokens.Create("Required", new IntSlot()));
            Assert.That(((IGraphDocumentValidation)working).CollectDiagnostics(owner), Is.Empty);
            Assert.That(owner.Inspected, Is.SameAs(working));
            Assert.That(owner.Second.Tokens, Is.Empty);
            Assert.That(owner.Second.ActionGroups[0].Timing, Is.EqualTo(Timing.End));
            Assert.That(owner.Second.IsValidated, Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UsageErrorsRemainInspectableWithoutBlockingCommit(bool official)
    {
        var owner = ScriptableObject.CreateInstance<ConfiguredOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.Reject = true;
            var original = owner.Second;
            var context = official ? LogicGraphEditor.Context : HGEditorExtensionContext.Default;
            Assert.That(window.BindDocument(owner, SecondBinding(), context), Is.True);
            var commands = window.GetDocumentCommands();
            Assert.That(commands.Validate().Count(item => item.Code == "consumer.rejected"), Is.EqualTo(1));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second, Is.Not.SameAs(original));
            owner.Reject = false;
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second.IsValidated, Is.False);
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void NonWindowSessionReportsUsageFailuresAndAllowsSaving()
    {
        var owner = ScriptableObject.CreateInstance<ConfiguredOwner>();
        try
        {
            var original = owner.Second;
            Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, SecondBinding(), out var session), Is.True);
            owner.ThrowConfiguration = true;
            Assert.That(((IGraphDocument)session.Document).RootKeys(owner), Is.Empty);
            Assert.That(session.CollectDiagnostics().Any(d => d.Code == "logicgraph.usage.configuration-failed"), Is.True);
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Second, Is.Not.SameAs(original));

            owner.ThrowConfiguration = false;
            owner.ThrowValidation = true;
            Assert.That(session.CollectDiagnostics().Any(d => d.Code == "logicgraph.usage.validation-failed"), Is.True);
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            owner.ThrowValidation = false;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void BatchVerificationReportsPerDocumentChangesEvenWhenOwnerRemainsInvalid()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        try
        {
            owner.First.Tokens.Add(null);
            LogAssert.Expect(LogType.Error, new Regex("logicgraph.token.missing"));
            Assert.That(HGOwnerValidation.Verify(owner, out bool changed), Is.False);
            Assert.That(changed, Is.True, "Second 由未驗證變成通過，仍必須記錄變更。");
            Assert.That(owner.First.IsValidated, Is.False);
            Assert.That(owner.Second.IsValidated, Is.True);
            LogAssert.Expect(LogType.Error, new Regex("logicgraph.token.missing"));
            Assert.That(HGOwnerValidation.Verify(owner, out changed), Is.False);
            Assert.That(changed, Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void SharedAssetChangesRevalidateAPlainOwnerThroughTheSameBatchPath()
    {
        var owner = ScriptableObject.CreateInstance<PlainOwner>();
        var asset = ScriptableObject.CreateInstance<TestActionAsset>();
        var action = new ReadValue();
        asset.SetTarget(action);
        var carrier = new GraphNode();
        carrier.SetAsset(asset);
        var slot = new ActionSlot<Pack>();
        slot.SetNode(carrier);
        owner.First.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start, Actions = new() { slot } });
        try
        {
            Assert.That(HGOwnerValidation.Verify(owner), Is.True);
            action.Input.SetNode(new GraphNode());
            LogAssert.Expect(LogType.Error, new Regex("logicgraph.node.empty"));
            Assert.That(HGOwnerValidation.Verify(owner, out bool changed, invalidate: true), Is.False);
            Assert.That(changed, Is.True);
            Assert.That(owner.First.IsValidated, Is.False);
            Assert.That(owner.Second.IsValidated, Is.True);
            action.Input.SetNode(null);
            Assert.That(HGOwnerValidation.Verify(owner, out changed, invalidate: true), Is.True);
            Assert.That(changed, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void EmptyTimingSetRejectsAllGroupsButNullRestoresUnrestrictedChoices()
    {
        var owner = ScriptableObject.CreateInstance<TimingOwner>();
        try
        {
            owner.Graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Start });
            Assert.That(((IGraphDocument)owner.Graph).RootKeys(owner), Is.Empty);
            Assert.That(((IGraphDocumentValidation)owner.Graph).CollectDiagnostics(owner).Single().Code,
                Is.EqualTo("logicgraph.timing.disallowed"));
            owner.Unrestricted = true;
            Assert.That(((IGraphDocument)owner.Graph).RootKeys(owner), Has.Count.EqualTo(2));
            Assert.That(((IGraphDocumentValidation)owner.Graph).CollectDiagnostics(owner), Is.Empty);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    private sealed class TimingOwner : ScriptableObject, ILogicGraphUsage<Timing, Pack>
    {
        public LogicGraph<Timing, Pack> Graph = new();
        public bool Unrestricted;
        public void ConfigureGraph(LogicGraphUsage<Timing, Pack> usage)
            => usage.AllowTimings(Unrestricted ? null : Array.Empty<Timing>());
    }
}
}
