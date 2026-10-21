namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using NUnit.Framework;

public sealed class LogicGraphExecutionTests
{
    private enum Timing { Run }
    private sealed class Pack { public int Count; }

    private abstract class NativeObject : FormulaBase<object, Pack> { }
    [Serializable] private sealed class ObjectToken : FormulaToken<object, CompatibleObjectSlot, Pack> { }
    private sealed class ObjectAsset : FormulaAsset<object, CompatibleObjectSlot, Pack> { }
    private sealed class CompatibleObjectSlot : FormulaSlot<object, NativeObject, ObjectToken, ObjectAsset, Pack>
    {
        public CompatibleObjectSlot() { }
        protected override bool AllowCompatibleResult => true;
    }

    private sealed class TextResult : FormulaBase<string, Pack>
    {
        public TokenTable<Pack> Seen;
        public string Value = "text";
        protected override async UniTask<string> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
        {
            await Task.Yield();
            Seen = tokens;
            pack.Count++;
            return Value;
        }
    }

    private sealed class WrongPackText : FormulaBase<string, string>
    {
        protected override UniTask<string> OnEvaluate(string pack, TokenTable<string> tokens)
            => UniTask.FromResult("wrong pack");
    }

    private abstract class NativeText : FormulaBase<string, Pack> { }
    [Serializable] private sealed class TextToken : FormulaToken<string, StrictTextSlot, Pack> { }
    [Serializable] private sealed class CompatibleTextToken : FormulaToken<string, CompatibleTextSlot, Pack> { }
    private sealed class TextAsset : FormulaAsset<string, StrictTextSlot, Pack> { }
    private class StrictTextSlot : FormulaSlot<string, NativeText, TextToken, TextAsset, Pack> { public StrictTextSlot() { } }
    private sealed class CompatibleTextSlot : FormulaSlot<string, NativeText, CompatibleTextToken, TextAsset, Pack>
    {
        public CompatibleTextSlot() { }
        private static readonly HashSet<Type> excluded = new() { typeof(KeyFamily) };
        protected override bool AllowCompatibleResult => true;
        protected override IReadOnlyCollection<Type> ExcludedFormulaFamilies => excluded;
    }
    private abstract class KeyFamily : FormulaBase<string, Pack> { }
    private sealed class KeyText : KeyFamily
    {
        protected override UniTask<string> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
            => UniTask.FromResult("key");
    }

