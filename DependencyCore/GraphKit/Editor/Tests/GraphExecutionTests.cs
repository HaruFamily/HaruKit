namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

public sealed class GraphExecutionTests
{
    [Test]
    public async Task HoldStopsOnlyTheSelectedExecutionAndCountsRepeatedVisits()
    {
        var source = new GraphExecutionSource();
        using var observation = source.Observe();
        using var first = source.Begin("first", "revision");
        using var second = source.Begin("second", "revision");
        var key = new GraphExecutionNodeKey("shared");
        first.SetHold(key, true);
        using var waiting = first.Enter(key);
        var gate = waiting.WaitAsync();
        Assert.That(gate.IsCompleted, Is.False);
        Assert.That(first.Query(key).State, Is.EqualTo(GraphExecutionState.Holding));
        Assert.That(first.State, Is.EqualTo(GraphExecutionState.Holding));
        Assert.Throws<InvalidOperationException>(() => waiting.Complete());
        using (var independent = second.Enter(key))
        {
            await independent.WaitAsync();
            independent.Complete();
        }
        Assert.That(second.Query(key).Completed, Is.EqualTo(1));
        Assert.That(gate.IsCompleted, Is.False);
        first.SetHold(key, false);
        await gate;
        Assert.That(first.Query(key).State, Is.EqualTo(GraphExecutionState.Running));
        waiting.Complete();
        using (var repeated = first.Enter(key))
        {
            await repeated.WaitAsync();
            Assert.That(first.Query(key).Completed, Is.EqualTo(1));
            Assert.That(first.Query(key).State, Is.EqualTo(GraphExecutionState.Running));
            repeated.Complete();
        }
        Assert.That(first.Query(key).Completed, Is.EqualTo(2));
    }

    [Test]
    public async Task LifetimeCancellationEndsAHeldVisitWithoutReportingCompletion()
    {
        var source = new GraphExecutionSource();
        using var lifetime = new CancellationTokenSource();
        using var session = source.Begin("cancel", "revision", lifetime.Token);
        var key = new GraphExecutionNodeKey("held");
        session.SetHold(key, true);
        using var visit = session.Enter(key);
        var gate = visit.WaitAsync();
        lifetime.Cancel();
        try { await gate; Assert.Fail("Cancellation must not resume the node body."); }
        catch (OperationCanceledException) { visit.Cancel(); }
        Assert.That(session.Query(key).Cancelled, Is.EqualTo(1));
        Assert.That(session.Query(key).Completed, Is.Zero);
        Assert.That(session.WaitingCount, Is.Zero);
        session.Dispose();
        Assert.That(session.State, Is.EqualTo(GraphExecutionState.Cancelled));
    }

    [Test]
    public async Task OverlappingVisitsAndAssetScopesKeepTheirOwnState()
    {
        var source = new GraphExecutionSource();
        using var session = source.Begin("overlap", "revision");
        var key = new GraphExecutionNodeKey("node", "asset:A");
        var other = new GraphExecutionNodeKey("node", "asset:B");
        using var first = session.Enter(key);
        await first.WaitAsync();
        session.SetHold(key, true);
        using var second = session.Enter(key);
        var gate = second.WaitAsync();
        first.Complete();
        Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Holding));
        Assert.That(session.Query(key).Completed, Is.EqualTo(1));
        Assert.That(session.Query(other).State, Is.EqualTo(GraphExecutionState.NotVisited));
        session.ReleaseAllHolds();
        await gate;
        second.Fail(new InvalidOperationException("node failed"));
        Assert.That(session.Query(key).State, Is.EqualTo(GraphExecutionState.Failed));
        Assert.That(session.Query(key).Error, Is.EqualTo("node failed"));
    }

    [Test]
    public void PreparedHoldAppliesOnlyToTheNextMatchingRevision()
    {
        var source = new GraphExecutionSource();
        using var observation = source.Observe();
        var key = new GraphExecutionNodeKey("node");
        source.SetHoldForNextExecution(key, "new", true);
        using var old = source.Begin("old", "old");
        using var next = source.Begin("next", "new");
        using var later = source.Begin("later", "new");
        Assert.That(old.IsHeld(key), Is.False);
        Assert.That(next.IsHeld(key), Is.True);
        Assert.That(later.IsHeld(key), Is.False);
    }

    [Test]
    public async Task LastObserverLeavingReleasesHoldsWithoutCancellingWork()
    {
        var source = new GraphExecutionSource();
        var first = source.Observe();
        var second = source.Observe();
        using var session = source.Begin("observed", "revision");
        var key = new GraphExecutionNodeKey("node");
        session.SetHold(key, true);
        using var visit = session.Enter(key);
        var gate = visit.WaitAsync();
        first.Dispose();
        Assert.That(gate.IsCompleted, Is.False);
        second.Dispose();
        await gate;
        Assert.That(session.CancellationToken.IsCancellationRequested, Is.False);
        visit.Complete();
        session.Complete();
        Assert.That(session.State, Is.EqualTo(GraphExecutionState.Completed));
        Assert.That(session.CancellationToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public void HistoryLimitNeverEvictsAnActiveExecution()
    {
        var source = new GraphExecutionSource();
        using var active = source.Begin("active", "revision");
        for (int i = 0; i < 40; i++) source.Begin("complete", "revision").Complete();
        Assert.That(source.Sessions.Count, Is.EqualTo(33));
        Assert.That(source.Sessions[0], Is.SameAs(active));
        active.Complete();
        Assert.That(source.Sessions.Count, Is.EqualTo(32));
    }

    [Test]
    public void SharedAssetVersionChangeIsRejectedWithinOneExecution()
    {
        var source = new GraphExecutionSource();
        using var session = source.Begin("asset", "revision");
        var original = new object();
        session.RegisterScope("asset", original);
        session.RegisterScope("asset", original);
        Assert.That(session.MatchesScope("asset", new object()), Is.False);
        Assert.Throws<InvalidOperationException>(() => session.RegisterScope("asset", new object()));
    }

    [Test]
    public void NodeStatusPrioritizesErrorsAndKeepsWarningsOutOfExecutionColors()
    {
        Assert.That(HGNodeStatus.ColorOf(true, true, default(GraphNodeExecutionSnapshot)), Is.EqualTo(HGStyles.Error));
        Assert.That(HGNodeStatus.ColorOf(true, false, null), Is.EqualTo(HGStyles.Warning));
        Assert.That(HGNodeStatus.ColorOf(true, false, default(GraphNodeExecutionSnapshot)), Is.EqualTo(HGStyles.ExecutionNotVisited));
        Assert.That(HGNodeStatus.ColorOf(false, false, null), Is.Null);
    }
}
}
