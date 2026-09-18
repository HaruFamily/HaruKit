using System;
using System.Collections;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using HaruFamily.Framework.LogicGraph;
using NUnit.Framework;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class GraphKitCrossToolSessionTests
    {
        [Test]
        public void ExplicitSessionsKeepLogicGraphPipelineAndConsumerScopesIndependent()
        {
            var logicOwner = ScriptableObject.CreateInstance<LogicOwner>();
            var pipelineOwner = ScriptableObject.CreateInstance<AssetPipeline>();
            var consumerOwner = ScriptableObject.CreateInstance<ConsumerOwner>();
            logicOwner.Graph = new LogicGraph<CrossTiming, CrossPack>();
            pipelineOwner.graph = new Graph();
            consumerOwner.Document = new ConsumerDocument();

            var logicBinding = new HGDocumentBinding<LogicGraph<CrossTiming, CrossPack>>("LogicGraph.Cross",
                owner => ((LogicOwner)owner).Graph, (owner, graph) => ((LogicOwner)owner).Graph = graph,
                () => new LogicGraph<CrossTiming, CrossPack>());
            var pipelineBinding = new HGDocumentBinding<Graph>("AssetPipeline.Graph",
                owner => ((AssetPipeline)owner).graph, (owner, graph) => ((AssetPipeline)owner).graph = graph,
                () => new Graph());
            var consumerBinding = new HGDocumentBinding<ConsumerDocument>("Consumer.Document",
                owner => ((ConsumerOwner)owner).Document, (owner, document) => ((ConsumerOwner)owner).Document = document,
                () => new ConsumerDocument());

            Assert.That(HGDocumentSession<LogicGraph<CrossTiming, CrossPack>>.TryOpen(logicOwner, logicBinding, out var logic), Is.True);
            Assert.That(HGDocumentSession<Graph>.TryOpen(pipelineOwner, pipelineBinding, out var pipeline), Is.True);
            Assert.That(HGDocumentSession<ConsumerDocument>.TryOpen(consumerOwner, consumerBinding, out var consumer), Is.True);

            var logicContext = ContextFor<LogicGraph<CrossTiming, CrossPack>>(HGCapabilities.SharedAssets | HGCapabilities.Tokens);
            var pipelineContext = ContextFor<Graph>(HGCapabilities.Catalogs);
            var consumerContext = ContextFor<ConsumerDocument>(HGCapabilities.None);

            Assert.That(logic.DocumentId, Is.EqualTo("LogicGraph.Cross"));
            Assert.That(pipeline.DocumentId, Is.EqualTo("AssetPipeline.Graph"));
            Assert.That(consumer.DocumentId, Is.EqualTo("Consumer.Document"));
            Assert.That(logic.Document, Is.Not.SameAs(logicOwner.Graph));
            Assert.That(pipeline.Document, Is.Not.SameAs(pipelineOwner.graph));
            Assert.That(consumer.Document, Is.Not.SameAs(consumerOwner.Document));
            Assert.That(logicContext.Supports(logicOwner, logic.Document), Is.True);
            Assert.That(logicContext.Supports(pipelineOwner, pipeline.Document), Is.False);
            Assert.That(pipelineContext.Supports(pipelineOwner, pipeline.Document), Is.True);
            Assert.That(pipelineContext.Supports(consumerOwner, consumer.Document), Is.False);
            Assert.That(consumerContext.Supports(consumerOwner, consumer.Document), Is.True);
            Assert.That(logicContext.CapabilitiesOf(logic.Document), Is.EqualTo(HGCapabilities.SharedAssets | HGCapabilities.Tokens));
            Assert.That(pipelineContext.CapabilitiesOf(pipeline.Document), Is.EqualTo(HGCapabilities.Catalogs));
            Assert.That(consumerContext.CapabilitiesOf(consumer.Document), Is.EqualTo(HGCapabilities.None));

            var logicRegistry = logic.CreatePortRegistry();
            var key = new HGPortKey("cross-tool", "/input", HGPortRole.Input);
            Assert.That(pipeline.Disconnect(logicRegistry, key), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
            Assert.That(consumer.Disconnect(logicRegistry, key), Is.EqualTo(HGSessionCommandResult.StaleGeneration));

            UnityEngine.Object.DestroyImmediate(logicOwner);
            UnityEngine.Object.DestroyImmediate(pipelineOwner);
            UnityEngine.Object.DestroyImmediate(consumerOwner);
        }

        private static HGEditorExtensionContext ContextFor<TDocument>(HGCapabilities capabilities)
            where TDocument : class, IGraphDocument
            => new HGEditorExtensionContext(new DocumentProvider<TDocument>(), profile: new HGEditorProfile(capabilities));

        private sealed class DocumentProvider<TDocument> : IHGEditorExtensionProvider
            where TDocument : class, IGraphDocument
        {
            public bool Supports(UnityEngine.Object owner, IGraphDocument document) => document is TDocument;
            public void AddPorts(HGPortBuildContext context) { }
        }

        private enum CrossTiming
        {
            Start,
        }

        private struct CrossPack { }

        private sealed class LogicOwner : ScriptableObject
        {
            public LogicGraph<CrossTiming, CrossPack> Graph;
        }

        private sealed class ConsumerOwner : ScriptableObject
        {
            public ConsumerDocument Document;
        }

        private sealed class ConsumerDocument : IGraphDocument
        {
            public List<GraphNode> Orphans { get; } = new();
            public IList Roots => Array.Empty<object>();
            public bool IsValidated { get; private set; }
            public Type PackType => typeof(CrossPack);
            public Type ItemSlotType => null;
            public string RootChip => "Consumer";
            public string RootNoun => "Consumer";
            public HGCapabilities Capabilities => HGCapabilities.None;
            public string WindowTitle => "Consumer";

            public void MarkDirty() => IsValidated = false;
            public void Verify() => IsValidated = true;
            public object DeepCopy() => new ConsumerDocument();
            public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => Array.Empty<object>();
            public object KeyOf(object root) => null;
            public string TitleOf(object root) => "";
            public IList ItemsOf(object root) => null;
            public object AddRoot(object key) => null;
        }
    }
}
