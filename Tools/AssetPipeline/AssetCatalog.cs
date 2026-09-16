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

    /// <summary>目錄篩選公式的非泛型入口，讓 CatalogCell 可依實際結果型別轉交資料。</summary>
    public interface ICatalogFormula
    {
        Type ResultType { get; }
        object EvaluateObject(List<Object> catalog);
    }

    /// <summary>
    /// 目錄的 ListCell。左側由它的載體輸出結果，右側 <see cref="filter"/> 接收以完整目錄資料為輸入的篩選公式。
    /// </summary>
    [Serializable]
    public class CatalogCell : GraphNodeContent, IGraphInlineNode
    {
        [SerializeReference, HGLabel("篩選")]
        private CatalogFormulaSlot filter = new CatalogFormulaSlot();

        [NonSerialized]
        private AssetCatalog owner;

        public FormulaSlotBase InputSlot => filter;
        public Type ResultType => filter.ResultType;
        public AssetCatalog Owner => owner;

        public void SetOwner(AssetCatalog value) => owner = value;

        public object EvaluateObject() => filter.Evaluate(owner?.Items ?? new List<Object>());
    }

    /// <summary>ListCell 右側的輸入欄位。未接篩選公式時直接回目錄完整資料。</summary>
    [Serializable]
    public class CatalogFormulaSlot : FormulaSlotBase
    {
        [SerializeReference]
        private GraphNode node;

        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override Type ResultType => ActiveFilter?.ResultType ?? typeof(List<Object>);
        public override Type PackType => typeof(List<Object>);
        public override Type BodyBaseType => typeof(ICatalogFormula);
        public override Type AssetBaseType => null;
        public override object DefaultObject { get => null; set { } }
        public override bool AcceptsBody(GraphNodeContent body) => body is ICatalogFormula;
        public override bool AcceptsAsset(ScriptableObject asset) => false;
        public override bool AcceptsToken(GraphToken endpoint) => false;

        public object Evaluate(List<Object> catalog) => ActiveFilter?.EvaluateObject(catalog) ?? catalog;

        /// <summary>目前生效的篩選公式，沒有就是 null。</summary>
        // 空槽與停用走同一條路（同 APFormulaSlot.Evaluate）：都當作沒有篩選。
        // ResultType 與 Evaluate 必須看同一個判定，否則停用一顆公式會讓格子對外宣稱的型別
        // 與實際回傳值對不上，下游只能在求值當下退保底值。
        private ICatalogFormula ActiveFilter
            => node != null && !node.Disabled ? node.BodyObject as ICatalogFormula : null;
    }

    /// <summary>
    /// 目錄：一包資產，底下每一格各自篩出一種結果。
    /// </summary>
    // 它是包（IGraphPack）不是公式：沒有結果型別、求不出值，任何欄位都不能向它取值——
    // 值一律從它底下的格子取。只有宣告 AcceptsPack 的產出格指得到它。
    // 動態內容是執行期產物，因此 [NonSerialized]；新舊靠 AssetPipeline.RunToken 分辨，
    // 上一次執行留下的內容在這一次一律當空的，不必在管線開頭走訪整張圖先清一遍。
    [HGNode("目錄", "一包資產；底下每一格各自篩出一種結果", "目錄")]
    [Serializable]
    public class AssetCatalog : GraphNodeContent, IGraphPack, IGraphInlineNodeOwner
    {
        public CatalogSource source = CatalogSource.Dynamic;

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

        [NonSerialized]
        private readonly List<Object> written = new List<Object>();

        [NonSerialized]
        private int filledRun;

        /// <summary>底下的格子。每一格是一顆節點，下游欄位指的是格子，不是目錄。</summary>
        public List<GraphNode> Cells
        {
            get { cells ??= new List<GraphNode>(); return cells; }
        }

        /// <summary>步驟能不能寫進來。原型模式的內容由使用者維護，步驟寫不進去。</summary>
        public bool AcceptsWrite => source == CatalogSource.Dynamic;

        /// <summary>這一包現在有什麼。格子靠它取內容，這是唯一的讀取入口。</summary>
        public List<Object> Items
            => source == CatalogSource.Prototype ? ReadPrototype() : ReadWritten();

        /// <summary>這一次執行已經收到幾個。不是這一次寫的內容一律當 0。</summary>
        public int WrittenCount => filledRun == AssetPipeline.RunToken ? written.Count : 0;

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

        /// <summary>步驟把產出寫進來，回傳實際加入幾個。同一次執行可以有多個步驟寫進同一顆。</summary>
        // 去重比參照不比路徑：這裡收的是步驟剛產出的資產物件。資產庫那邊收的是使用者拖進來的選取，
        // 同一個 .asset 的不同子資產各自是 Object，才需要比路徑。
        public int Write(IEnumerable<Object> assets)
        {
            if (!AcceptsWrite)
            {
                AssetPipeline.ReportFormulaWarning("目錄設為原型來源，不接受步驟寫入。");
                return 0;
            }

            if (assets == null) return 0;
            BeginRun();

            int added = 0;
            foreach (Object asset in assets)
            {
                if (asset == null) continue;
                if (written.Contains(asset)) continue;

                written.Add(asset);
                added++;
            }
            return added;
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
            if (filledRun != AssetPipeline.RunToken) return result;

            // 中途被刪掉的資產不往下傳，讀的人拿到的一律是還在的東西。
            foreach (Object asset in written)
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

        /// <summary>這一次執行第一次被寫入時，先清掉上一次留下的內容。</summary>
        private void BeginRun()
        {
            if (filledRun == AssetPipeline.RunToken) return;
            written.Clear();
            filledRun = AssetPipeline.RunToken;
        }
    }

    /// <summary>
    /// 步驟的產出端：只接得上 <see cref="AssetCatalog"/>。
    /// </summary>
    // 這一格從來不求值——步驟要的是節點本身，不是它的內容。它也不屬於任何公式族：
    // 結果型別是 void，收不收得下只看 AcceptsPack，所以來源選單與拉線都只會給目錄。
    [HGKind("目錄")]
    [Serializable]
    public class APCatalogOutputSlot : FormulaSlotBase
    {
        [SerializeReference]
        private GraphNode _node;

        public override GraphNode Node => _node;

        public override void SetNode(GraphNode node) => _node = node;

        /// <summary>包求不出值，所以這一格沒有結果型別。</summary>
        public override Type ResultType => typeof(void);

        public override Type PackType => typeof(APPack);

        public override Type BodyBaseType => null;

        public override Type AssetBaseType => null;

        /// <summary>方向是反的：步驟寫進去，不向它取值。編輯器據此換接點與線的顏色，也不畫常數框。</summary>
        public override bool IsOutput => true;

        /// <summary>只收包。公式、資產、Token 一律接不上。</summary>
        public override bool AcceptsPack => true;

        /// <summary>原型來源的目錄接不上：它的內容由目錄庫供應，步驟寫不進去。</summary>
        // 擋在拉線與落點，不是擋在執行：接得上卻什麼都不會發生是最難查的一種錯。
        // 目錄改成原型來源時，已經接上的這條線由編輯器當場斷開。
        public override bool AcceptsPackObject(GraphNodeContent pack)
            => pack is AssetCatalog catalog && catalog.AcceptsWrite;

        public override bool AcceptsBody(GraphNodeContent body) => false;

        public override bool AcceptsAsset(ScriptableObject asset) => false;

        public override bool AcceptsToken(GraphToken endpoint) => false;

        /// <summary>沒有常數模式，這一格永遠不取值。</summary>
        public override object DefaultObject { get => null; set { } }

        /// <summary>從產出接點拉到空白處就是要一顆目錄，沒有第二種選擇，不必再讓人選一次。</summary>
        public override GraphNodeContent CreateDefaultPack() => new AssetCatalog();

        /// <summary>接到的目錄；沒接、停用、內容不對或設成原型來源時回 null。</summary>
        // 原型來源的目錄擋在這裡而不是讓步驟寫進去再忽略：接得上卻什麼都不會發生是最難查的一種錯。
        public AssetCatalog Target
        {
            get
            {
                GraphNode node = _node;
                if (node == null || node.Disabled || node.Kind != NodeKind.Pack) return null;

                var catalog = node.PackObject as AssetCatalog;
                return catalog != null && catalog.AcceptsWrite ? catalog : null;
            }
        }
    }
}
