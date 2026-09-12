using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Int : APFormulaBase<int>
    {
    }

    [Serializable]
    public class FormulaAsset_Int : APFormulaSlot<int, Formula_Int>
    {
        public FormulaAsset_Int()
        {
        }

        public FormulaAsset_Int(int defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListInt : APFormulaBase<List<int>>
    {
    }

    [Serializable]
    public class FormulaAsset_ListInt : APFormulaSlot<List<int>, Formula_ListInt>
    {
        public FormulaAsset_ListInt()
        {
            _default = new List<int>();
        }

        public FormulaAsset_ListInt(List<int> defaultValue) : base(defaultValue)
        {
        }
    }
}
