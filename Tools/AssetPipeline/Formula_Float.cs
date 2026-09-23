using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Float<TPack> : FormulaBase<float, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_Float")]
    [HGKind(null, group: "基本變數型別")]
    public class FloatSlot : FormulaSlot<float, Formula_Float<NullPack>>
    {
        public FloatSlot()
        {
        }

        public FloatSlot(float defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListFloat<TPack> : FormulaBase<List<float>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_ListFloat")]
    [HGKind(null, group: "基本變數型別/清單")]
    public class ListFloatSlot : FormulaSlot<List<float>, Formula_ListFloat<NullPack>>
    {
        public ListFloatSlot()
        {
            _default = new List<float>();
        }

        public ListFloatSlot(List<float> defaultValue) : base(defaultValue)
        {
        }
    }
}
