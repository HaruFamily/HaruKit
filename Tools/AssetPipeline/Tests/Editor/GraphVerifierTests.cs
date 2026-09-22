using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using NUnit.Framework;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class GraphVerifierTests
    {
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
