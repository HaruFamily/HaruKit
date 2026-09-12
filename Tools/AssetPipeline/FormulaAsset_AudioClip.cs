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
    public class FormulaAsset_AudioClip : APFormulaSlot<AudioClip, Formula_AudioClip>
    {
        public FormulaAsset_AudioClip()
        {
        }

        public FormulaAsset_AudioClip(AudioClip defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_AudioClipList : Formula_ObjectList<AudioClip>
    {
    }

    [Serializable]
    public class FormulaAsset_AudioClipList : APFormulaSlot<List<AudioClip>, Formula_AudioClipList>
    {
        public FormulaAsset_AudioClipList()
        {
            _default = new List<AudioClip>();
        }

        public FormulaAsset_AudioClipList(List<AudioClip> defaultValue) : base(defaultValue)
        {
        }
    }
}
