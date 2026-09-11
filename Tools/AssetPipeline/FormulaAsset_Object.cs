using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Object : IFormula<Object>
    {
        public abstract Object Caculate();
    }

    /// <summary>
    /// Object 公式的型別化中間層：CaculateTyped 回傳具體 T，繼承來的 Caculate 只做 T→Object 上轉（編譯期安全，無 is/as 下轉）。
    /// 讓 Formula_&lt;具體型別&gt; 直接 is-a Formula_Object，可被 Formula_Object 欄位的 picker 直接選取。
    /// </summary>
    [Serializable]
    public abstract class Formula_Object<T> : Formula_Object, IFormula<T> where T : Object
    {
        public abstract T CaculateTyped();

        T IFormula<T>.Caculate()
        {
            return CaculateTyped();
        }

        public sealed override Object Caculate()
        {
            return CaculateTyped();
        }
    }

    [Serializable]
    public class Formula_Object_AssetPipeline : Formula_Object
    {
        public AssetPipelineSource source = new AssetPipelineSource();

        public override Object Caculate()
        {
            if (source == null) return null;

            List<Object> assets = source.GetAssets<Object>();
            if (assets == null || assets.Count == 0) return null;

            return assets[0];
        }
    }

    [Serializable]
    public abstract class Formula_ObjectList : IFormula<List<Object>>
    {
        public abstract List<Object> Caculate();
    }

    /// <summary>
    /// Object 清單公式的型別化中間層：CaculateTyped 回傳 List&lt;T&gt;，繼承來的 Caculate 逐項 T→Object 上轉（編譯期安全）。
    /// </summary>
    [Serializable]
    public abstract class Formula_ObjectList<T> : Formula_ObjectList, IFormula<List<T>> where T : Object
    {
        public abstract List<T> CaculateTyped();

        List<T> IFormula<List<T>>.Caculate()
        {
            return CaculateTyped();
        }

        public sealed override List<Object> Caculate()
        {
            var result = new List<Object>();
            List<T> typed = CaculateTyped();
            if (typed == null) return result;

            foreach (T item in typed)
                result.Add(item);

            return result;
        }
    }

    [Serializable]
    public class Formula_ObjectList_AssetPipeline : Formula_ObjectList
    {
        public AssetPipelineSource source = new AssetPipelineSource();

        public override List<Object> Caculate()
        {
            if (source == null) return new List<Object>();
            return source.GetAssets<Object>();
        }
    }

    [Serializable]
    public class FormulaAsset_Object : FormulaAsset_Asset<Object, Formula_Object>
    {
        public FormulaAsset_Object()
        {
        }

        public FormulaAsset_Object(Object @default) : base(@default)
        {
        }
    }

    [Serializable]
    public class FormulaAsset_ObjectList : FormulaAsset_AssetList<Object, Formula_ObjectList>
    {
        public FormulaAsset_ObjectList()
        {
            @default = new List<Object>();
        }

        public FormulaAsset_ObjectList(List<Object> @default) : base(@default)
        {
        }
    }
}
