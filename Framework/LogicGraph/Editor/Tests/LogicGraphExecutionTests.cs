namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using NUnit.Framework;

public sealed class LogicGraphExecutionTests
{
    private enum Timing { Run }
    private sealed class Pack { public int Count; }
    [Serializable]
    private sealed class Increment : ActionBase<Pack>
    {
        protected override UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        { pack.Count++; return UniTask.CompletedTask; }
    }
    [Serializable]
    private sealed class Failing : ActionBase<Pack>
    {
        protected override UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            => throw new InvalidOperationException("expected node failure");
    }

    private sealed class TestActionAsset : ActionAssetBase<Pack> { }

    [Serializable]
    private sealed class Waiting : ActionBase<Pack>
    {
        [NonSerialized] public CancellationToken Seen;
        [NonSerialized] public readonly TaskCompletionSource<bool> Started = new();

        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            Seen = tokens.CancellationToken;
            Started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, tokens.CancellationToken);
            pack.Count++;
        }
    }

    [Test]
    public void NodeAuthorsOverrideOnlyTheProtectedHooksWhileSlotsRemainPublic()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var execute = typeof(ActionBase<Pack>).GetMethod("Execute", flags);
        var evaluate = typeof(FormulaBase<int, Pack>).GetMethod("Evaluate", flags);
        Assert.That(execute.IsAssembly, Is.True);
        Assert.That(evaluate.IsAssembly, Is.True);
        Assert.That(evaluate.IsVirtual, Is.False);
        Assert.That(typeof(ActionBase<Pack>).GetMethod("OnExecute", flags).IsFamily, Is.True);
        Assert.That(typeof(FormulaBase<int, Pack>).GetMethod("OnEvaluate", flags).IsFamily, Is.True);
        Assert.That(typeof(ActionSlot<Pack>).GetMethod("Execute").IsPublic, Is.True);
        Assert.That(typeof(TokenTable<Pack>).GetProperty("CancellationToken").CanWrite, Is.False);
    }

    [Test]
    public async Task DisabledAssetRootDoesNotExecuteOrEnterAHold()
    {
        var asset = UnityEngine.ScriptableObject.CreateInstance<TestActionAsset>();
        var graph = Create(new Increment(), out var carrier);
        var root = new GraphNode(new Increment());
        root.EnsureId();
        root.Disabled = true;
        asset.SetRoot(root);
        carrier.SetAsset(asset);
        using var observation = graph.ExecutionSource.Observe();
        var key = new GraphExecutionNodeKey(root.Id, "asset:" + asset.GetInstanceID());
        graph.ExecutionSource.SetHoldForNextExecution(key, graph.ExecutionRevision, true);
        try
        {
            var pack = new Pack();
            var running = graph.TriggerAction(Timing.Run, pack).AsTask();
            Assert.That(running.IsCompleted, Is.True, "停用根節點不可進入 Hold。");
            await running;
            await asset.Execute(pack, null);
            Assert.That(pack.Count, Is.Zero);
            Assert.That(graph.ExecutionSource.Sessions[0].Query(key).State, Is.EqualTo(GraphExecutionState.NotVisited));
            root.Disabled = false;
            await asset.Execute(pack, null);
            Assert.That(pack.Count, Is.EqualTo(1));
        }
        finally { graph.CancelObservedExecutions(); UnityEngine.Object.DestroyImmediate(asset); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NodeAwaitReceivesCancellationWithAndWithoutAnObserver(bool observed)
    {
        var action = new Waiting();
        var graph = Create(action, out _);
        using var cancellation = new CancellationTokenSource();
        using var observation = observed ? graph.ExecutionSource.Observe() : null;
        try
        {
            var pack = new Pack();
            var running = graph.TriggerAction(Timing.Run, pack, cancellation.Token).AsTask();
            await action.Started.Task;
            Assert.That(action.Seen.CanBeCanceled, Is.True);
            cancellation.Cancel();
            try { await running; Assert.Fail("節點內的 await 應收到取消。"); }
            catch (OperationCanceledException) { }
            Assert.That(pack.Count, Is.Zero);
            if (observed)
                Assert.That(graph.ExecutionSource.Sessions[0].State, Is.EqualTo(GraphExecutionState.Cancelled));
        }
        finally { cancellation.Cancel(); graph.CancelObservedExecutions(); }
    }

    private static LogicGraph<Timing, Pack> Create(ActionBase<Pack> body, out GraphNode node)
    {
        var graph = new LogicGraph<Timing, Pack>();
        node = new GraphNode(body);
        node.EnsureId();
        var slot = new ActionSlot<Pack>();
        slot.SetNode(node);
        graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack> { Timing = Timing.Run, Actions = new() { slot } });
        graph.MarkValidated();
        return graph;
    }

    [Test]
    public async Task RuntimeCopiesShareObservationButHoldOnlyOneExecutionChain()
    {
        var authored = Create(new Increment(), out var node);
        var first = authored.DeepCopy();
        var second = authored.DeepCopy();
        Assert.That(first.ExecutionSource, Is.SameAs(authored.ExecutionSource));
        Assert.That(first.ExecutionRevision, Is.EqualTo(authored.ExecutionRevision));
        using var observation = authored.ExecutionSource.Observe();
        var key = new GraphExecutionNodeKey(node.Id);
        authored.ExecutionSource.SetHoldForNextExecution(key, authored.ExecutionRevision, true);
        var firstPack = new Pack();
        var held = first.TriggerAction(Timing.Run, firstPack).AsTask();
        var session = authored.ExecutionSource.Sessions[0];
        try
        {
            Assert.That(firstPack.Count, Is.Zero);
            Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Holding));
            var secondPack = new Pack();
            await second.TriggerAction(Timing.Run, secondPack);
            Assert.That(secondPack.Count, Is.EqualTo(1));
            Assert.That(firstPack.Count, Is.Zero);
            session.SetHold(key, false);
            await held;
            Assert.That(firstPack.Count, Is.EqualTo(1));
            Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Completed));
        }
        finally { first.CancelObservedExecutions(); second.CancelObservedExecutions(); }
    }

    [Test]
    public async Task RetiringOneRuntimeCopyCancelsItsWaitWithoutExecutingTheNode()
    {
        var graph = Create(new Increment(), out var node);
        using var observation = graph.ExecutionSource.Observe();
        graph.ExecutionSource.SetHoldForNextExecution(new GraphExecutionNodeKey(node.Id), graph.ExecutionRevision, true);
        var pack = new Pack();
        var running = graph.TriggerAction(Timing.Run, pack).AsTask();
        graph.CancelObservedExecutions();
        try { await running; Assert.Fail("Retired graphs must not resume their held node."); }
        catch (OperationCanceledException) { }
        Assert.That(pack.Count, Is.Zero);
        Assert.That(graph.ExecutionSource.Sessions[0].State, Is.EqualTo(GraphExecutionState.Cancelled));
    }

    [Test]
    public async Task ExceptionsRemainVisibleToTheCallerAndExecutionView()
    {
        var graph = Create(new Failing(), out var node);
        using var observation = graph.ExecutionSource.Observe();
        try { await graph.TriggerAction(Timing.Run, new Pack()); Assert.Fail("Failure must propagate."); }
        catch (InvalidOperationException exception) { Assert.That(exception.Message, Is.EqualTo("expected node failure")); }
        var session = graph.ExecutionSource.Sessions[0];
        Assert.That(session.State, Is.EqualTo(GraphExecutionState.Failed));
        Assert.That(session.Query(new GraphExecutionNodeKey(node.Id)).Error, Is.EqualTo("expected node failure"));
    }

    [Test]
    public async Task UnobservedAndDisabledNodesKeepTheirExecutionSemantics()
    {
        var graph = Create(new Increment(), out var node);
        var pack = new Pack();
        await graph.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
        Assert.That(graph.ExecutionSource.Sessions, Is.Empty);
        using var observation = graph.ExecutionSource.Observe();
        node.Disabled = true;
        await graph.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
        Assert.That(graph.ExecutionSource.Sessions[0].Query(new GraphExecutionNodeKey(node.Id)).State,
            Is.EqualTo(GraphExecutionState.NotVisited));
    }

    [Test]
    public void EditingADocumentDoesNotRelabelAlreadyCreatedRuntimeCopies()
    {
        var graph = Create(new Increment(), out _);
        var runtime = graph.DeepCopy();
        string revision = runtime.ExecutionRevision;
        graph.MarkDirty();
        Assert.That(runtime.ExecutionRevision, Is.EqualTo(revision));
        Assert.That(graph.ExecutionRevision, Is.Not.EqualTo(revision));
        Assert.That(runtime.ExecutionSource, Is.SameAs(graph.ExecutionSource));
    }

    [Test]
    public async Task MissingPersistedNodeIdentityReportsAnIncompleteProjectionWithoutSkippingExecution()
    {
        var graph = Create(new Increment(), out var node);
        node.ResetId();
        using var observation = graph.ExecutionSource.Observe();
        var pack = new Pack();
        await graph.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
        Assert.That(graph.ExecutionSource.Sessions[0].MappingDiagnostic.Code, Is.EqualTo("graphkit.execution.node-id-missing"));
    }
}
}
