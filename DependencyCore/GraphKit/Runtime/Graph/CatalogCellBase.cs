namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using UnityEngine;

/// <summary>
/// 目錄格子右側的輸入欄位：接一顆吃 <typeparamref name="T"/> 的公式，未接時整包原樣往下傳。
/// </summary>
// 結果型別由接上的公式決定，這是它與一般公式欄位最大的不同——一般欄位的結果型別是固定的，
// 這一格的「族」是「吃這一種包的全部公式」，範圍靠 CandidatePackType 收窄。
// 求值不在這一層：怎麼呼叫公式由使用端決定（同步或非同步），Core 只答型別關係。
[Serializable]
public class CatalogFormulaSlot<T> : FormulaSlotBase
{
    [SerializeReference]
    private GraphNode node;

    public override GraphNode Node => node;

    public override void SetNode(GraphNode value) => node = value;

    /// <summary>沒接篩選公式時就是整包的型別。</summary>
    public override Type ResultType => ActiveFilter?.ResultType ?? typeof(T);

    public override Type PackType => typeof(T);

    /// <summary>收「答得出型別的公式」，再由 <see cref="CandidatePackType"/> 收窄到吃這一種包的。</summary>
    // 兩個條件分開：介面決定「問得出型別嗎」，pack 型別決定「吃的是不是這一包」。
    // 沒有目錄專屬的公式基底——任何 pack 為 T 的公式都接得上。
    public override Type BodyBaseType => typeof(ITypedFormulaNode);

    public override Type CandidatePackType => typeof(T);

    public override Type AssetBaseType => null;

    /// <summary>沒有常數模式：沒接公式時的保底值是整包資料，不是使用者填的東西。</summary>
    public override object DefaultObject { get => null; set { } }

    public override bool AcceptsBody(GraphNodeContent body)
        => body is ITypedFormulaNode formula && formula.PackType == typeof(T);

    /// <summary>目前生效的篩選公式，沒有就是 null。</summary>
    // 空槽與停用走同一條路：都當作沒有篩選。ResultType 與求值必須看同一個判定，
    // 否則停用一顆公式會讓格子對外宣稱的型別與實際回傳值對不上，下游只能在求值當下退保底值。
    // pack 型別不符的一併當作沒有篩選：Verify 會先擋下來，這裡是執行期的最後一道，
    // 讓它退回整包而不是在轉型時炸開。
    protected ITypedFormulaNode ActiveFilter
    {
        get
        {
            if (node == null || node.Disabled) return null;
            var formula = node.BodyObject as ITypedFormulaNode;
            return formula != null && formula.PackType == typeof(T) ? formula : null;
        }
    }
}

/// <summary>
/// 目錄底下的一格：左側由自己的載體輸出結果，右側接一顆篩選公式。
/// </summary>
// 它是容器內的一列，不是內嵌的公式節點——每一格仍有自己的 GraphNode 載體，
// 其他欄位照樣指得到它，改變的只有畫法（見 IGraphInlineNodeOwner）。
//
// InputSlot 是 abstract 而不是這一層的序列化欄位：欄位的實體型別會寫進 [SerializeReference] 記錄，
// 由使用端決定才不會讓 Core 的泛型參數變成資料格式的一部分。
// Owner 不序列化——存成回頭指向母目錄的欄位會讓資料成環，由目錄在載入與深複製後統一指派。
[Serializable]
public abstract class CatalogCellBase<T> : GraphNodeContent, IGraphInlineNode
{
    [NonSerialized]
    private CatalogBase<T> owner;

    /// <summary>母目錄。載入或深複製之後由目錄呼叫 <see cref="SetOwner"/> 指回來。</summary>
    public CatalogBase<T> Owner => owner;

    /// <summary>右側的輸入欄位。序列化欄位住在使用端的子類。</summary>
    public abstract FormulaSlotBase InputSlot { get; }

    /// <summary>這一格對下游宣稱的型別＝右側欄位算出來的型別。</summary>
    public Type ResultType => InputSlot.ResultType;

    public void SetOwner(CatalogBase<T> value) => owner = value;
}

}
