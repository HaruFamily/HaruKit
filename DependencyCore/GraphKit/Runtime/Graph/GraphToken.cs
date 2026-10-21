namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 具名Token的頭端（發出點）：自己是一個固定節點，只有一個「來源」接點，並擁有獨立畫布與候選池。
///
/// 沒接來源＝具名常數（值就是 Slot 的預設值）；接了來源＝具名公式。兩種來源同一個載體，
/// 所以「取一個常數的名字」不必再多發明一種節點。
/// </summary>
// 通用工具可直接使用；具體 Token 子類宣告輸入型別與對外族，Core 不認識專案型別。
[Serializable]
public class GraphToken : IGraphHead, IOrphanPool
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

    public GraphToken() { }

    public GraphToken(string name, FormulaSlotBase slot)
    {
        _name = name;
        SetSlot(slot);
    }

    /// <summary>Token名稱。唯一性是「族＋名稱」（族＝Slot 型別），所以同名不同族可以並存。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? null : _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>取值用的欄位。沒接節點時它自己就是常數。</summary>
    public FormulaSlotBase Slot => _slot;

    /// <summary>替換輸入欄位；具體 Token 檢查其宣告型別。Id、座標與候選池不動。</summary>
    public virtual void SetSlot(FormulaSlotBase slot) => _slot = slot;

    /// <summary>對外公式族的宣告；與內部接收來源的 Slot 分開。</summary>
    public virtual FormulaSlotBase OutputSlot => _slot;

    /// <summary>序列化資料是否符合此 Token 的型別宣告；不修改資料。</summary>
    public virtual string ConfigurationError => null;

    /// <summary>目前公式族已宣告另一個 Token 型別時，此端點不再符合宣告。</summary>
    public virtual bool IsObsolete => Slot is UnionSlotBase || (Slot?.TokenType is Type declared && GetType() != declared);

    /// <summary>求值結果型別。Slot 未指定時為 null。</summary>
    public virtual Type ResultType => OutputSlot?.ResultType;

    /// <summary>族身份（＝Slot 型別）。同名唯一性與拉線相容都看它，不看結果型別。</summary>
    public virtual Type FamilyType => OutputSlot?.FamilyType;

    /// <summary>求值封包型別。Slot 未指定時為 null。</summary>
    public virtual Type PackType => OutputSlot?.PackType;

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
