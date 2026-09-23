namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using System;
using HaruFamily.DependencyCore.GraphKit;

public interface IFormulaSlot<T, TPack>
{
    UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens);
}

/// <summary>保留 Pack 與 Token 上下文的跨結果型別求值入口。</summary>
internal interface IFormula<TPack> : ITypedFormulaNode
{
    UniTask<object> EvaluateObject(TPack pack, TokenTable<TPack> tokens);
}

/// <summary>求值節點的擴充入口。一般新增算式繼承既有公式族；具體節點標記 [Serializable] 與 [HGNode]。</summary>
/// <typeparam name="T">求值結果型別；公式族由具體 Slot 型別區分，不只看結果型別。</typeparam>
/// <typeparam name="TPack">呼叫端提供的執行上下文；沿用收到的 pack 與 tokens 呼叫子公式 Slot。</typeparam>
public abstract class FormulaBase<T, TPack> : FormulaNodeShape<T, TPack>, IFormula<TPack>
{
    Type ITypedFormulaNode.ResultType => typeof(T);
    Type ITypedFormulaNode.PackType => typeof(TPack);
    async UniTask<object> IFormula<TPack>.EvaluateObject(TPack pack, TokenTable<TPack> tokens)
        => await Evaluate(pack, tokens);

    internal UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens) => OnEvaluate(pack, tokens);

    /// <summary>實作求值；子公式走 Slot，非同步工作沿用 tokens.CancellationToken。</summary>
    protected abstract UniTask<T> OnEvaluate(TPack pack, TokenTable<TPack> tokens);
}

}
