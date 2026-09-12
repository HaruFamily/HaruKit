namespace HaruFamily.Framework.LogicGraph
{
using System;
using UnityEngine;

// 非泛型 base：Verify 與編輯器不必帶泛型參數就能取節點、結果型別與型別相容判定。
public abstract class FormulaSlotBase
{
    /// <summary>目前接的節點。null＝常數模式，直接用預設值。</summary>
    public abstract GraphNode Node { get; }

    /// <summary>接上或斷開節點。斷開傳 null 即回常數模式。</summary>
    public abstract void SetNode(GraphNode node);

    /// <summary>求值結果型別（TResult）。</summary>
    public abstract Type ResultType { get; }

    /// <summary>公式求值封包型別（TPack）。資產綁定驗證用。</summary>
    public abstract Type PackType { get; }

    /// <summary>
    /// 族身份：具體 Slot 型別本身。同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
    /// 所以「這一格收不收得下那個來源」「變數同不同名」「TokenTable 登記在哪一格」一律看這個，不看結果型別。
    /// </summary>
    // 用 GetType() 而不是另外宣告一個 enum／字串：族本來就是「哪一種 Slot」，多一層宣告就多一處會對不上。
    public Type Kind => GetType();

    /// <summary>不分型別存取預設值，供編輯器輸入框讀寫。</summary>
    public abstract object DefaultObject { get; set; }

    /// <summary>
    /// 常數框要畫成哪個型別。預設＝結果型別；子類可回別的型別，讓畫不出輸入框的結果型別
    /// （清單這種）仍有一格可編的「沒接線時取什麼」。只影響常數框，不影響拉線相容性——
    /// chip、候選過濾、Verify 一律看 <see cref="Kind"/>。
    /// </summary>
    public virtual Type DefaultEditType => ResultType;

    /// <summary>這個欄位收得下的內嵌公式基底型別（TFormula）。型別選單用它過濾。</summary>
    public abstract Type BodyBaseType { get; }

    /// <summary>這個欄位收得下的公式資產型別（TAsset）。</summary>
    public abstract Type AssetBaseType { get; }

    /// <summary>這個欄位能不能接這個內嵌內容。</summary>
    public abstract bool AcceptsBody(LogicGraphNode body);

    /// <summary>這個欄位能不能接這個資產。</summary>
    public abstract bool AcceptsAsset(ScriptableObject asset);

    /// <summary>這個欄位能不能接這個具名變數。必須是同一族（<see cref="Kind"/>）。</summary>
    public abstract bool AcceptsEndpoint(GraphEndpoint endpoint);
}

}
