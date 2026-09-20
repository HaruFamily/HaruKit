namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>副作用節點的擴充入口。具體節點標記 [Serializable] 與 [HGNode]，覆寫 OnExecute。</summary>
/// <typeparam name="TPack">呼叫端提供的執行上下文，例如本次操作的目標與狀態；圖內具名求值由 TokenTable 負責。</typeparam>
public abstract class ActionBase<TPack> : ActionNodeShape<TPack>
{
    internal UniTask Execute(TPack pack, TokenTable<TPack> tokens) => OnExecute(pack, tokens);

    /// <summary>實作副作用；子動作走 Slot，非同步工作沿用 tokens.CancellationToken。</summary>
    protected abstract UniTask OnExecute(TPack pack, TokenTable<TPack> tokens);
}

}
