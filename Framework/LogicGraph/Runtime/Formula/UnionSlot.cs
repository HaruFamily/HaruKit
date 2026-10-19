using System;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using UnityEngine;

namespace HaruFamily.Framework.LogicGraph
{
    /// <summary>多族輸入的固定結果求值入口；子類透過選中 Slot 的 Evaluate 求值及轉換。</summary>
    [Serializable]
    public abstract class UnionSlot<TResult, TPack> : UnionSlotBase, IFormulaSlot<TResult, TPack>
    {
        [SerializeField] private TResult _default;
        public TResult Default { get => _default; set => _default = value; }
        public sealed override object DefaultObject
        {
            get => _default;
            set
            {
                if (value is TResult typed) { _default = typed; return; }
                if (value == null && !typeof(TResult).IsValueType) { _default = default; return; }
                Debug.LogWarning($"[LogicGraph] Union 預設值型別不符（欄位 {typeof(TResult).Name}，傳入 {value?.GetType().Name ?? "null"}），忽略。");
            }
        }

        public sealed override Type ResultType => typeof(TResult);
        public sealed override Type PackType => typeof(TPack);
        protected sealed override bool IsBranchAllowed(FormulaSlotBase slot)
            => slot.PackType == typeof(TPack) && slot is IFormulaSlot<TPack>;

        protected static FormulaSlotBase NewBranch<TSlot>() where TSlot : FormulaSlotBase, new()
        {
            if (typeof(UnionSlotBase).IsAssignableFrom(typeof(TSlot)))
                throw new InvalidOperationException("A Union branch cannot itself be a Union.");
            return new TSlot();
        }

        public async UniTask<TResult> Evaluate(TPack pack, TokenTable<TPack> tokens)
        {
            tokens?.CancellationToken.ThrowIfCancellationRequested();
            if (Node == null || Node.Kind == NodeKind.Empty || Node.Disabled) return _default;
            string error = ConfigurationError;
            if (error != null) throw new InvalidOperationException(error);
            // 執行觀察、停用、Token／Property 與資產 scope 均由內層 Slot 負責，外層不重複 EnterNode。
            TResult result = await OnEvaluate(pack, tokens);
            tokens?.CancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        protected abstract UniTask<TResult> OnEvaluate(TPack pack, TokenTable<TPack> tokens);
    }

    [Serializable]
    public abstract class UnionSlot<TResult, TPack, TA, TB> : UnionSlot<TResult, TPack>
        where TA : FormulaSlotBase, new()
        where TB : FormulaSlotBase, new()
    {
        protected UnionSlot() => SetSelectedSlot(NewBranch<TA>());
        public override int BranchCount => 2;
        protected override FormulaSlotBase CreateBranch(int index) => index switch
        {
            0 => NewBranch<TA>(), 1 => NewBranch<TB>(), _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    [Serializable]
    public abstract class UnionSlot<TResult, TPack, TA, TB, TC> : UnionSlot<TResult, TPack, TA, TB>
        where TA : FormulaSlotBase, new()
        where TB : FormulaSlotBase, new()
        where TC : FormulaSlotBase, new()
    {
        public override int BranchCount => 3;
        protected override FormulaSlotBase CreateBranch(int index) => index == 2 ? NewBranch<TC>() : base.CreateBranch(index);
    }

    [Serializable]
    public abstract class UnionSlot<TResult, TPack, TA, TB, TC, TD> : UnionSlot<TResult, TPack, TA, TB, TC>
        where TA : FormulaSlotBase, new()
        where TB : FormulaSlotBase, new()
        where TC : FormulaSlotBase, new()
        where TD : FormulaSlotBase, new()
    {
        public override int BranchCount => 4;
        protected override FormulaSlotBase CreateBranch(int index) => index == 3 ? NewBranch<TD>() : base.CreateBranch(index);
    }

    [Serializable]
    public abstract class UnionSlot<TResult, TPack, TA, TB, TC, TD, TE> : UnionSlot<TResult, TPack, TA, TB, TC, TD>
        where TA : FormulaSlotBase, new()
        where TB : FormulaSlotBase, new()
        where TC : FormulaSlotBase, new()
        where TD : FormulaSlotBase, new()
        where TE : FormulaSlotBase, new()
    {
        public override int BranchCount => 5;
        protected override FormulaSlotBase CreateBranch(int index) => index == 4 ? NewBranch<TE>() : base.CreateBranch(index);
    }
}
