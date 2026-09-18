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
    public void RegistryRejectsPortFromOldGeneration()
    {
        var registry = new HGPortRegistry(4);

        Assert.That(registry.Add(AggregatePort(3)), Is.False);
        Assert.That(registry.Add(AggregatePort(4)), Is.True);
        Assert.That(registry.Ports, Has.Count.EqualTo(1));
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
        var row = new HGRow
        {
            OwnerNodeId = "node",
            Path = "/value",
            Kind = HGRowKind.InputPort,
            InputPortPosition = new Vector2(12f, 34f),
        };
        node.Rows.Add(row);
        graph.Nodes.Add(node);
        var build = new HGPortBuildContext(graph, 4);
        var key = new HGPortKey("node", "/value", HGPortRole.Input);

        Assert.That(build.Nodes, Has.Count.EqualTo(1));
        Assert.That(build.Nodes[0].Id, Is.EqualTo("node"));
        Assert.That(build.Fields, Has.Count.EqualTo(1));
        Assert.That(build.Fields[0].InputAnchor.NodeId, Is.EqualTo("node"));
        Assert.That(build.Fields[0].InputAnchor.Position, Is.EqualTo(new Vector2(12f, 34f)));
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
    public void CatalogSlotWritesToCatalogUsesItsOwnOverride()
    {
        Assert.That(new TestCatalogSlot().WritesToCatalog, Is.False);
    }

    [Test]
    public void LinkOwnersExposeRoleSpecificEndpoints()
    {
        var input = new HGNodeView();
        var output = new HGNodeView();
        var link = new HGLink { InputOwner = input, OutputOwner = output };

        Assert.That(link.InputOwner, Is.SameAs(input));
        Assert.That(link.OutputOwner, Is.SameAs(output));
    }

    [Test]
    public void SlotAndTokenExposeFamilyType()
    {
        var slot = new TestFormulaSlot();
        var token = new GraphToken("value", slot);

        Assert.That(slot.FamilyType, Is.EqualTo(typeof(TestFormulaSlot)));
        Assert.That(token.FamilyType, Is.EqualTo(typeof(TestFormulaSlot)));
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
    public void RegistryRejectsDuplicateMissingOwnerAndEmptySourceWithoutMutatingIndexes()
    {
        var registry = new HGPortRegistry(4);
        var inputKey = new HGPortKey("node", "/input", HGPortRole.Input);
        var policy = new HGDelegatePortPolicy(() => true);

        Assert.That(registry.AddInput(inputKey, new TestSlot(), policy, Presentation(new object())), Is.True);
        Assert.That(registry.AddInput(inputKey, new TestSlot(), policy, Presentation(new object())), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.DuplicateKey));
        Assert.That(registry.AddInput(new HGPortKey("missing", "/input", HGPortRole.Input), new TestSlot(), policy,
            Presentation(null)), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.MissingOwner));
        Assert.That(registry.AddOutput(new HGPortKey("output", "/source", HGPortRole.Output),
            new HGDelegatePortSource(null, null, _ => true), policy, Presentation(new object())), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.InvalidBinding));
        Assert.That(registry.Ports, Has.Count.EqualTo(1));
        Assert.That(registry.Descriptors, Has.Count.EqualTo(1));
        Assert.That(registry.TryGet(inputKey, out _), Is.True);
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
    public void ExplicitEditorProfileOverridesLegacyDocumentCapabilities()
    {
        var context = new HGEditorExtensionContext(new TestProvider(), profile: new HGEditorProfile(HGCapabilities.Catalogs));
        var legacyContext = new HGEditorExtensionContext(new TestProvider());
        var legacyDocument = new TestDocument(HGCapabilities.Tokens);

        Assert.That(context.CapabilitiesOf(new TestDocument()), Is.EqualTo(HGCapabilities.Catalogs));
        Assert.That(HGGraph.Has(context, new TestDocument(), HGCapabilities.Catalogs), Is.True);
        Assert.That(HGGraph.Has(context, new TestDocument(), HGCapabilities.Tokens), Is.False);
        Assert.That(legacyContext.CapabilitiesOf(legacyDocument), Is.EqualTo(HGCapabilities.Tokens));
    }

    [Test]
    public void ModelRoutesRootOperationsThroughItsExplicitAdapter()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var adapter = new TestRootAdapter();
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        model.SetRootAdapter(adapter);

        Assert.That(model.AvailableRootKeys, Is.EqualTo(new object[] { "custom" }));
        Assert.That(model.RootItemSlotType, Is.EqualTo(typeof(TestSlot)));
        Assert.That(model.ReadRootGroups()[0].Root, Is.SameAs(adapter.Root));
        Assert.That(model.HasRoot("custom"), Is.True);

        var added = model.AddRoot("new-root");
        Assert.That(adapter.AddedKey, Is.EqualTo("new-root"));
        Assert.That(model.NewRootItem(added.Items), Is.TypeOf<TestSlot>());
        model.RemoveRoot(added);
        Assert.That(adapter.RemovedRoot, Is.SameAs(added.Root));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void ModelKeepsRootKeyIdentityWhenDisplayTextCollides()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var first = new SameLabelRootKey();
        var second = new SameLabelRootKey();
        var adapter = new TestRootAdapter(TestRootKind.Enum, "same", first, second);
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        model.SetRootAdapter(adapter);

        Assert.That(model.AvailableRootKeys, Is.EqualTo(new object[] { TestRootKind.Enum, "same", first, second }));
        Assert.That(model.HasRoot(TestRootKind.Enum), Is.True);
        Assert.That(model.HasRoot("same"), Is.True);
        Assert.That(model.HasRoot(first), Is.True);
        Assert.That(model.HasRoot(second), Is.True);
        Assert.That(model.HasRoot(new SameLabelRootKey()), Is.False);
        var rootObjects = new List<object>();
        var ids = new HashSet<string>();
        foreach (var root in model.ReadRootGroups())
        {
            rootObjects.Add(root.Root);
            Assert.That(ids.Add(HGGraph.GroupHeadId(model, root.Root)), Is.True);
        }
        var built = HGGraph.Build(model, rootObjects, null, "roots", "Roots");
        Assert.That(built.Nodes.Count, Is.EqualTo(4));
        Assert.That(built.Diagnostics, Is.Empty);
        model.AddRoot(second);
        Assert.That(adapter.AddedKey, Is.SameAs(second));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void DocumentRootAdapterKeepsNonSlotRootsAndFixedItemsReadable()
    {
        var document = new NonSlotDocument();
        var firstRoot = new NonSlotRoot { Items = new[] { 3, 5 } };
        document.Roots.Add(firstRoot);
        var adapter = HGModel.HGDocumentRootAdapter.Instance;

        var roots = adapter.ReadRoots(document);

        Assert.That(roots, Has.Count.EqualTo(1));
        Assert.That(roots[0].Root, Is.SameAs(firstRoot));
        Assert.That(roots[0].Items, Is.SameAs(firstRoot.Items));
        Assert.That(roots[0].Items.IsFixedSize, Is.True);
        Assert.That(adapter.CreateItem(document), Is.EqualTo(0));
        Assert.That(adapter.AddRoot(document, "second").Root, Is.TypeOf<NonSlotRoot>());
        Assert.That(adapter.RemoveRoot(document, firstRoot), Is.True);
        Assert.That(adapter.RemoveRoot(document, firstRoot), Is.False);
    }

    [Test]
    public void ListItemSourceRejectsStructuralChangesForArraysButAllowsValueWrites()
    {
        var values = new[] { 1, 2 };
        var source = new HGListItemSource(values, typeof(int));

        Assert.That(source.CanEditStructure, Is.False);
        Assert.That(source.Add(3), Is.False);
        Assert.That(source.Insert(0, 3), Is.False);
        Assert.That(source.RemoveAt(0), Is.False);
        Assert.That(source.Move(0, 1), Is.False);
        Assert.That(source.Set(1, 7), Is.True);
        Assert.That(values, Is.EqualTo(new[] { 1, 7 }));
    }

    [Test]
    public void TokenFreeDocumentBindsWithoutAnITokenOwner()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var model = new HGModel();

        Assert.That(owner.Document, Is.Not.InstanceOf<ITokenOwner>());
        Assert.That(model.Bind(owner), Is.True);
        Assert.That(model.OwnerTokens, Is.Empty);

        UnityEngine.Object.DestroyImmediate(owner);
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
    public void ConnectionCoordinatorIsSymmetricAndAggregateCannotStartOrConnect()
    {
        const int generation = 5;
        var slot = new TestSlot();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, source => source.Accepts(slot)), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "/value", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var aggregate = AggregatePort(generation);

        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(HGPortConnection.Check(output, input, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(aggregate.CanStart, Is.False);
        Assert.That(HGPortConnection.Check(input, aggregate, generation), Is.EqualTo(HGPortConnectionResult.AggregateOnly));
        Assert.That(HGPortConnection.Check(aggregate, input, generation), Is.EqualTo(HGPortConnectionResult.AggregateOnly));
    }

    [Test]
    public void LinkPortResolverUsesCustomPrimaryOutputAndClassifiesUnresolvedEndpoints()
    {
        const int generation = 6;
        var inputRow = new HGRow { OwnerNodeId = "input", Path = "/value" };
        var source = new GraphNode();
        source.EnsureId();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("provider", "/custom", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(source, source, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation, true);
        var ports = new Dictionary<HGPortKey, HGPort> { [input.Key] = input, [output.Key] = output };
        var primaryOutputs = new Dictionary<GraphNode, HGPort> { [source] = output };
        var resolved = new HGLink { ParentRow = inputRow, OutputOwner = new HGNodeView { Carrier = source } };

        Assert.That(HGLinkPortResolver.Resolve(resolved, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.Resolved));
        Assert.That(resolved.InputPort, Is.SameAs(input));
        Assert.That(resolved.OutputPort, Is.SameAs(output));

        var missingInput = new HGLink { ParentRow = new HGRow { OwnerNodeId = "missing", Path = "/value" } };
        Assert.That(HGLinkPortResolver.Resolve(missingInput, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.InputUnresolved));

        var missingSource = new HGLink { ParentRow = inputRow };
        Assert.That(HGLinkPortResolver.Resolve(missingSource, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.OutputUnresolved));

        var missingPrimary = new HGLink { ParentRow = inputRow, OutputOwner = new HGNodeView { Carrier = source } };
        Assert.That(HGLinkPortResolver.Resolve(missingPrimary, ports, new Dictionary<GraphNode, HGPort>(), generation),
            Is.EqualTo(HGLinkPortResolution.PrimaryOutputMissing));
        Assert.That(missingPrimary.InputPort, Is.SameAs(input));
        Assert.That(missingPrimary.OutputPort, Is.Null);
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
    public void LegacyDocumentDiscoveryRejectsMissingCandidate()
    {
        Assert.That(HGModel.FindSystemField(typeof(TestNoDocumentOwner)), Is.Null);
    }

    [Test]
    public void TypedDocumentBindingRejectsAliasedClone()
    {
        var document = new TestDocument(deepCopy: current => current);
        var binding = new HGDocumentBinding<TestDocument>("test-document", _ => document, (owner, copy) => { });

        Assert.That(binding.TryClone(document, out _), Is.False);
    }

    [Test]
    public void TypedDocumentBindingRejectsNullAndWrongTypeClone()
    {
        var binding = new HGDocumentBinding<TestDocument>("test-document", _ => null, (owner, copy) => { });

        Assert.That(binding.TryClone(new TestDocument(deepCopy: _ => null), out _), Is.False);
        Assert.That(binding.TryClone(new TestDocument(deepCopy: _ => new object()), out _), Is.False);
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

    [TestCase(false)]
    [TestCase(true)]
    public void ModelCommitGuardsExplicitAndLegacyBindingsAgainstOwnerReplacement(bool legacy)
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        try
        {
            owner.Document = new TestDocument();
            var model = new HGModel();
            var binding = new HGDocumentBinding<TestDocument>("Document", x => ((TestDocumentOwner)x).Document,
                (x, document) => ((TestDocumentOwner)x).Document = document);
            Assert.That(legacy ? model.Bind(owner) : model.Bind(owner, binding), Is.True);
            model.MarkDirty();
            var working = model.Data;
            var external = new TestDocument();
            owner.Document = external;
            Assert.That(model.Save(), Is.False);
            Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
            Assert.That(model.Data, Is.SameAs(working));
            Assert.That(owner.Document, Is.SameAs(external));
            Assert.That(model.Dirty, Is.True);
            Assert.That(model.CanUndo, Is.True);
            Assert.That(model.TryReload(), Is.True);
            Assert.That(model.Save(), Is.True);
            Assert.That(model.Save(), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void ModelFailedReloadKeepsWorkingCopyHistoryAndConflictBaseline()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        try
        {
            owner.Document = new TestDocument();
            bool failRead = false;
            var binding = new HGDocumentBinding<TestDocument>("Document",
                x => failRead ? throw new InvalidOperationException("read failed") : ((TestDocumentOwner)x).Document,
                (x, document) => ((TestDocumentOwner)x).Document = document);
            var model = new HGModel();
            Assert.That(model.Bind(owner, binding), Is.True);
            model.MarkDirty();
            var working = model.Data;
            owner.Document = new TestDocument();
            failRead = true;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[GraphKit] 文件 'Document' 重載失敗，目前工作副本與歷程保留：read failed");
            Assert.That(model.TryReload(), Is.False);
            Assert.That(model.Data, Is.SameAs(working));
            Assert.That(model.Dirty, Is.True);
            Assert.That(model.CanUndo, Is.True);
            failRead = false;
            Assert.That(model.Save(), Is.False);
            Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void DescriptorRejectsInvalidWritesWithoutChangingTheTarget()
    {
        var writable = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value);
        var readOnly = HGFieldDescriptor.Create<TestValueOwner, int>("readonly", target => target.Count);
        var text = HGFieldDescriptor.Create<TestValueOwner, string>("text", target => target.Text,
            (target, value) => target.Text = value);
        var target = new TestValueOwner { Count = 2, Text = "before" };

        Assert.That(writable.TryWrite(target, "wrong", out var wrongType), Is.False);
        Assert.That(wrongType, Is.Null);
        Assert.That(writable.TryWrite(target, null, out var nullValue), Is.False);
        Assert.That(nullValue, Is.Null);
        Assert.That(readOnly.TryWrite(target, 7, out var readOnlyValue), Is.False);
        Assert.That(readOnlyValue, Is.Null);
        Assert.That(text.TryWrite(target, null, out var nullableValue), Is.True);
        Assert.That(nullableValue, Is.Null);
        Assert.That(target.Count, Is.EqualTo(2));
        Assert.That(target.Text, Is.Null);
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
    public void HiddenLabelDrawerMeasuresTheActualDrawWidthAndReportsMeasureFailure()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, hideLabel: true);
        var drawer = new TestValueDrawer();
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        var row = view.Nodes[0].Rows[0];
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(HGGraph.ValueFieldRect(new Rect(0, 0, 300, row.Height), row).width));
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(276f));
        metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), new ThrowingDrawer());
        view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        Assert.That(view.Diagnostics.Exists(issue => issue.Code == "graphkit.metadata.drawer-failed"), Is.True);
        Assert.That(view.Nodes[0].Rows[0].DrawerError, Is.Not.Empty);
    }

    [Test]
    public void RegistryRemainsAtomicWhenPresentationThrowsAndCollectionsAreReadOnly()
    {
        var registry = new HGPortRegistry(1);
        var key = new HGPortKey("node", "/input", HGPortRole.Input);
        var broken = new HGDelegatePortPresentation(new object(), () => throw new InvalidOperationException("position"),
            () => Rect.zero, () => true, () => false);
        Assert.That(registry.AddInput(key, new TestSlot(), new HGDelegatePortPolicy(() => true), broken), Is.False);
        Assert.That(registry.Ports, Is.Empty);
        Assert.That(registry.TryGet(key, out _), Is.False);
        Assert.That(registry.Descriptors, Is.Empty);
        Assert.Throws<NotSupportedException>(() => ((IList<HGPort>)registry.Ports).Clear());
        Assert.That(typeof(HGPortBuildContext).GetMethod("TryGet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), Is.Null);
    }

    private sealed class ThrowingDrawer : IHGValueDrawer
    {
        public float Measure(in HGValueDrawerContext context, float width) => throw new InvalidOperationException("measure");
        public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value) => throw new InvalidOperationException("draw");
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
    public void MetadataProvidersKeepTheirDrawersScopedToTheirOwnContext()
    {
        var firstDrawer = new TestValueDrawer();
        var secondDrawer = new TestValueDrawer();
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value);
        var descriptor = new HGNodeDescriptor(typeof(TestValueOwner), new[] { field });

        var first = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "first", "First",
            metadata: new TestMetadataProvider(descriptor, firstDrawer));
        var second = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "second", "Second",
            metadata: new TestMetadataProvider(descriptor, secondDrawer));

        Assert.That(first.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(firstDrawer));
        Assert.That(second.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(secondDrawer));
        Assert.That(first.Nodes[0].Rows[0].ValueDrawer, Is.Not.SameAs(second.Nodes[0].Rows[0].ValueDrawer));
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
    public void ReflectionShowIfUsesCachedConditionsAndReportsFailuresInTheGraph()
    {
        var hiddenView = HGGraph.Build(new HGModel(), new object[] { new ShowIfOwner() }, null, "test", "Test");
        var shownView = HGGraph.Build(new HGModel(), new object[] { new ShowIfOwner { Show = true } }, null, "test", "Test");
        var invalidView = HGGraph.Build(new HGModel(), new object[] { new InvalidShowIfOwner() }, null, "test", "Test");
        var nonBooleanView = HGGraph.Build(new HGModel(), new object[] { new NonBooleanShowIfOwner() }, null, "test", "Test");
        var throwingView = HGGraph.Build(new HGModel(), new object[] { new ThrowingShowIfOwner() }, null, "test", "Test");

        Assert.That(hiddenView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(shownView.Nodes[0].Rows, Has.Count.EqualTo(2));
        Assert.That(invalidView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(invalidView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Diagnostics[0].Message, Does.Contain("Visible"));
        Assert.That(throwingView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(throwingView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(throwingView.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.visibility-failed"));
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
    public void DescriptorFactoryFailureReportsOneLocationAndDoesNotWrite()
    {
        var field = HGFieldDescriptor.CreateSlotWithFactory<TestSlotOwner, TestSlot>("input", target => target.Input,
            (target, value) => target.Input = value, _ => throw new InvalidOperationException("broken"), "Input");
        var target = new TestSlotOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null));

        Assert.That(target.Input, Is.Null);
        Assert.That(view.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(view.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.factory-failed"));
        Assert.That(view.Diagnostics[0].Location.FieldPath, Is.EqualTo("/input"));
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

    private sealed class TestRootAdapter : IHGRootAdapter
    {
        private readonly ArrayList items = new();
        private readonly List<HGRootGroupView> roots = new();

        public object Root { get; }
        public object AddedKey { get; private set; }
        public object RemovedRoot { get; private set; }

        public TestRootAdapter(params object[] rootKeys)
        {
            if (rootKeys == null || rootKeys.Length == 0) rootKeys = new object[] { "custom" };
            foreach (object key in rootKeys)
                roots.Add(new HGRootGroupView { Root = new object(), RootKey = key, Items = items,
                    StableId = key is SameLabelRootKey ? Guid.NewGuid().ToString("N") : null });
            Root = roots[0].Root;
        }

        public IReadOnlyList<object> RootKeys(IGraphDocument document, UnityEngine.Object owner)
        {
            var keys = new List<object>();
            foreach (var root in roots) keys.Add(root.RootKey);
            return keys;
        }

        public IReadOnlyList<HGRootGroupView> ReadRoots(IGraphDocument document)
            => roots;

        public HGRootGroupView AddRoot(IGraphDocument document, object rootKey)
        {
            AddedKey = rootKey;
            return new HGRootGroupView { Root = Root, RootKey = rootKey, Items = items };
        }

        public bool RemoveRoot(IGraphDocument document, object root)
        {
            RemovedRoot = root;
            return ReferenceEquals(root, Root);
        }

        public Type ItemType(IGraphDocument document) => typeof(TestSlot);
        public object CreateItem(IGraphDocument document) => new TestSlot();
    }

    private enum TestRootKind
    {
        Enum,
    }

    private sealed class SameLabelRootKey
    {
        public override string ToString() => "same";
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

    private sealed class TestCatalogSlot : CatalogSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override bool AcceptsCatalogObject(GraphNodeContent pack) => false;

        public override bool WritesToCatalog => false;
    }

    private sealed class TestFormulaSlot : FormulaSlotBase
    {
        private GraphNode node;
        private object defaultValue;

        public override GraphNode Node => node;
        public override Type ResultType => typeof(int);
        public override Type PackType => typeof(object);
        public override object DefaultObject { get => defaultValue; set => defaultValue = value; }
        public override Type BodyBaseType => null;
        public override Type AssetBaseType => null;
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

    private sealed class TestNoDocumentOwner : ScriptableObject { }

    private sealed class TestValueOwner
    {
        public int Count;
        public int Ignored;
        public string Text;
    }

    private sealed class ShowIfOwner
    {
        public bool Show;
        [HGShowIf(nameof(Show))] public int Conditional;
    }

    private sealed class InvalidShowIfOwner
    {
        [HGShowIf("Missing")] public int Conditional;
    }

    private sealed class NonBooleanShowIfOwner
    {
        [HGShowIf(nameof(Visible))] public int Conditional;
        private string Visible => "not a bool";
    }

    private sealed class ThrowingShowIfOwner
    {
        [HGShowIf(nameof(Throws))] public int Conditional;
        private bool Throws => throw new InvalidOperationException("broken");
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

    private sealed class NonSlotRoot
    {
        public int[] Items;
    }

    private sealed class NonSlotDocument : IGraphDocument
    {
        public List<GraphNode> Orphans { get; } = new();
        public IList Roots { get; } = new ArrayList();
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(int);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Non-slot roots";

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => new NonSlotDocument();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { "first", "second" };
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => (root as NonSlotRoot)?.Items;
        public object AddRoot(object key)
        {
            var root = new NonSlotRoot { Items = Array.Empty<int>() };
            Roots.Add(root);
            return root;
        }
    }

    private sealed class TestDocument : IGraphDocument
    {
        private readonly HGCapabilities capabilities;

        private readonly Func<TestDocument, object> deepCopy;

        public TestDocument(HGCapabilities capabilities = HGCapabilities.None, Func<TestDocument, object> deepCopy = null)
        {
            this.capabilities = capabilities;
            this.deepCopy = deepCopy;
        }

        public List<GraphNode> Orphans { get; } = new();
        public IList Roots { get; } = new ArrayList();
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(TestSlot);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => capabilities;
        public string WindowTitle => "Test";

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => deepCopy != null ? deepCopy(this) : new TestDocument();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new List<object>();
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => null;
        public object AddRoot(object key) => null;
    }
}
}
