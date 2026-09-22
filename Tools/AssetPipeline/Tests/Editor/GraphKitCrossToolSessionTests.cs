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
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void SharedValidatorHonorsDisabledChildrenAndEnabledSharedPaths(
            bool logicGraph, bool alsoEnabled, bool disableNode)
        {
            ScriptableObject owner = null;
            try
            {
                var model = new HGModel();
                if (logicGraph)
                {
                    var logicOwner = ScriptableObject.CreateInstance<LogicOwner>();
                    owner = logicOwner;
                    var broken = new ActionSlot<CrossPack>();
                    broken.SetNode(new GraphNode());
                    var inner = new LogicSequence { actions = new() { broken } };
                    var disabled = new ActionSlot<CrossPack>(inner);
                    if (disableNode) disabled.Node.Disabled = true;
                    else disabled.Disabled = true;
                    var outer = new LogicSequence { actions = new() { disabled } };
                    if (alsoEnabled) outer.actions.Add(new ActionSlot<CrossPack>(inner));
                    logicOwner.Graph = new LogicGraph<CrossTiming, CrossPack>();
                    logicOwner.Graph.ActionGroups.Add(new ActionTimingGroup<CrossTiming, CrossPack>
                    {
                        Timing = CrossTiming.Start,
                        Actions = new() { new ActionSlot<CrossPack>(outer) },
                    });
                    Assert.That(model.Bind(owner, new HGDocumentBinding<LogicGraph<CrossTiming, CrossPack>>("LogicGraph.Cross",
                        value => ((LogicOwner)value).Graph, (value, graph) => ((LogicOwner)value).Graph = graph)), Is.True);
                    var diagnostics = logicOwner.Graph.CollectDiagnostics();
                    Assert.That(System.Linq.Enumerable.Any(diagnostics, item => item.Severity == GraphDiagnosticSeverity.Error),
                        Is.EqualTo(alsoEnabled), "LogicGraph Core 與共用編輯器應保留一致的啟用路徑判準。");
                }
                else
                {
                    var pipelineOwner = ScriptableObject.CreateInstance<AssetPipeline>();
                    owner = pipelineOwner;
                    var broken = new ActionSlot();
                    broken.SetNode(new GraphNode());
                    var inner = new PipelineSequence { actions = new() { broken } };
                    var disabled = new ActionSlot(inner);
                    if (disableNode) disabled.Node.Disabled = true;
                    else disabled.Disabled = true;
                    var outer = new PipelineSequence { actions = new() { disabled } };
                    if (alsoEnabled) outer.actions.Add(new ActionSlot(inner));
                    pipelineOwner.graph = new Graph();
                    var root = (ActionGroup)((IGraphDocument)pipelineOwner.graph).AddRoot(Graph.PipelineKey);
                    root.Actions.Add(new ActionSlot(outer));
                    Assert.That(model.Bind(owner, new HGDocumentBinding<Graph>("AssetPipeline.Graph",
                        value => ((AssetPipeline)value).graph, (value, graph) => ((AssetPipeline)value).graph = graph)), Is.True);
                    Assert.That(GraphVerifier.CollectDiagnostics(pipelineOwner.graph).Count > 0, Is.EqualTo(alsoEnabled));
                }

                HGReport report = HGValidator.Run(model);
                Assert.That(report.ErrorCount > 0, Is.EqualTo(alsoEnabled));
                if (!alsoEnabled) Assert.That(report.WarningCount, Is.GreaterThan(0));
            }
            finally { if (owner != null) UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ResultNavigationKeepsTheCurrentDocumentAndDirtyState()
        {
            var owner = ScriptableObject.CreateInstance<AssetPipeline>();
            var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
            try
            {
                ((IGraphDocument)owner.graph).AddRoot(Graph.PipelineKey);
                var carrier = new GraphNode();
                carrier.EnsureId();
                carrier.SetProperty(new GraphProperty("Property", new ObjectListSlot(), false));
                owner.graph.Orphans.Add(carrier);
                Graph original = owner.graph;
                var binding = new HGDocumentBinding<Graph>("AssetPipeline.Graph",
                    value => ((AssetPipeline)value).graph, (value, graph) => ((AssetPipeline)value).graph = graph);
                Assert.That(window.BindDocument(owner, binding, ContextFor<Graph>(HGCapabilities.Properties)), Is.True);
                var commands = window.GetDocumentCommands();
                bool dirty = commands.Query().IsDirty;

                Assert.That(window.IsBoundToDocument(owner, "AssetPipeline.Graph"), Is.True);
                Assert.That(window.FocusDocumentNode(carrier.Id), Is.True);
                Assert.That(commands.Query().IsDirty, Is.EqualTo(dirty));
                Assert.That(owner.graph, Is.SameAs(original));
            }
            finally
            {
                window.GetDocumentCommands()?.Cancel();
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PublicWindowCommandsAreBoundToTheSelectedToolDocument()
        {
            var logicOwner = ScriptableObject.CreateInstance<LogicOwner>();
            var pipelineOwner = ScriptableObject.CreateInstance<AssetPipeline>();
            var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
            try
            {
                logicOwner.Graph = new LogicGraph<CrossTiming, CrossPack>();
                pipelineOwner.graph = new Graph();
                var logicBinding = new HGDocumentBinding<LogicGraph<CrossTiming, CrossPack>>("LogicGraph.Cross",
                    owner => ((LogicOwner)owner).Graph, (owner, graph) => ((LogicOwner)owner).Graph = graph);
                var pipelineBinding = new HGDocumentBinding<Graph>("AssetPipeline.Graph",
                    owner => ((AssetPipeline)owner).graph, (owner, graph) => ((AssetPipeline)owner).graph = graph);
                Assert.That(window.BindDocument(logicOwner, logicBinding, ContextFor<LogicGraph<CrossTiming, CrossPack>>(HGCapabilities.Tokens)), Is.True);
                HGWindowSession old = window.GetDocumentCommands();
                Assert.That(old.Query(), Is.Not.Null);
                Assert.That(window.BindDocument(pipelineOwner, pipelineBinding, ContextFor<Graph>(HGCapabilities.Properties)), Is.True);
                Assert.That(old.Query(), Is.Null);
                Assert.That(old.Commit(), Is.EqualTo(HGSessionCommandResult.StaleGeneration));
                HGWindowSnapshot current = window.GetDocumentCommands().Query();
                Assert.That(current, Is.Not.Null);
                Assert.That(current.IsDirty, Is.False);
            }
            finally
            {
                window.GetDocumentCommands()?.Cancel();
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(logicOwner);
                UnityEngine.Object.DestroyImmediate(pipelineOwner);
            }
        }

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
            var pipelineContext = ContextFor<Graph>(HGCapabilities.Properties);
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
            Assert.That(pipelineContext.CapabilitiesOf(pipeline.Document), Is.EqualTo(HGCapabilities.Properties));
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

        [Serializable]
        private sealed class PipelineSequence : ActionBase, ISequentialActionContainer
        {
            public List<ActionSlot> actions = new();
            public IReadOnlyList<ActionSlotBase> SequentialActions => actions;
            protected override void OnExecute(PipelineActionContext context)
            {
                foreach (ActionSlot action in actions)
                {
                    action.Execute(context);
                    if (context.Result.HasFailure) return;
                }
            }
        }

        [Serializable]
        private sealed class LogicSequence : ActionBase<CrossPack>, ISequentialActionContainer
        {
            public List<ActionSlot<CrossPack>> actions = new();
            public IReadOnlyList<ActionSlotBase> SequentialActions => actions;
            protected override async Cysharp.Threading.Tasks.UniTask OnExecute(CrossPack pack, TokenTable<CrossPack> tokens)
            {
                foreach (ActionSlot<CrossPack> action in actions) await action.Execute(pack, tokens);
            }
        }

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
