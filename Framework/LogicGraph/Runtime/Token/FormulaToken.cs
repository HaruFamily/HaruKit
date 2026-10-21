using System;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Framework.LogicGraph
{
    [Serializable]
    public abstract class FormulaTokenBase<TResult, TPack> : GraphToken
    {
        public sealed override Type ResultType => typeof(TResult);
        public sealed override Type PackType => typeof(TPack);
    }

    /// <summary>具名公式的輸入與結果；對外族由 FormulaSlot 的 Token 配對宣告。</summary>
    [Serializable]
    public abstract class FormulaToken<TResult, TInput, TPack> : FormulaTokenBase<TResult, TPack>
        where TInput : FormulaSlotBase, IFormulaSlot<TResult, TPack>, new()
    {
        [NonSerialized] private FormulaSlotBase output;

        protected FormulaToken() => SetSlot(new TInput());

        public TInput Input => Slot as TInput;
        public sealed override bool IsObsolete => Slot is not TInput || !FormulaFamily.DeclaresToken(GetType(), typeof(TPack));
        public sealed override FormulaSlotBase OutputSlot => output ??= FormulaFamily.CreateOutput(GetType(), typeof(TPack));
        public sealed override Type FamilyType => OutputSlot.FamilyType;
        public sealed override string ConfigurationError
        {
            get
            {
                if (Slot is not TInput) return $"Token 輸入必須為 {typeof(TInput).Name}。";
                try { _ = OutputSlot; return null; }
                catch (InvalidOperationException error) { return error.Message; }
            }
        }

        public sealed override void SetSlot(FormulaSlotBase slot)
        {
            if (slot is not TInput)
                throw new ArgumentException($"Token input must be {typeof(TInput).Name}.", nameof(slot));
            base.SetSlot(slot);
        }
    }
}
