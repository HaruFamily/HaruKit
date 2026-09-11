using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Float : IFormula<float>
    {
        public abstract float Caculate();
    }

    [Serializable]
    public class FormulaAsset_Float : FormulaAsset<float, Formula_Float>
    {
        public FormulaAsset_Float()
        {
        }

        public FormulaAsset_Float(float @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListFloat : IFormula<List<float>>
    {
        public abstract List<float> Caculate();
    }

    [Serializable]
    public class FormulaAsset_ListFloat : FormulaAsset<List<float>, Formula_ListFloat>
    {
        public FormulaAsset_ListFloat()
        {
            @default = new List<float>();
        }

        public FormulaAsset_ListFloat(List<float> @default) : base(@default)
        {
        }
    }
}
