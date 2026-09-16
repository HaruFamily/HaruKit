namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;

public abstract class ActionBase<TPack> : ActionNodeShape<TPack>
{
    public async UniTask Execute(TPack pack, TokenTable<TPack> tokens)
    {
        await OnExecute(pack, tokens);
    }
    protected abstract UniTask OnExecute(TPack pack, TokenTable<TPack> tokens);
}

}
