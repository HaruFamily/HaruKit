namespace PinPlugin.ActionSystem
{
using System;
using UnityEngine;

/// <summary>節點的內容種類。一個節點同時只會是其中一種，切換即清掉其他來源。</summary>
public enum NodeKind
{
    /// <summary>編輯中的空節點：已建立載體但還沒選內容。合法的編輯狀態，存檔驗證會擋。</summary>
    Empty = 0,

    /// <summary>內嵌的具體 Action / Formula。</summary>
    Inline = 1,

    /// <summary>共用資產（FormulaAssetBase / ActionAssetBase）。</summary>
    Asset = 2,

    /// <summary>具名共用變數（Token）。</summary>
    Token = 3,
}

/// <summary>
/// 節點圖的唯一載體：一個畫面上的節點＝一個 GraphNode。
/// 換來源＝換載體裡的內容（SetBody / SetAsset / SetToken），Id、座標、備註與所有連入邊全部保留。
/// </summary>
// 非泛型才能讓候選池、複製貼上、座標與編輯器走訪全部走同一條路徑；
// 型別安全收斂在 Slot 的 GetBody<T>() / GetAsset<T>() 一處，不合型別由 Verify() 於編輯期擋下。
[Serializable]
public class GraphNode
{
    [SerializeField, HideInInspector]
    private string _id;

    [SerializeField, HideInInspector]
    private Vector2 _pos;

    // false 代表使用者沒有手動擺過位置，交給自動排版。
    [SerializeField, HideInInspector]
    private bool _hasPos;

    [SerializeField, HideInInspector]
    private string _note;

    [SerializeField]
    private NodeKind _kind = NodeKind.Empty;

    [SerializeReference]
    private ActionSystemNode _body;

    [SerializeField]
    private ScriptableObject _asset;

    [SerializeField]
    private string _tokenKey;

    public GraphNode() { }

    /// <summary>程式建立內嵌節點（測試與程式組圖用）。</summary>
    public GraphNode(ActionSystemNode body)
    {
        SetBody(body);
    }

    /// <summary>節點在圖上的穩定識別碼，座標與選取狀態都靠它。空字串代表尚未指派。</summary>
    public string Id => _id;

    public NodeKind Kind => _kind;

    /// <summary>節點座標。<see cref="HasPos"/> 為 false 時無意義。</summary>
    public Vector2 Pos
    {
        get => _pos;
        set { _pos = value; _hasPos = true; }
    }

    public bool HasPos => _hasPos;

    /// <summary>清掉手動座標，讓自動排版接手。</summary>
    public void ClearPos() { _hasPos = false; _pos = Vector2.zero; }

    /// <summary>節點備註（右鍵新增）。空字串或 null 代表沒有備註。</summary>
    public string Note { get => _note; set => _note = value; }

    /// <summary>Token 模式的變數名稱；其他模式為 null。</summary>
    public string TokenKey => _kind == NodeKind.Token ? _tokenKey : null;

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    /// <summary>複製節點後必須換新識別碼，否則兩個節點會共用同一筆座標與選取狀態。</summary>
    public void ResetId() => _id = null;

    /// <summary>取內嵌內容並檢查型別。型別不符回 null，由呼叫端 Log 後走保底值。</summary>
    public TBody GetBody<TBody>() where TBody : ActionSystemNode => _body as TBody;

    /// <summary>取資產並檢查型別。型別不符回 null，由呼叫端 Log 後走保底值。</summary>
    public TAsset GetAsset<TAsset>() where TAsset : ScriptableObject => _asset as TAsset;

    /// <summary>不分型別取內嵌內容，給編輯器與驗證走訪用。</summary>
    public ActionSystemNode BodyObject => _body;

    /// <summary>不分型別取資產，給編輯器與驗證走訪用。</summary>
    public ScriptableObject AssetObject => _asset;

    /// <summary>換成內嵌 Action / Formula。Id、座標、備註與連入邊不變。</summary>
    public void SetBody(ActionSystemNode body)
    {
        _body = body;
        _asset = null;
        _tokenKey = null;
        _kind = body != null ? NodeKind.Inline : NodeKind.Empty;
    }

    /// <summary>換成共用資產引用。</summary>
    public void SetAsset(ScriptableObject asset)
    {
        _asset = asset;
        _body = null;
        _tokenKey = null;
        _kind = NodeKind.Asset;
    }

    /// <summary>換成 Token 引用。</summary>
    public void SetToken(string key)
    {
        _tokenKey = key;
        _body = null;
        _asset = null;
        _kind = NodeKind.Token;
    }

    /// <summary>清成空節點（編輯中狀態）。</summary>
    public void Clear()
    {
        _body = null;
        _asset = null;
        _tokenKey = null;
        _kind = NodeKind.Empty;
    }
}

}
