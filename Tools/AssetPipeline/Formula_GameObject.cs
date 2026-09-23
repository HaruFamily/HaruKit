using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_GameObject<TPack> : FormulaBase<GameObject, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_GameObject")]
    [HGKind(null, group: "Unity 資產型別")]
    public class GameObjectSlot : FormulaSlot<GameObject, Formula_GameObject<NullPack>>
    {
        public GameObjectSlot()
        {
        }

        public GameObjectSlot(GameObject defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_GameObjectList<TPack> : FormulaBase<List<GameObject>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_GameObjectList")]
    [HGKind(null, group: "Unity 資產型別/清單")]
    public class GameObjectListSlot : FormulaSlot<List<GameObject>, Formula_GameObjectList<NullPack>>
    {
        public GameObjectListSlot()
        {
            _default = new List<GameObject>();
        }

        public GameObjectListSlot(List<GameObject> defaultValue) : base(defaultValue)
        {
        }
    }
}
