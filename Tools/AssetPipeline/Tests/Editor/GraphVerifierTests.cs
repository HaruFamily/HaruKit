using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using NUnit.Framework;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class GraphVerifierTests
    {
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
        private sealed class KeyText : KeyFamily
        {
            protected override string OnEvaluate(NullPack pack) => "key";
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
