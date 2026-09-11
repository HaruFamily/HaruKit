namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;

public abstract class ActionBase<TPack> : LogicGraphNode
{
    public async UniTask Execute(TPack pack, TokenTable<TPack> tokens)
    {
        await OnExecute(pack, tokens);
    }
    protected abstract UniTask OnExecute(TPack pack, TokenTable<TPack> tokens);
}

}
