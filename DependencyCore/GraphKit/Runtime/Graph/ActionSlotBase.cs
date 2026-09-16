namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 動作欄位的非泛型基底：編輯器與驗證不必帶泛型參數就能取節點、座標與型別相容判定。
/// </summary>
// 刻意不放任何序列化欄位——欄位全留在 ActionSlot<TPack>，序列化格式與既有資料不變。
// 形狀比照 FormulaSlotBase：非泛型契約在上、資料與求值在泛型子類。
// Node／SetNode／三個 Accepts 在 GraphSlotBase，與公式、目錄欄位共用。
public abstract class ActionSlotBase : GraphSlotBase, IGraphHead, IOrphanPool
{
    /// <summary>停用中的動作不執行，但設定完整保留。</summary>
    public abstract bool Disabled { get; set; }

    /// <summary>同名動作的區分標籤，只影響顯示。</summary>
    public abstract string Label { get; set; }

    /// <summary>頭端節點的穩定識別碼，焦點與座標都靠它。空字串代表尚未指派。</summary>
    public abstract string Id { get; }

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public abstract string EnsureId();

    /// <summary>複製頭端後必須換新識別碼，否則兩個頭端共用同一筆座標與焦點。</summary>
    public abstract void ResetId();

    public abstract Vector2 Pos { get; set; }

    public abstract bool HasPos { get; }

    public abstract void ClearPos();

    /// <summary>本頭端的候選節點池。僅視覺化編輯器使用。</summary>
    public abstract List<GraphNode> Orphans { get; }

    /// <summary>求值封包型別（TPack）。</summary>
    public abstract Type PackType { get; }

    /// <summary>這個欄位收得下的內嵌動作基底型別。型別選單用它過濾。</summary>
    public abstract Type BodyBaseType { get; }

    /// <summary>這個欄位收得下的動作資產型別。</summary>
    public abstract Type AssetBaseType { get; }
}

}
