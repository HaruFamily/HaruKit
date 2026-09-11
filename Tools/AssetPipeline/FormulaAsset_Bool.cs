using System;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Bool : IFormula<bool>
    {
        public abstract bool Caculate();
    }

    [Serializable]
    public class FormulaAsset_Bool : FormulaAsset<bool, Formula_Bool>
    {
        public FormulaAsset_Bool()
        {
        }

        public FormulaAsset_Bool(bool @default) : base(@default)
        {
        }
    }
}
