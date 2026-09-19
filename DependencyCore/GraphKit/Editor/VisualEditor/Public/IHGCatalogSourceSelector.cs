namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;

/// <summary>目錄內容提供可替換的來源；來源種類、子節點保留與回綁由使用端負責。</summary>
public interface IHGCatalogSourceSelector
{
    IReadOnlyList<Type> SourceTypes { get; }

    /// <summary>
    /// 檢查所有引用後替換載體的目錄內容。拒絕時不得修改資料；成功須保留子載體與連線。
    /// slots 包含目前文件的隱藏與候選來源，實作者只檢查 Node 指向 carrier 的欄位。
    /// Dirty、Undo 與重建由呼叫端在成功後處理。
    /// </summary>
    bool TryReplaceSource(GraphNode carrier, Type sourceType, IEnumerable<GraphSlotBase> slots, out string error);
}
}
