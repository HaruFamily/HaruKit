using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_TextAsset<TPack> : FormulaBase<TextAsset, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_TextAsset")]
    [HGKind(null, group: "Unity 資產型別")]
    public class TextAssetSlot : FormulaSlot<TextAsset, Formula_TextAsset<NullPack>>
    {
        public TextAssetSlot()
        {
        }

        public TextAssetSlot(TextAsset defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_TextAssetList<TPack> : FormulaBase<List<TextAsset>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_TextAssetList")]
    [HGKind(null, group: "Unity 資產型別/清單")]
    public class TextAssetListSlot : FormulaSlot<List<TextAsset>, Formula_TextAssetList<NullPack>>
    {
        public TextAssetListSlot()
        {
            _default = new List<TextAsset>();
        }

        public TextAssetListSlot(List<TextAsset> defaultValue) : base(defaultValue)
        {
        }
    }
}
