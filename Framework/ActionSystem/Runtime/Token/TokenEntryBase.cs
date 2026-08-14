namespace PinPlugin.ActionSystem
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Token（具名共用變數）的宣告，同時是節點圖的頭端（發出點）：自己是一個固定節點，只有一個「來源」接點。
/// 專案端每種 result kind 各繼承一次，只需補上具體型別的 Slot。
/// </summary>
[Serializable]
public abstract class TokenEntryBase : ITokenEntry
{
    [SerializeField]
    private string _key;

    [SerializeField, HideInInspector]
    private string _id;

    [SerializeField, HideInInspector]
    private Vector2 _pos;

    // false 代表使用者沒有手動擺過位置，交給自動排版。
    [SerializeField, HideInInspector]
    private bool _hasPos;

    // 候選節點池：本 Token 專用，不執行、不參與驗證。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    public string Key { get => _key; set => _key = value; }

    /// <summary>取值用的欄位。子類回傳自己的具體 Slot。</summary>
    public abstract FormulaSlotBase Slot { get; }

    /// <summary>頭端節點的穩定識別碼，焦點與座標都靠它；改名不影響。</summary>
    public string Id => _id;

    public string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    public void ResetId() => _id = null;

    public Vector2 Pos
    {
        get => _pos;
        set { _pos = value; _hasPos = true; }
    }

    public bool HasPos => _hasPos;

    public void ClearPos() { _hasPos = false; _pos = Vector2.zero; }

    /// <summary>本 Token 的候選節點清單。僅視覺化編輯器使用。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }
}

}
