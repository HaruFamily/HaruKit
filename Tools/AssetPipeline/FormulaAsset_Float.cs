using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Float : APFormulaBase<float>
    {
    }

    [Serializable]
    public class FormulaAsset_Float : APFormulaSlot<float, Formula_Float>
    {
        public FormulaAsset_Float()
        {
        }

        public FormulaAsset_Float(float defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListFloat : APFormulaBase<List<float>>
    {
    }

    [Serializable]
    public class FormulaAsset_ListFloat : APFormulaSlot<List<float>, Formula_ListFloat>
    {
        public FormulaAsset_ListFloat()
        {
            _default = new List<float>();
        }

        public FormulaAsset_ListFloat(List<float> defaultValue) : base(defaultValue)
        {
        }
    }
}
