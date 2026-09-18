namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public enum GraphExecutionState { NotVisited, Holding, Running, Completed, Failed, Cancelled }

/// <summary>A persisted node identity qualified by its document/asset scope.</summary>
public readonly struct GraphExecutionNodeKey : IEquatable<GraphExecutionNodeKey>
{
    public string Scope { get; }
    public string NodeId { get; }
    public GraphExecutionNodeKey(string nodeId, string scope = "")
    {
        if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException("A stable node id is required.", nameof(nodeId));
        NodeId = nodeId;
        Scope = scope ?? "";
    }
    public bool Equals(GraphExecutionNodeKey other) => Scope == other.Scope && NodeId == other.NodeId;
    public override bool Equals(object obj) => obj is GraphExecutionNodeKey other && Equals(other);
    public override int GetHashCode() => ((Scope?.GetHashCode() ?? 0) * 397) ^ (NodeId?.GetHashCode() ?? 0);
}

/// <summary>Immutable aggregate for repeated or overlapping visits to one node in one execution.</summary>
public readonly struct GraphNodeExecutionSnapshot
{
    public GraphExecutionState State { get; }
    public int Visits { get; }
    public int Waiting { get; }
    public int Running { get; }
    public int Completed { get; }
    public int Failed { get; }
    public int Cancelled { get; }
    public string Error { get; }
    internal GraphNodeExecutionSnapshot(GraphExecutionState state, int visits, int waiting, int running,
        int completed, int failed, int cancelled, string error)
    { State = state; Visits = visits; Waiting = waiting; Running = running; Completed = completed; Failed = failed; Cancelled = cancelled; Error = error; }
}

/// <summary>
/// Main-thread observation source shared by document copies. No execution semantics or Editor dependency.
/// Observers opt in; the final observer leaving releases Holds without cancelling the consumer's work.
/// </summary>
public sealed class GraphExecutionSource
{
    private const int HistoryLimit = 32;
    private readonly List<GraphExecutionSession> sessions = new();
    private readonly HashSet<GraphExecutionNodeKey> nextHolds = new();
    private string nextRevision;
    private int observers;
    private long nextId;
    public bool IsObserved => observers > 0;
    public IReadOnlyList<GraphExecutionSession> Sessions => sessions.AsReadOnly();

    public IDisposable Observe()
    {
        observers++;
        return new Observation(this);
    }

    public bool IsHeldForNextExecution(GraphExecutionNodeKey key, string revision)
        => nextRevision == revision && nextHolds.Contains(key);

    public void SetHoldForNextExecution(GraphExecutionNodeKey key, string revision, bool held)
    {
        if (!IsObserved) return;
        if (nextRevision != revision) { nextHolds.Clear(); nextRevision = revision; }
        if (held) nextHolds.Add(key); else nextHolds.Remove(key);
    }

    public GraphExecutionSession Begin(string name, string revision, CancellationToken cancellationToken = default)
    {
        var session = new GraphExecutionSession(this, ++nextId, name, revision, cancellationToken);
        if (IsObserved && nextRevision == revision)
        {
            foreach (var key in nextHolds) session.SetHold(key, true);
            nextHolds.Clear(); // The preparation targets one invocation, not every future execution chain.
        }
        sessions.Add(session);
        TrimHistory();
        return session;
    }

    public void CancelAll()
    {
        nextHolds.Clear();
        foreach (var session in sessions.ToArray()) session.Cancel();
    }

    public void ReleaseAllHolds()
    {
        nextHolds.Clear();
        foreach (var session in sessions.ToArray()) session.ReleaseAllHolds();
    }

    public void ClearNextHolds() => nextHolds.Clear();

    public void TrimHistory()
    {
        int completed = 0;
        for (int i = sessions.Count - 1; i >= 0; i--)
            if (sessions[i].IsFinished && ++completed > HistoryLimit) sessions.RemoveAt(i);
    }

