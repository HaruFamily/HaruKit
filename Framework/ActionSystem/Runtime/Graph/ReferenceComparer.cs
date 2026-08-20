namespace HaruFamily.Framework.ActionSystem
{
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// 走訪節點圖時的 visited 比較器：一律比參考，不比值。
///
/// 圖走訪用 <c>HashSet&lt;object&gt;</c> 記已走過的東西，預設比較器會對 struct 走值相等——
/// 兩個內容相同的 struct 會被當成同一個，第二個底下的子樹就整段被跳過（不求值、不驗證、
/// 不算 Asset 引用），而且完全無聲。struct 沒有參考環，多走幾次只是多花時間，漏走才是錯。
/// </summary>
// 編輯器端另有同語意的 AGRefComparer（住在 Editor assembly，Runtime 取不到）。
public sealed class ReferenceComparer : IEqualityComparer<object>
{
    public static readonly ReferenceComparer Instance = new();

    public new bool Equals(object left, object right) => ReferenceEquals(left, right);

    public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
}
}
