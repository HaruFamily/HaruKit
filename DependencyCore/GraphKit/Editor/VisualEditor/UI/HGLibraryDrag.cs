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

    /// <summary>正在拖的目錄。null＝這次拖的不是目錄。</summary>
    // 存介面不存 id：拖曳只活在這一次互動裡，期間目錄物件不會被換掉；
    // 真正寫進節點的才是 Id（改名不斷），那是落下時的事。
    public IGraphCatalogLibrary Catalog { get; private set; }

    /// <summary>按下之後真的移動過。沒移動過就還是一次點擊，不是拖曳。</summary>
    public bool AssetActive { get; private set; }

    public bool TokenActive { get; private set; }

    public bool CatalogActive { get; private set; }

    /// <summary>有任何一種拖曳進行中。視窗用它決定要不要持續 Repaint。</summary>
    public bool Active => AssetActive || TokenActive || CatalogActive;

    /// <summary>拖著資產、可以落下了。</summary>
    public bool DroppingAsset => AssetActive && Asset != null;

    /// <summary>拖著Token、可以落下了。</summary>
    public bool DroppingToken => TokenActive && Token != null;

    /// <summary>拖著目錄、可以落下了。</summary>
    public bool DroppingCatalog => CatalogActive && Catalog != null;

    private ScriptableObject pendingAssetClick;
    private GraphToken pendingTokenClick;
    private IGraphCatalogLibrary pendingCatalogClick;

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

    public void BeginCatalog(IGraphCatalogLibrary catalog)
    {
        Catalog = catalog;
        pendingCatalogClick = catalog;
    }

    /// <summary>MouseDrag 時呼叫：把「按著某個東西」升級成「真的在拖」。</summary>
    public void PromoteOnDrag()
    {
        if (Event.current.type != EventType.MouseDrag) return;
        if (Asset != null) AssetActive = true;
        if (Token != null) TokenActive = true;
        if (Catalog != null) CatalogActive = true;
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

    public bool IsSource(IGraphCatalogLibrary catalog) => catalog != null && ReferenceEquals(Catalog, catalog);

    public bool IsPendingClick(IGraphCatalogLibrary catalog)
        => !CatalogActive && catalog != null && ReferenceEquals(pendingCatalogClick, catalog);

    public void ClearToken()
    {
        TokenActive = false;
        Token = null;
        pendingTokenClick = null;
    }

    public void ClearCatalog()
    {
        CatalogActive = false;
        Catalog = null;
        pendingCatalogClick = null;
    }

    /// <summary>清掉兩種拖曳的待處理狀態。按下與放開都要清，兩邊都不能只靠一邊。</summary>
    // MouseUp 不保證收得到——在視窗外放開就沒有那個事件，狀態會一直掛著，
    // 之後任何一次拖曳都會被誤判成「還在拖那個東西」。
    public void Clear()
    {
        ClearAsset();
        ClearToken();
        ClearCatalog();
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

    public void DrawCatalogGhost()
    {
        if (!CatalogActive || Catalog == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        // 目錄的內容是一批資產，所以殘影走「目錄色→資產色」，和節點 Header 同一條漸層邏輯。
        HGStyles.GradientFill(r, HGStyles.HeaderCatalog, HGStyles.HeaderAsset, GhostCorner);
        GUI.Label(r, Catalog.Name ?? "（未命名）", HGStyles.Chip);
    }
}

}
