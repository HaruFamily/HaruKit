namespace HaruFamily.Framework.ActionSystem
{
using System;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ASNodeAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Group { get; }
    public int Priority { get; }

    public ASNodeAttribute(string name, string description = null, string group = null, int priority = 0)
    {
        Name = name;
        Description = description;
        Group = group;
        Priority = priority;
    }
}

/// <summary>隱藏欄位，不建立 Graph 參數列。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASHideAttribute : Attribute { }

/// <summary>條件為 true 時才建立欄位的 Graph 參數列。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASShowAttribute : Attribute
{
    public string ConditionName { get; }

    public ASShowAttribute(string conditionName)
    {
        ConditionName = conditionName;
    }
}

/// <summary>
/// 覆寫一個公式族在 Graph 上顯示的結果型別名（節點 Header 右側與參數列最前面的 chip）。
/// 標在該族的 Slot 類上，例：<c>[ASKind("Entity")] class EntityIdListSlot : FormulaSlot&lt;List&lt;int&gt;, …&gt;</c>。
/// 沒標就用型別名；chip 寬約 44px，取兩到四個字。
/// </summary>
// 標在 Slot 而不是結果型別：結果型別可能是 BCL 型別（List<int>）根本標不上去，Slot 才是「族」的唯一載體。
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ASKindAttribute : Attribute
{
    public string Name { get; }

    public ASKindAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>覆寫欄位或 enum 成員的顯示名稱。</summary>
public enum ASLabelMode
{
    Show,
    Hide,
}

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASLabelAttribute : Attribute
{
    public string Name { get; }
    public ASLabelMode Mode { get; }

    public ASLabelAttribute(string name)
    {
        Name = name;
        Mode = ASLabelMode.Show;
    }

    public ASLabelAttribute(ASLabelMode mode, string name = null)
    {
        Name = name;
        Mode = mode;
    }
}

/// <summary>欄位的 Graph 懸停說明。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASDescriptionAttribute : Attribute
{
    public string Text { get; }

    public ASDescriptionAttribute(string text)
    {
        Text = text;
    }
}

/// <summary>把 enum 欄位繪製成按鈕列；支援 [Flags] enum 多選。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASEnumAttribute : Attribute { }

}
