namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;

public abstract class ActionBase<TPack> : ActionNodeShape<TPack>
{
    internal UniTask Execute(TPack pack, TokenTable<TPack> tokens) => OnExecute(pack, tokens);

    /// <summary>實作副作用；子動作走 Slot，非同步工作沿用 tokens.CancellationToken。</summary>
    protected abstract UniTask OnExecute(TPack pack, TokenTable<TPack> tokens);
}

}
