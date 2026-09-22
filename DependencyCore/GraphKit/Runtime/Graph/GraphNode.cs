namespace HaruFamily.DependencyCore.GraphKit
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

    /// <summary>本圖的具名Token（<see cref="GraphToken"/>）。節點只是引用，內容住在端點自己的畫布。</summary>
    Token = 4,

    /// <summary>
    /// 圖內的具名變數（<see cref="GraphProperty"/>）。節點只是引用，定義住圖層清單。
    /// </summary>
    // 與 Token 同構：定義不住節點，所以多個讀寫引用共用同一份儲存位置，改名不斷線。
    // 它讀得出目前值，所以一般求值欄位指得到它；相容判定走 AcceptsProperty。
    Property = 6,
}

/// <summary>Property 節點接收端的穩定身分。寫入連線由 Action 的 PropertySlot 保存。</summary>
[Serializable]
public sealed class GraphPropertyInputSlot : GraphSlotBase
{
    [SerializeReference]
    private GraphNode node;

    public override GraphNode Node => node;
    public override void SetNode(GraphNode value) => node = value;
}

/// <summary>
/// 節點圖的唯一載體：一個畫面上的節點＝一個 GraphNode。
    /// 換來源＝換載體裡的內容（SetBody / SetAsset / SetToken / SetProperty），Id、座標、備註與所有連入邊全部保留。
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
    private GraphNodeContent _body;

    [SerializeField]
    private ScriptableObject _asset;

    // 具名Token引用：直接指向頭端物件，不存名字字串。改名不斷、刪除當場變空、型別編輯期就擋得住。
    [SerializeReference]
    private GraphToken _endpoint;

    // LocalProperty 的私有定義。舊資料也使用這個欄位；_protoProperty 為 null 時一律視為 LocalProperty。
    [SerializeReference]
    private GraphProperty _property;

    // ProtoProperty 的圖層定義。Proto 模式可尚未選 Key，因此要和模式旗標分開保存。
    [SerializeReference]
    private GraphProperty _protoProperty;

    [SerializeField]
    private bool _isProtoProperty;

    // Property 的下方 Input 必須有自己的持久綁定，不能借用指向這顆節點的 ParentSlot。
    [SerializeReference]
    private GraphPropertyInputSlot _propertyInput = new();

    // 資產呼叫點的參數綁定。它屬於這次引用，不屬於共用資產。
    [SerializeField]
    private List<NamedFormulaSlot> _bindings = new();

    public GraphNode() { }

    /// <summary>程式建立內嵌節點（測試與程式組圖用）。</summary>
    public GraphNode(GraphNodeContent body)
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

    /// <summary>Token 模式指向的具名Token頭端；其他模式為 null。</summary>
    public GraphToken Token => _kind == NodeKind.Token ? _endpoint : null;

    /// <summary>Property 模式指向的定義；其他模式為 null。</summary>
    public GraphProperty Property => _kind != NodeKind.Property ? null : _isProtoProperty ? _protoProperty : _property;

    /// <summary>Property 節點目前是否為 ProtoProperty 模式。Proto 模式可以尚未選擇 Key。</summary>
    public bool IsProtoProperty => _kind == NodeKind.Property && _isProtoProperty;

    /// <summary>Property 節點下方輸入的持久綁定；Proto 尚未選 Key 時也存在。</summary>
    public GraphPropertyInputSlot PropertyInput => _propertyInput ??= new GraphPropertyInputSlot();

    /// <summary>保留來源資料的相容入口；不寫入共享定義，也不參與 Property 求值。</summary>
    public void SetPropertyInput(GraphNode source)
    {
        PropertyInput.SetNode(source);
    }

    public List<NamedFormulaSlot> Bindings
    {
        get { _bindings ??= new List<NamedFormulaSlot>(); return _bindings; }
    }

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    /// <summary>複製節點後必須換新識別碼，否則兩個節點會共用同一筆座標與選取狀態。</summary>
    public void ResetId() => _id = null;

    /// <summary>取內嵌內容並檢查型別。型別不符回 null，由呼叫端 Log 後走保底值。</summary>
    public TBody GetBody<TBody>() where TBody : GraphNodeContent => _body as TBody;

    /// <summary>取資產並檢查型別。型別不符回 null，由呼叫端 Log 後走保底值。</summary>
    public TAsset GetAsset<TAsset>() where TAsset : ScriptableObject => _asset as TAsset;

    /// <summary>不分型別取內嵌內容，給編輯器與驗證走訪用。</summary>
    public GraphNodeContent BodyObject => _body;

    /// <summary>不分型別取資產，給編輯器與驗證走訪用。</summary>
    public ScriptableObject AssetObject => _asset;

    /// <summary>換成內嵌 Action / Formula。Id、座標、備註與連入邊不變。</summary>
    public void SetBody(GraphNodeContent body)
    {
        ClearPropertyInput();
        _body = body;
        _asset = null;
        _endpoint = null;
        _property = null;
        _protoProperty = null;
        _isProtoProperty = false;
        Bindings.Clear();
        _kind = body != null ? NodeKind.Inline : NodeKind.Empty;
    }

    /// <summary>換成具名Token引用。</summary>
    // 端點為 null 時退成空節點而不是「沒有Token的Token節點」：那種狀態畫得出來、存得下去，
    // 卻永遠求不出值，只會變成畫布上一顆看不懂的節點。沒有Token＝還沒選內容。
    public void SetToken(GraphToken endpoint)
    {
        if (endpoint == null) { Clear(); return; }

        ClearPropertyInput();
        _endpoint = endpoint;
        _body = null;
        _asset = null;
        _property = null;
        _protoProperty = null;
        _isProtoProperty = false;
        Bindings.Clear();
        _kind = NodeKind.Token;
    }

    /// <summary>換成共用資產引用。<b>不動 Bindings</b>——換資產時要保留哪些綁定由呼叫端決定。</summary>
    // 編輯器換資產是「先 ReconcileAssetBindings 留下同名同型的綁定，再 SetAsset」，
    // 所以這裡清掉 Bindings 反而會把剛保留的東西洗掉。SetBody / Clear 是換成另一種內容，才清。
    public void SetAsset(ScriptableObject asset)
    {
        ClearPropertyInput();
        _asset = asset;
        _body = null;
        _endpoint = null;
        _property = null;
        _protoProperty = null;
        _isProtoProperty = false;
        _kind = NodeKind.Asset;
    }

    /// <summary>換成 Property 引用。定義住圖層清單，這裡只存引用。</summary>
    // 定義為 null 時退成空節點，理由與 SetToken 相同：畫得出來也存得下去，卻永遠沒有內容。
    public void SetProperty(GraphProperty property)
    {
        if (property == null) { Clear(); return; }

        if (property.Proto) SetProtoProperty(property);
        else SetLocalProperty(property);
    }

    /// <summary>換成節點私有的 LocalProperty。</summary>
    public void SetLocalProperty(GraphProperty property)
    {
        if (property == null) { Clear(); return; }
        _property = property;
        _protoProperty = null;
        _isProtoProperty = false;
        _body = null;
        _asset = null;
        _endpoint = null;
        Bindings.Clear();
        _kind = NodeKind.Property;
    }

    /// <summary>換成 ProtoProperty 模式。null 代表尚未選擇 Key，仍保留節點與連線。</summary>
    public void SetProtoProperty(GraphProperty property)
    {
        _protoProperty = property;
        _property = null;
        _isProtoProperty = true;
        _body = null;
        _asset = null;
        _endpoint = null;
        Bindings.Clear();
        _kind = NodeKind.Property;
    }

    /// <summary>清成空節點（編輯中狀態）。</summary>
    public void Clear()
    {
        ClearPropertyInput();
        _body = null;
        _asset = null;
        _endpoint = null;
        _property = null;
        _protoProperty = null;
        _isProtoProperty = false;
        Bindings.Clear();
        _kind = NodeKind.Empty;
    }

    private void ClearPropertyInput()
    {
        PropertyInput.SetNode(null);
    }
}

}
