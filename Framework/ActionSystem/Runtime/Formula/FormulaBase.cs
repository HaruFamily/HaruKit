namespace HaruFamily.Framework.ActionSystem
{
using Cysharp.Threading.Tasks;

public interface IFormulaSlot<T, TPack>
{
    UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens);
}

public abstract class FormulaBase<T, TPack> : ActionSystemNode
{
    public virtual async UniTask<T> Evaluate(TPack pack, TokenTable<TPack> tokens)
    {
        return await OnEvaluate(pack, tokens);
    }
    protected abstract UniTask<T> OnEvaluate(TPack pack, TokenTable<TPack> tokens);
}

}
