using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class PipelineGraphAnalyzerTests
    {
        private AssetPipeline pipeline;

        [SetUp]
        public void SetUp()
        {
            pipeline = ScriptableObject.CreateInstance<AssetPipeline>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(pipeline);
        }

        [Test]
        public void Analyze_WarnsWhenDynamicKeyIsReadBeforeProduce()
        {
            pipeline.pipelineAssets.Add(new ReadStep(AssetPipelineSourceFlags.Dynamic, "Generated"));

            PipelineGraphAnalysis result = PipelineGraphAnalyzer.Analyze(pipeline);

            Assert.That(result.Warnings, Has.Some.Contains("producer 前"));
        }

        [Test]
        public void Analyze_WarnsWhenPrototypeKeyIsMissing()
        {
            pipeline.pipelineAssets.Add(new ReadStep(AssetPipelineSourceFlags.Prototype, "Characters"));

            PipelineGraphAnalysis result = PipelineGraphAnalyzer.Analyze(pipeline);

            Assert.That(result.Warnings, Has.Some.Contains("缺少 prototype key"));
        }

        [Test]
        public void Analyze_DoesNotWarnWhenDynamicKeyIsProducedInOrder()
        {
            pipeline.pipelineAssets.Add(new PipelineAsset_RegisterAudioClipsFromFolder { dynamicKey = "Generated" });
            pipeline.pipelineAssets.Add(new ReadStep(AssetPipelineSourceFlags.Dynamic, "Generated"));

            PipelineGraphAnalysis result = PipelineGraphAnalyzer.Analyze(pipeline);

            Assert.That(result.Warnings, Has.None.Contains("producer 前"));
        }

        [Serializable]
        private sealed class ReadStep : IPipelineAsset
        {
            public AssetPipelineSource source;

            public ReadStep()
            {
            }

            public ReadStep(AssetPipelineSourceFlags flags, string key)
            {
                source = new AssetPipelineSource
                {
                    sourceFlags = flags,
                    keys = new List<string> { key }
                };
            }

            public void Execute()
            {
            }
        }
    }
}
