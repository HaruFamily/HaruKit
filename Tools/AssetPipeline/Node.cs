using System;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>公式的非泛型求值入口；結果與 Pack 身分由型別契約提供。</summary>
    internal interface IFormula : ITypedFormulaNode
    {
        object EvaluateObject(object pack);
    }

    /// <summary>統一的同步公式。無外部資料時由 AP 輸入槽提供 NullPack。</summary>
    [Serializable]
    public abstract class FormulaBase<TResult, TPack> : FormulaNodeShape<TResult, TPack>, IFormula
    {
        public Type ResultType => typeof(TResult);
        public Type PackType => typeof(TPack);
        object IFormula.EvaluateObject(object pack) => Evaluate((TPack)pack);
        internal TResult Evaluate(TPack pack) => OnEvaluate(pack);
        protected abstract TResult OnEvaluate(TPack pack);
    }

    /// <summary>管線動作節點：使用本步 Context 執行，資產修改經 context.Assets 納入交易。</summary>
    [Serializable]
    public abstract class ActionBase : ActionNodeShape
    {
        internal void Execute(PipelineActionContext context) => OnExecute(context);

        protected abstract void OnExecute(PipelineActionContext context);
    }
}
