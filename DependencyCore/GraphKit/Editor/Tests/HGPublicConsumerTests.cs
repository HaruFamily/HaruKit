namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Third-consumer proof. This file uses only public Runtime and Editor contracts, never editor implementation types.</summary>
public sealed class HGPublicConsumerTests
{
    [Test]
    public void PublicSession_ConnectsCommitsAndCancelsWithoutLeakingAcrossDocuments()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.A = ConsumerDocument.Create();
        owner.B = ConsumerDocument.Create();
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        Assert.That(session.DocumentId, Is.EqualTo("Consumer.B"));
        var registry = session.CreatePortRegistry();
        ConsumerSlot input = session.Document.Root.Items[0];
        GraphNode source = session.Document.Orphans[0];
        var inputKey = new HGPortKey("consumer", "/root/input", HGPortRole.Input);
        var outputKey = new HGPortKey("consumer", "/source", HGPortRole.Output);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);

        Assert.That(registry.AddInput(inputKey, input, new HGDelegatePortPolicy(() => true, port => port.Accepts(input)), presentation), Is.True);
        Assert.That(registry.AddOutput(outputKey, new HGDelegatePortSource(source, source, _ => true),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(session.Connect(registry, outputKey, inputKey), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(input.Node, Is.SameAs(source));
        Assert.That(session.IsDirty, Is.True);
        Assert.That(session.CanUndo, Is.True);
        Assert.That(session.Document.Orphans, Has.Count.EqualTo(1));
        Assert.That(owner.A.Root.Items[0].Node, Is.Null);
        Assert.That(owner.B.Root.Items[0].Node, Is.Null);
        var unchangedRegistry = session.CreatePortRegistry();
        var unchangedInput = session.Document.Root.Items[0];
        var generationBeforeNoChange = session.Generation;
        Assert.That(unchangedRegistry.AddInput(inputKey, unchangedInput,
            new HGDelegatePortPolicy(() => true, port => port.Accepts(unchangedInput)), presentation), Is.True);
        Assert.That(unchangedRegistry.AddOutput(outputKey,
            new HGDelegatePortSource(unchangedInput.Node, unchangedInput.Node, _ => true),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(session.Connect(unchangedRegistry, outputKey, inputKey), Is.EqualTo(HGSessionCommandResult.NoChange));
        Assert.That(session.Generation, Is.EqualTo(generationBeforeNoChange));
        Assert.That(session.Disconnect(registry, inputKey), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
        Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Null);
        Assert.That(session.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Not.Null);

        var replaceRegistry = session.CreatePortRegistry();
        var replacementInput = session.Document.Root.Items[0];
        var previous = replacementInput.Node;
        var replacement = session.Document.Orphans[0];
        Assert.That(replaceRegistry.AddInput(inputKey, replacementInput,
            new HGDelegatePortPolicy(() => true, port => port.Accepts(replacementInput)), presentation), Is.True);
        Assert.That(session.ReplaceSource(replaceRegistry, inputKey,
            new HGDelegatePortSource(replacement, replacement, _ => true)), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.SameAs(replacement));
        Assert.That(session.Document.Orphans, Contains.Item(previous));

        Assert.That(session.DeleteNode(replacement), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Null);
        Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Not.Null);

        Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.IsDirty, Is.False);
        Assert.That(owner.B.Root.Items[0].Node, Is.Not.Null);
        Assert.That(owner.A.Root.Items[0].Node, Is.Null);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var reopened), Is.True);
        var reconnectRegistry = reopened.CreatePortRegistry();
        var reopenedInput = reopened.Document.Root.Items[0];
        Assert.That(reconnectRegistry.AddInput(inputKey, reopenedInput,
            new HGDelegatePortPolicy(() => true, port => port.Accepts(reopenedInput)), presentation), Is.True);
        Assert.That(reopened.Disconnect(reconnectRegistry, inputKey), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(reopened.Document.Root.Items[0].Node, Is.Null);
        Assert.That(reopened.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(reopened.Document.Root.Items[0].Node, Is.Not.Null);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicExtensionContext_CollectsConsumerDomainDiagnostic()
    {
        var context = new HGEditorExtensionContext(new ConsumerProvider());
        var diagnostics = new List<GraphDiagnostic>();

        context.CollectDiagnostics(null, null, diagnostics);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Code, Is.EqualTo("consumer.root.required"));
    }

    [Test]
    public void PublicExtensionContext_ProvidesTypedConsumerMetadataAndCustomDrawer()
    {
        var provider = new ConsumerProvider();
        var context = new HGEditorExtensionContext(provider, provider, new HGEditorProfile(HGCapabilities.None));
        var body = new ConsumerBody { Percent = new ConsumerPercent(0.25f), Input = new ConsumerSlot() };

        Assert.That(context.Metadata.TryGetNodeDescriptor(typeof(ConsumerBody), out var descriptor), Is.True);
        Assert.That(descriptor.Fields, Has.Count.EqualTo(2));
        Assert.That(context.Metadata.TryGetValueDrawer(typeof(ConsumerPercent), out var drawer), Is.True);

        var field = descriptor.Fields[0];
        var drawerContext = new HGValueDrawerContext(field, body, false);
        Assert.That(drawer.Measure(drawerContext, 140f), Is.EqualTo(18f));

        var result = drawer.Draw(new Rect(0f, 0f, 140f, 18f), drawerContext, field.Read(body));
        Assert.That(result.Changed, Is.True);
        field.Write(body, result.Value);
        Assert.That(body.Percent.Value, Is.EqualTo(0.75f));
        Assert.That(context.Profile.Capabilities, Is.EqualTo(HGCapabilities.None));
    }

    [Test]
    public void PublicSession_DeleteNodeRemovesInlineCellAndReturnsItsDirectSource()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create();
        var inlineOwner = new ConsumerInlineOwner();
        var directSource = new GraphNode(new ConsumerBody());
        directSource.EnsureId();
        var cell = new GraphNode(new ConsumerBody { Input = new ConsumerSlot() });
        ((ConsumerBody)cell.BodyObject).Input.SetNode(directSource);
        inlineOwner.ChildNodes.Add(cell);
        owner.B.Root.Items[0].SetNode(new GraphNode(inlineOwner));
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        var copiedOwner = (ConsumerInlineOwner)session.Document.Root.Items[0].Node.BodyObject;
        var copiedSource = ((ConsumerBody)copiedOwner.ChildNodes[0].BodyObject).Input.Node;

        Assert.That(session.DeleteNode(copiedOwner.ChildNodes[0]), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(copiedOwner.ChildNodes, Is.Empty);
        Assert.That(session.Document.Orphans, Contains.Item(copiedSource));
        Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(((ConsumerInlineOwner)session.Document.Root.Items[0].Node.BodyObject).ChildNodes, Has.Count.EqualTo(1));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicSession_DeletesFromATokenFreeDocument()
    {
        var owner = ScriptableObject.CreateInstance<TokenFreeConsumerOwner>();
        owner.Document = TokenFreeConsumerDocument.Create();
        var binding = new HGDocumentBinding<TokenFreeConsumerDocument>("Consumer.TokenFree",
            target => ((TokenFreeConsumerOwner)target).Document,
            (target, document) => ((TokenFreeConsumerOwner)target).Document = document,
            TokenFreeConsumerDocument.Create);

        Assert.That(HGDocumentSession<TokenFreeConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        Assert.That(session.Document, Is.Not.InstanceOf<ITokenOwner>());
        Assert.That(session.DeleteNode(session.Document.Orphans[0]), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Orphans, Is.Empty);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicSession_ReturnsDisconnectedTokenSourceToItsTokenPool()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create();
        var token = new GraphToken("value", new ConsumerFormulaSlot());
        var source = new GraphNode(new ConsumerBody());
        source.EnsureId();
        token.Orphans.Add(source);
        owner.B.Tokens.Add(token);
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        var copiedToken = session.Document.Tokens[0];
        var copiedSource = copiedToken.Orphans[0];
        var key = new HGPortKey("token", "/slot", HGPortRole.Input);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);
        var connectRegistry = session.CreatePortRegistry();
        Assert.That(connectRegistry.AddInput(key, copiedToken.Slot, new HGDelegatePortPolicy(() => true, _ => true), presentation), Is.True);
        Assert.That(session.ReplaceSource(connectRegistry, key,
            new HGDelegatePortSource(copiedSource, copiedSource, _ => true)), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(copiedToken.Orphans, Is.Empty);

        var disconnectRegistry = session.CreatePortRegistry();
        var currentToken = session.Document.Tokens[0];
        Assert.That(disconnectRegistry.AddInput(key, currentToken.Slot, new HGDelegatePortPolicy(() => true, _ => true), presentation), Is.True);
        Assert.That(session.Disconnect(disconnectRegistry, key), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(currentToken.Orphans, Has.Count.EqualTo(1));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicSession_RejectsRegistryFromAnotherDocumentWithTheSameGeneration()
    {
        var firstOwner = ScriptableObject.CreateInstance<ConsumerOwner>();
        var secondOwner = ScriptableObject.CreateInstance<ConsumerOwner>();
        firstOwner.B = ConsumerDocument.Create();
        secondOwner.B = ConsumerDocument.Create();
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(firstOwner, binding, out var first), Is.True);
        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(secondOwner, binding, out var second), Is.True);
        Assert.That(first.Generation, Is.EqualTo(second.Generation));
        var registry = first.CreatePortRegistry();
        var inputKey = new HGPortKey("consumer", "/root/input", HGPortRole.Input);
        var outputKey = new HGPortKey("consumer", "/source", HGPortRole.Output);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);
        var source = new GraphNode(new ConsumerBody());

        Assert.That(registry.AddInput(inputKey, first.Document.Root.Items[0],
            new HGDelegatePortPolicy(() => true, port => port.Accepts(first.Document.Root.Items[0])), presentation), Is.True);
        Assert.That(registry.AddOutput(outputKey, new HGDelegatePortSource(source, source, _ => true),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(second.Connect(registry, outputKey, inputKey), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
        Assert.That(second.Disconnect(registry, inputKey), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
        Assert.That(second.ReplaceSource(registry, inputKey, new HGDelegatePortSource(source, source, _ => true)),
            Is.EqualTo(HGSessionCommandResult.StaleGeneration));

        UnityEngine.Object.DestroyImmediate(firstOwner);
        UnityEngine.Object.DestroyImmediate(secondOwner);
    }

    [Test]
    public void PublicSession_RechecksLockedInputBeforeConnectWithoutMutating()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create();
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        Assert.That(session.DocumentId, Is.EqualTo("Consumer.B"));
        var registry = session.CreatePortRegistry();
        var input = session.Document.Root.Items[0];
        var source = session.Document.Orphans[0];
        var inputKey = new HGPortKey("consumer", "/root/input", HGPortRole.Input);
        var outputKey = new HGPortKey("consumer", "/source", HGPortRole.Output);
        bool locked = false;
        var inputPresentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => locked);
        var outputPresentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);

        Assert.That(registry.AddInput(inputKey, input, new HGDelegatePortPolicy(() => true, port => port.Accepts(input)),
            inputPresentation), Is.True);
        Assert.That(registry.AddOutput(outputKey, new HGDelegatePortSource(source, source, _ => true),
            new HGDelegatePortPolicy(() => true), outputPresentation), Is.True);
        Assert.That(registry.TryGet(inputKey, out var inputPort), Is.True);
        Assert.That(registry.TryGet(outputKey, out var outputPort), Is.True);
        Assert.That(HGPortConnection.Check(inputPort, outputPort, session.Generation), Is.EqualTo(HGPortConnectionResult.Allowed));

        locked = true;

        Assert.That(session.Connect(registry, outputKey, inputKey), Is.EqualTo(HGSessionCommandResult.Rejected));
        Assert.That(input.Node, Is.Null);
        Assert.That(session.Document.Orphans, Contains.Item(source));
        Assert.That(session.Generation, Is.EqualTo(0));
        Assert.That(session.IsDirty, Is.False);
        Assert.That(session.CanUndo, Is.False);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicSession_PreservesDirtyWorkingCopyWhenCommitValidationFails()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create(rejectValidation: true);
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        var registry = session.CreatePortRegistry();
        var input = session.Document.Root.Items[0];
        var source = session.Document.Orphans[0];
        var inputKey = new HGPortKey("consumer", "/root/input", HGPortRole.Input);
        var outputKey = new HGPortKey("consumer", "/source", HGPortRole.Output);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);
        Assert.That(registry.AddInput(inputKey, input, new HGDelegatePortPolicy(() => true, port => port.Accepts(input)),
            presentation), Is.True);
        Assert.That(registry.AddOutput(outputKey, new HGDelegatePortSource(source, source, _ => true),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(session.Connect(registry, outputKey, inputKey), Is.EqualTo(HGSessionCommandResult.Changed));
        var workingCopy = session.Document;
        int generation = session.Generation;

        Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
        Assert.That(session.Document, Is.SameAs(workingCopy));
        Assert.That(session.Document.Root.Items[0].Node, Is.SameAs(source));
        Assert.That(session.IsDirty, Is.True);
        Assert.That(session.CanUndo, Is.True);
        Assert.That(session.Generation, Is.EqualTo(generation));
        Assert.That(owner.B.Root.Items[0].Node, Is.Null);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    private sealed class ConsumerOwner : ScriptableObject
    {
        public ConsumerDocument A;
        public ConsumerDocument B;
    }

    private sealed class TokenFreeConsumerOwner : ScriptableObject
    {
        public TokenFreeConsumerDocument Document;
    }

    [Serializable]
    private sealed class TokenFreeConsumerDocument : IGraphDocument
    {
        public List<GraphNode> Orphans { get; } = new();
        public IList Roots => Array.Empty<object>();
        public bool IsValidated { get; private set; }
        public Type PackType => typeof(object);
        public Type ItemSlotType => typeof(ConsumerSlot);
        public string RootChip => "Test";
        public string RootNoun => "Test";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Token-free Consumer";

        public static TokenFreeConsumerDocument Create()
        {
            var document = new TokenFreeConsumerDocument();
            document.Orphans.Add(new GraphNode(new ConsumerBody()));
            return document;
        }

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => Create();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => Array.Empty<object>();
        public object KeyOf(object root) => null;
        public string TitleOf(object root) => "";
        public IList ItemsOf(object root) => null;
        public object AddRoot(object key) => null;
    }

    private sealed class ConsumerProvider : IHGEditorExtensionProvider, IHGEditorDiagnosticProvider, IHGEditorMetadataProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context) { }
        public void CollectDiagnostics(UnityEngine.Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
            => diagnostics.Add(new GraphDiagnostic("consumer.root.required", GraphDiagnosticSeverity.Error,
                "Consumer requires a root.", new GraphDiagnosticLocation(fieldPath: "Root")));

        public bool TryGetNodeDescriptor(Type nodeType, out HGNodeDescriptor descriptor)
        {
            descriptor = nodeType == typeof(ConsumerBody)
                ? new HGNodeDescriptor(typeof(ConsumerBody), new HGFieldDescriptor[]
                {
                    HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", body => body.Percent,
                        (body, value) => body.Percent = value),
                    HGFieldDescriptor.CreateSlot<ConsumerBody, ConsumerSlot>("Input", body => body.Input),
                })
                : null;
            return descriptor != null;
        }

        public bool TryGetValueDrawer(Type valueType, out IHGValueDrawer drawer)
        {
            drawer = valueType == typeof(ConsumerPercent) ? new ConsumerPercentDrawer() : null;
            return drawer != null;
        }
    }

    [Serializable]
    private sealed class ConsumerDocument : IGraphDocument, ITokenOwner
    {
        public ConsumerRoot Root = new ConsumerRoot();
        private List<GraphNode> orphans = new List<GraphNode>();
        private List<GraphToken> tokens = new List<GraphToken>();
        private bool rejectValidation;
        public List<GraphNode> Orphans => orphans;
        public List<GraphToken> Tokens => tokens;
        public bool IsValidated { get; private set; }
        public IList Roots => new[] { Root };
        public Type PackType => typeof(object);
        public Type ItemSlotType => typeof(ConsumerSlot);
        public string RootChip => "Test";
        public string RootNoun => "Test";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Public Consumer";

        public static ConsumerDocument Create() => Create(false);

        public static ConsumerDocument Create(bool rejectValidation)
        {
            var document = new ConsumerDocument();
            document.rejectValidation = rejectValidation;
            document.Root.Items.Add(new ConsumerSlot());
            var source = new GraphNode(new ConsumerBody());
            source.EnsureId();
            document.Orphans.Add(source);
            var replacement = new GraphNode(new ConsumerBody());
            replacement.EnsureId();
            document.Orphans.Add(replacement);
            return document;
        }

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = !rejectValidation;
        public object DeepCopy() => GraphDeepCopy.Copy(this);
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { "root" };
        public object KeyOf(object root) => ReferenceEquals(root, Root) ? "root" : null;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => ReferenceEquals(root, Root) ? Root.Items : null;
        public object AddRoot(object key) => Root;
    }

    [Serializable]
    private sealed class ConsumerRoot
    {
        public List<ConsumerSlot> Items = new List<ConsumerSlot>();
    }

    [Serializable]
    private sealed class ConsumerSlot : GraphSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }

    [Serializable]
    private sealed class ConsumerFormulaSlot : FormulaSlotBase
    {
        private GraphNode node;

        public override GraphNode Node => node;
        public override Type ResultType => typeof(object);
        public override Type PackType => typeof(object);
        public override object DefaultObject { get; set; }
        public override Type BodyBaseType => typeof(GraphNodeContent);
        public override Type AssetBaseType => typeof(ScriptableObject);
        public override void SetNode(GraphNode value) => node = value;
    }

    [Serializable]
    private sealed class ConsumerBody : GraphNodeContent
    {
        public ConsumerPercent Percent;
        public ConsumerSlot Input;
    }

    [Serializable]
    private struct ConsumerPercent
    {
        public float Value;

        public ConsumerPercent(float value)
        {
            Value = value;
        }
    }

    private sealed class ConsumerPercentDrawer : IHGValueDrawer
    {
        public float Measure(in HGValueDrawerContext context, float width) => 18f;

        public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value)
            => new HGValueDrawerResult(true, new ConsumerPercent(0.75f));
    }

    [Serializable]
    private sealed class ConsumerInlineOwner : GraphNodeContent, IGraphInlineNodeOwner
    {
        private List<GraphNode> childNodes = new List<GraphNode>();
        public List<GraphNode> ChildNodes => childNodes;
        public GraphNode CreateChild()
        {
            var child = new GraphNode();
            child.EnsureId();
            childNodes.Add(child);
            return child;
        }

        public void RemoveChild(GraphNode child) => childNodes.Remove(child);
    }
}
}
