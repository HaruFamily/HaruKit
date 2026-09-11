using System;
using System.Collections.Generic;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_AudioClip : Formula_Object<AudioClip>
    {
    }

    [Serializable]
    public class FormulaAsset_AudioClip : FormulaAsset_Asset<AudioClip, Formula_AudioClip>
    {
        public FormulaAsset_AudioClip()
        {
        }

        public FormulaAsset_AudioClip(AudioClip @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_AudioClipList : Formula_ObjectList<AudioClip>
    {
    }

    [Serializable]
    public class Formula_AudioClipList_AssetPipeline : Formula_AudioClipList
    {
        public AssetPipelineSource source = new AssetPipelineSource();

        public override List<AudioClip> CaculateTyped()
        {
            if (source == null) return new List<AudioClip>();
            return source.GetAssets<AudioClip>();
        }
    }

    [Serializable]
    public class FormulaAsset_AudioClipList : FormulaAsset_AssetList<AudioClip, Formula_AudioClipList>
    {
        public FormulaAsset_AudioClipList()
        {
            @default = new List<AudioClip>();
        }

        public FormulaAsset_AudioClipList(List<AudioClip> @default) : base(@default)
        {
        }
    }
}
