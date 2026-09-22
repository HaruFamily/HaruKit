namespace HaruFamily.DependencyCore.GraphKit
{
using System;

/// <summary>
/// 寫入目標欄位的非泛型基底：指著一顆 Property 節點，替換它的目前值。
/// </summary>
// 與 FormulaSlotBase 分開：這一格指定寫入目標，不求值。
// 它回答的唯一問題是「寫得進哪一種 Property」，所以只有族身分與相容判定兩個成員。
//
// **寫入目標不是求值依賴**：驗證與循環檢查必須把這種欄位排除在求值圖之外，
// 否則「讀 Property → 加一 → 寫回同一顆」會被誤判成遞迴求值。
//
// 刻意不放序列化欄位，同另外三個 slot 基底。
public abstract class PropertySlotBase : GraphSlotBase
{
    /// <summary>寫進去的值屬於哪一族（＝值型別宣告用的 Slot 型別）。</summary>
    public abstract Type FamilyType { get; }

    /// <summary>寫不寫得進這一顆 Property。族不同一律不收，不比結果型別。</summary>
    // 必須接受 null：節點還沒選定義時也會被問一次。
    public override bool AcceptsProperty(GraphProperty property)
        => property?.FamilyType != null && property.FamilyType == FamilyType;
}

}
