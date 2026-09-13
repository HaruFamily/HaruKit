namespace HaruFamily.Framework.LogicGraph
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 畫布上的頭端：自己是一顆固定節點，所以要記得座標。
/// ActionSlot、GraphEndpoint、ActionTimingGroup 與兩種資產基底都是頭端。
/// </summary>
// 這五個型別原本各自宣告一模一樣的 Pos／HasPos／ClearPos，編輯器只能靠成員名反射取值。
// 抽成介面後編輯器改走型別，重新命名成員時編譯器會抓到，反射抓不到。
public interface IGraphHead
{
    /// <summary>頭端座標。<see cref="HasPos"/> 為 false 時無意義。</summary>
    Vector2 Pos { get; set; }

    /// <summary>false 代表使用者沒有手動擺過位置，交給自動排版。</summary>
    bool HasPos { get; }

    /// <summary>清掉手動座標，讓自動排版接手。</summary>
    void ClearPos();
}

/// <summary>
/// 擁有候選節點池的畫布主人。候選節點只供編輯，不執行、不參與驗證。
/// </summary>
// 不併進 IGraphHead：ActionTimingGroup 是頭端但沒有候選池，合併會逼它長出一個假的空清單。
public interface IOrphanPool
{
    /// <summary>本畫布的候選節點清單。</summary>
    List<GraphNode> Orphans { get; }
}

/// <summary>
/// 擁有具名變數清單的圖主人。對資產而言這份清單同時就是它對呼叫端的參數介面。
/// </summary>
public interface IEndpointOwner
{
    /// <summary>本圖的具名變數。從端點開始的整棵子樹都是正式資料。</summary>
    List<GraphEndpoint> Endpoints { get; }
}

/// <summary>
/// 一張可編輯的圖對編輯器的完整形狀。編輯器靠這個介面在 Owner 身上找到要編的欄位，
/// 不認識任何具體的圖型別，所以同一套編輯器可以編不同領域的圖。
/// </summary>
public interface IGraphDocument : IOrphanPool, IEndpointOwner
{
    /// <summary>畫布上的 HEAD 們。元素型別由實作決定（LogicGraph 給的是時機群組）。</summary>
    IList Roots { get; }

    /// <summary>是否已通過驗證。未通過的圖 runtime 不執行。</summary>
    bool IsValidated { get; }

    /// <summary>內容變動，撤銷已驗證狀態。</summary>
    void MarkDirty();

    /// <summary>依 Core 規則驗證整張圖並更新 <see cref="IsValidated"/>。錯誤由實作記進 Console。</summary>
    // 契約刻意是無參數的：呼叫端（存檔）不需要知道實作額外收什麼選項。
    void Verify();

    /// <summary>深層複製整張圖（含 SerializeReference 多型樹）。編輯器的工作副本靠它產生。</summary>
    object DeepCopy();

    /// <summary>求值封包型別。編輯器用它過濾「屬於這張圖的公式族」。</summary>
    Type PackType { get; }

    /// <summary>root 底下那串項目的元素型別。空 root 也要建得出新項目，所以不能從現有內容推。</summary>
    Type ItemSlotType { get; }

    /// <summary>
    /// 這張畫布可以有哪些 root，以識別值表示（LogicGraph 給的是時機 enum 值）。
    /// owner 可能進一步縮小範圍，傳 null 代表不過濾。
    /// </summary>
    // 識別值型別由實作決定，編輯器只做相等比較與 ToString()，不假設它是 enum。
    IReadOnlyList<object> RootKeys(UnityEngine.Object owner);

    /// <summary>這個 root 的識別值。</summary>
    object KeyOf(object root);

    /// <summary>這個 root 在節點 Header 上的顯示名。</summary>
    string TitleOf(object root);

    /// <summary>root 節點的身分標籤。root 不回傳值，標籤是它與一般節點的唯一區別；空字串代表不顯示。</summary>
    // 由圖自己提供而不是寫死在編輯器裡：領域不同，root 的身分也不同（時機／管線／…）。
    string RootChip { get; }

    /// <summary>
    /// 句子裡稱呼 root 的名詞，例如「時機」「管線」。編輯器用它組選單、提示與 log。
    /// </summary>
    // 只給名詞，不要帶「節點」「群組」：那兩個字由編輯器自己按句子接上，接法各處不同。
    string RootNoun { get; }

    /// <summary>這個 root 底下的項目清單。</summary>
    IList ItemsOf(object root);

    /// <summary>依識別值建立 root 並加入 <see cref="Roots"/>；已存在則回傳既有的，建不出來回 null。</summary>
    object AddRoot(object key);
}

}
