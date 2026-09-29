using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HaruFamily.DependencyCore.GraphKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class GraphVerifierTests
    {
        [Test]
        public void ObjectSlot_ReadsGameObjectToken_WithNullAndSourceFallback()
        {
            var value = new GameObject("Token result");
            var fallback = new GameObject("Receiver fallback");
            try
            {
                var formula = new GameObjectFormula { Value = value };
                var source = new GameObjectSlot(value);
                source.SetNode(new GraphNode(formula));
                var token = new GraphToken("Object", source);
                var node = new GraphNode();
                node.SetToken(token);
                var action = new ReadObject();
                action.Input.Default = fallback;
                action.Input.SetNode(node);
                var graph = new Graph();
                graph.Tokens.Add(token);
                var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
                root.Actions.Add(new ActionSlot(action));

                Assert.That(action.Input.AcceptsToken(token), Is.True);
                Assert.That(GraphVerifier.Collect(graph), Is.Empty);
                Assert.That(action.Input.Evaluate(), Is.SameAs(value));
                formula.Value = null;
                Assert.That(action.Input.Evaluate(), Is.Null);
                source.Node.Disabled = true;
                Assert.That(action.Input.Evaluate(), Is.SameAs(value), "來源停用使用來源的保底值。");
                node.Disabled = true;
                Assert.That(action.Input.Evaluate(), Is.SameAs(fallback), "引用停用使用接收端的保底值。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ObjectSlot_ReadsGameObjectProperty_ButObjectWriterCannotWriteIt(bool proto)
        {
            var value = new GameObject("Property result");
            try
            {
                var property = new GraphProperty("Object", new GameObjectSlot(value), proto);
                var node = new GraphNode();
                node.SetProperty(property);
                var action = new ReadObject();
                action.Input.Default = value;
                action.Input.SetNode(node);
                var graph = new Graph();
                if (proto) graph.Properties.Add(property);
                var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
                root.Actions.Add(new ActionSlot(action));

                Assert.That(action.Input.AcceptsProperty(property), Is.True);
                Assert.That(GraphVerifier.Collect(graph), Is.Empty);
                if (proto) Assert.That(action.Input.Evaluate(), Is.SameAs(value));
                else Assert.That(action.Input.Evaluate(), Is.Null);
                var writer = new PropertySlot<GameObject, GameObjectSlot>();
                writer.SetNode(node);
                Assert.That(writer.Write(value), Is.True);
                Assert.That(action.Input.Evaluate(), Is.SameAs(value));
                Assert.That(writer.Write(null), Is.True);
                Assert.That(action.Input.Evaluate(), Is.Null);

                var wrongWriter = new PropertySlot<UnityEngine.Object, ObjectSlot>();
                wrongWriter.SetNode(node);
                Assert.That(wrongWriter.AcceptsProperty(property), Is.False);
                Assert.That(wrongWriter.Write(value), Is.False);
                Assert.That(property.CurrentValue, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(value); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SlotSources_ShareOptInPackAndExclusionPolicy_AndRejectBeforeEvaluation(bool propertySource)
        {
            var open = new CompatibleStringSlot { Default = "fallback" };
            var foreign = new ForeignTextSlot { Default = "text" };
            var blockedBody = new KeyText();
            var blocked = new KeyTextSlot();
            blocked.SetNode(new GraphNode(blockedBody));
            var strict = new StringSlot();
            var node = new GraphNode();
            var graph = new Graph();
            var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
            root.Actions.Add(new ActionSlot(new ReadText { Input = open }));
            open.SetNode(node);

            bool Accepts(FormulaSlotBase receiver, FormulaSlotBase source)
                => propertySource ? receiver.AcceptsProperty(new GraphProperty("Value", source))
                    : receiver.AcceptsToken(new GraphToken("Value", source));

            Assert.That(Accepts(strict, foreign), Is.False);
            Assert.That(Accepts(open, foreign), Is.True);
            Assert.That(Accepts(open, new CompatibleStringSlot()), Is.True, "同族優先於黑名單。");
            Assert.That(Accepts(open, blocked), Is.False);
            Assert.That(Accepts(open, new OtherPackTextSlot()), Is.False);
            Assert.That(Accepts(open, new IntSlot()), Is.False);

            if (propertySource) node.SetProperty(new GraphProperty("Value", foreign, true));
            else
            {
                var token = new GraphToken("Value", foreign);
                graph.Tokens.Add(token);
                node.SetToken(token);
            }
            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
            Assert.That(open.Evaluate(), Is.EqualTo("text"));

            if (propertySource) node.SetProperty(new GraphProperty("Blocked", blocked));
            else
            {
                var token = new GraphToken("Blocked", blocked);
                graph.Tokens.Add(token);
                node.SetToken(token);
            }
            string kind = propertySource ? "property" : "token";
            Assert.That(GraphVerifier.CollectDiagnostics(graph).Exists(
                item => item.Code == $"assetpipeline.{kind}.target-incompatible"), Is.True);
            LogAssert.Expect(LogType.Warning, new Regex(propertySource ? "接的Property" : "接的Token"));
            Assert.That(open.Evaluate(), Is.EqualTo("fallback"));
            Assert.That(blockedBody.Calls, Is.Zero);
        }

        [Test]
        public void CrossFamilyTokenCycle_UsesGuardAndReleasesItAfterEvaluation()
        {
            var first = new CompatibleStringSlot { Default = "first" };
            var second = new OtherCompatibleStringSlot { Default = "second" };
            var firstToken = new GraphToken("First", first);
            var secondToken = new GraphToken("Second", second);
            var firstNode = new GraphNode();
            firstNode.SetToken(secondToken);
            first.SetNode(firstNode);
            var secondNode = new GraphNode();
            secondNode.SetToken(firstToken);
            second.SetNode(secondNode);
            var graph = new Graph();
            graph.Tokens.Add(firstToken);
            graph.Tokens.Add(secondToken);

            Assert.That(GraphVerifier.CollectDiagnostics(graph).Exists(
                item => item.Code == "assetpipeline.token.cycle"), Is.True);
            LogAssert.Expect(LogType.Warning, new Regex("Token \\[Second\\] 遞迴求值"));
            Assert.That(first.Evaluate(), Is.EqualTo("first"));
            second.SetNode(null);
            Assert.That(first.Evaluate(), Is.EqualTo("second"));
        }

        [Test]
        public void ObjectSlot_UpcastsAudioFormula_AndPreservesNullAndDisabledFallback()
        {
            var clip = AudioClip.Create("FormulaSlot test", 1, 1, 44100, false);
            try
            {
                var formula = new AudioFormula { Value = clip };
                var node = new GraphNode(formula);
                var action = new ReadObject();
                action.Input.Default = clip;
                action.Input.SetNode(node);
                var graph = new Graph();
                var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
                root.Actions.Add(new ActionSlot(action));

                Assert.That(action.Input.AcceptsBody(formula), Is.True);
                Assert.That(GraphVerifier.Collect(graph), Is.Empty);
                Assert.That(action.Input.Evaluate(), Is.SameAs(clip));
                formula.Value = null;
                Assert.That(action.Input.Evaluate(), Is.Null);
                node.Disabled = true;
                Assert.That(action.Input.Evaluate(), Is.SameAs(clip));
                Assert.That(action.Input.AcceptsBody(new OtherPackAudio()), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }

        [Test]
        public void CompatibleResults_KeepDefaultFamilyIsolation_AndExcludeDerivedFamilies()
        {
            var foreign = new ForeignText();
            var blocked = new KeyText();
            var strict = new StringSlot();
            var open = new CompatibleStringSlot();
            Assert.That(strict.AcceptsBody(foreign), Is.False);
            Assert.That(open.AcceptsBody(foreign), Is.True);
            Assert.That(open.AcceptsBody(blocked), Is.False);
            Assert.That(open.AcceptsBody(new NativeText()), Is.True);
            Assert.That(open.AcceptsBody(new AudioFormula()), Is.False);
            open.SetNode(new GraphNode(foreign));
            Assert.That(open.Evaluate(), Is.EqualTo("text"));

            var graph = new Graph();
            var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
            var action = new ReadText { Input = open };
            root.Actions.Add(new ActionSlot(action));
            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
            open.SetNode(new GraphNode(blocked));
            Assert.That(GraphVerifier.CollectDiagnostics(graph).Exists(
                item => item.Code == "assetpipeline.slot.body-incompatible"), Is.True);
        }

        [Serializable]
        private sealed class AudioFormula : Formula_AudioClip<NullPack>
        {
            public AudioClip Value;
            protected override AudioClip OnEvaluate(NullPack pack) => Value;
        }

        private sealed class OtherPackAudio : Formula_AudioClip<string>
        {
            protected override AudioClip OnEvaluate(string pack) => null;
        }

        [Serializable]
        private class ForeignText : FormulaBase<string, NullPack>
        {
            protected override string OnEvaluate(NullPack pack) => "text";
        }

        [Serializable]
        private abstract class KeyFamily : FormulaBase<string, NullPack> { }

        [Serializable]
        private abstract class DerivedKeyFamily : KeyFamily { }

        [Serializable]
        private sealed class KeyText : DerivedKeyFamily
        {
            public int Calls;
            protected override string OnEvaluate(NullPack pack) { Calls++; return "key"; }
        }

        [Serializable]
        private class ForeignTextSlot : FormulaSlot<string, ForeignText> { }

        [Serializable]
        private sealed class KeyTextSlot : FormulaSlot<string, DerivedKeyFamily> { }

        [Serializable]
        private sealed class OtherPackTextSlot : ForeignTextSlot
        {
            public override Type PackType => typeof(string);
        }

        [Serializable]
        private sealed class OtherCompatibleStringSlot : ForeignTextSlot
        {
            protected override bool AllowCompatibleResult => true;
        }

        [Serializable]
        private sealed class GameObjectFormula : Formula_GameObject<NullPack>
        {
            public GameObject Value;
            protected override GameObject OnEvaluate(NullPack pack) => Value;
        }

        private sealed class NativeText : Formula_String<NullPack>
        {
            protected override string OnEvaluate(NullPack pack) => "native";
        }

        [Serializable]
        private sealed class CompatibleStringSlot : StringSlot
        {
            private static readonly HashSet<Type> excluded = new() { typeof(KeyFamily), typeof(Formula_String<NullPack>) };
            protected override bool AllowCompatibleResult => true;
            protected override IReadOnlyCollection<Type> ExcludedFormulaFamilies => excluded;
        }

        [Serializable]
        private sealed class ReadObject : ActionBase
        {
            public ObjectSlot Input = new();
            protected override void OnExecute(PipelineActionContext context) => Input.Evaluate();
        }

        [Serializable]
        private sealed class ReadText : ActionBase
        {
            public CompatibleStringSlot Input = new();
            protected override void OnExecute(PipelineActionContext context) => Input.Evaluate();
        }

        [Test]
        public void Verify_FailsWhenActionSlotHasNoContent()
        {
            var graph = new Graph();
            var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
            root.Actions.Add(new ActionSlot());

            Assert.That(GraphVerifier.Collect(graph), Has.Some.Contains("沒有接任何動作內容"));
        }

        [Test]
        public void Verify_IgnoresDisabledAction()
        {
            var graph = new Graph();
            var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
            var slot = new ActionSlot { Disabled = true };
            root.Actions.Add(slot);

            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
        }

        [Test]
        public void Verify_RejectsNodeCycles()
        {
            var graph = new Graph();
            var root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
            var action = new SequenceAction();
            var slot = new ActionSlot(action);
            action.actions.Add(slot);
            root.Actions.Add(slot);

            Assert.That(GraphVerifier.CollectDiagnostics(graph).Exists(item => item.Code == "assetpipeline.node.cycle"), Is.True);
        }

        [Serializable]
        private sealed class SequenceAction : ActionBase, ISequentialActionContainer
        {
            public List<ActionSlot> actions = new();
            public IReadOnlyList<ActionSlotBase> SequentialActions => actions;
            protected override void OnExecute(PipelineActionContext context) { }
        }
    }
}
