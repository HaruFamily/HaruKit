namespace HaruFamily.Framework.ActionSystem
{
using System;
using UnityEngine;

/// <summary>資產呼叫點的一個具名參數綁定。關閉覆蓋時使用資產內部標註節點的計算結果。</summary>
[Serializable]
public sealed class NamedFormulaSlot
{
    [SerializeField]
    private string _name;

    [SerializeReference]
    private FormulaSlotBase _slot;

    [SerializeField]
    private bool _overrideEnabled;

    public NamedFormulaSlot(string name, FormulaSlotBase slot)
    {
        _name = name;
        _slot = slot;
    }

    public string Name => _name;
    public FormulaSlotBase Slot => _slot;
    public bool OverrideEnabled { get => _overrideEnabled; set => _overrideEnabled = value; }

    public void SetName(string name) => _name = name;
    public void SetSlot(FormulaSlotBase slot) => _slot = slot;
}
}
