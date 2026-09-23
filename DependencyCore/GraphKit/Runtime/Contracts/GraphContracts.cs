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
/// 把節點圖的版面收合狀態存在自己身上的文件或資產。與座標一樣是作者安排的版面、進版控，但不影響執行。
/// 沒實作的文件，編輯器只在記憶體裡記，換對象或重新編譯就消失。
/// </summary>
public interface IGraphViewStateOwner
{
    GraphViewState ViewState { get; }
}

/// <summary>
/// 一份文件的版面收合狀態。key 由編輯器產生（節點 Id＋欄位路徑，或節點 Id），這裡只負責存取，不解讀。
/// 只記手動切換過的項目：沒有記錄就是展開、清單則交給編輯器的自動折疊規則。
/// </summary>
[Serializable]
public sealed class GraphViewState
{
    // 欄位（Slot 或清單標題）收起子樹。只存 true，沒有記錄就是展開。
    [SerializeField] private List<string> _hidden = new();

    // 清單折疊要存兩邊：沒有記錄時編輯器依項數自動折疊，手動展開也得記得住。
    [SerializeField] private List<string> _folded = new();
    [SerializeField] private List<string> _unfolded = new();

    // 有內容的註解框預設展開，這裡記使用者收起來的節點。
    [SerializeField] private List<string> _notesCollapsed = new();

    public IReadOnlyList<string> Hidden => _hidden ??= new List<string>();
    public IReadOnlyList<string> Folded => _folded ??= new List<string>();
    public IReadOnlyList<string> Unfolded => _unfolded ??= new List<string>();
    public IReadOnlyList<string> NotesCollapsed => _notesCollapsed ??= new List<string>();

    /// <summary>回傳這次有沒有真的改到；沒改到時呼叫端不該標未存檔。</summary>
    public bool SetHidden(string key, bool hidden) => Toggle(_hidden ??= new List<string>(), key, hidden);

    public bool SetFolded(string key, bool folded)
    {
        bool changed = Toggle(_folded ??= new List<string>(), key, folded);
        changed |= Toggle(_unfolded ??= new List<string>(), key, !folded);
        return changed;
    }

    public bool SetNoteCollapsed(string nodeId, bool collapsed) => Toggle(_notesCollapsed ??= new List<string>(), nodeId, collapsed);

    private static bool Toggle(List<string> list, string key, bool present)
    {
        if (string.IsNullOrEmpty(key)) return false;
        bool contains = list.Contains(key);
        if (contains == present) return false;
        if (present) list.Add(key);
        else list.Remove(key);
        return true;
    }
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
/// 動作內容的循序執行契約。清單必須提供實際使用的子 Slot，順序與執行順序一致。
/// </summary>
/// <remarks>
/// 子 Slot 仍存於序列化欄位；此介面只描述時序，不執行、不求值，也不取代結構走訪。
/// 容器自身的輸入在子動作之前讀取，輸出在全部子動作完成後才可用。
/// 正常完成時依序執行全部子動作（遵守 Slot／節點停用）；失敗或取消可中止整條執行鏈。
/// 條件分支、平行、零次迴圈及任意交錯讀寫不符合此契約，不可實作。
/// </remarks>
public interface ISequentialActionContainer
{
    /// <summary>依執行順序提供子動作；getter 無副作用，null 視為空清單。</summary>
    IReadOnlyList<ActionSlotBase> SequentialActions { get; }
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

/// <summary>答得出自己結果型別與求值封包型別的節點內容。</summary>
// 型別自述，**不是求值介面**：框架只需要知道「這顆回什麼、吃什麼」就足以做候選過濾、
// 相容判定與型別顯示；怎麼呼叫它留給使用端——同步或非同步、要不要 await，兩個使用端的答案不同，
// 而 GraphKit 不依賴任何非同步函式庫。
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
/// 擁有 ProtoProperty 定義清單的圖主人。定義是圖的內容，跟著工作副本與存檔交易走。
/// </summary>
// 與 ITokenOwner 分開而不是合併成一個「具名清單」介面：Token 是取值來源（有自己的畫布與候選池），
// Property 是可寫的儲存位置（沒有畫布），兩者的 CRUD、驗證與刪除連帶處理都不同。
public interface IPropertyOwner
{
    /// <summary>本圖的 ProtoProperty 定義。LocalProperty 住在 GraphNode，不在這份清單。</summary>
    List<GraphProperty> Properties { get; }
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

    /// <summary>Property：左欄 Property 庫、換來源選單的 Property 分組。定義住圖的工作副本。</summary>
    Properties = 8,
}

/// <summary>Optional observation capability. Copies share a source, but retain their own document revision.</summary>
public interface IGraphExecutionDocument
{
    GraphExecutionSource ExecutionSource { get; }
    string ExecutionRevision { get; }
}

/// <summary>
/// 一張可編輯的圖對編輯器的完整形狀。編輯器靠這個介面在 Owner 身上找到要編的欄位，
/// 不認識任何具體的圖型別，所以同一套編輯器可以編不同領域的圖；具名 Token 是選用的
/// <see cref="ITokenOwner"/> 能力，而非所有文件的必要資料。
/// </summary>
public interface IGraphDocument : IOrphanPool
{
    /// <summary>畫布上的 HEAD 們。元素型別由實作決定（LogicGraph 給的是時機群組）。</summary>
    IList Roots { get; }

    /// <summary>是否已通過驗證。未通過的圖 runtime 不執行。</summary>
    bool IsValidated { get; }

    /// <summary>內容變動，撤銷已驗證狀態。</summary>
    void InvalidateValidation();

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
    /// 新接入的 Tool 應在 <c>IHGRootAdapter</c> 依 owner 過濾；owner 參數僅保留給舊文件實作。
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
