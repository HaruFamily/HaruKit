using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Int<TPack> : FormulaBase<int, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_Int")]
    public class IntSlot : FormulaSlot<int, Formula_Int<NullPack>>
    {
        public IntSlot()
        {
        }

        public IntSlot(int defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListInt<TPack> : FormulaBase<List<int>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_ListInt")]
    public class ListIntSlot : FormulaSlot<List<int>, Formula_ListInt<NullPack>>
    {
        public ListIntSlot()
        {
            _default = new List<int>();
        }

        public ListIntSlot(List<int> defaultValue) : base(defaultValue)
        {
        }
    }
}
