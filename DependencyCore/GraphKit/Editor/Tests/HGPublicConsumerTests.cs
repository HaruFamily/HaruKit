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
        Assert.That(session.Document.Orphans, Is.Empty);
        Assert.That(owner.A.Root.Items[0].Node, Is.Null);
        Assert.That(owner.B.Root.Items[0].Node, Is.Null);
        Assert.That(session.Disconnect(registry, inputKey), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
        Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Null);
        Assert.That(session.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[0].Node, Is.Not.Null);

        Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
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

    private sealed class ConsumerOwner : ScriptableObject
    {
        public ConsumerDocument A;
        public ConsumerDocument B;
    }

    private sealed class ConsumerProvider : IHGEditorExtensionProvider, IHGEditorDiagnosticProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context) { }
        public void CollectDiagnostics(UnityEngine.Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
            => diagnostics.Add(new GraphDiagnostic("consumer.root.required", GraphDiagnosticSeverity.Error,
                "Consumer requires a root.", new GraphDiagnosticLocation(fieldPath: "Root")));
    }

    [Serializable]
    private sealed class ConsumerDocument : IGraphDocument
    {
        public ConsumerRoot Root = new ConsumerRoot();
        private List<GraphNode> orphans = new List<GraphNode>();
        private List<GraphToken> tokens = new List<GraphToken>();
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

        public static ConsumerDocument Create()
        {
            var document = new ConsumerDocument();
            document.Root.Items.Add(new ConsumerSlot());
            var source = new GraphNode(new ConsumerBody());
            source.EnsureId();
            document.Orphans.Add(source);
            return document;
        }

        public void MarkDirty() => IsValidated = false;
        public void Verify() => IsValidated = true;
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
    private sealed class ConsumerBody : GraphNodeContent
    {
    }
}
}
