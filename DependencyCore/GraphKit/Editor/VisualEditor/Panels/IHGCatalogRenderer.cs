namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 目錄庫的一列怎麼畫、拖進來的東西收不收。由編輯對象（Owner）實作，沒實作就走通用畫法。
/// </summary>
// 這一半上不了 Runtime 契約：IMGUI 與 DragAndDrop 都是 Editor 專屬，而 GraphContracts 在 Runtime。
// 也不該由框架猜——「一個項目長什麼樣」「什麼東西拖得進來」是領域知識：
// 資產分組要畫圖示、點名字 ping 到 Project、只收 Project 裡的資產；換一種內容全都不成立。
//
// 框架保留的是版面：分區幾何、展開收合、搜尋、就地改名、復原堆疊。
public interface IHGCatalogRenderer
{
    /// <summary>
    /// 畫目錄裡的一個項目。回 true 代表使用者在這一列按了移除。
    /// </summary>
    // 回傳「要不要移除」而不是讓實作直接動資料：移除要進復原堆疊，那是框架的責任。
    bool DrawCatalogItem(Rect rect, IGraphCatalogLibrary library, object item);

    /// <summary>
    /// 目前這次拖放要交給這個庫的東西；不收就回 null 或空清單。
    /// </summary>
    // 由實作自己讀 DragAndDrop：收不收得下是型別問題，而「這個庫裝什麼」只有使用端知道
    //（見 IGraphCatalogLibrary.ItemType）。
    //
    // **library 可能是 null**：拖到「＋ 新增目錄」上時目標庫還不存在，問的是
    // 「這一輪拖曳有沒有我收得下的東西」。只有一種內容的使用端可以直接忽略這個參數。
    IReadOnlyList<object> AcceptCatalogDrag(IGraphCatalogLibrary library);

    /// <summary>空目錄時那一行提示。回 null 用框架的預設句。</summary>
    // 提示要說出下一步，而下一步是領域行為（「把 Project 的資產拖上來」對別種內容是錯的）。
    string CatalogEmptyHint(IGraphCatalogLibrary library);
}

}
