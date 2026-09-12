using System;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Bool : APFormulaBase<bool>
    {
    }

    [Serializable]
    public class FormulaAsset_Bool : APFormulaSlot<bool, Formula_Bool>
    {
        public FormulaAsset_Bool()
        {
        }

        public FormulaAsset_Bool(bool defaultValue) : base(defaultValue)
        {
        }
    }
}
