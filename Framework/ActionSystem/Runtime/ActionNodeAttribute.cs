namespace PinPlugin.ActionSystem
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
