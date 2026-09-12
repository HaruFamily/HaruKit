using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_String : APFormulaBase<string>
    {
    }

    [Serializable]
    public class FormulaAsset_String : APFormulaSlot<string, Formula_String>
    {
        public FormulaAsset_String()
        {
            _default = string.Empty;
        }

        public FormulaAsset_String(string defaultValue) : base(defaultValue)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListString : APFormulaBase<List<string>>
    {
    }

    [Serializable]
    public class FormulaAsset_ListString : APFormulaSlot<List<string>, Formula_ListString>
    {
        public FormulaAsset_ListString()
        {
            _default = new List<string>();
        }

        public FormulaAsset_ListString(List<string> defaultValue) : base(defaultValue)
        {
        }
    }

    // Formula_StringList 與 Formula_ListString 是兩個族（＝兩種 Slot），不是同一個東西的別名：
    // 欄位宣告成哪一個，右鍵選單就只列那一族的公式。合併它們會讓兩邊的候選互相污染。
    [Serializable]
    public abstract class Formula_StringList : Formula_ListString
    {
    }

    [Serializable]
    public class FormulaAsset_StringList : APFormulaSlot<List<string>, Formula_StringList>
    {
        public FormulaAsset_StringList()
        {
            _default = new List<string>();
        }

        public FormulaAsset_StringList(List<string> defaultValue) : base(defaultValue)
        {
        }
    }
}
