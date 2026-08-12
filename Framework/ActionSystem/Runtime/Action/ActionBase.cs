namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;

public abstract class ActionBase<TPack> : ActionSystemNode
{
    public async UniTask Execute(TPack pack, TokenCache<TPack> tokens)
    {
        await OnExecute(pack, tokens);
    }
    protected abstract UniTask OnExecute(TPack pack, TokenCache<TPack> tokens);
}

}
