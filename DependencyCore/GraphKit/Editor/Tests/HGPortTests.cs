namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

public class HGPortTests
{
    [Test]
    public void KeyUsesStableOwnerPathAndRole()
    {
        var first = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var rebuilt = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var output = new HGPortKey("node", "/steps[2]", HGPortRole.Output);

        Assert.That(rebuilt, Is.EqualTo(first));
        Assert.That(output, Is.Not.EqualTo(first));
    }

    [Test]
    public void BuildContextRejectsPortFromOldGeneration()
    {
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 4);

        Assert.That(build.Add(AggregatePort(3)), Is.False);
        Assert.That(build.Add(AggregatePort(4)), Is.True);
        Assert.That(graph.Ports, Has.Count.EqualTo(1));
    }

    [Test]
    public void RoleSpecificBuilderAssignsGenerationAndRejectsIncompletePorts()
    {
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 4);
        var presentation = Presentation(new object());

        Assert.That(build.AddInput(new HGPortKey("input", "/value", HGPortRole.Output), new TestSlot(),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(graph.Ports[0].Key.Role, Is.EqualTo(HGPortRole.Input));
        Assert.That(graph.Ports[0].Generation, Is.EqualTo(4));
        Assert.That(build.AddOutput(new HGPortKey("output", "", HGPortRole.Output),
            new HGDelegatePortSource(null, null, _ => true), new HGDelegatePortPolicy(() => true), presentation, true),
            Is.False);
        Assert.That(build.LastResult, Is.EqualTo(HGPortBuildResult.InvalidBinding));
    }

    [Test]
    public void BuildContextExposesReadOnlyNodeFieldAndPortSnapshots()
    {
        var graph = new HGGraphView();
        var node = new HGNodeView { Id = "node", Pos = new Vector2(10f, 20f) };
        var row = new HGRow { OwnerNodeId = "node", Path = "/value", Kind = HGRowKind.InputPort };
        node.Rows.Add(row);
        graph.Nodes.Add(node);
        var build = new HGPortBuildContext(graph, 4);
        var key = new HGPortKey("node", "/value", HGPortRole.Input);

        Assert.That(build.Nodes, Has.Count.EqualTo(1));
        Assert.That(build.Nodes[0].Id, Is.EqualTo("node"));
        Assert.That(build.Fields, Has.Count.EqualTo(1));
        Assert.That(build.Fields[0].InputAnchor.NodeId, Is.EqualTo("node"));
        var presentation = new HGDelegatePortPresentation(new object(), build.Fields[0].InputAnchor,
            () => Vector2.zero, () => Rect.zero, () => true, () => false);
        var locator = (IHGPortPresentationLocator)presentation;
        Assert.That(locator.NodeId, Is.EqualTo("node"));
        Assert.That(locator.FieldPath, Is.EqualTo("/value"));
        var providerKey = new HGPortKey("provider", "/adapter", HGPortRole.Input);
        Assert.That(build.AddInput(providerKey, new TestSlot(), new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(build.TryGetDescriptor(providerKey, out var port), Is.True);
        Assert.That(port.Anchor.NodeId, Is.EqualTo("node"));
        Assert.That(port.Anchor.FieldPath, Is.EqualTo("/value"));
        Assert.That(port.IsPrimaryOutput, Is.False);
    }

    [Test]
    public void RegistryRequiresOneExplicitPrimaryOutputPerSource()
    {
        var registry = new HGPortRegistry(4);
        var source = new GraphNode();
        var first = new HGPortKey("node", "/first", HGPortRole.Output);
        var second = new HGPortKey("node", "/second", HGPortRole.Output);
        var portSource = new HGDelegatePortSource(source, source, _ => true);

        Assert.That(registry.AddOutput(first, portSource, new HGDelegatePortPolicy(() => true), Presentation(new object()), true), Is.True);
        Assert.That(registry.TryGetPrimaryOutputDescriptor(source, out var primary), Is.True);
        Assert.That(primary.Key, Is.EqualTo(first));
        Assert.That(primary.IsPrimaryOutput, Is.True);
        Assert.That(registry.AddOutput(second, portSource, new HGDelegatePortPolicy(() => true), Presentation(new object()), true), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.DuplicatePrimaryOutput));
        Assert.That(registry.Descriptors, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExplicitProviderAddsAdapterThroughBuildSeam()
    {
        var extension = new HGEditorExtensionContext(new TestProvider());
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 9);

        extension.Provider.AddPorts(build);

        Assert.That(graph.Ports, Has.Count.EqualTo(1));
        Assert.That(graph.Ports[0].Key.OwnerId, Is.EqualTo("test-adapter"));
        Assert.That(graph.Ports[0].Binding, Is.InstanceOf<IHGAggregatePortBinding>());
    }

    [Test]
    public void ExtensionContextCollectsOptionalDomainDiagnostics()
    {
        var extension = new HGEditorExtensionContext(new TestProvider());
        var diagnostics = new List<GraphDiagnostic>();

        extension.CollectDiagnostics(null, null, diagnostics);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Code, Is.EqualTo("test.domain.invalid"));
    }

    [Test]
    public void ConnectionCoordinatorAppliesGenerationVisibilityLockAndPolicy()
    {
        const int generation = 5;
        var slot = new TestSlot();
        bool locked = false;
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input),
            new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, source => source.Accepts(slot)),
            new HGDelegatePortPresentation(slot, () => Vector2.zero, () => Rect.zero,
                () => true, () => locked), generation);
        var source = new HGDelegatePortSource(new GraphNode(), null, candidate => ReferenceEquals(candidate, slot));
        var output = new HGPort(new HGPortKey("output", "", HGPortRole.Output),
            new HGOutputPortBinding(source),
            new HGDelegatePortPolicy(() => true),
            new HGDelegatePortPresentation(new object(), () => Vector2.one, () => Rect.zero,
                () => true, () => false), generation);

        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.True);
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        var external = new HGDelegatePortSource(null, null, candidate => ReferenceEquals(candidate, slot));
        Assert.That(HGPortConnection.CheckInputSource(input, external, generation),
            Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(HGPortConnection.CanConnect(input, output, generation + 1), Is.False);
        Assert.That(HGPortConnection.Check(input, output, generation + 1),
            Is.EqualTo(HGPortConnectionResult.StaleGeneration));
        locked = true;
        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.False);
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Locked));
    }

    [Test]
    public void ConnectionCoordinatorPreservesDetailedPolicyRejection()
    {
        const int generation = 5;
        var slot = new TestSlot();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, checkAcceptance: _ => HGPortConnectionResult.DomainRejected),
            Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "/value", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);

        Assert.That(HGPortConnection.Check(input, output, generation),
            Is.EqualTo(HGPortConnectionResult.DomainRejected));
    }

    [Test]
    public void ConnectionCoordinatorRejectsSameRoleAggregateAndHiddenPorts()
    {
        const int generation = 5;
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var secondInput = new HGPort(new HGPortKey("other", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
                () => false, () => false), generation);

        Assert.That(HGPortConnection.Check(input, secondInput, generation), Is.EqualTo(HGPortConnectionResult.SameRole));
        Assert.That(HGPortConnection.Check(input, AggregatePort(generation), generation),
            Is.EqualTo(HGPortConnectionResult.AggregateOnly));
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Hidden));
    }

    [Test]
    public void SourceAcceptancePreservesDetailedRejection()
    {
        var slot = new TestSlot();
        var source = new HGDelegatePortSource(null, null, _ => false,
            _ => HGPortConnectionResult.IncompatibleFamily);

        Assert.That(HGPortConnection.CheckSourceAcceptance(source, slot),
            Is.EqualTo(HGPortConnectionResult.IncompatibleFamily));
    }

    [Test]
    public void TypedDocumentBindingClonesAndWritesOnlyItsDocumentType()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var binding = new HGDocumentBinding<TestDocument>("test-document",
            target => (target as TestDocumentOwner)?.Document,
            (target, document) => (target as TestDocumentOwner).Document = document,
            () => new TestDocument());

        Assert.That(binding.TryRead(owner, out var source), Is.True);
        Assert.That(binding.TryClone(source, out var copy), Is.True);
        Assert.That(copy, Is.TypeOf<TestDocument>());
        Assert.That(copy, Is.Not.SameAs(source));
        Assert.That(binding.TryWrite(owner, copy), Is.True);
        Assert.That(owner.Document, Is.SameAs(copy));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void LegacyDocumentDiscoveryRejectsMultipleCandidates()
    {
        Assert.That(HGModel.FindSystemField(typeof(TestDocumentOwner)), Is.Not.Null);
        Assert.That(HGModel.FindSystemField(typeof(TestMultiDocumentOwner)), Is.Null);
    }

    [Test]
    public void ModelBindsAnIsolatedTypedWorkingDocument()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        Assert.That(model.Data, Is.TypeOf<TestDocument>());
        Assert.That(model.Data, Is.Not.SameAs(owner.Document));
        Assert.That(model.Doc, Is.SameAs(model.Data));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void NodeDescriptorUsesTypedFieldAccessAndRejectsDuplicateIds()
    {
        var descriptor = HGFieldDescriptor.Create<TestValueOwner, int>("count",
            target => target.Count, (target, value) => target.Count = value, "Count");
        var target = new TestValueOwner { Count = 2 };

        Assert.That(descriptor.Read(target), Is.EqualTo(2));
        descriptor.Write(target, 7);
        Assert.That(target.Count, Is.EqualTo(7));
        Assert.Throws<ArgumentException>(() => new HGNodeDescriptor(typeof(TestValueOwner),
            new[] { descriptor, descriptor }));
    }

    [Test]
    public void DescriptorReplacesReflectionRowsAndMeasuresItsValueDrawer()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count",
            target => target.Count, (target, value) => target.Count = value, "Count");
        var drawer = new TestValueDrawer();
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner { Count = 2, Ignored = 3 } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(view.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(drawer));
        Assert.That(view.Nodes[0].Rows[0].Height, Is.EqualTo(38f));
        Assert.That(drawer.MeasuredTarget.Count, Is.EqualTo(2));
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(180f));
    }

    [Test]
    public void DescriptorCarriesExistingRowPresentationMetadata()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, hideLabel: true, labelWidthUnits: 4, labelWidthRatio: 0.5f,
            forceEnumButtons: true);
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        var row = view.Nodes[0].Rows[0];

        Assert.That(row.HideLabel, Is.True);
        Assert.That(row.LabelWidthUnits, Is.EqualTo(4));
        Assert.That(row.LabelWidthRatio, Is.EqualTo(0.5f));
        Assert.That(row.ForceEnumButtons, Is.True);
    }

    [Test]
    public void DescriptorDrawerMeasuresWithItsConfiguredLabelWidth()
    {
        var drawer = new TestValueDrawer();
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, labelWidthUnits: 4);
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);

        Assert.That(drawer.MeasuredWidth, Is.EqualTo(200f));
    }

    [Test]
    public void DescriptorVisibilityHidesRowsAndFailsOpenWithOneDiagnostic()
    {
        var hidden = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, isVisible: _ => false);
        var hiddenView = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { hidden }), null));
        Assert.That(hiddenView.Nodes[0].Rows, Is.Empty);

        var failing = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, isVisible: _ => throw new InvalidOperationException("broken"));
        var failingView = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { failing }), null));
        var report = new HGReport();
        report.ReplaceGraphViewDiagnostics(failingView.Diagnostics);

        Assert.That(failingView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(failingView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(failingView.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.visibility-failed"));
        Assert.That(report.ErrorCount, Is.EqualTo(1));
    }

    [Test]
    public void DescriptorSlotUsesTheExistingInputPortRow()
    {
        var slot = new TestSlot();
        var field = HGFieldDescriptor.CreateSlot<TestSlotOwner, TestSlot>("input", target => target.Input, "Input");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestSlotOwner { Input = slot } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.InputPort));
        Assert.That(view.Nodes[0].Rows[0].InputSlot, Is.SameAs(slot));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(field.ReadOnly, Is.False);
    }

    [Test]
    public void DescriptorSlotFactoryNormalizesOnlyTheWorkingTarget()
    {
        var field = HGFieldDescriptor.CreateSlotWithFactory<TestSlotOwner, TestSlot>("input", target => target.Input,
            (target, value) => target.Input = value, _ => new TestSlot(), "Input");
        var target = new TestSlotOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null));

        Assert.That(target.Input, Is.Not.Null);
        Assert.That(view.Normalized, Is.True);
        Assert.That(view.Nodes[0].Rows[0].InputSlot, Is.SameAs(target.Input));
    }

    [Test]
    public void DescriptorGroupControlsExpansionWithoutLeafTypeInference()
    {
        var field = HGFieldDescriptor.CreateGroup<TestGroupOwner, TestValueOwner>("child", target => target.Child, "Child");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestGroupOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestGroupOwner { Child = new TestValueOwner { Count = 2 } } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.Group));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(view.Nodes[0].Rows[0].Children, Is.Not.Empty);
    }

    [Test]
    public void DescriptorListUsesTheExistingListItemSource()
    {
        var field = HGFieldDescriptor.CreateList<TestListOwner, int>("values", target => target.Values, "Values");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestListOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestListOwner { Values = new List<int> { 2 } } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.List));
        Assert.That(view.Nodes[0].Rows[0].Items, Is.TypeOf<HGListItemSource>());
        Assert.That(((HGListItemSource)view.Nodes[0].Rows[0].Items).ElementType, Is.EqualTo(typeof(int)));
        Assert.That(field.ReadOnly, Is.False);
    }

    [Test]
    public void DescriptorListFactoryNormalizesOnlyTheWorkingTarget()
    {
        var field = HGFieldDescriptor.CreateListWithFactory<TestListOwner, List<int>, int>("values", target => target.Values,
            (target, value) => target.Values = value, _ => new List<int>(), "Values");
        var target = new TestListOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestListOwner), new[] { field }), null));

        Assert.That(target.Values, Is.Not.Null);
        Assert.That(view.Normalized, Is.True);
        Assert.That(view.Nodes[0].Rows[0].Items, Is.TypeOf<HGListItemSource>());
    }

    [Test]
    public void GraphDiagnosticKeepsOnlyStableLocationIdentifiers()
    {
        var location = new GraphDiagnosticLocation("document", "focus", "node", "token", "/field");
        var diagnostic = new GraphDiagnostic("test.invalid", GraphDiagnosticSeverity.Error, "Invalid value", location, "Fix it");

        Assert.That(diagnostic.Code, Is.EqualTo("test.invalid"));
        Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        Assert.That(diagnostic.Location.FieldPath, Is.EqualTo("/field"));
        Assert.Throws<ArgumentException>(() => new GraphDiagnostic("", GraphDiagnosticSeverity.Error, "Invalid value"));
    }

    [Test]
    public void ReportReplacesPortResolutionDiagnosticsWithoutRemovingStructuralIssues()
    {
        var report = new HGReport();
        report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.slot.invalid", GraphDiagnosticSeverity.Error,
            "Structural failure"), "Slot", null, null, null));
        report.ReplaceGraphViewDiagnostics(new[]
        {
            new GraphDiagnostic("graphkit.port-resolution.input-unresolved", GraphDiagnosticSeverity.Error,
                "Input unresolved", new GraphDiagnosticLocation(focusId: "focus", fieldPath: "/input")),
        });
        report.ReplaceGraphViewDiagnostics(new[]
        {
            new GraphDiagnostic("graphkit.metadata.visibility-failed", GraphDiagnosticSeverity.Warning,
                "Visibility failed", new GraphDiagnosticLocation(fieldPath: "/field")),
        });

        Assert.That(report.Issues, Has.Count.EqualTo(2));
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.slot.invalid"), Is.True);
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.port-resolution.input-unresolved"), Is.False);
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.metadata.visibility-failed"), Is.True);
    }

    private static HGPort AggregatePort(int generation, string owner = "aggregate")
        => new HGPort(new HGPortKey(owner, "/items", HGPortRole.Aggregate),
            HGAggregatePortBinding.Instance,
            new HGDelegatePortPolicy(() => false),
            new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
                () => true, () => false), generation);

    private static IHGPortPresentation Presentation(object owner)
        => new HGDelegatePortPresentation(owner, () => Vector2.zero, () => Rect.zero,
            () => true, () => false);

    private sealed class TestProvider : IHGEditorExtensionProvider, IHGEditorDiagnosticProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context)
            => context.AddAggregate(new HGPortKey("test-adapter", "/aggregate", HGPortRole.Aggregate),
                 HGPortTests.Presentation(new object()));

        public void CollectDiagnostics(UnityEngine.Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
            => diagnostics.Add(new GraphDiagnostic("test.domain.invalid", GraphDiagnosticSeverity.Error, "Invalid test domain"));
    }

    private sealed class TestMetadataProvider : IHGEditorMetadataProvider
    {
        private readonly HGNodeDescriptor descriptor;
        private readonly IHGValueDrawer drawer;

        public TestMetadataProvider(HGNodeDescriptor descriptor, IHGValueDrawer drawer)
        {
            this.descriptor = descriptor;
            this.drawer = drawer;
        }

        public bool TryGetNodeDescriptor(Type nodeType, out HGNodeDescriptor result)
        {
            result = nodeType == descriptor.NodeType ? descriptor : null;
            return result != null;
        }

        public bool TryGetValueDrawer(Type valueType, out IHGValueDrawer result)
        {
            result = valueType == typeof(int) ? drawer : null;
            return result != null;
        }
    }

    private sealed class TestValueDrawer : IHGValueDrawer
    {
        public TestValueOwner MeasuredTarget;
        public float MeasuredWidth;

        public float Measure(in HGValueDrawerContext context, float width)
        {
            MeasuredTarget = context.Target as TestValueOwner;
            MeasuredWidth = width;
            return 35f;
        }

        public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value)
            => HGValueDrawerResult.Unchanged(value);
    }

    private sealed class TestSlot : GraphSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }

    private sealed class TestDocumentOwner : ScriptableObject
    {
        public TestDocument Document;
    }

    private sealed class TestMultiDocumentOwner : ScriptableObject
    {
        public TestDocument First;
        public TestDocument Second;
    }

    private sealed class TestValueOwner
    {
        public int Count;
        public int Ignored;
    }

    private sealed class TestSlotOwner
    {
        public TestSlot Input;
    }

    private sealed class TestGroupOwner
    {
        public TestValueOwner Child;
    }

    private sealed class TestListOwner
    {
        public List<int> Values;
    }

    private sealed class TestDocument : IGraphDocument
    {
        public List<GraphNode> Orphans { get; } = new();
        public List<GraphToken> Tokens { get; } = new();
        public IList Roots { get; } = new ArrayList();
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(TestSlot);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Test";

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => new TestDocument();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new List<object>();
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => null;
        public object AddRoot(object key) => null;
    }
}
}
