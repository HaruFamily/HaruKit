using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_AudioClip<TPack> : FormulaBase<AudioClip, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_AudioClip")]
    public class AudioClipSlot : FormulaSlot<AudioClip, Formula_AudioClip<NullPack>>
    {
        public AudioClipSlot()
        {
        }

        public AudioClipSlot(AudioClip defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_AudioClipList<TPack> : FormulaBase<List<AudioClip>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_AudioClipList")]
    public class AudioClipListSlot : FormulaSlot<List<AudioClip>, Formula_AudioClipList<NullPack>>
    {
        public AudioClipListSlot()
        {
            _default = new List<AudioClip>();
        }

        public AudioClipListSlot(List<AudioClip> defaultValue) : base(defaultValue)
        {
        }
    }
}
