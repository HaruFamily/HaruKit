using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 目錄的 ListCell。左側由它的載體輸出結果，右側 <see cref="filter"/> 接收以完整目錄資料為輸入的篩選公式。
    /// </summary>
    // 結構與型別判定在 CatalogCellBase／CatalogFormulaSlot<T>，這一層只補 GraphKit 上不去的那一半——同步求值。
    [Serializable]
    public class CatalogCell : CatalogCellBase<List<Object>>
    {
        [SerializeReference, HGLabel("篩選")]
        private CatalogFormulaSlot filter = new CatalogFormulaSlot();

        public override FormulaSlotBase InputSlot => filter;

        public object EvaluateObject() => filter.Evaluate(Owner?.Read() ?? new List<Object>());
    }

    /// <summary>ListCell 右側的輸入欄位。未接篩選公式時直接回目錄完整資料。</summary>
    // 非泛型空殼：型別判定全在 CatalogFormulaSlot<List<Object>>，這一層只加同步求值。
    [Serializable]
    public class CatalogFormulaSlot : CatalogFormulaSlot<List<Object>>
    {
        /// <summary>套用篩選；沒接、停用或型別不符時原樣回整包。</summary>
        public object Evaluate(List<Object> catalog)
            => (ActiveFilter as IPackedFormula)?.EvaluateObject(catalog) ?? catalog;
    }

    /// <summary>
    /// 資產目錄的共同基底：一包 Project 資產，底下每一格各自篩出一種結果。
    /// </summary>
    // 它是目錄不是公式：沒有結果型別、求不出值，任何一般欄位都不能向它取值——值一律從底下的格子取，
    // 指得到它的欄位只有 CatalogSlotBase。
    //
    // 這一層只管格子；「內容從哪來」由子類各自回答一次 Read()，不在同一個型別上用旗標分岔——
    // 分岔過的版本要在 Read、接受寫入、拉線相容與驗證四處各判一次同一件事。
    [Serializable]
    public abstract class AssetCatalogBase : CatalogNodeShape<List<Object>>, IGraphInlineNodeOwner
    {
        // 不畫成一般參數列：GraphKit 將格子畫成目錄節點內的 ListCell。
        [HGHide]
        [SerializeReference]
        private List<GraphNode> cells = new List<GraphNode>();

        /// <summary>底下的格子。每一格是一顆節點，下游欄位指的是格子，不是目錄。</summary>
        public List<GraphNode> Cells
        {
            get { cells ??= new List<GraphNode>(); return cells; }
        }

        List<GraphNode> IGraphNodeOwner.ChildNodes => Cells;

        /// <summary>新增一個 ListCell：未接篩選公式時左側輸出完整目錄資料。</summary>
        GraphNode IGraphNodeOwner.CreateChild()
        {
            var cell = new GraphNode();
            cell.EnsureId();
            cell.SetBody(new CatalogCell());
            Cells.Add(cell);
            return cell;
        }

        void IGraphNodeOwner.RemoveChild(GraphNode child)
        {
            if (child == null) return;
            Cells.Remove(child);
        }

        /// <summary>把每一格的 Owner 指回自己。加格、載入與深複製之後都要呼叫。</summary>
        // 格子的 Owner 不序列化（存成回頭指向母目錄的欄位會讓資料成環），由目錄統一指派。
        // 驗證與執行前由 GraphVerifier.Collect 對整張圖走一趟，呼叫端不必自己記得補。
        public void SyncCells()
        {
            foreach (GraphNode node in Cells)
            {
                if (node?.BodyObject is CatalogCell cell) cell.SetOwner(this);
            }
        }
    }

    /// <summary>
    /// 動態目錄：動作執行時把產出寫進來，跑到該動作之後才有值。
    /// </summary>
    // 內容是執行期產物，所以 index 與 initialized 都不序列化。
    // 跨輪次要不要清空由寫入端 CatalogOutputSlot 的 reset 欄位決定，不在這裡依輪次推——
    // 這一層只看得到「Index 有內容」，而那可能來自同一批的前一次寫入，也可能來自上一輪。
    [HGNode("動態目錄", "動作把產出寫進來；跑到寫入的動作之後才有值", "目錄")]
    [Serializable]
    public sealed class DynamicAssetCatalog : AssetCatalogBase
    {
        [NonSerialized]
        private List<Object> index;

        [NonSerialized]
        private bool initialized;

        /// <summary>動作把產出寫進來，回傳實際加入幾個。同一次執行可以有多個動作寫進同一顆。</summary>
        public int Write(IEnumerable<Object> assets, bool reset)
        {
            if (reset || !initialized)
            {
                index = new List<Object>();
                initialized = true;
            }

            if (assets == null) return 0;

            int before = index.Count;
            // 去重比參照不比路徑：這裡收的是動作剛產出的資產物件。
            foreach (Object asset in assets)
            {
                if (asset == null) continue;
                if (index.Contains(asset)) continue;
                index.Add(asset);
            }
            return index.Count - before;
        }

        /// <summary>未初始化時回空清單，讀的人不必先寫過才拿得到集合。</summary>
        // 中途被刪掉的資產不往下傳，讀的人拿到的一律是有效引用。
        public override List<Object> Read()
        {
            var result = new List<Object>();
            if (!initialized) return result;

            foreach (Object asset in index)
                if (asset != null) result.Add(asset);

            return result;
        }
    }

    /// <summary>
    /// 原型目錄：取一份既有的目錄庫內容，隨時都有值。
    /// </summary>
    // 它是純引用，沒有自己的內容，所以沒有寫入狀態機，動作也接不上它（見 CatalogOutputSlot）。
    [HGNode("原型目錄", "取一份既有的目錄庫內容；隨時都有值", "目錄")]
    [Serializable]
    public sealed class PrototypeAssetCatalog : AssetCatalogBase, ICatalogLibraryConsumer
    {
        /// <summary>要取哪一個目錄，值是目錄的穩定 id。</summary>
        // 存 id 不存名字：左欄改名不該讓引用失聯。編輯器靠 [HGCatalog] 把它畫成目錄下拉。
        [HGCatalog]
        public string catalogId = string.Empty;

        /// <summary>只接得上裝 Project 資產的庫。</summary>
        // 是 Object 不是 List<Object>：比的是庫裝什麼，不是這顆目錄整包是什麼。
        Type ICatalogLibraryConsumer.CatalogItemType => typeof(Object);

        /// <summary>每次重讀目錄庫。</summary>
        // 不快取：目錄庫在編輯期隨時會被改，只在第一次讀取時決定內容會讓畫面停在舊值。
        public override List<Object> Read()
        {
            var result = new List<Object>();
            AssetPipeline pipeline = AssetPipeline.current;
            if (pipeline == null || string.IsNullOrWhiteSpace(catalogId)) return result;

            AssetPipelineAssetGroup group = pipeline.FindCatalogById(catalogId.Trim());
            if (group == null) return result;

            foreach (Object asset in group.assets)
            {
                if (asset == null) continue;
                if (result.Contains(asset)) continue;
                result.Add(asset);
            }
            return result;
        }
    }

    /// <summary>
    /// 動作的產出端：只接得上 <see cref="DynamicAssetCatalog"/>。
    /// </summary>
    // 這一格從來不求值——動作要的是節點本身，不是它的內容。公式、資產、Token 一律接不上，
    // 那三個由 GraphSlotBase 預設回 false，不必逐一宣告。
    [HGKind("目錄")]
    [Serializable]
    public class CatalogOutputSlot : CatalogSlotBase
    {
        [SerializeReference]
        private GraphNode _node;

        /// <summary>這一次寫入是不是重來：勾了就先清空目錄再寫，沒勾就接上去。</summary>
        // 「新的一輪開始」沒有任何動作知道，所以由使用者在圖上指定哪一項負責重來。
        // 預設 false＝累積；一顆目錄有多個寫入端時只有第一個該勾。
        [HGLabel("重來")]
        public bool reset;

        public override GraphNode Node => _node;

        public override void SetNode(GraphNode node) => _node = node;

        /// <summary>原型目錄接不上：它的內容由目錄庫供應，動作寫不進去。</summary>
        // 擋在拉線與落點，不是擋在執行：接得上卻什麼都不會發生是最難查的一種錯。
        public override bool AcceptsCatalogObject(GraphNodeContent pack) => pack is DynamicAssetCatalog;

        /// <summary>從產出接點拉到空白處就是要一顆動態目錄，沒有第二種選擇，不必再讓人選一次。</summary>
        public override GraphNodeContent CreateDefaultCatalog() => new DynamicAssetCatalog();

        /// <summary>把一批產出寫進接上的目錄，回傳實際加入幾個。沒接目錄就是 0。</summary>
        // 派發收在這裡，對稱 FormulaSlot.Evaluate／ActionSlot.Execute：
        // 呼叫端不必自己取 Target、判 null、再決定要不要重來。
        public int Write(IEnumerable<Object> assets)
        {
            DynamicAssetCatalog catalog = Target;
            return catalog == null ? 0 : catalog.Write(assets, reset);
        }

        /// <summary>接到的目錄；沒接、停用或內容不是動態目錄時回 null。</summary>
        public DynamicAssetCatalog Target
        {
            get
            {
                GraphNode node = _node;
                if (node == null || node.Disabled || node.Kind != NodeKind.Catalog) return null;

                return node.CatalogObject as DynamicAssetCatalog;
            }
        }
    }
}
