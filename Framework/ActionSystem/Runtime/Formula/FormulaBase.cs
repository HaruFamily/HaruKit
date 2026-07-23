namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;

public interface IFormulaSlot<T, TPack>
{
    UniTask<T> Evaluate(TPack pack, TokenCache<TPack> tokens);
}

public abstract class FormulaBase<T, TPack>
{
    public virtual async UniTask<T> Evaluate(TPack pack, TokenCache<TPack> tokens)
    {
        return await OnEvaluate(pack, tokens);
    }
    protected abstract UniTask<T> OnEvaluate(TPack pack, TokenCache<TPack> tokens);
}

}
