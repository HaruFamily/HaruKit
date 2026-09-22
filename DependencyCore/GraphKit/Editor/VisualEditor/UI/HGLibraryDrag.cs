namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>
/// 從清單面板拖到畫布的跨區拖曳：拖的是哪一個、有沒有真的拖起來、放開時算點擊還是落下。
/// </summary>
// 框架級服務，不屬於任何一個面板。同一個面板可以落在左框或右框，落點又永遠在畫布，
// 所以「我是來源」這件事不能由面板自己記——記了就綁死在某一個框裡。
// 面板只負責宣告「這一格開始拖了」，判斷與殘影都在這裡。
public sealed class HGLibraryDrag
{
    private const float GhostCorner = 3f;

    /// <summary>正在拖的資產。null＝這次拖的不是資產。</summary>
    public ScriptableObject Asset { get; private set; }

    /// <summary>正在拖的Token。null＝這次拖的不是Token。</summary>
    public GraphToken Token { get; private set; }

    /// <summary>正在拖的 Property 定義。null＝這次拖的不是 Property。</summary>
    // 存定義物件不存 id：落下時節點存的也是物件參照（`GraphNode.SetProperty`），改名不斷線。
    public GraphProperty Property { get; private set; }

    /// <summary>按下之後真的移動過。沒移動過就還是一次點擊，不是拖曳。</summary>
    public bool AssetActive { get; private set; }

    public bool TokenActive { get; private set; }

    public bool PropertyActive { get; private set; }

    /// <summary>有任何一種拖曳進行中。視窗用它決定要不要持續 Repaint。</summary>
    public bool Active => AssetActive || TokenActive || PropertyActive;

    /// <summary>拖著資產、可以落下了。</summary>
    public bool DroppingAsset => AssetActive && Asset != null;

    /// <summary>拖著Token、可以落下了。</summary>
    public bool DroppingToken => TokenActive && Token != null;

    /// <summary>拖著 Property、可以落下了。</summary>
    public bool DroppingProperty => PropertyActive && Property != null;

    private ScriptableObject pendingAssetClick;
    private GraphToken pendingTokenClick;
    private GraphProperty pendingPropertyClick;

    public void BeginAsset(ScriptableObject asset)
    {
        Asset = asset;
        pendingAssetClick = asset;
    }

    public void BeginToken(GraphToken endpoint)
    {
        Token = endpoint;
        pendingTokenClick = endpoint;
    }

    public void BeginProperty(GraphProperty property)
    {
        Property = property;
        pendingPropertyClick = property;
    }

    /// <summary>MouseDrag 時呼叫：把「按著某個東西」升級成「真的在拖」。</summary>
    public void PromoteOnDrag()
    {
        if (Event.current.type != EventType.MouseDrag) return;
        if (Asset != null) AssetActive = true;
        if (Token != null) TokenActive = true;
        if (Property != null) PropertyActive = true;
    }

    /// <summary>這一格是不是這次拖曳的來源。</summary>
    public bool IsSource(ScriptableObject asset) => Asset != null && Asset == asset;

    public bool IsSource(GraphToken endpoint) => endpoint != null && ReferenceEquals(Token, endpoint);

    /// <summary>放開時該算成一次點擊（按下時是這一格，而且中途沒有拖動過）。</summary>
    public bool IsPendingClick(ScriptableObject asset) => !AssetActive && asset != null && pendingAssetClick == asset;

    public bool IsPendingClick(GraphToken endpoint)
        => !TokenActive && endpoint != null && ReferenceEquals(pendingTokenClick, endpoint);

    public void ClearAsset()
    {
        AssetActive = false;
        Asset = null;
        pendingAssetClick = null;
    }

    public void ClearToken()
    {
        TokenActive = false;
        Token = null;
        pendingTokenClick = null;
    }

    public bool IsSource(GraphProperty property) => property != null && ReferenceEquals(Property, property);

    public bool IsPendingClick(GraphProperty property)
        => !PropertyActive && property != null && ReferenceEquals(pendingPropertyClick, property);

    public void ClearProperty()
    {
        PropertyActive = false;
        Property = null;
        pendingPropertyClick = null;
    }

    /// <summary>清掉全部拖曳的待處理狀態。按下與放開都要清，兩邊都不能只靠一邊。</summary>
    // MouseUp 不保證收得到——在視窗外放開就沒有那個事件，狀態會一直掛著，
    // 之後任何一次拖曳都會被誤判成「還在拖那個東西」。
    public void Clear()
    {
        ClearAsset();
        ClearToken();
        ClearProperty();
    }

    public void DrawAssetGhost()
    {
        if (!AssetActive || Asset == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        // 沒有結果型別＝動作資產，和節點那邊同一條判定。
        bool isActionAsset = HGReflect.AssetResultType(Asset) == null;
        HGStyles.GradientFill(r, HGStyles.HeaderAsset,
            isActionAsset ? HGStyles.HeaderAction : HGStyles.HeaderFormula, GhostCorner);
        GUI.Label(r, Asset.name, HGStyles.Chip);
    }

    public void DrawTokenGhost()
    {
        if (!TokenActive || Token == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        HGStyles.GradientFill(r, HGStyles.HeaderToken, HGStyles.HeaderFormula, GhostCorner);
        GUI.Label(r, Token.Name ?? "（未命名）", HGStyles.Chip);
    }

    public void DrawPropertyGhost()
    {
        if (!PropertyActive || Property == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        // Property 色→公式色：落下後那顆節點提供的是一個型別化的值，和公式節點接在同一種欄位上。
        HGStyles.GradientFill(r, HGStyles.HeaderProperty, HGStyles.HeaderFormula, GhostCorner);
        GUI.Label(r, Property.Name ?? "（未命名）", HGStyles.Chip);
    }

}

}
