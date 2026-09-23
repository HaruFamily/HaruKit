namespace HaruFamily.Framework.LogicGraph
{
using System;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>可直接宣告在 Action 的型別化 Property 寫入欄位，不需要另建具體子類。</summary>
/// <remarks>以一般序列化欄位持有封閉泛型；TResult 應與 TSlot 的結果型別一致。</remarks>
[Serializable]
public class PropertySlot<TResult, TSlot> : SetPropertySlot<TResult, TSlot>
    where TSlot : FormulaSlotBase
{
}

/// <summary>LogicGraph Action 的型別化 Property 寫入端；值存 runtime scope，不寫回圖定義。</summary>
[Serializable]
public abstract class SetPropertySlot<TResult, TSlot> : PropertySlotBase where TSlot : FormulaSlotBase
{
    [UnityEngine.SerializeReference]
    private GraphNode node;

    public override GraphNode Node => node;
    public override void SetNode(GraphNode value) => node = value;
    public override Type FamilyType => typeof(TSlot);

    public bool Write<TPack>(TResult value, TokenTable<TPack> tokens)
    {
        GraphProperty property = node?.Kind == NodeKind.Property && !node.Disabled ? node.Property : null;
        return AcceptsProperty(property) && tokens?.Properties?.Write(property, value) == true;
    }
}
}