    [Test]
    public async Task CompatibleFormula_UpcastsWithOriginalContext_AndPreservesNullAndDisabledFallback()
    {
        var pack = new Pack();
        var tokens = new TokenTable<Pack>();
        var formula = new TextResult();
        var node = new GraphNode(formula);
        var slot = new CompatibleObjectSlot { Default = "fallback" };
        slot.SetNode(node);
        Assert.That(slot.AcceptsBody(formula), Is.True);
        Assert.That(slot.AcceptsBody(new WrongPackText()), Is.False);
        Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("text"));
        Assert.That(formula.Seen, Is.SameAs(tokens));
        Assert.That(pack.Count, Is.EqualTo(1));
        formula.Value = null;
        Assert.That(await slot.Evaluate(pack, tokens), Is.Null);
        node.Disabled = true;
        Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("fallback"));
        Assert.That(pack.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task CompatibleFormula_SameResultRequiresOptIn_AndBlacklistRejectsKeyFamily()
    {
        var formula = new TextResult();
        var slot = new CompatibleTextSlot();
        Assert.That(new StrictTextSlot().AcceptsBody(formula), Is.False);
        Assert.That(slot.AcceptsBody(formula), Is.True);
        Assert.That(slot.AcceptsBody(new KeyText()), Is.False);
        Assert.That(slot.AcceptsBody(new IntFormula()), Is.False);
        slot.SetNode(new GraphNode(formula));
        Assert.That(await slot.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("text"));
    }
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
    private class IntFormula : FormulaBase<int, Pack>
    {
        protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens) => UniTask.FromResult(0);
    }

    [Serializable] private sealed class IntToken : FormulaToken<int, IntSlot, Pack> { }
    [Serializable] private sealed class ContextualToken : FormulaToken<int, ContextualIntSlot, Pack> { }
    private sealed class IntFormulaAsset : FormulaAsset<int, IntSlot, Pack> { }

    [Serializable]
    private sealed class IntSlot : FormulaSlot<int, IntFormula, IntToken, IntFormulaAsset, Pack>
    {
        public IntSlot() { }
        public IntSlot(int value) : base(value) { }
    }

    [Serializable]
    private sealed class ContextualIntSlot : FormulaSlot<int, IntFormula, ContextualToken, IntFormulaAsset, Pack>
    {
        public ContextualIntSlot() { }
        public int Offset;
        protected override int EvaluateUnlinked(Pack pack) => pack.Count + Offset;
    }

    [Serializable]
    private sealed class ReadContextualInt : IntFormula
    {
        public ContextualIntSlot Value = new();
        protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
            => Value.Evaluate(pack, tokens);
    }

    [Test]
    public async Task UnlinkedDefault_UsesCurrentPackThroughInterfaceAndToken_WithoutChangingLinkedFallback()
    {
        var pack = new Pack { Count = 12 };
        var table = new TokenTable<Pack>();
        var slot = new ContextualIntSlot { Offset = 3, Default = -1 };
        IFormulaSlot<int, Pack> typed = slot;
        Assert.That(await typed.Evaluate(pack, table), Is.EqualTo(15));

        var token = GraphTestTokens.Create("entity", slot);
        table.Register(token);
        var proxy = new GraphNode();
        proxy.SetToken(token);
        var caller = new ContextualIntSlot { Default = -2 };
        caller.SetNode(proxy);
        pack.Count = 20;
        Assert.That(await caller.Evaluate(pack, table), Is.EqualTo(23));
        Assert.That(await table.Resolve<int>(typeof(ContextualIntSlot), "entity", pack), Is.EqualTo(23));

        slot.SetNode(new GraphNode(new IntFormula()));
        Assert.That(await typed.Evaluate(pack, table), Is.Zero);
        slot.Node.Disabled = true;
        Assert.That(await typed.Evaluate(pack, table), Is.EqualTo(-1));
        slot.SetNode(new GraphNode());
        Assert.That(await typed.Evaluate(pack, table), Is.EqualTo(-1));
    }

    [Test]
    public async Task UnlinkedDefault_ResolvesAssetParameterAndCallerOverrideWithCurrentPack()
    {
        var asset = UnityEngine.ScriptableObject.CreateInstance<IntFormulaAsset>();
        try
        {
            var parameter = GraphTestTokens.Create("entity", new ContextualIntSlot { Offset = 1 });
            asset.Tokens.Add(parameter);
            var proxy = new GraphNode();
            proxy.SetToken(parameter);
            var body = new ReadContextualInt();
            body.Value.SetNode(proxy);
            asset.SetTarget(body);
            AssetGraphSchema.InvalidateCache();

            var carrier = new GraphNode();
            carrier.SetAsset(asset);
            var binding = new NamedFormulaSlot("entity", new ContextualIntSlot { Offset = 5 });
            carrier.Bindings.Add(binding);
            var caller = new ContextualIntSlot();
            caller.SetNode(carrier);
            var pack = new Pack { Count = 10 };
            var table = new TokenTable<Pack>();
            Assert.That(await caller.Evaluate(pack, table), Is.EqualTo(11));
            binding.OverrideEnabled = true;
            Assert.That(await caller.Evaluate(pack, table), Is.EqualTo(15));
            pack.Count = 20;
            Assert.That(await caller.Evaluate(pack, table), Is.EqualTo(25));
            carrier.Disabled = true;
            Assert.That(await caller.Evaluate(pack, table), Is.Zero);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
            AssetGraphSchema.InvalidateCache();
        }
    }

    [Serializable]
    private sealed class IncrementProperty : ActionBase<Pack>
    {
        public IntSlot Input = new();
        public PropertySlot<int, IntSlot> Target = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            => Target.Write(await Input.Evaluate(pack, tokens) + 1, tokens);
    }

    [Serializable]
    private sealed class ReadProperty : ActionBase<Pack>
    {
        public IntSlot Input = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            => pack.Count += await Input.Evaluate(pack, tokens);
    }

    [Serializable]
    private sealed class IncrementAndReadProperty : ActionBase<Pack>
    {
        public IntSlot Input = new();
        public PropertySlot<int, IntSlot> Target = new();
        protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            int value = await Input.Evaluate(pack, tokens) + 1;
            Target.Write(value, tokens);
            pack.Count += value;
        }
    }

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
    public async Task UnverifiedSavedContentExecutesAfterRuntimeValidation()
    {
        var graph = Create(new Increment(), out _);
        graph.Tokens.Add(GraphTestTokens.Create("Amount", new IntSlot(7)));
        graph.InvalidateValidation();
        var runtime = graph.DeepCopy();
        var pack = new Pack();

        Assert.That(runtime.IsValidated, Is.False);
        Assert.That(runtime.TryValidate(out var error), Is.True, error);
        Assert.That(await runtime.CreateTokenTable().Resolve<int>(typeof(IntSlot), "Amount", pack), Is.EqualTo(7));
        await runtime.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
        Assert.That(graph.IsValidated, Is.False, "Runtime 檢查不可替作者資產寫入已驗證旗標。");
    }

    [Test]
    public async Task RuntimeRejectsInvalidContentDespiteStoredFlagAndRechecksAfterRepair()
    {
        var graph = Create(new Increment(), out var node);
        graph.Tokens.Add(GraphTestTokens.Create("Amount", new IntSlot(7)));
        node.Clear();
        Assert.That(graph.IsValidated, Is.True);
        Assert.That(graph.TryValidate(out var error), Is.False);
        StringAssert.Contains("logicgraph.action.missing", error);
        var pack = new Pack();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await graph.TriggerAction(Timing.Run, pack);
                Assert.Fail("不合法圖必須向呼叫端回報失敗，包括快取失敗的再次執行。");
            }
            catch (InvalidOperationException exception)
            {
                StringAssert.Contains("logicgraph.action.missing", exception.Message);
            }
        }
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
            new System.Text.RegularExpressions.Regex("\\[LogicGraph\\] 圖內容不合法"));
        Assert.That(graph.CreateTokenTable().Has(typeof(IntSlot), "Amount"), Is.False);
        graph.CreateTokenTable();
        Assert.That(pack.Count, Is.Zero);

        node.SetBody(new Increment());
        graph.InvalidateValidation();
        await graph.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
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
        graph.InvalidateValidation();
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

    [Test]
    public async Task PropertiesPersistPerRuntimeGraphUntilExplicitlyInitialized()
    {
        var property = new GraphProperty("Count", new IntSlot(0));
        var graph = PropertyGraph(property, out _);
        var copy = graph.DeepCopy();

        var first = new Pack();
        await graph.TriggerAction(Timing.Run, first);
        await graph.TriggerAction(Timing.Run, first);
        Assert.That(first.Count, Is.EqualTo(3)); // 1 then 2

        var isolated = new Pack();
        await copy.TriggerAction(Timing.Run, isolated);
        Assert.That(isolated.Count, Is.EqualTo(1));

        graph.InitializeProperties();
        var reset = new Pack();
        await graph.TriggerAction(Timing.Run, reset);
        Assert.That(reset.Count, Is.EqualTo(1));
    }

    [Serializable]
    private sealed class CaptureEvaluationScope : ActionBase<Pack>
    {
        public TokenTable<Pack> Captured;
        protected override UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
        {
            Captured = tokens.CreateEvaluationScope();
            return UniTask.CompletedTask;
        }
    }

    [Test]
    public async Task DeferredScopeKeepsLivePropertiesButUsesTheNewCancellationToken()
    {
        var property = new GraphProperty("Count", new IntSlot(0));
        var graph = PropertyGraph(property, out _);
        var captured = new CaptureEvaluationScope();
        graph.ActionGroups[0].Actions.Add(Slot(new GraphNode(captured)));
        var value = GraphTestTokens.Create("Amount", PropertyInput(property));
        graph.Tokens.Add(value);
        using var original = new CancellationTokenSource();
        await graph.TriggerAction(Timing.Run, new Pack(), original.Token, "capture");
        var saved = captured.Captured;
        original.Cancel();
        Assert.That(saved.CreateEvaluationScope().ScopeIdentity, Is.SameAs(saved.ScopeIdentity));
        Assert.That(saved.ScopeIdentity, Is.SameAs(graph.CreateTokenTable().ScopeIdentity));

        Assert.That(await saved.CreateEvaluationScope().Resolve<int>(typeof(IntSlot), "Amount", new Pack()), Is.EqualTo(1));
        await graph.TriggerAction(Timing.Run, new Pack());
        Assert.That(await saved.CreateEvaluationScope().Resolve<int>(typeof(IntSlot), "Amount", new Pack()), Is.EqualTo(2));

        using var current = new CancellationTokenSource();
        var evaluation = saved.CreateEvaluationScope(current.Token);
        current.Cancel();
        Assert.That(evaluation.CancellationToken, Is.EqualTo(current.Token));
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await evaluation.Resolve<int>(typeof(IntSlot), "Amount", new Pack()));
    }

    [Test]
    public async Task DeferredAssetScopeKeepsCallerBindingsAndItsOwnPropertyScope()
    {
        var asset = UnityEngine.ScriptableObject.CreateInstance<TestActionAsset>();
        try
        {
            var captured = new CaptureEvaluationScope();
            asset.SetRoot(new GraphNode(captured));
            asset.Tokens.Add(GraphTestTokens.Create("Amount", new IntSlot(-1)));
            var local = new GraphProperty("Local", new IntSlot(7), true);
            asset.Properties.Add(local);
            AssetGraphSchema.InvalidateCache();
            var callerProperty = new GraphProperty("Caller", new IntSlot(10), true);
            Assert.That(callerProperty.InitialValue, Is.EqualTo(10));
            var graph = PropertyGraph(callerProperty, out _);
            var proxy = new GraphNode();
            var callerToken = GraphTestTokens.Create("FromCaller", PropertyInput(callerProperty));
            graph.Tokens.Add(callerToken);
            proxy.SetToken(callerToken);
            var bindingValue = new IntSlot();
            bindingValue.SetNode(proxy);
            var assetCall = AssetSlot(asset);
            assetCall.Node.Bindings.Add(new NamedFormulaSlot("Amount", bindingValue) { OverrideEnabled = true });
            graph.ActionGroups[0].Actions.Add(assetCall);
            await graph.TriggerAction(Timing.Run, new Pack());
            var saved = captured.Captured;
            Assert.That(saved.ScopeIdentity, Is.Not.SameAs(graph.CreateTokenTable().ScopeIdentity));
            Assert.That(await saved.CreateEvaluationScope().Resolve<int>(typeof(IntSlot), "Amount", new Pack()), Is.EqualTo(11));
            PropertyTarget(local).Write(42, saved);
            await graph.TriggerAction(Timing.Run, new Pack());
            Assert.That(captured.Captured.ScopeIdentity, Is.SameAs(saved.ScopeIdentity));
            Assert.That(await saved.CreateEvaluationScope().Resolve<int>(typeof(IntSlot), "Amount", new Pack()), Is.EqualTo(12));
            Assert.That(await PropertyInput(local).Evaluate(new Pack(), saved.CreateEvaluationScope()), Is.EqualTo(42));
            Assert.That(await PropertyInput(local).Evaluate(new Pack(), graph.CreateTokenTable()), Is.EqualTo(7));
            graph.ActionGroups[0].Actions.Add(AssetSlot(asset));
            await graph.TriggerAction(Timing.Run, new Pack());
            Assert.That(captured.Captured.ScopeIdentity, Is.Not.SameAs(saved.ScopeIdentity), "同一資產的不同呼叫位置必須有獨立身分。");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(asset);
            AssetGraphSchema.InvalidateCache();
        }
    }

    [Test]
    public async Task EditingTheGraphDoesNotResetPropertiesThatOnlyExplicitInitializationClears()
    {
        var property = new GraphProperty("Count", new IntSlot(0));
        var graph = PropertyGraph(property, out _);

        var first = new Pack();
        await graph.TriggerAction(Timing.Run, first);
        Assert.That(first.Count, Is.EqualTo(1));

        graph.InvalidateValidation();
        graph.MarkValidated();

        var second = new Pack();
        await graph.TriggerAction(Timing.Run, second);
        Assert.That(second.Count, Is.EqualTo(2), "InvalidateValidation 是編輯與驗證狀態，不是明確初始化。");
    }

    [Test]
    public async Task PropertiesAddedAfterTheFirstRunGetTheirOwnStorageInsteadOfDroppingWrites()
    {
        var graph = PropertyGraph(new GraphProperty("Count", new IntSlot(0)), out _);
        await graph.TriggerAction(Timing.Run, new Pack());

        // root scope 在第一次執行時就建好了；之後才加進圖的定義仍然要有儲存位置，否則寫入會無聲失敗。
        var added = new GraphProperty("Added", new IntSlot(0), true);
        graph.Properties.Add(added);
        var group = graph.ActionGroups[0];
        group.Actions.Clear();
        group.Actions.Add(Slot(new GraphNode(new IncrementProperty { Input = PropertyInput(added), Target = PropertyTarget(added) })));
        group.Actions.Add(Slot(new GraphNode(new ReadProperty { Input = PropertyInput(added) })));
        graph.InvalidateValidation();
        graph.MarkValidated();

        var pack = new Pack();
        await graph.TriggerAction(Timing.Run, pack);
        Assert.That(pack.Count, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedAssetPropertiesAreScopedByCallingNode(bool addedAfterFirstRun)
    {
        var property = new GraphProperty("Count", new IntSlot(0), true);
        var asset = UnityEngine.ScriptableObject.CreateInstance<TestActionAsset>();
        asset.Properties.Add(property);
        asset.SetRoot(new GraphNode(new IncrementAndReadProperty
        {
            Input = PropertyInput(property),
            Target = PropertyTarget(property),
        }));

        var graph = new LogicGraph<Timing, Pack>();
        graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack>
        {
            Timing = Timing.Run,
            Actions = new() { AssetSlot(asset), AssetSlot(asset) },
        });
        graph.MarkValidated();
        try
        {
            var first = new Pack();
            await graph.TriggerAction(Timing.Run, first);
            Assert.That(first.Count, Is.EqualTo(2));
            if (addedAfterFirstRun)
            {
                var added = new GraphProperty("Added", new IntSlot(1), true);
                asset.Properties.Add(added);
                asset.SetRoot(new GraphNode(new IncrementAndReadProperty
                {
                    Input = PropertyInput(added), Target = PropertyTarget(added),
                }));
            }
            var second = new Pack();
            await graph.TriggerAction(Timing.Run, second);
            Assert.That(second.Count, Is.EqualTo(4));
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
    }

    private static LogicGraph<Timing, Pack> PropertyGraph(GraphProperty property, out GraphNode node)
    {
        node = new GraphNode(new IncrementProperty { Input = PropertyInput(property), Target = PropertyTarget(property) });
        var reader = new GraphNode(new ReadProperty { Input = PropertyInput(property) });
        var graph = new LogicGraph<Timing, Pack>();
        graph.Properties.Add(property);
        graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack>
        {
            Timing = Timing.Run,
            Actions = new() { Slot(node), Slot(reader) },
        });
        graph.MarkValidated();
        return graph;
    }

    private static IntSlot PropertyInput(GraphProperty property)
    {
        var input = new IntSlot();
        var node = new GraphNode();
        node.SetProperty(property);
        input.SetNode(node);
        return input;
    }

    private static PropertySlot<int, IntSlot> PropertyTarget(GraphProperty property)
    {
        var target = new PropertySlot<int, IntSlot>();
        var node = new GraphNode();
        node.SetProperty(property);
        target.SetNode(node);
        return target;
    }

    private static ActionSlot<Pack> Slot(GraphNode node)
    {
        var slot = new ActionSlot<Pack>();
        slot.SetNode(node);
        return slot;
    }

    private static ActionSlot<Pack> AssetSlot(TestActionAsset asset)
    {
        var slot = new ActionSlot<Pack>();
        var node = new GraphNode();
        node.SetAsset(asset);
        slot.SetNode(node);
        return slot;
    }
}
}
