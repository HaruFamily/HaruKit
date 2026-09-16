using NUnit.Framework;
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    /// <summary>
    /// 動態目錄的時序規則：寫入它的動作必須排在讀取它的動作之前。
    /// </summary>
    // 這條是 AssetPipeline 獨有的，GraphKit 的具名Token沒有先後概念，所以測的是 GraphVerifier 而不是 HGValidator。
    public sealed class GraphVerifierTests
    {
        private const string OrderError = "寫入它的動作不在前面";

        private Graph graph;
        private ActionGroup root;

        [SetUp]
        public void SetUp()
        {
            graph = new Graph();
            root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
        }

        private void AddAction(ActionBase action)
        {
            root.Actions.Add(new ActionSlot(action));
        }

        /// <summary>建一顆目錄節點，並在底下開一格。寫入端接目錄，讀取端接那一格。</summary>
        // 目錄是包，一般欄位接不上它；讀取一律走格子，所以測試要把兩顆節點都建出來。
        // 節點放進候選池、不手動 SyncCells：格子的 Owner 該由驗證器自己接回去，那正是要測的路。
        private GraphNode CatalogNode(out GraphNode cell, CatalogSource source = CatalogSource.Dynamic)
        {
            var node = new GraphNode();
            node.EnsureId();

            var catalog = new AssetCatalog { source = source };
            node.SetCatalog(catalog);
            graph.Orphans.Add(node);

            cell = ((IGraphNodeOwner)catalog).CreateChild();
            return node;
        }

        /// <summary>把目錄接到一個寫入動作上。被欄位指到的節點會離開候選池，與編輯器一致。</summary>
        private void AddWriteAction(GraphNode catalog)
        {
            graph.Orphans.Remove(catalog);
            AddAction(new WriteAction(catalog));
        }

        /// <summary>給格子接上一顆篩選公式，回傳那顆公式的節點。</summary>
        private static GraphNode SetFilter(GraphNode cell, GraphNodeContent filter)
        {
            var node = new GraphNode(filter);
            node.EnsureId();
            ((CatalogCell)cell.BodyObject).InputSlot.SetNode(node);
            return node;
        }

        [Test]
        public void Verify_FailsWhenCatalogIsReadBeforeWrite()
        {
            CatalogNode(out GraphNode cell);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        [Test]
        public void Verify_PassesWhenCatalogIsWrittenInOrder()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            AddWriteAction(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains(OrderError));
        }

        [Test]
        public void Verify_FailsWhenWriterComesAfterReader()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            AddAction(new ReadAction(cell));
            AddWriteAction(catalog);

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        /// <summary>原型來源隨時都有內容，不受動作順序影響。</summary>
        // 這顆目錄沒有任何動作寫得進去，節點只在候選池裡：驗證器得自己走到那裡把格子接回母目錄，
        // 否則整條原型路徑會停在「沒有母目錄」，執行期也取不到內容。所以這裡斷言的是整份無錯。
        [Test]
        public void Verify_IgnoresOrderForPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell, CatalogSource.Prototype);
            ((AssetCatalog)catalog.CatalogObject).prototypeCatalogId = "any-id";
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Verify_FailsWhenOutputSlotTakesPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out _, CatalogSource.Prototype);
            ((AssetCatalog)catalog.CatalogObject).prototypeCatalogId = "any-id";
            AddWriteAction(catalog);

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("設為原型來源"));
        }

        [Test]
        public void Verify_FailsWhenPrototypeCatalogHasNoKey()
        {
            CatalogNode(out GraphNode cell, CatalogSource.Prototype);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有指定目錄"));
        }

        /// <summary>目錄是包，一般清單欄位接不上它——只有格子才是取值端點。</summary>
        [Test]
        public void Verify_FailsWhenOrdinaryFieldTakesTheCatalog()
        {
            GraphNode catalog = CatalogNode(out _);
            AddAction(new ReadAction(catalog));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("收不下包"));
        }

        [Test]
        public void Verify_FailsWhenActionSlotHasNoContent()
        {
            root.Actions.Add(new ActionSlot());

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有接任何動作內容"));
        }

        [Test]
        public void Verify_IgnoresDisabledAction()
        {
            var slot = new ActionSlot();
            slot.Disabled = true;
            root.Actions.Add(slot);

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        /// <summary>格子的結果型別跟著它接的篩選公式走，不再是整包目錄。</summary>
        [Test]
        public void Verify_FailsWhenFilteredCellDoesNotMatchTheField()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            SetFilter(cell, new CountFilter());
            AddWriteAction(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("型別不相容"));
        }

        /// <summary>停用篩選公式＝沒有篩選：格子回到整包目錄的型別，原本的欄位又接得上。</summary>
        [Test]
        public void Verify_PassesWhenFilterIsDisabled()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            SetFilter(cell, new CountFilter()).Disabled = true;
            AddWriteAction(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        /// <summary>母目錄已經被刪掉、只剩格子還被欄位指著：取不到內容，要當場擋下來。</summary>
        [Test]
        public void Verify_FailsWhenCellHasNoCatalog()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            graph.Orphans.Remove(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有母目錄"));
        }

        /// <summary>原型來源的目錄接不上產出格：拉線與落點都靠這一條擋在編輯當下。</summary>
        [Test]
        public void OutputSlot_RejectsPrototypeCatalog()
        {
            GraphNode written = CatalogNode(out _);
            GraphNode prototype = CatalogNode(out _, CatalogSource.Prototype);
            var slot = new CatalogOutputSlot();

            Assert.That(slot.AcceptsCatalogObject(written.CatalogObject), Is.True);
            Assert.That(slot.AcceptsCatalogObject(prototype.CatalogObject), Is.False);
            Assert.That(slot.AcceptsCatalogObject(null), Is.False);
        }

        /// <summary>最小的篩選公式：整包有幾個。</summary>
        // 測試自備一顆而不是借用專案端那幾種：具體篩法住在使用端專案，測試組件看不到它們。
        [Serializable]
        private sealed class CountFilter : PackedFormulaBase<int, List<Object>>
        {
            public override int Evaluate(List<Object> catalog) => catalog.Count;
        }

        /// <summary>只宣告「我把產出寫進這顆目錄」的假動作，不做任何事。</summary>
        [Serializable]
        private sealed class WriteAction : ActionBase
        {
            public CatalogOutputSlot output = new CatalogOutputSlot();

            public WriteAction(GraphNode catalog)
            {
                output.SetNode(catalog);
            }

            public override void Execute()
            {
            }
        }

        /// <summary>只宣告「我讀這顆節點」的假動作，不做任何事。</summary>
        [Serializable]
        private sealed class ReadAction : ActionBase
        {
            public FormulaAsset_ObjectList objects = new FormulaAsset_ObjectList();

            public ReadAction(GraphNode node)
            {
                objects.SetNode(node);
            }

            public override void Execute()
            {
            }
        }
    }
}
