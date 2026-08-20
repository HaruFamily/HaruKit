namespace HaruFamily.Framework.ActionSystem
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 具名變數的頭端（發出點）：自己是一個固定節點，只有一個「來源」接點，並擁有獨立畫布與候選池。
///
/// 沒接來源＝具名常數（值就是 Slot 的預設值）；接了來源＝具名公式。兩種來源同一個載體，
/// 所以「取一個常數的名字」不必再多發明一種節點。
/// </summary>
// 不再 per result kind 各繼承一次：Slot 用 [SerializeReference] 存既有的 IntSlot / FloatSlot…，
// 型別資訊由 Slot.ResultType / PackType 提供，Core 不必知道專案有哪幾種 kind。
[Serializable]
public class GraphEndpoint
{
    [SerializeField]
    private string _name;

    [SerializeField, HideInInspector]
    private string _id;

    [SerializeField, HideInInspector]
    private Vector2 _pos;

    // false 代表使用者沒有手動擺過位置，交給自動排版。
    [SerializeField, HideInInspector]
    private bool _hasPos;

    [SerializeReference]
    private FormulaSlotBase _slot;

    // 候選節點池：本端點畫布專用，不執行、不參與驗證。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    public GraphEndpoint() { }

    public GraphEndpoint(string name, FormulaSlotBase slot)
    {
        _name = name;
        _slot = slot;
    }

    /// <summary>變數名稱。唯一性是「結果型別＋名稱」，所以同名不同型可以並存。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? null : _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>取值用的欄位。沒接節點時它自己就是常數。</summary>
    public FormulaSlotBase Slot => _slot;

    /// <summary>換結果型別＝換整個 Slot。Id、座標與候選池不動。</summary>
    public void SetSlot(FormulaSlotBase slot) => _slot = slot;

    /// <summary>求值結果型別。Slot 未指定時為 null。</summary>
    public Type ResultType => _slot?.ResultType;

    /// <summary>求值封包型別。Slot 未指定時為 null。</summary>
    public Type PackType => _slot?.PackType;

    /// <summary>頭端節點的穩定識別碼，焦點與座標都靠它；改名不影響。</summary>
    public string Id => _id;

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    /// <summary>複製端點後必須換新識別碼，否則兩個頭端共用同一筆座標與焦點。</summary>
    public void ResetId() => _id = null;

    public Vector2 Pos
    {
        get => _pos;
        set { _pos = value; _hasPos = true; }
    }

    public bool HasPos => _hasPos;

    public void ClearPos() { _hasPos = false; _pos = Vector2.zero; }

    /// <summary>本端點畫布的候選節點清單。僅視覺化編輯器使用。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }
}

}
