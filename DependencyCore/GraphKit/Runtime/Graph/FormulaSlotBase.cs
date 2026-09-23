namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
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
    /// Token／Property 的相容與具名登記看這個；Inline 公式可由接收端另行開放跨族結果相容。
    /// </summary>
    // 用 GetType() 而不是另外宣告一個 enum／字串：族本來就是「哪一種 Slot」，多一層宣告就多一處會對不上。
    public Type FamilyType => GetType();

    /// <summary>不分型別存取預設值，供編輯器輸入框讀寫。</summary>
    public abstract object DefaultObject { get; set; }

    /// <summary>
    /// 常數框要畫成哪個型別。預設＝結果型別；子類可回別的型別，讓畫不出輸入框的結果型別
    /// （清單這種）仍有一格可編的「沒接線時取什麼」。只影響常數框，不影響拉線相容性——
    /// chip 保留族身分，候選與 Verify 走 Slot 的接收契約。
    /// </summary>
    public virtual Type DefaultEditType => ResultType;

    /// <summary>
    /// 從這一格拉到空白處時要直接建好的內容；回 null（預設）＝長一顆空節點，由使用者選。
    /// </summary>
    // 空節點代表「還沒決定要接什麼」，那對一般欄位是有意義的狀態，所以這不是通則也不是依族推導，
    // 而是個別欄位自己宣告：只有「拉出來必定是某一種」的欄位才覆寫它。
    public virtual GraphNodeContent CreateDefaultBody() => null;

    /// <summary>這個欄位原本的內嵌公式族基底（TFormula），不因跨族接收而改變。</summary>
    public abstract Type BodyBaseType { get; }

    /// <summary>允許跨公式族接收相同或可安全指派的結果型別；不影響 Token、Property 或資產。</summary>
    protected virtual bool AllowCompatibleResult => false;

    /// <summary>跨族接收的黑名單，填入封閉公式基底型別；連同其子類排除。同族不受影響。</summary>
    protected virtual IReadOnlyCollection<Type> ExcludedFormulaFamilies => null;

    /// <summary>公式候選搜尋範圍；最終仍須以 AcceptsBody 檢查實例。</summary>
    public virtual Type CandidateBodyBaseType => BodyBaseType;

    /// <summary>跨族型別規則。使用端須先確認來源具有自己的求值契約。</summary>
    protected bool AcceptsCompatibleBody(GraphNodeContent body)
    {
        if (!AllowCompatibleResult || body is not ITypedFormulaNode formula) return false;
        if (formula.PackType != PackType || formula.ResultType == null
            || !ResultType.IsAssignableFrom(formula.ResultType)) return false;

        var excluded = ExcludedFormulaFamilies;
        if (excluded != null)
            foreach (var family in excluded)
                if (family != null && family.IsAssignableFrom(body.GetType())) return false;
        return true;
    }

    /// <summary>
    /// 候選再依公式的 pack 型別收窄；回 null（預設）＝不加額外的 pack 篩選。
    /// </summary>
    // 為「pack 固定、結果型別任意」的欄位而生：那是個述詞，不是一個型別，BodyBaseType 表達不出來
    // （FormulaNodeShape<,> 是開放泛型收不了，FormulaNodeShape<int,T> 又把結果鎖死）。
    // 不併進 BodyBaseType：兩者是「繼承自誰」與「裝的是哪種包」兩個獨立條件，合併會讓其中一邊失去表達力。
    public virtual Type CandidatePackType => null;

    /// <summary>這個欄位收得下的公式資產型別（TAsset）。</summary>
    public abstract Type AssetBaseType { get; }
}

}
