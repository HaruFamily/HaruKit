using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

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

    /// <summary>管線步驟節點：有副作用、沒有結果型別。</summary>
    [Serializable]
    public abstract class APActionBase : ActionNodeBase<APPack>
    {
        public abstract void Execute();
    }

    /// <summary>
    /// 會產出 dynamic key 的步驟。
    /// </summary>
    // dynamic key 是「這個步驟跑完才存在」的東西，不是具名變數：變數是公式來源，步驟沒有回傳值寫不進端點。
    // 所以時序（產出者必須排在讀取者之前）由 APGraph.Verify 依步驟順序檢查，GraphKit 不認識這個概念。
    public interface IDynamicKeyProducer
    {
        bool TryGetDynamicOutputKey(out string key);
    }

    /// <summary>
    /// 會讀 dynamic key 的公式。
    /// </summary>
    // 驗證器靠這個介面問「你讀了哪些 key」，不去認識任何具體公式型別——
    // 具體公式住在使用端專案，框架看不到它們。
    public interface IDynamicKeyReader
    {
        IEnumerable<string> DynamicInputKeys { get; }
    }

    /// <summary>
    /// 會讀 prototype key 的公式。驗證器據此檢查 key 有沒有對應的資產群組。
    /// </summary>
    public interface IPrototypeKeyReader
    {
        IEnumerable<string> PrototypeInputKeys { get; }
    }
}
