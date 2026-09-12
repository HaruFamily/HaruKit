using HaruFamily.Framework.LogicGraph;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    /// <summary>
    /// dynamic key 的時序規則：產出它的步驟必須排在讀取它的步驟之前。
    /// </summary>
    // 這條是 AssetPipeline 獨有的，GraphKit 的具名變數沒有先後概念，所以測的是 APGraphVerifier 而不是 LGValidator。
    public sealed class APGraphVerifierTests
    {
        private APGraph graph;
        private APStepGroup root;

        [SetUp]
        public void SetUp()
        {
            graph = new APGraph();
            root = (APStepGroup)((IGraphDocument)graph).AddRoot(APGraph.PipelineKey);
        }

        private void AddStep(APActionBase step)
        {
            root.Steps.Add(new APActionSlot(step));
        }

        [Test]
        public void Verify_FailsWhenDynamicKeyIsReadBeforeProduce()
        {
            AddStep(new ReadStep("Generated"));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("產出它的步驟不在前面"));
        }

        [Test]
        public void Verify_PassesWhenDynamicKeyIsProducedInOrder()
        {
            AddStep(new ProduceStep("Generated"));
            AddStep(new ReadStep("Generated"));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains("產出它的步驟不在前面"));
        }

        [Test]
        public void Verify_FailsWhenProducerComesAfterReader()
        {
            AddStep(new ReadStep("Generated"));
            AddStep(new ProduceStep("Generated"));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("產出它的步驟不在前面"));
        }

        [Test]
        public void Verify_FailsWhenStepSlotHasNoContent()
        {
            root.Steps.Add(new APActionSlot());

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有接任何步驟內容"));
        }

        [Test]
        public void Verify_IgnoresDisabledStep()
        {
            var slot = new APActionSlot();
            slot.Disabled = true;
            root.Steps.Add(slot);

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        /// <summary>只宣告「我產出這個 dynamic key」的假步驟，不做任何事。</summary>
        // 測試自備 producer 而不是借用真實步驟：具體步驟住在使用端專案，測試組件看不到它們。
        [Serializable]
        private sealed class ProduceStep : APActionBase, IDynamicKeyProducer
        {
            private readonly string key;

            public ProduceStep(string key)
            {
                this.key = key;
            }

            public bool TryGetDynamicOutputKey(out string outputKey)
            {
                outputKey = key;
                return !string.IsNullOrWhiteSpace(key);
            }

            public override void Execute()
            {
            }
        }

        /// <summary>只宣告「我讀這個 dynamic key」的假步驟，不做任何事。</summary>
        [Serializable]
        private sealed class ReadStep : APActionBase, IDynamicKeyReader
        {
            public AssetPipelineSource source;

            public ReadStep()
            {
            }

            public ReadStep(string key)
            {
                source = new AssetPipelineSource
                {
                    sourceFlags = AssetPipelineSourceFlags.Dynamic,
                    keys = new List<string> { key },
                };
            }

            public IEnumerable<string> DynamicInputKeys => source.ReadKeys(AssetPipelineSourceFlags.Dynamic);

            public override void Execute()
            {
            }
        }
    }
}
