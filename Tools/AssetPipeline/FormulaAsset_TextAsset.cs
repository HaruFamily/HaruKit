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
    public class FormulaAsset_TextAsset : APFormulaSlot<TextAsset, Formula_TextAsset>
    {
        public FormulaAsset_TextAsset()
        {
        }

        public FormulaAsset_TextAsset(TextAsset defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_TextAssetList : Formula_ObjectList<TextAsset>
    {
    }

    [Serializable]
    public class FormulaAsset_TextAssetList : APFormulaSlot<List<TextAsset>, Formula_TextAssetList>
    {
        public FormulaAsset_TextAssetList()
        {
            _default = new List<TextAsset>();
        }

        public FormulaAsset_TextAssetList(List<TextAsset> defaultValue) : base(defaultValue)
        {
        }
    }
}
