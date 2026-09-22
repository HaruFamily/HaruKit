namespace HaruFamily.Framework.LogicGraph
{
using System;
using HaruFamily.DependencyCore.GraphKit;

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