    private sealed class Observation : IDisposable
    {
        private GraphExecutionSource source;
        public Observation(GraphExecutionSource source) { this.source = source; }
        public void Dispose()
        {
            if (source == null) return;
            if (--source.observers == 0) source.ReleaseAllHolds();
            source = null;
        }
    }
}

/// <summary>One execution chain. Consumers report visits and cooperate by awaiting each visit's Hold gate.</summary>
public sealed class GraphExecutionSession : IDisposable
{
    private readonly GraphExecutionSource source;
    private sealed class NodeState
    {
        public int Visits, Waiting, Running, Completed, Failed, Cancelled;
        public GraphExecutionState Last = GraphExecutionState.NotVisited;
        public string Error;
    }
    private readonly Dictionary<GraphExecutionNodeKey, NodeState> nodes = new();
    private readonly HashSet<GraphExecutionNodeKey> holds = new();
    private readonly HashSet<GraphNodeExecution> active = new();
    private readonly Dictionary<string, object> scopes = new();
    private readonly CancellationTokenSource cancellation;
    private long nextVisitId;
    private GraphExecutionState result = GraphExecutionState.Running;
    public long Id { get; }
    public string Name { get; }
    public string Revision { get; }
    public bool IsFinished { get; private set; }
    public GraphExecutionState State => !IsFinished && WaitingCount > 0 ? GraphExecutionState.Holding : result;
    public GraphDiagnostic MappingDiagnostic { get; private set; }
    public CancellationToken CancellationToken { get; }
    public int WaitingCount
    {
        get { int count = 0; foreach (var visit in active) if (visit.IsWaiting) count++; return count; }
    }
    public int RunningCount => active.Count - WaitingCount;

    internal GraphExecutionSession(GraphExecutionSource source, long id, string name, string revision, CancellationToken token)
    {
        this.source = source;
        Id = id;
        Name = name ?? "";
        Revision = revision;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationToken = cancellation.Token;
    }

    /// <summary>Captures the reference identity of a shared scope, such as an asset's root carrier.</summary>
    public void RegisterScope(string scope, object identity)
    {
        scope ??= "";
        if (scopes.TryGetValue(scope, out var previous) && !ReferenceEquals(previous, identity))
            throw new InvalidOperationException("Execution scope changed during the run: " + scope);
        scopes[scope] = identity;
    }
    public bool MatchesScope(string scope, object identity)
        => !scopes.TryGetValue(scope ?? "", out var previous) || ReferenceEquals(previous, identity);

    public void ReportUnmappedNode(string scope)
    {
        MappingDiagnostic ??= new GraphDiagnostic("graphkit.execution.node-id-missing", GraphDiagnosticSeverity.Warning,
            $"執行節點缺少穩定 Id（{(string.IsNullOrEmpty(scope) ? "目前文件" : scope)}）；無法可靠對應畫面。請先保存圖，再建立 runtime 副本。");
    }

    public bool IsHeld(GraphExecutionNodeKey key) => holds.Contains(key);
    public void SetHold(GraphExecutionNodeKey key, bool held)
    {
        if (IsFinished) return;
        if (held) holds.Add(key);
        else
        {
            holds.Remove(key);
            foreach (var visit in new List<GraphNodeExecution>(active))
                if (visit.Key.Equals(key)) visit.Release();
        }
    }
    public void ReleaseAllHolds()
    {
        holds.Clear();
        foreach (var visit in new List<GraphNodeExecution>(active)) visit.Release();
    }

    public GraphNodeExecution Enter(GraphExecutionNodeKey key)
    {
        if (IsFinished) throw new InvalidOperationException("The execution has ended.");
        if (string.IsNullOrEmpty(key.NodeId)) throw new ArgumentException("A stable node id is required.", nameof(key));
        CancellationToken.ThrowIfCancellationRequested();
        if (!nodes.TryGetValue(key, out var state)) nodes.Add(key, state = new NodeState());
        bool waiting = holds.Contains(key);
        state.Visits++;
        if (waiting) state.Waiting++; else state.Running++;
        var visit = new GraphNodeExecution(this, key, ++nextVisitId, waiting);
        active.Add(visit);
        return visit;
    }

