namespace HaruFamily.DependencyCore.GraphKit
{

/// <summary>
/// 目錄欄位的非泛型基底：指著一顆目錄節點（<see cref="CatalogNodeShape{T}"/>），方向可以是寫進去或讀出來。
/// </summary>
// 與 FormulaSlotBase 分家的理由是包不求值。當它還寄生在公式欄位上時，
// ResultType 回 void、BodyBaseType／AssetBaseType 回 null、DefaultObject 的 setter 是空的、
// 三個 Accepts 全回 false——七個成員沒有一個說得出真話，而 FormulaSlotBase 還得為它
// 多開 WritesToCatalog／AcceptsPack／AcceptsCatalogObject／CreateDefaultCatalog 四個只有它會用的 virtual。
//
// 「這一格收不收包」不再需要宣告層的旗標：是不是 CatalogSlotBase 本身就回答了那個問題。
// 刻意不放序列化欄位，同另外兩個 slot 基底。
public abstract class CatalogSlotBase : GraphSlotBase
{
    /// <summary>方向：true＝擁有者往裡面寫，false＝從裡面讀。</summary>
    public virtual bool WritesToCatalog => true;

    /// <summary>
    /// 這一格收不收得下<b>這一顆</b>包。
    /// </summary>
    // 必須接受 null：載體還沒裝好內容時也會被問一次。
    // 逐顆判定而不是只看型別——同一種包可能因為自己的設定而暫時收不下（例：改成外部供應來源）。
    public abstract bool AcceptsCatalogObject(GraphNodeContent pack);

    /// <summary>從這一格拉到空白處時要直接建好的包；回 null＝要使用者自己選。</summary>
    public virtual GraphNodeContent CreateDefaultCatalog() => null;
}

}
