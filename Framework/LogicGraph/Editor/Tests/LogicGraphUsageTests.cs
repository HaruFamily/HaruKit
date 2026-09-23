namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
using System;
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

    private class PrivateOwner : ScriptableObject
    {
        [SerializeField] private LogicGraph<Timing, Pack> graph = new();
        [NonSerialized] public LogicGraph<Timing, Pack> Scratch = new();
        public LogicGraph<Timing, Pack> Graph => graph;
    }

    private sealed class InheritedOwner : PrivateOwner { }

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
            working.Tokens.Add(new GraphToken("Required", new IntSlot()));
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
    public void UsageErrorsBlockCommitWithDefaultAndOfficialContexts(bool official)
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
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(owner.Second, Is.SameAs(original));
            owner.Reject = false;
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
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
    public void NonWindowDefaultSessionAlsoEnforcesUsageAndConfigurationFailures()
    {
        var owner = ScriptableObject.CreateInstance<ConfiguredOwner>();
        try
        {
            var original = owner.Second;
            Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, SecondBinding(), out var session), Is.True);
            owner.ThrowConfiguration = true;
            Assert.That(((IGraphDocument)session.Document).RootKeys(owner), Is.Empty);
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo("logicgraph.usage.configuration-failed"));
            Assert.That(owner.Second, Is.SameAs(original));

            owner.ThrowConfiguration = false;
            owner.ThrowValidation = true;
            Assert.That(session.Commit(), Is.EqualTo(HGSessionCommandResult.ValidationFailed));
            Assert.That(session.LastDiagnostic.Code, Is.EqualTo("logicgraph.usage.validation-failed"));
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
