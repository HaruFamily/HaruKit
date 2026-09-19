using System;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Bool<TPack> : FormulaBase<bool, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_Bool")]
    public class BoolSlot : FormulaSlot<bool, Formula_Bool<NullPack>>
    {
        public BoolSlot()
        {
        }

        public BoolSlot(bool defaultValue) : base(defaultValue)
        {
        }
    }
}
