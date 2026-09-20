namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;

public interface IFormulaSlot<T, TPack>
{
    UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens);
}

public abstract class FormulaBase<T, TPack> : FormulaNodeShape<T, TPack>
{
    internal UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens) => OnEvaluate(pack, tokens);

    /// <summary>實作求值；子公式走 Slot，非同步工作沿用 tokens.CancellationToken。</summary>
    protected abstract UniTask<T> OnEvaluate(TPack pack, TokenTable<TPack> tokens);
}

}
