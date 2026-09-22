using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_String<TPack> : FormulaBase<string, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_String")]
    public class StringSlot : FormulaSlot<string, Formula_String<NullPack>>
    {
        public StringSlot()
        {
            _default = string.Empty;
        }

        public StringSlot(string defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_StringList<TPack> : FormulaBase<List<string>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_StringList")]
    public class StringListSlot : FormulaSlot<List<string>, Formula_StringList<NullPack>>
    {
        public StringListSlot()
        {
            _default = new List<string>();
        }

        public StringListSlot(List<string> defaultValue) : base(defaultValue)
        {
        }
    }
}
