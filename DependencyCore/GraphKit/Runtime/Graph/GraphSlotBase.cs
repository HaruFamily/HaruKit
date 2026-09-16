namespace HaruFamily.DependencyCore.GraphKit
{
using UnityEngine;

/// <summary>
/// 欄位的共同非泛型基底：走訪、取節點與相容判定不必先知道這是哪一種欄位。
/// </summary>
// 三種欄位（Formula／Action／Catalog）真正共通的只有「指著一顆節點」與「收不收得下某個來源」。
// 其餘（結果型別、座標、候選池、包的方向）各自宣告，不往上堆——
// 上移一個只有一種欄位用得到的成員，等於逼另外兩種長出假的實作。
//
// 刻意不放序列化欄位，同 FormulaSlotBase／ActionSlotBase：欄位全在最下層的泛型子類，
// 多這一層中介不會動到任何既有資料的序列化格式。
public abstract class GraphSlotBase
{
    /// <summary>目前接的節點。null 的意義由各種欄位自己定義（常數／空槽／未接）。</summary>
    public abstract GraphNode Node { get; }

    /// <summary>接上或斷開節點。</summary>
    public abstract void SetNode(GraphNode node);

    /// <summary>能不能接這個內嵌內容。預設一律不能，收得下的欄位自己覆寫。</summary>
    // 三個 Accepts 在這一層是 virtual 而不是 abstract：目錄欄位只收包，
    // 逼它實作三個永遠回 false 的成員只會讓「它其實不收這些」變得不明顯。
    public virtual bool AcceptsBody(GraphNodeContent body) => false;

    /// <summary>能不能接這個資產。</summary>
    public virtual bool AcceptsAsset(ScriptableObject asset) => false;

    /// <summary>能不能接這個具名Token。</summary>
    public virtual bool AcceptsToken(GraphToken endpoint) => false;
}

}
