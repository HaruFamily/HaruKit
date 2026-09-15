using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 求值封包型別。AssetPipeline 的節點全部同步求值、不帶執行上下文，
    /// 這個型別只用來滿足 GraphKit 的 pack 型別參數，永遠不會被實例化。
    /// </summary>
    // GraphKit 用 pack 型別把「屬於哪張圖」寫進型別層，編輯器靠它過濾候選公式族。
    // 不給 pack 的話，LogicGraph 的公式會出現在 AssetPipeline 的選單裡。
    public sealed class APPack
    {
        private APPack() { }
    }

    /// <summary>
    /// 同步求值能力。求值宣告在介面而不是基底類別上，
    /// 中間層（例如 Formula_Object&lt;T&gt;）才能同時是「Object 公式」與「T 公式」——
    /// 類別只能繼承一個基底，介面可以宣告多個結果型別。
    /// </summary>
    // 刻意不加 out：協變會讓 IAPFormula<AudioClip> 與繼承來的 IAPFormula<Object> 在轉型時歧義。
    public interface IAPFormula<T>
    {
        T Evaluate();
    }

    /// <summary>管線公式節點：同步求一個值，沒有副作用。</summary>
    // 繼承 FormulaNodeBase 是為了讓編輯器在型別層答得出「這顆是公式、結果型別、屬於哪張圖」，
    // 不必建實例。零欄位，不影響序列化。
    [Serializable]
    public abstract class APFormulaBase<T> : FormulaNodeBase<T, APPack>, IAPFormula<T>
    {
        /// <summary>求值。失敗不丟例外，回 default 並由呼叫端走保底值。</summary>
        public abstract T Evaluate();
    }

    /// <summary>以目錄完整資料為輸入的公式。結果型別由外層 CatalogCell 轉交給下游欄位。</summary>
    [Serializable]
    public abstract class CatalogFormulaBase<TResult> : FormulaNodeBase<TResult, List<Object>>, ICatalogFormula
    {
        public Type ResultType => typeof(TResult);
        public object EvaluateObject(List<Object> catalog) => Evaluate(catalog);
        public abstract TResult Evaluate(List<Object> catalog);
    }

    /// <summary>管線步驟節點：有副作用、沒有結果型別。</summary>
    [Serializable]
    public abstract class APActionBase : ActionNodeBase<APPack>
    {
        public abstract void Execute();
    }

    /// <summary>
    /// 會讀 prototype key 的公式。驗證器據此檢查 key 有沒有對應的資產群組。
    /// </summary>
    public interface IPrototypeKeyReader
    {
        IEnumerable<string> PrototypeInputKeys { get; }
    }
}
