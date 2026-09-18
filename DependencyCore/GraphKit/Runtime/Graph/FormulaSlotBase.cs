namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using UnityEngine;

// 非泛型 base：Verify 與編輯器不必帶泛型參數就能取節點、結果型別與型別相容判定。
// Node／SetNode／三個 Accepts 在 GraphSlotBase，與動作、目錄欄位共用。
public abstract class FormulaSlotBase : GraphSlotBase
{
    /// <summary>求值結果型別（TResult）。</summary>
    public abstract Type ResultType { get; }

    /// <summary>公式求值封包型別（TPack）。資產綁定驗證用。</summary>
    public abstract Type PackType { get; }

    /// <summary>
    /// 族身份：具體 Slot 型別本身。同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
    /// 所以「這一格收不收得下那個來源」「Token同不同名」「TokenTable 登記在哪一格」一律看這個，不看結果型別。
    /// </summary>
    // 用 GetType() 而不是另外宣告一個 enum／字串：族本來就是「哪一種 Slot」，多一層宣告就多一處會對不上。
    public Type FamilyType => GetType();

    /// <summary>不分型別存取預設值，供編輯器輸入框讀寫。</summary>
    public abstract object DefaultObject { get; set; }

    /// <summary>
    /// 常數框要畫成哪個型別。預設＝結果型別；子類可回別的型別，讓畫不出輸入框的結果型別
    /// （清單這種）仍有一格可編的「沒接線時取什麼」。只影響常數框，不影響拉線相容性——
    /// chip、候選過濾、Verify 一律看 <see cref="FamilyType"/>。
    /// </summary>
    public virtual Type DefaultEditType => ResultType;

    /// <summary>
    /// 從這一格拉到空白處時要直接建好的內容；回 null（預設）＝長一顆空節點，由使用者選。
    /// </summary>
    // 空節點代表「還沒決定要接什麼」，那對一般欄位是有意義的狀態，所以這不是通則也不是依族推導，
    // 而是個別欄位自己宣告：只有「拉出來必定是某一種」的欄位才覆寫它。
    public virtual GraphNodeContent CreateDefaultBody() => null;

    /// <summary>這個欄位收得下的內嵌公式基底型別（TFormula）。型別選單用它過濾。</summary>
    public abstract Type BodyBaseType { get; }

    /// <summary>
    /// 候選再依公式的 pack 型別收窄；回 null（預設）＝只看 <see cref="BodyBaseType"/>。
    /// </summary>
    // 為「pack 固定、結果型別任意」的欄位而生：那是個述詞，不是一個型別，BodyBaseType 表達不出來
    // （FormulaNodeShape<,> 是開放泛型收不了，FormulaNodeShape<int,T> 又把結果鎖死）。
    // 不併進 BodyBaseType：兩者是「繼承自誰」與「裝的是哪種包」兩個獨立條件，合併會讓其中一邊失去表達力。
    public virtual Type CandidatePackType => null;

    /// <summary>這個欄位收得下的公式資產型別（TAsset）。</summary>
    public abstract Type AssetBaseType { get; }
}

}
