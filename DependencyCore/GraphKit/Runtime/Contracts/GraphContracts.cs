namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 畫布上的頭端：自己是一顆固定節點，所以要記得座標。
/// ActionSlot、GraphToken、ActionTimingGroup 與兩種資產基底都是頭端。
/// </summary>
// 這五個型別原本各自宣告一模一樣的 Pos／HasPos／ClearPos，編輯器只能靠成員名反射取值。
// 抽成介面後編輯器改走型別，重新命名成員時編譯器會抓到，反射抓不到。
public interface IGraphHead
{
    /// <summary>頭端座標。<see cref="HasPos"/> 為 false 時無意義。</summary>
    Vector2 Pos { get; set; }

    /// <summary>false 代表使用者沒有手動擺過位置，交給自動排版。</summary>
    bool HasPos { get; }

    /// <summary>清掉手動座標，讓自動排版接手。</summary>
    void ClearPos();
}

/// <summary>
/// 內容是被寫進來的節點：值由執行期的某個擁有者交給它，不是自己算出來的。
/// </summary>
// 純標記，只給編輯器換 Header 身分色用——一顆「等別人寫進來」的節點跟一般公式長一樣的話，
// 圖上就分不出資料往哪個方向流。求值仍走它自己的公式介面，框架不介入內容怎麼來。
public interface IGraphSink
{
}

/// <summary>
/// 目錄：內容住在節點自己身上的容器。它<b>不是公式</b>——沒有結果型別，也求不出值。
/// </summary>
// 純標記。泛型層只需要知道「這顆節點不參與求值」，內容是什麼、怎麼裝滿由使用端決定。
// 值要從它底下的子節點取（見 IGraphNodeOwner），指得到它的欄位是 CatalogSlotBase，收不收得下由該欄位逐顆判定。
//
// 與 IGraphCatalogLibrary 不同：這個是畫布上的一顆節點，那個是左欄手動蒐集的一份資產分組。
public interface IGraphCatalog
{
}

/// <summary>
/// 內容底下自己帶一串子節點的節點。子節點是完整的 <see cref="GraphNode"/>——
/// 有自己的 Id 與座標，所以任何欄位都指得到它，畫布上也各自是一顆節點。
/// </summary>
// 「一列可以被別人指」在資料上就等於「那一列是一顆節點」：連線只存節點參照，沒有列位址。
// 編輯器靠這個介面走訪與增刪，不認識任何具體的擁有者型別。
public interface IGraphNodeOwner
{
    /// <summary>底下的子節點。順序即顯示順序。</summary>
    List<GraphNode> ChildNodes { get; }

    /// <summary>新增一個子節點並回傳它。內容由實作決定，通常是空節點交給使用者選。</summary>
    GraphNode CreateChild();

    /// <summary>移除一個子節點。不存在就什麼都不做。</summary>
    void RemoveChild(GraphNode child);
}

/// <summary>子節點由容器本體繪製成一列，而不是各自成為畫布節點的容器。</summary>
public interface IGraphInlineNodeOwner : IGraphNodeOwner
{
}

/// <summary>內嵌列的資料來源：左側輸出由載體提供，右側輸入走 <see cref="InputSlot"/>。</summary>
public interface IGraphInlineNode
{
    FormulaSlotBase InputSlot { get; }
    Type ResultType { get; }
}

/// <summary>答得出自己結果型別與求值封包型別的節點內容。</summary>
// 型別自述，**不是求值介面**：框架只需要知道「這顆回什麼、吃什麼」就足以做候選過濾、
// 相容判定與型別顯示；怎麼呼叫它留給使用端——同步或非同步、要不要 await，兩個使用端的答案不同，
// 而 GraphKit 不依賴任何非同步函式庫。同一個理由讓 IGraphInlineNode 也只宣告型別、不宣告求值。
public interface ITypedFormulaNode
{
    /// <summary>求值結果型別。</summary>
    Type ResultType { get; }

    /// <summary>求值時要餵進來的封包型別。</summary>
    Type PackType { get; }
}

/// <summary>
/// 擁有候選節點池的畫布主人。候選節點只供編輯，不執行、不參與驗證。
/// </summary>
// 不併進 IGraphHead：ActionTimingGroup 是頭端但沒有候選池，合併會逼它長出一個假的空清單。
public interface IOrphanPool
{
    /// <summary>本畫布的候選節點清單。</summary>
    List<GraphNode> Orphans { get; }
}

