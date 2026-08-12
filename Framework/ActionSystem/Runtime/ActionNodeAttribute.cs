namespace PinPlugin.ActionSystem
{
using System;

/// <summary>
/// 標在具體 Action / Formula 類別上，決定它在視覺化編輯器裡的分類、名稱與說明。
/// </summary>
// 這是 ActionSystem 自己的屬性，不依賴 Odin：其他專案不裝 Odin 也能給節點取名字。
// 沒有標的類別會退回「類別名轉可讀字串」，需求規定不得直接顯示程式類別名，所以新增節點時請一律標上。
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ActionNodeAttribute : Attribute
{
    /// <summary>建立節點的選單分類，可用 '/' 分層。</summary>
    public string Category { get; }

    /// <summary>節點顯示名稱。</summary>
    public string Name { get; }

    /// <summary>節點說明，顯示在節點標題下方。</summary>
    public string Description { get; }

    /// <summary>同分類內的排序，小的在前。</summary>
    public int Priority { get; }

    public ActionNodeAttribute(string category, string name, string description = null, int priority = 0)
    {
        Category = category;
        Name = name;
        Description = description;
        Priority = priority;
    }
}

/// <summary>
/// 標在 Action / Formula 的欄位上，決定那一列參數欄位的顯示名稱與說明。
/// </summary>
// 不標也能跑：編輯器會退回 Odin 的 LabelText / TabGroup，再退回欄位名轉可讀字串。
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ActionParamAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public ActionParamAttribute(string name, string description = null)
    {
        Name = name;
        Description = description;
    }
}

}
