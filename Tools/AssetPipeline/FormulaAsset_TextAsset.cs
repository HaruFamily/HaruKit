using System;
using System.Collections.Generic;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_TextAsset : Formula_Object<TextAsset>
    {
    }

    [Serializable]
    public class FormulaAsset_TextAsset : FormulaAsset_Asset<TextAsset, Formula_TextAsset>
    {
        public FormulaAsset_TextAsset()
        {
        }

        public FormulaAsset_TextAsset(TextAsset @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_TextAssetList : Formula_ObjectList<TextAsset>
    {
    }

    [Serializable]
    public class Formula_TextAssetList_AssetPipeline : Formula_TextAssetList
    {
        public AssetPipelineSource source = new AssetPipelineSource();

        public override List<TextAsset> CaculateTyped()
        {
            if (source == null) return new List<TextAsset>();
            return source.GetAssets<TextAsset>();
        }
    }

    [Serializable]
    public class FormulaAsset_TextAssetList : FormulaAsset_AssetList<TextAsset, Formula_TextAssetList>
    {
        public FormulaAsset_TextAssetList()
        {
            @default = new List<TextAsset>();
        }

        public FormulaAsset_TextAssetList(List<TextAsset> @default) : base(@default)
        {
        }
    }
}