/// <summary>
/// 擁有具名Token清單的圖主人。對資產而言這份清單同時就是它對呼叫端的參數介面。
/// </summary>
public interface ITokenOwner
{
    /// <summary>本圖的具名Token。從端點開始的整棵子樹都是正式資料。</summary>
    List<GraphToken> Tokens { get; }
}

/// <summary>
/// 一張圖可選擇啟用的能力。未列入的能力，編輯器把對應的區塊、選單項與右鍵整組收掉。
/// </summary>
// 用旗標而不是一個能力一個 bool：新增可選區只加一個列舉值，介面不會每次長出一個新成員。
[Flags]
public enum HGCapabilities
{
    None = 0,

    /// <summary>共用資產：左欄資產庫與引用區、右鍵「轉存為資產」、換來源選單的資產分組。</summary>
    SharedAssets = 1,

    /// <summary>具名 Token：左欄 Token 庫、右鍵「轉存為 Token」、換來源選單的 Token 分組。</summary>
    Tokens = 2,

    /// <summary>資產目錄：左欄目錄庫。內容住在 Owner，不在圖的工作副本裡。</summary>
    Catalogs = 4,
}

/// <summary>
/// 一份具名的資產目錄：手動蒐集的一批專案資產。
/// </summary>
// Id 與 Name 分開：節點引用的是 Id，顯示的是 Name。改名不該讓引用失聯——
// 「節點存名字去找目標」那個設計已經淘汰過一次，見 GraphNode 的 NodeKind 註解。
public interface IGraphCatalogLibrary
{
    /// <summary>穩定識別碼。建立後不再變動，改名不影響它。</summary>
    string Id { get; }

    /// <summary>顯示名稱。可就地改名。</summary>
    string Name { get; }

    /// <summary>這個庫裝的是哪一種東西。節點靠它判斷自己接不接得上。</summary>
    // 由庫自己宣告而不是從 Items 推：空的庫也要答得出來，否則第一筆加進去之前無法判定相容性。
    Type ItemType { get; }

    /// <summary>目錄內容。順序即加入順序。</summary>
    // 非泛型：泛型參數會逼 ICatalogOwner 跟著泛型化，編輯器就只能反射掃泛型介面實例去找「全部的庫」，
    // 那正是框架明令禁止的作法。型別由 ItemType 自述，內容怎麼畫由 Editor 側的繪製契約決定。
    IReadOnlyList<object> Items { get; }
}

/// <summary>會從目錄庫取內容的節點：宣告自己接得上哪一種庫。</summary>
// 多個庫並存時，「這顆節點的下拉該列哪些目錄」只有節點自己答得出來。
// 不比庫的具體型別而比 ItemType：同一種內容可以有多個庫，節點要的是內容種類，不是某一個庫。
public interface ICatalogLibraryConsumer
{
    /// <summary>接得上的庫的 <see cref="IGraphCatalogLibrary.ItemType"/>；null＝不從庫取內容。</summary>
    Type CatalogItemType { get; }
}

/// <summary>
/// 擁有資產目錄的編輯對象。**實作在 Owner 上，不在 <see cref="IGraphDocument"/> 上**。
/// </summary>
// 目錄的內容是「專案資產的分組」，不是圖的內容：它不進編輯器的工作副本，改了就直接寫 Owner，
// 和共用資產庫同一個模式。掛在圖的契約上會讓它跟著存檔交易走，語意反而不對。
// 復原是另一件事：編輯器靠 CaptureCatalogs／RestoreCatalogs 把每次修改記進與圖同一個 Undo 堆疊。
public interface ICatalogOwner
{
    /// <summary>全部目錄。</summary>
    IReadOnlyList<IGraphCatalogLibrary> Catalogs { get; }

    /// <summary>建一個新目錄並回傳它。名稱由實作自動產生，之後再改名。</summary>
    IGraphCatalogLibrary CreateCatalog();

    /// <summary>改名。失敗時回 false 並給出原因（例如重名）。</summary>
    bool RenameCatalog(string id, string name, out string error);

    /// <summary>刪掉整個目錄。</summary>
    void DeleteCatalog(string id);

    /// <summary>加入項目。重複項與型別不符的由實作跳過，回傳實際加入幾個。</summary>
    // 收 object 不收具體型別：驗型是庫自己的事（它才知道自己的 ItemType），
    // 編輯器只負責把使用者給的東西交過來。
    int AddToCatalog(string id, IReadOnlyList<object> items);

    /// <summary>移除單一項目。</summary>
    void RemoveFromCatalog(string id, object item);

