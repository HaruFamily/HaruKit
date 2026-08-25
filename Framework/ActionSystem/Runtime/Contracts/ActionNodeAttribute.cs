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

    /// <summary>
    /// 節點寬度，單位＝格線格數（`AGGraph.GridSize`，20px）。0／未給＝預設 15 格（300px）。
    /// 具名指定：<c>[ASNode("Nearest", "…", "目標", 2, Width = 20)]</c>。
    /// </summary>
    // 單位是格不是 px：自動排版的子欄 x 走 SnapUpToGrid，寬度不是 20 的倍數會讓欄距多出 1～19px 的隨機縫。
    public int Width { get; set; }

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

/// <summary>
/// 條件為 true 時才建立欄位的 Graph 參數列；conditionName 指向同類（或基底類）的 bool 欄位或無參數 bool 屬性。
/// 例：<c>[ASShowIf(nameof(ShowAddAmount))]</c>。
/// </summary>
// 不是 [ASHide] 的反義：[ASHide] 是「這欄位不屬於節點圖」的靜態排除，這個是「相關性隨另一欄位變動」。
// 條件設錯時 fail-open（記 Error 照顯示），所以不能拿它當「保證不顯示」用。
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASShowIfAttribute : Attribute
{
    public string ConditionName { get; }

    public ASShowIfAttribute(string conditionName)
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

/// <summary>覆寫欄位或 enum 成員的顯示名稱；只想調標籤欄寬度時用無參數建構子，例 <c>[ASLabel(Width = 5)]</c>。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASLabelAttribute : Attribute
{
    public string Name { get; }

    /// <summary>
    /// 這一列標籤欄的寬度，單位＝格線格數（`AGGraph.GridSize`，20px），與 `ASNode.Width` 同單位。
    /// 0／未給＝預設（節點寬的 30%，進位到整格，不設上限）。
    /// 具名指定：<c>[ASLabel("類型標籤", Width = 5)]</c>。
    /// 與 <see cref="WidthRatio"/> 二選一，兩個都標時 Width 勝出（不吼：標錯畫面當場看得出來）。
    /// </summary>
    // 用格數不用 px：在圖上直接數幾格就好，不必回頭換算像素。
    public int Width { get; set; }

    /// <summary>
    /// 這一列標籤欄佔整列的比例，**0～1**。0／未給＝預設 0.3。
    /// 算完一樣往上進位到整格，欄寬永遠落在格線上。
    /// 具名指定：<c>[ASLabel("類型標籤", WidthRatio = 0.6f)]</c>。
    /// </summary>
    // 節點加寬時要跟著長的欄位用這個；要「剛好塞得下這幾個字」的用 Width。
    public float WidthRatio { get; set; }

    public ASLabelAttribute() { }

    public ASLabelAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>只藏欄位左側的標籤，欄位本身照畫；整列都不要就用 <see cref="ASHideAttribute"/>。</summary>
// 不做成 ASLabel 的 mode 參數：命名與畫不畫是兩個決定，合在一起會允許「取了一個永遠不顯示的名字」這種無意義狀態。
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class ASHideLabelAttribute : Attribute { }

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
