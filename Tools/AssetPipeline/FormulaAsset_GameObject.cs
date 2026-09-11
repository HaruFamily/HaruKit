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
    public class FormulaAsset_GameObject : FormulaAsset_Asset<GameObject, Formula_GameObject>
    {
        public FormulaAsset_GameObject()
        {
        }

        public FormulaAsset_GameObject(GameObject @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_GameObjectList : Formula_ObjectList<GameObject>
    {
    }

    [Serializable]
    public class Formula_GameObjectList_AssetPipeline : Formula_GameObjectList
    {
        public AssetPipelineSource source = new AssetPipelineSource();

        public override List<GameObject> CaculateTyped()
        {
            if (source == null) return new List<GameObject>();
            return source.GetAssets<GameObject>();
        }
    }

    [Serializable]
    public class FormulaAsset_GameObjectList : FormulaAsset_AssetList<GameObject, Formula_GameObjectList>
    {
        public FormulaAsset_GameObjectList()
        {
            @default = new List<GameObject>();
        }

        public FormulaAsset_GameObjectList(List<GameObject> @default) : base(@default)
        {
        }
    }
}
