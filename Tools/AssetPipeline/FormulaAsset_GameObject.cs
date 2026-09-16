using System;
using System.Collections.Generic;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_GameObject : Formula_Object<GameObject>
    {
    }

    [Serializable]
    public class FormulaAsset_GameObject : FormulaSlot<GameObject, Formula_GameObject>
    {
        public FormulaAsset_GameObject()
        {
        }

        public FormulaAsset_GameObject(GameObject defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_GameObjectList : Formula_ObjectList<GameObject>
    {
    }

    [Serializable]
    public class FormulaAsset_GameObjectList : FormulaSlot<List<GameObject>, Formula_GameObjectList>
    {
        public FormulaAsset_GameObjectList()
        {
            _default = new List<GameObject>();
        }

        public FormulaAsset_GameObjectList(List<GameObject> defaultValue) : base(defaultValue)
        {
        }
    }
}
