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
    public abstract class Formula_ListString<TPack> : FormulaBase<List<string>, TPack>
    {
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_ListString")]
    public class ListStringSlot : FormulaSlot<List<string>, Formula_ListString<NullPack>>
    {
        public ListStringSlot()
        {
            _default = new List<string>();
        }

        public ListStringSlot(List<string> defaultValue) : base(defaultValue)
        {
        }
    }

    // Formula_StringList 與 Formula_ListString 是兩個族（＝兩種 Slot），不是同一個東西的別名：
    // 欄位宣告成哪一個，右鍵選單就只列那一族的公式。合併它們會讓兩邊的候選互相污染。
    [Serializable]
    public abstract class Formula_StringList<TPack> : Formula_ListString<TPack>
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
