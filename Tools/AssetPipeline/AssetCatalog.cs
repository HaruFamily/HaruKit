using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>目錄整包內容的來源。</summary>
    public enum CatalogSource
    {
        /// <summary>步驟執行時寫進來，跑到那一步之後才有值。</summary>
        Dynamic = 0,

        /// <summary>取一份既有的原型資產群組，隨時都有值。</summary>
        Prototype = 1,
    }

    /// <summary>
    /// 目錄的 ListCell。左側由它的載體輸出結果，右側 <see cref="filter"/> 接收以完整目錄資料為輸入的篩選公式。
    /// </summary>
    // 類別名與 filter 的欄位型別都刻意不動：兩者都寫進既有資產的 [SerializeReference] 記錄，
    // 改了等於斷開既有圖上的篩選公式連線。結構與型別判定在 CatalogCellBase／CatalogFormulaSlot<T>，
    // 這一層只補 GraphKit 上不去的那一半——同步求值。
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
    // 不直接用泛型基底當欄位型別，理由同 CatalogCell——既有資產記的是這個類別。
    [Serializable]
    public class CatalogFormulaSlot : CatalogFormulaSlot<List<Object>>
    {
        /// <summary>套用篩選；沒接、停用或型別不符時原樣回整包。</summary>
        public object Evaluate(List<Object> catalog)
            => (ActiveFilter as IAPPackedFormula)?.EvaluateObject(catalog) ?? catalog;
    }

    /// <summary>
    /// 目錄：一包資產，底下每一格各自篩出一種結果。
    /// </summary>
    // 它是包（IGraphCatalog）不是公式：沒有結果型別、求不出值，任何欄位都不能向它取值——
    // 值一律從它底下的格子取。只有目錄欄位（CatalogSlotBase）指得到它。
    // 動態內容是執行期產物，內容與初始化旗標都住在 CatalogBase 且不序列化。
    // 沒有自動重置：跨輪次要不要清空由寫入端 APCatalogOutputSlot 的 reset 欄位決定，
    // 所以一顆目錄有多個寫入端時，只有第一個該勾（見 Doc/2026-09-1/PLAN_Catalog泛型化與CatalogSlot.md §2.6、§3.4）。
    [HGNode("目錄", "一包資產；底下每一格各自篩出一種結果", "目錄")]
    [Serializable]
    public class AssetCatalog : CatalogBase<List<Object>>, IGraphCatalog, IGraphInlineNodeOwner,
        ICatalogLibraryConsumer
    {
        public CatalogSource source = CatalogSource.Dynamic;

        /// <summary>原型來源只接得上裝 Project 資產的庫。</summary>
        // 是 Object 不是 List<Object>：比的是庫裝什麼，不是這顆目錄整包是什麼。
        Type ICatalogLibraryConsumer.CatalogItemType => typeof(Object);

        /// <summary>原型模式要取哪一個目錄，值是目錄的穩定 id。</summary>
        // 存 id 不存名字：左欄改名不該讓引用失聯。編輯器靠 [HGCatalog] 把它畫成目錄下拉。
        [HGCatalog]
        [HGShowIf(nameof(IsPrototype))]
        public string prototypeCatalogId = string.Empty;

        /// <summary>原型來源時才顯示目錄下拉。</summary>
        public bool IsPrototype => source == CatalogSource.Prototype;

        // 不畫成一般參數列：GraphKit 將格子畫成 Catalog 節點內的 ListCell。
        [HGHide]
        [SerializeReference]
        private List<GraphNode> cells = new List<GraphNode>();

        /// <summary>底下的格子。每一格是一顆節點，下游欄位指的是格子，不是目錄。</summary>
        public List<GraphNode> Cells
        {
            get { cells ??= new List<GraphNode>(); return cells; }
        }

        /// <summary>步驟能不能寫進來。原型模式的內容由使用者維護，步驟寫不進去。</summary>
        public bool AcceptsWrite => source == CatalogSource.Dynamic;

        /// <summary>這一包現在有什麼。格子靠它取內容，這是唯一的讀取入口。</summary>
        // 原型來源每次重讀目錄庫，不走 CatalogBase 的內容：目錄庫在編輯期隨時會被改，
        // 只在第一次讀取時決定內容會讓畫面停在舊值。動態來源才是「被寫進來的那一包」。
        public override List<Object> Read()
            => source == CatalogSource.Prototype ? ReadPrototype() : ReadWritten();

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

        /// <summary>動態內容從空清單開始。</summary>
        protected override void OnInit() => Index = new List<Object>();

        /// <summary>接上去，重複的跳過。</summary>
        // 去重比參照不比路徑：這裡收的是步驟剛產出的資產物件。資產庫那邊收的是使用者拖進來的選取，
        // 同一個 .asset 的不同子資產各自是 Object，才需要比路徑。
        protected override void OnWrite(List<Object> value)
        {
            foreach (Object asset in value)
            {
                if (asset == null) continue;
                if (Index.Contains(asset)) continue;

                Index.Add(asset);
            }
        }

        /// <summary>步驟把產出寫進來，回傳實際加入幾個。同一次執行可以有多個步驟寫進同一顆。</summary>
        // reset 由寫入端的 APCatalogOutputSlot 給，不在這裡依輪次推：一顆目錄有多個寫入端時，
        // 「這一次是不是重來」只有圖上的接法答得出來，內容本身看不出差別。
        public int Write(IEnumerable<Object> assets, bool reset)
        {
            if (!AcceptsWrite)
            {
                AssetPipeline.ReportFormulaWarning("目錄設為原型來源，不接受步驟寫入。");
                return 0;
            }

            if (assets == null) return 0;

            int before = reset || !Initialized ? 0 : Index.Count;
            base.Write(new List<Object>(assets), reset);
            return Index.Count - before;
        }

        /// <summary>把每一格的 Owner 指回自己。加格、載入與深複製之後都要呼叫。</summary>
        // 格子的 Owner 不序列化（存成回頭指向母目錄的欄位會讓資料成環），由目錄統一指派。
        // 驗證與執行前由 APGraphVerifier.Collect 對整張圖走一趟，呼叫端不必自己記得補。
        public void SyncCells()
        {
            foreach (GraphNode node in Cells)
            {
                if (node?.BodyObject is CatalogCell inlineCell) inlineCell.SetOwner(this);
            }
        }

        private List<Object> ReadWritten()
        {
            var result = new List<Object>();
            if (!Initialized) return result;

            // 中途被刪掉的資產不往下傳，讀的人拿到的一律是還在的東西。
            foreach (Object asset in Index)
                if (asset != null) result.Add(asset);

            return result;
        }

        private List<Object> ReadPrototype()
        {
            var result = new List<Object>();
            AssetPipeline pipeline = AssetPipeline.current;
            if (pipeline == null || string.IsNullOrWhiteSpace(prototypeCatalogId)) return result;

            AssetPipelineAssetGroup group = pipeline.FindCatalogById(prototypeCatalogId.Trim());
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
    /// 步驟的產出端：只接得上 <see cref="AssetCatalog"/>。
    /// </summary>
    // 這一格從來不求值——步驟要的是節點本身，不是它的內容。公式、資產、Token 一律接不上，
    // 那三個由 GraphSlotBase 預設回 false，不必逐一宣告。
    [HGKind("目錄")]
    [Serializable]
    public class APCatalogOutputSlot : CatalogSlotBase
    {
        [SerializeReference]
        private GraphNode _node;

        /// <summary>這一次寫入是不是重來：勾了就先清空目錄再寫，沒勾就接上去。</summary>
        // 「新的一輪開始」沒有任何步驟知道，所以由使用者在圖上指定哪一步負責重來。
        // 預設 false＝累積；一顆目錄有多個寫入端時只有第一個該勾。
        [HGLabel("重來")]
        public bool reset;

        public override GraphNode Node => _node;

        public override void SetNode(GraphNode node) => _node = node;

        /// <summary>原型來源的目錄接不上：它的內容由目錄庫供應，步驟寫不進去。</summary>
        // 擋在拉線與落點，不是擋在執行：接得上卻什麼都不會發生是最難查的一種錯。
        // 目錄改成原型來源時，已經接上的這條線由編輯器當場斷開。
        public override bool AcceptsCatalogObject(GraphNodeContent pack)
            => pack is AssetCatalog catalog && catalog.AcceptsWrite;

        /// <summary>從產出接點拉到空白處就是要一顆目錄，沒有第二種選擇，不必再讓人選一次。</summary>
        public override GraphNodeContent CreateDefaultCatalog() => new AssetCatalog();

        /// <summary>把一批產出寫進接上的目錄，回傳實際加入幾個。沒接目錄就是 0。</summary>
        // 派發收在這裡，對稱 FormulaSlot.Evaluate／ActionSlot.Execute：
        // 呼叫端不必自己取 Target、判 null、再決定要不要重來。
        public int Write(IEnumerable<Object> assets)
        {
            AssetCatalog catalog = Target;
            return catalog == null ? 0 : catalog.Write(assets, reset);
        }

        /// <summary>接到的目錄；沒接、停用、內容不對或設成原型來源時回 null。</summary>
        // 原型來源的目錄擋在這裡而不是讓步驟寫進去再忽略：接得上卻什麼都不會發生是最難查的一種錯。
        public AssetCatalog Target
        {
            get
            {
                GraphNode node = _node;
                if (node == null || node.Disabled || node.Kind != NodeKind.Catalog) return null;

                var catalog = node.CatalogObject as AssetCatalog;
                return catalog != null && catalog.AcceptsWrite ? catalog : null;
            }
        }
    }
}
