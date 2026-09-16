using System;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Bool : FormulaBase<bool>
    {
    }

    [Serializable]
    public class FormulaAsset_Bool : FormulaSlot<bool, Formula_Bool>
    {
        public FormulaAsset_Bool()
        {
        }

        public FormulaAsset_Bool(bool defaultValue) : base(defaultValue)
        {
        }
    }
}