    /// <summary>
    /// 把一份目錄做成畫布上的節點內容（包）。回 null＝這個領域不支援把目錄拉進畫布。
    /// </summary>
    // 由 Owner 建而不是編輯器建：包的具體型別住在使用端，泛型層只負責把它塞進 GraphNode.SetCatalog。
    GraphNodeContent CreateCatalogNode(IGraphCatalogLibrary catalog);

    /// <summary>抄一份目前的全部目錄，交給編輯器的復原歷程保管。</summary>
    // 回傳 object：目錄的實體型別由實作決定，編輯器只負責保管與交還，不讀裡面的內容。
    // 抄的時候清單容器必須是新的——就地增刪的容器共用出去，快照會跟著被改掉。
    object CaptureCatalogs();

    /// <summary>用 <see cref="CaptureCatalogs"/> 的快照覆寫全部目錄。認不得的快照直接忽略。</summary>
    void RestoreCatalogs(object snapshot);
}

/// <summary>
/// 一張可編輯的圖對編輯器的完整形狀。編輯器靠這個介面在 Owner 身上找到要編的欄位，
/// 不認識任何具體的圖型別，所以同一套編輯器可以編不同領域的圖。
/// </summary>
public interface IGraphDocument : IOrphanPool, ITokenOwner
{
    /// <summary>畫布上的 HEAD 們。元素型別由實作決定（LogicGraph 給的是時機群組）。</summary>
    IList Roots { get; }

    /// <summary>是否已通過驗證。未通過的圖 runtime 不執行。</summary>
    bool IsValidated { get; }

    /// <summary>內容變動，撤銷已驗證狀態。</summary>
    void MarkDirty();

    /// <summary>依 Core 規則驗證整張圖並更新 <see cref="IsValidated"/>。錯誤由實作記進 Console。</summary>
    // 契約刻意是無參數的：呼叫端（存檔）不需要知道實作額外收什麼選項。
    void Verify();

    /// <summary>深層複製整張圖（含 SerializeReference 多型樹）。編輯器的工作副本靠它產生。</summary>
    object DeepCopy();

    /// <summary>求值封包型別。編輯器用它過濾「屬於這張圖的公式族」。</summary>
    Type PackType { get; }

    /// <summary>root 底下那串項目的元素型別。空 root 也要建得出新項目，所以不能從現有內容推。</summary>
    Type ItemSlotType { get; }

    /// <summary>
    /// 這張畫布可以有哪些 root，以識別值表示（LogicGraph 給的是時機 enum 值）。
    /// owner 可能進一步縮小範圍，傳 null 代表不過濾。
    /// </summary>
    // 識別值型別由實作決定，編輯器只做相等比較與 ToString()，不假設它是 enum。
    IReadOnlyList<object> RootKeys(UnityEngine.Object owner);

    /// <summary>這個 root 的識別值。</summary>
    object KeyOf(object root);

    /// <summary>這個 root 在節點 Header 上的顯示名。</summary>
    string TitleOf(object root);

    /// <summary>root 節點的身分標籤。root 不回傳值，標籤是它與一般節點的唯一區別；空字串代表不顯示。</summary>
    // 由圖自己提供而不是寫死在編輯器裡：領域不同，root 的身分也不同（時機／管線／…）。
    string RootChip { get; }

    /// <summary>
    /// 句子裡稱呼 root 的名詞，例如「時機」「管線」。編輯器用它組選單、提示與 log。
    /// </summary>
    // 只給名詞，不要帶「節點」「群組」：那兩個字由編輯器自己按句子接上，接法各處不同。
    string RootNoun { get; }

    /// <summary>
    /// 本圖啟用哪些可選能力。沒宣告的能力，對應的區塊、選單項與右鍵整組不出現。
    /// </summary>
    // 由圖宣告而不是讓編輯器去推：推得出來的只有「現在一筆都沒有」，
    // 推不出「這個領域永遠不會有」——那兩件事在畫面上長得一樣，但一個該顯示空清單，一個該整區收掉。
    HGCapabilities Capabilities { get; }

    /// <summary>
    /// 節點圖編輯器的視窗標題。領域自己命名，編輯器不寫死。
    /// </summary>
    // 同一個 EditorWindow 服務所有領域，標題是使用者辨認「現在編的是哪一種圖」的唯一線索。
    string WindowTitle { get; }

    /// <summary>這個 root 底下的項目清單。</summary>
    IList ItemsOf(object root);

    /// <summary>依識別值建立 root 並加入 <see cref="Roots"/>；已存在則回傳既有的，建不出來回 null。</summary>
    object AddRoot(object key);
}

}
