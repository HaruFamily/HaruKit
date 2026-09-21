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
    public void WindowConsumerRebuildsCustomEndpointsAndCommitsUndoableValuesThroughPublicCommands()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.A = ConsumerDocument.Create();
            owner.B = ConsumerDocument.Create();
            string sourceId = owner.B.Orphans[0].Id;
            var provider = new WindowConsumerProvider(sourceId);
            var context = new HGEditorExtensionContext(provider, provider, new HGEditorProfile(HGCapabilities.None));
            Assert.That(window.BindDocument(owner, Binding(), context), Is.True);
            var commands = window.GetDocumentCommands();
            var before = commands.Query();
            Assert.That(commands.Connect(before.Generation, provider.Output, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            var connected = commands.Query();
            Assert.That(connected.Links.Count, Is.EqualTo(1));
            Assert.That(connected.Links[0].Input, Is.EqualTo(provider.Input));
            Assert.That(connected.Links[0].Output, Is.EqualTo(provider.Output));
            Assert.That(commands.Disconnect(before.Generation, provider.Input), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
            Assert.That(commands.EditValue(connected.Generation, sourceId, "/Percent", new ConsumerPercent(0.75f)),
                Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            provider.Reject = true;
            var liveErrors = commands.Validate();
            Assert.That(System.Linq.Enumerable.Any(liveErrors, issue => issue.Code == "consumer.rejected"
                && issue.Location.NodeId == sourceId && issue.Location.FieldPath == "/Percent"), Is.True);
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(owner.B.Root.Items[0].Node, Is.Null);
            provider.Reject = false;
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(((ConsumerBody)owner.B.Root.Items[0].Node.BodyObject).Percent.Value, Is.EqualTo(0.75f));
            Assert.That(owner.A.Root.Items[0].Node, Is.Null);
            var saved = commands.Query();
            Assert.That(commands.Disconnect(saved.Generation, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Query().Links[0].Output, Is.EqualTo(provider.Output));
            Assert.That(window.BindDocument(owner, Binding(), context), Is.True);
            Assert.That(commands.Query(), Is.Null);
            commands = window.GetDocumentCommands();
            Assert.That(commands.Query().Links[0].Input, Is.EqualTo(provider.Input));
            var reloaded = commands.Query();
            Assert.That(commands.Disconnect(reloaded.Generation, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            reloaded = commands.Query();
            Assert.That(commands.Connect(reloaded.Generation, provider.Input, provider.Output), Is.EqualTo(HGSessionCommandResult.Changed));
            commands.Cancel();
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void CommitKeepsTheSameSessionIsolatedForFurtherEditsAndCancel()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            var binding = Binding();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
            var key = new HGPortKey("root", "/input", HGPortRole.Input);
            var registry = session.CreatePortRegistry();
            registry.AddInput(key, session.Document.Root.Items[0], AcceptsSource(session.Document.Root.Items[0]), ConsumerPresentation());
            Assert.That(session.ReconnectInput(registry, key, Source(session.Document.Orphans[0])), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Document, Is.Not.SameAs(owner.B));
            Assert.That(session.Document.Root.Items[0], Is.Not.SameAs(owner.B.Root.Items[0]));
            registry = session.CreatePortRegistry();
            registry.AddInput(key, session.Document.Root.Items[0], AcceptsSource(session.Document.Root.Items[0]), ConsumerPresentation());
            Assert.That(session.Disconnect(registry, key), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.B.Root.Items[0].Node, Is.Not.Null);
            Assert.That(session.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Document.Root.Items[0].Node, Is.Not.Null);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void CurrentRegistryCannotMutateLiveForeignDetachedOrStaleSlots()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.A = ConsumerDocument.Create();
            owner.B = ConsumerDocument.Create();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var session), Is.True);
            var stale = session.Document.Root.Items[0];
            session.Cancel();
            foreach (var slot in new[] { owner.A.Root.Items[0], owner.B.Root.Items[0], new ConsumerSlot(), stale })
            {
                var source = session.Document.Orphans[0];
                var before = new GraphNode(new ConsumerBody());
                slot.SetNode(before);
                var key = new HGPortKey("root", "/input", HGPortRole.Input);
                var registry = session.CreatePortRegistry();
                registry.AddInput(key, slot, AcceptsSource(slot), ConsumerPresentation());
                Assert.That(session.ReconnectInput(registry, key, Source(source)), Is.EqualTo(HGSessionCommandResult.Rejected));
                Assert.That(session.Disconnect(registry, key), Is.EqualTo(HGSessionCommandResult.Rejected));
                Assert.That(slot.Node, Is.SameAs(before));
                Assert.That(session.Document.Orphans, Contains.Item(source));
                Assert.That(session.IsDirty, Is.False);
                Assert.That(session.CanUndo, Is.False);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void SetterFailureRestoresDocumentAndPreservesRedoWithoutWritingOwner()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var session), Is.True);
            var field = HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", x => x.Percent,
                (x, value) => x.Percent = value);
            var body = (ConsumerBody)session.Document.Orphans[0].BodyObject;
            session.EditValue(body, field, new ConsumerPercent(0.5f));
            session.Undo();
            body = (ConsumerBody)session.Document.Orphans[0].BodyObject;
            var failing = HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", x => x.Percent,
                (x, value) => { x.Percent = value; throw new InvalidOperationException("setter failed"); });
            int generation = session.Generation;
            Assert.That(session.EditValue(body, failing, new ConsumerPercent(1f)), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(((ConsumerBody)session.Document.Orphans[0].BodyObject).Percent.Value, Is.Zero);
            Assert.That(session.CanRedo, Is.True);
            Assert.That(session.Generation, Is.GreaterThan(generation));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo("graphkit.session.mutation-failed"));
            Assert.That(((ConsumerBody)owner.B.Orphans[0].BodyObject).Percent.Value, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void CarrierReplacementPreservesIdentityAndAllSharedReferences()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            var carrier = owner.B.Orphans[0];
            owner.B.Root.Items[0].SetNode(carrier);
            var second = new ConsumerSlot();
            second.SetNode(carrier);
            owner.B.Root.Items.Add(second);
            carrier.Note = "retain";
            carrier.Pos = new Vector2(40, 80);
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var session), Is.True);
            carrier = session.Document.Root.Items[0].Node;
            string id = carrier.Id;
            var sharedChild = session.Document.Orphans[1];
            var newInput = new ConsumerSlot();
            newInput.SetNode(sharedChild);
            Assert.That(session.ReplaceSource(carrier, HGCarrierSource.Body(new ConsumerBody { Percent = new ConsumerPercent(0.8f), Input = newInput })),
                Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Document.Root.Items[1].Node, Is.SameAs(carrier));
            Assert.That(carrier.Id, Is.EqualTo(id));
            Assert.That(carrier.Note, Is.EqualTo("retain"));
            Assert.That(carrier.Pos, Is.EqualTo(new Vector2(40, 80)));
            Assert.That(((ConsumerBody)carrier.BodyObject).Percent.Value, Is.EqualTo(0.8f));
            Assert.That(((ConsumerBody)carrier.BodyObject).Input.Node, Is.SameAs(sharedChild));
            Assert.That(session.Document.Orphans.Contains(sharedChild), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void SessionUsesTheSameProviderDiagnosticsForLiveAndCommit()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), new HGEditorExtensionContext(new ConsumerProvider()),
                out var session, out _), Is.True);
            var live = session.CollectDiagnostics()[0];
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo(live.Code));
            Assert.That(session.LastDiagnostic.Location.FieldPath, Is.EqualTo(live.Location.FieldPath));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void ConcurrentSessionsRejectStaleCommitAndCancelAdoptsTheLatestOwner()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var first), Is.True);
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var second), Is.True);
            var field = HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", x => x.Percent,
                (x, value) => x.Percent = value);
            Assert.That(second.EditValue(second.Document.Orphans[0].BodyObject, field, new ConsumerPercent(0.5f)),
                Is.EqualTo(HGSessionCommandResult.Changed));
            owner.A = ConsumerDocument.Create(); // An unrelated document is not a conflict.
            Assert.That(first.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            var saved = owner.B;
            Assert.That(second.Commit(), Is.EqualTo(HGSessionCommandResult.Conflict));
            Assert.That(second.LastDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
            Assert.That(owner.B, Is.SameAs(saved));
            Assert.That(second.IsDirty, Is.True);
            Assert.That(second.CanUndo, Is.True);
            Assert.That(((ConsumerBody)second.Document.Orphans[0].BodyObject).Percent.Value, Is.EqualTo(0.5f));
            Assert.That(second.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(second.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void RevisionDetectsInPlaceChangesAndRebasesAfterCommitAndCancel()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            int revision = 0;
            var binding = new HGDocumentBinding<ConsumerDocument>("B", x => ((ConsumerOwner)x).B,
                (x, document) => { ((ConsumerOwner)x).B = document; revision++; },
                create: null, readRevision: _ => revision.ToString());
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
            var live = owner.B;
            ((ConsumerBody)live.Orphans[0].BodyObject).Percent = new ConsumerPercent(0.8f);
            revision++;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Conflict));
            Assert.That(owner.B, Is.SameAs(live));
            Assert.That(session.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(((ConsumerBody)session.Document.Orphans[0].BodyObject).Percent.Value, Is.EqualTo(0.8f));
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void FailedAssignmentPreservesDocumentAndHistoryIncludingNull(bool initiallyNull, bool throwAfterAssignment)
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = initiallyNull ? null : ConsumerDocument.Create();
            var original = owner.B;
            bool fail = true;
            var binding = new HGDocumentBinding<ConsumerDocument>("B", x => ((ConsumerOwner)x).B,
                (x, document) =>
                {
                    if (fail && !throwAfterAssignment) throw new InvalidOperationException("before assignment");
                    ((ConsumerOwner)x).B = document;
                    if (fail) throw new InvalidOperationException("after assignment");
                }, () => ConsumerDocument.Create());
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
            var field = HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", x => x.Percent,
                (x, value) => x.Percent = value);
            session.EditValue(session.Document.Orphans[0].BodyObject, field, new ConsumerPercent(0.5f));
            session.EditValue(session.Document.Orphans[0].BodyObject, field, new ConsumerPercent(0.75f));
            session.Undo();
            int generation = session.Generation;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.WriteFailed));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo("graphkit.commit.write-failed"));
            Assert.That(owner.B, Is.SameAs(original));
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.CanUndo, Is.True);
            Assert.That(session.CanRedo, Is.True);
            Assert.That(session.Generation, Is.EqualTo(generation));
            fail = false;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(((ConsumerBody)owner.B.Orphans[0].BodyObject).Percent.Value, Is.EqualTo(0.5f));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FailedRecoveryBlocksFurtherWritesUntilExplicitReload()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            var original = owner.B;
            int writes = 0;
            bool fail = true;
            var binding = new HGDocumentBinding<ConsumerDocument>("B", x => ((ConsumerOwner)x).B,
                (x, document) =>
                {
                    writes++;
                    if (fail && ReferenceEquals(document, original)) throw new InvalidOperationException("recovery failed");
                    ((ConsumerOwner)x).B = document;
                    if (fail) throw new InvalidOperationException("write failed");
                });
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.WriteFailed));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo("graphkit.commit.recovery-required"));
            Assert.That(writes, Is.EqualTo(2));
            fail = false;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.WriteFailed));
            Assert.That(writes, Is.EqualTo(2));
            owner.B = original; // Tool/user repairs Owner before explicitly accepting a new baseline.
            Assert.That(session.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PublicSessionRetainsOnlyTheLastFortyUndoStepsAndClearsRedoOnBranch()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        try
        {
            owner.B = ConsumerDocument.Create();
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, Binding(), out var session), Is.True);
            var field = HGFieldDescriptor.Create<ConsumerBody, ConsumerPercent>("Percent", x => x.Percent,
                (x, value) => x.Percent = value);
            for (int i = 1; i <= 45; i++)
                Assert.That(session.EditValue(session.Document.Orphans[0].BodyObject, field, new ConsumerPercent(i)),
                    Is.EqualTo(HGSessionCommandResult.Changed));
            for (int i = 0; i < 40; i++) Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.NoChange));
            Assert.That(((ConsumerBody)session.Document.Orphans[0].BodyObject).Percent.Value, Is.EqualTo(5f));
            Assert.That(session.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            session.EditValue(session.Document.Orphans[0].BodyObject, field, new ConsumerPercent(100f));
            Assert.That(session.CanRedo, Is.False);
            Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(((ConsumerBody)session.Document.Orphans[0].BodyObject).Percent.Value, Is.EqualTo(6f));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void WindowCommandsReportConflictAndKeepEditsUntilCancel()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.B = ConsumerDocument.Create();
            string sourceId = owner.B.Orphans[0].Id;
            var provider = new WindowConsumerProvider(sourceId);
            var context = new HGEditorExtensionContext(provider, provider, new HGEditorProfile(HGCapabilities.None));
            Assert.That(window.BindDocument(owner, Binding(), context), Is.True);
            var commands = window.GetDocumentCommands();
            var snapshot = commands.Query();
            Assert.That(commands.Connect(snapshot.Generation, provider.Output, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            var external = (ConsumerDocument)owner.B.DeepCopy();
            owner.B = external;
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Conflict));
            Assert.That(owner.B, Is.SameAs(external));
            Assert.That(commands.Query().IsDirty, Is.True);
            Assert.That(System.Linq.Enumerable.Any(commands.Validate(), d => d.Code == "graphkit.commit.owner-changed"), Is.True);
            Assert.That(commands.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            var reloaded = commands.Query();
            Assert.That(reloaded.IsDirty, Is.False);
            Assert.That(reloaded.Links, Is.Empty);
            // Cancel adopts the external document, whose root is still unconnected.
            var diagnostics = commands.Validate();
            Assert.That(System.Linq.Enumerable.Any(diagnostics, d => d.Code == "graphkit.commit.owner-changed"), Is.False);
            Assert.That(System.Linq.Enumerable.Any(diagnostics, d => d.Code == "graphkit.root.action-missing"), Is.True);
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            reloaded = commands.Query();
            Assert.That(commands.Connect(reloaded.Generation, provider.Output, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(commands.Validate(), d => d.Severity == GraphDiagnosticSeverity.Error),
                d => d.Code + ": " + d.Message), Is.Empty);
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.B.Root.Items[0].Node.Id, Is.EqualTo(sourceId));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WindowSavePersistsInvalidEditsAsDraftWhileProgrammaticCommitRemainsStrict(bool coreOnlyFailure)
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.B = ConsumerDocument.Create(coreOnlyFailure);
            var original = owner.B;
            string sourceId = owner.B.Orphans[0].Id;
            var provider = new WindowConsumerProvider(sourceId) { Reject = !coreOnlyFailure };
            var context = new HGEditorExtensionContext(provider, provider, new HGEditorProfile(HGCapabilities.None));
            Assert.That(window.BindDocument(owner, Binding(), context), Is.True);
            var commands = window.GetDocumentCommands();
            var snapshot = commands.Query();
            Assert.That(commands.Connect(snapshot.Generation, provider.Output, provider.Input), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(owner.B, Is.SameAs(original));
            Assert.That(commands.Query().IsDirty, Is.True);

            Assert.DoesNotThrow(() => window.SaveChanges());

            Assert.That(owner.B, Is.Not.SameAs(original));
            Assert.That(owner.B.Root.Items[0].Node.Id, Is.EqualTo(sourceId));
            Assert.That(owner.B.IsValidated, Is.False);
            Assert.That(commands.Query().IsDirty, Is.False);
            Assert.That(window.hasUnsavedChanges, Is.False);
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    private static HGDocumentBinding<ConsumerDocument> Binding()
        => new("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);

    private static HGDelegatePortPresentation ConsumerPresentation()
        => new(new object(), () => Vector2.zero, () => Rect.zero, () => true, () => false);

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
    public void PublicSession_KeepsSharedSourceOutOfTheCandidatePoolUntilItsLastUserDisconnects()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create();
        owner.B.Root.Items.Add(new ConsumerSlot());
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);
        var firstKey = new HGPortKey("consumer", "/root/first", HGPortRole.Input);
        var secondKey = new HGPortKey("consumer", "/root/second", HGPortRole.Input);
        var outputKey = new HGPortKey("consumer", "/source", HGPortRole.Output);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        var shared = session.Document.Orphans[0];
        var firstRegistry = session.CreatePortRegistry();
        Assert.That(firstRegistry.AddInput(firstKey, session.Document.Root.Items[0], AcceptsSource(session.Document.Root.Items[0]), presentation), Is.True);
        Assert.That(firstRegistry.AddOutput(outputKey, Source(shared), new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(session.Connect(firstRegistry, outputKey, firstKey), Is.EqualTo(HGSessionCommandResult.Changed));

        var secondRegistry = session.CreatePortRegistry();
        Assert.That(secondRegistry.AddInput(secondKey, session.Document.Root.Items[1], AcceptsSource(session.Document.Root.Items[1]), presentation), Is.True);
        Assert.That(secondRegistry.AddOutput(outputKey, Source(shared), new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(session.Connect(secondRegistry, outputKey, secondKey), Is.EqualTo(HGSessionCommandResult.Changed));

        var disconnectRegistry = session.CreatePortRegistry();
        Assert.That(disconnectRegistry.AddInput(firstKey, session.Document.Root.Items[0], AcceptsSource(session.Document.Root.Items[0]), presentation), Is.True);
        Assert.That(session.Disconnect(disconnectRegistry, firstKey), Is.EqualTo(HGSessionCommandResult.Changed));
        Assert.That(session.Document.Root.Items[1].Node, Is.SameAs(shared));
        Assert.That(session.Document.Orphans.Contains(shared), Is.False);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void PublicSession_RejectsDetailedSourceFailureWithoutCreatingUndoOrDirtyState()
    {
        var owner = ScriptableObject.CreateInstance<ConsumerOwner>();
        owner.B = ConsumerDocument.Create();
        var binding = new HGDocumentBinding<ConsumerDocument>("Consumer.B", target => ((ConsumerOwner)target).B,
            (target, document) => ((ConsumerOwner)target).B = document, ConsumerDocument.Create);
        var inputKey = new HGPortKey("consumer", "/root/input", HGPortRole.Input);
        var presentation = new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
            () => true, () => false);

        Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(owner, binding, out var session), Is.True);
        var source = session.Document.Orphans[0];
        var registry = session.CreatePortRegistry();
        Assert.That(registry.AddInput(inputKey, session.Document.Root.Items[0], AcceptsSource(session.Document.Root.Items[0]), presentation), Is.True);
        var rejected = new HGDelegatePortSource(source, source, _ => false,
            _ => HGPortConnectionResult.IncompatibleFamily);

        Assert.That(session.ReplaceSource(registry, inputKey, rejected), Is.EqualTo(HGSessionCommandResult.Rejected));
        Assert.That(session.Document.Root.Items[0].Node, Is.Null);
        Assert.That(session.Document.Orphans, Contains.Item(source));
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

    private static HGDelegatePortPolicy AcceptsSource(ConsumerSlot input)
        => new HGDelegatePortPolicy(() => true, source => source.Accepts(input));

    private static HGDelegatePortSource Source(GraphNode node)
        => new HGDelegatePortSource(node, node, _ => true);

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

    private sealed class WindowConsumerProvider : IHGEditorExtensionProvider, IHGEditorMetadataProvider, IHGEditorDiagnosticProvider
    {
        private readonly string sourceId;
        private readonly ConsumerProvider metadata = new();
        public bool Reject;
        public HGPortKey Input { get; private set; }
        public HGPortKey Output { get; private set; }
        public WindowConsumerProvider(string sourceId) { this.sourceId = sourceId; }
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => document is ConsumerDocument;
        public void AddPorts(HGPortBuildContext context)
        {
            HGPortKey input = default, output = default;
            foreach (var port in context.Ports)
            {
                if (port.Key.Role == HGPortRole.Input && port.Key.OwnerId.StartsWith("head:")) input = port.Key;
                if (port.Key.Role == HGPortRole.Output && port.Key.OwnerId == sourceId) output = port.Key;
            }
            Input = new HGPortKey(input.OwnerId, "/custom-input", HGPortRole.Input);
            Output = new HGPortKey(sourceId, "/custom-output", HGPortRole.Output);
            if (!context.AddInputAlias(input, Input, context.CreatePresentation(input, new Vector2(-12, 0)))
                || !context.AddOutputAlias(output, Output, context.CreatePresentation(output, new Vector2(12, 0)))
                || !context.SelectPrimaryInput(Input) || !context.SelectPrimaryOutput(Output))
                throw new InvalidOperationException("Consumer endpoint registration failed.");
        }
        public bool TryGetNodeDescriptor(Type type, out HGNodeDescriptor descriptor) => metadata.TryGetNodeDescriptor(type, out descriptor);
        public bool TryGetValueDrawer(Type type, out IHGValueDrawer drawer) => metadata.TryGetValueDrawer(type, out drawer);
        public void CollectDiagnostics(UnityEngine.Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
        {
            if (Reject) diagnostics.Add(new GraphDiagnostic("consumer.rejected", GraphDiagnosticSeverity.Error,
                "Consumer rejects this value.", new GraphDiagnosticLocation(nodeId: sourceId, fieldPath: "/Percent")));
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
        public override bool AcceptsBody(GraphNodeContent value) => value is ConsumerBody;
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

    private sealed class ConsumerPercentDrawer : HGValueDrawer<ConsumerPercent>
    {
        public override float Measure(in HGValueDrawerContext context, float width) => 18f;

        public override HGValueDrawerResult DrawValue(Rect rect, in HGValueDrawerContext context, ConsumerPercent value)
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