    public GraphNodeExecutionSnapshot Query(GraphExecutionNodeKey key)
    {
        if (!nodes.TryGetValue(key, out var node)) return default;
        var state = node.Failed > 0 ? GraphExecutionState.Failed
            : node.Waiting > 0 ? GraphExecutionState.Holding
            : node.Running > 0 ? GraphExecutionState.Running : node.Last;
        return new GraphNodeExecutionSnapshot(state, node.Visits, node.Waiting, node.Running,
            node.Completed, node.Failed, node.Cancelled, node.Error);
    }

    internal void Started(GraphNodeExecution visit)
    {
        var node = nodes[visit.Key];
        node.Waiting--; node.Running++;
    }
    internal void Ended(GraphNodeExecution visit, GraphExecutionState result, string error)
    {
        if (!active.Remove(visit)) return;
        var node = nodes[visit.Key];
        if (visit.IsWaiting) node.Waiting--; else node.Running--;
        if (result == GraphExecutionState.Completed) node.Completed++;
        else if (result == GraphExecutionState.Failed) { node.Failed++; node.Error = error; }
        else node.Cancelled++;
        node.Last = result;
    }

    public void Cancel() { if (!IsFinished) cancellation.Cancel(); }
    public void Complete() => Finish(CancellationToken.IsCancellationRequested ? GraphExecutionState.Cancelled : GraphExecutionState.Completed);
    public void Fail() => Finish(GraphExecutionState.Failed);
    public void Dispose() { if (!IsFinished) Finish(GraphExecutionState.Cancelled); }

    private void Finish(GraphExecutionState result)
    {
        if (IsFinished) return;
        // Any unjoined child is cancelled; a completed parent must never leave an unreachable Hold behind.
        if (result != GraphExecutionState.Completed || active.Count > 0) cancellation.Cancel();
        foreach (var visit in new List<GraphNodeExecution>(active)) visit.Cancel();
        holds.Clear();
        this.result = result;
        IsFinished = true;
        cancellation.Dispose();
        source.TrimHistory();
    }
}

/// <summary>One node invocation. Hold waits are cancellable Tasks; no thread is blocked and no game time is modified.</summary>
public sealed class GraphNodeExecution : IDisposable
{
    private readonly GraphExecutionSession session;
    private readonly TaskCompletionSource<bool> gate;
    private bool ended;
    private bool waitStarted;
    public GraphExecutionNodeKey Key { get; }
    public long Id { get; }
    public bool IsWaiting { get; private set; }
    internal GraphNodeExecution(GraphExecutionSession session, GraphExecutionNodeKey key, long id, bool waiting)
    {
        this.session = session; Key = key; Id = id; IsWaiting = waiting;
        if (waiting) gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task WaitAsync()
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        if (waitStarted || ended) throw new InvalidOperationException("A node invocation can enter its gate only once.");
        waitStarted = true;
        if (gate != null)
        {
            using (session.CancellationToken.Register(() => gate.TrySetCanceled())) await gate.Task;
            session.CancellationToken.ThrowIfCancellationRequested();
            if (ended) throw new OperationCanceledException();
            session.Started(this);
            IsWaiting = false;
        }
    }
    internal void Release() => gate?.TrySetResult(true);
    public void Complete()
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        if (IsWaiting) throw new InvalidOperationException("Await the Hold gate before completing a node.");
        End(GraphExecutionState.Completed, null);
    }
    public void Fail(Exception exception) => End(GraphExecutionState.Failed, exception?.Message);
    public void Cancel() => End(GraphExecutionState.Cancelled, null);
    public void Dispose() { if (!ended) Cancel(); }
    private void End(GraphExecutionState result, string error)
    {
        if (ended) return;
        session.Ended(this, result, error);
        ended = true;
        gate?.TrySetCanceled();
    }
}
}
