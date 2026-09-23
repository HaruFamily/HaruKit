using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Object<TPack> : FormulaBase<Object, TPack>
    {
    }

    [Serializable]
    public abstract class Formula_ObjectList<TPack> : FormulaBase<List<Object>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_Object")]
    [HGKind(null, group: "Unity 資產型別")]
    public class ObjectSlot : FormulaSlot<Object, Formula_Object<NullPack>>
    {
        protected override bool AllowCompatibleResult => true;

        public ObjectSlot()
        {
        }

        public ObjectSlot(Object defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_ObjectList")]
    [HGKind(null, group: "Unity 資產型別/清單")]
    public class ObjectListSlot : FormulaSlot<List<Object>, Formula_ObjectList<NullPack>>
    {
        public ObjectListSlot()
        {
            _default = new List<Object>();
        }

        public ObjectListSlot(List<Object> defaultValue) : base(defaultValue)
        {
        }
    }
}
