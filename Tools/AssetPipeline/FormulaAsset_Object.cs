using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Object : FormulaBase<Object>
    {
    }

    /// <summary>
    /// Object 公式的型別化中間層：<see cref="EvaluateTyped"/> 回具體 T，繼承來的 Evaluate 只做 T→Object 上轉。
    /// </summary>
    // 讓 Formula_<具體型別> 同時 is-a Formula_Object（可放進 Object 欄位）又能接上自己那一族的欄位。
    // 靠介面而不是第二個基底：類別只能繼承一個，IFormula<T> 可以再宣告一個結果型別。
    [Serializable]
    public abstract class Formula_Object<T> : Formula_Object, IFormula<T> where T : Object
    {
        public abstract T EvaluateTyped();

        T IFormula<T>.Evaluate()
        {
            return EvaluateTyped();
        }

        public sealed override Object Evaluate()
        {
            return EvaluateTyped();
        }
    }

    [Serializable]
    public abstract class Formula_ObjectList : FormulaBase<List<Object>>
    {
    }

    /// <summary>
    /// Object 清單公式的型別化中間層：<see cref="EvaluateTyped"/> 回 List&lt;T&gt;，繼承來的 Evaluate 逐項上轉。
    /// </summary>
    [Serializable]
    public abstract class Formula_ObjectList<T> : Formula_ObjectList, IFormula<List<T>> where T : Object
    {
        public abstract List<T> EvaluateTyped();

        List<T> IFormula<List<T>>.Evaluate()
        {
            return EvaluateTyped();
        }

        public sealed override List<Object> Evaluate()
        {
            var result = new List<Object>();
            List<T> typed = EvaluateTyped();
            if (typed == null) return result;

            foreach (T item in typed)
                result.Add(item);

            return result;
        }
    }

    [Serializable]
    public class FormulaAsset_Object : FormulaSlot<Object, Formula_Object>
    {
        public FormulaAsset_Object()
        {
        }

        public FormulaAsset_Object(Object defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public class FormulaAsset_ObjectList : FormulaSlot<List<Object>, Formula_ObjectList>
    {
        public FormulaAsset_ObjectList()
        {
            _default = new List<Object>();
        }

        public FormulaAsset_ObjectList(List<Object> defaultValue) : base(defaultValue)
        {
        }
    }
}
