namespace PinPlugin.ActionSystem
{
using System.Collections.Generic;

public abstract class TokenFormulaSlot<TResult, TAsset, TFormula, TEntry, TPack>
    : FormulaSlot<TResult, TAsset, TFormula, TPack>
    where TAsset : FormulaAsset<TResult, TPack>
    where TFormula : FormulaBase<TResult, TPack>
    where TEntry : class, ITokenEntry, new()
{
    protected TokenFormulaSlot(bool active) : base(active) { }
    protected TokenFormulaSlot(bool active, TResult result) : base(active) { _default = result; }

#if UNITY_EDITOR
    protected override bool TryAddTokenEntry(IActionSystemOwner owner, string key, UseType mode, TFormula formula, TAsset asset, TResult constant)
    {
        var list = (owner as ITokenEntryOwner<TPack>)?.GetTokenPack()?.FindList<TEntry>();
        if (list == null) return false;
        var entry = new TEntry { Key = key };
        var slot = (FormulaSlot<TResult, TAsset, TFormula, TPack>)entry.Slot;
        return EditorPopulateAndAdd(list, entry, slot, mode, formula, asset, constant);
    }
#endif
}

}
