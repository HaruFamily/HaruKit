namespace PinPlugin.ActionSystem
{
using System;
using System.Collections.Generic;
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

    // 3 = 舊的 Token 引用節點，2026-08-17 隨標註化移除。Token 不再是一種內容，
    // 而是任何節點都能掛的一個名字（見 GraphNode.TokenName）。編號不重用。
}

/// <summary>
/// 節點圖的唯一載體：一個畫面上的節點＝一個 GraphNode。
/// 換來源＝換載體裡的內容（SetBody / SetAsset），Id、座標、備註、標註與所有連入邊全部保留。
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

    // 停用用「反向旗標」：既有資產沒有這個欄位，反序列化後 false = 啟用，不會整批被關掉。
    [SerializeField]
    private bool _disabled;

    [SerializeField]
    private NodeKind _kind = NodeKind.Empty;

    [SerializeReference]
    private ActionSystemNode _body;

    [SerializeField]
    private ScriptableObject _asset;

    // 標註（Token）：這顆節點的值可以被外面指名讀取，也可以被外面指名覆蓋。
    // 掛在載體而不是內容上——換型別時名字要留著，和 _id / _pos / _note 同一層。
    [SerializeField]
    private string _tokenName;

    // 資產呼叫點的參數綁定。它屬於這次引用，不屬於共用資產。
    [SerializeField]
    private List<NamedFormulaSlot> _bindings = new();

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

    /// <summary>
    /// 停用中的節點不求值，所有引用它的欄位一律取自己的保底值（Formula 取 _default、Action 直接跳過）。
    /// 載體是共用單位，所以停用一顆被多個欄位指著的節點會同時影響全部引用處。
    /// </summary>
    public bool Disabled { get => _disabled; set => _disabled = value; }

    /// <summary>
    /// 標註名稱。空＝沒標註。有名字代表這顆節點是這張圖的對外端點：
    /// Owner 的圖 → 可被 Inspector 用字串查；資產的圖 → 是這個資產的參數，呼叫端可以覆蓋。
    /// </summary>
    public string TokenName => string.IsNullOrEmpty(_tokenName) ? null : _tokenName;

    public bool IsToken => !string.IsNullOrEmpty(_tokenName);

    public List<NamedFormulaSlot> Bindings
    {
        get { _bindings ??= new List<NamedFormulaSlot>(); return _bindings; }
    }

    /// <summary>標註或取消標註（傳 null / 空字串即取消）。換內容不影響標註。</summary>
    public void SetTokenName(string name) => _tokenName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

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

    /// <summary>換成內嵌 Action / Formula。Id、座標、備註、標註與連入邊不變。</summary>
    public void SetBody(ActionSystemNode body)
    {
        _body = body;
        _asset = null;
        Bindings.Clear();
        _kind = body != null ? NodeKind.Inline : NodeKind.Empty;
    }

    /// <summary>換成共用資產引用。</summary>
    public void SetAsset(ScriptableObject asset)
    {
        _asset = asset;
        _body = null;
        _kind = NodeKind.Asset;
    }

    /// <summary>清成空節點（編輯中狀態）。標註不清——名字是載體的身分，不是內容的。</summary>
    public void Clear()
    {
        _body = null;
        _asset = null;
        Bindings.Clear();
        _kind = NodeKind.Empty;
    }
}

}
