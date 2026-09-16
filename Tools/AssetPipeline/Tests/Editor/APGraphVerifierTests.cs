using NUnit.Framework;
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    /// <summary>
    /// 動態目錄的時序規則：寫入它的步驟必須排在讀取它的步驟之前。
    /// </summary>
    // 這條是 AssetPipeline 獨有的，GraphKit 的具名Token沒有先後概念，所以測的是 APGraphVerifier 而不是 HGValidator。
    public sealed class APGraphVerifierTests
    {
        private const string OrderError = "寫入它的步驟不在前面";

        private APGraph graph;
        private APStepGroup root;

        [SetUp]
        public void SetUp()
        {
            graph = new APGraph();
            root = (APStepGroup)((IGraphDocument)graph).AddRoot(APGraph.PipelineKey);
        }

        private void AddStep(APActionBase step)
        {
            root.Steps.Add(new APActionSlot(step));
        }

        /// <summary>建一顆目錄節點，並在底下開一格。寫入端接目錄，讀取端接那一格。</summary>
        // 目錄是包，一般欄位接不上它；讀取一律走格子，所以測試要把兩顆節點都建出來。
        // 節點放進候選池、不手動 SyncCells：格子的 Owner 該由驗證器自己接回去，那正是要測的路。
        private GraphNode CatalogNode(out GraphNode cell, CatalogSource source = CatalogSource.Dynamic)
        {
            var node = new GraphNode();
            node.EnsureId();

            var catalog = new AssetCatalog { source = source };
            node.SetPack(catalog);
            graph.Orphans.Add(node);

            cell = ((IGraphNodeOwner)catalog).CreateChild();
            return node;
        }

        /// <summary>把目錄接到一個寫入步驟上。被欄位指到的節點會離開候選池，與編輯器一致。</summary>
        private void AddWriteStep(GraphNode catalog)
        {
            graph.Orphans.Remove(catalog);
            AddStep(new WriteStep(catalog));
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
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        [Test]
        public void Verify_PassesWhenCatalogIsWrittenInOrder()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            AddWriteStep(catalog);
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains(OrderError));
        }

        [Test]
        public void Verify_FailsWhenWriterComesAfterReader()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            AddStep(new ReadStep(cell));
            AddWriteStep(catalog);

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        /// <summary>原型來源隨時都有內容，不受步驟順序影響。</summary>
        // 這顆目錄沒有任何步驟寫得進去，節點只在候選池裡：驗證器得自己走到那裡把格子接回母目錄，
        // 否則整條原型路徑會停在「沒有母目錄」，執行期也取不到內容。所以這裡斷言的是整份無錯。
        [Test]
        public void Verify_IgnoresOrderForPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell, CatalogSource.Prototype);
            ((AssetCatalog)catalog.PackObject).prototypeCatalogId = "any-id";
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Verify_FailsWhenOutputSlotTakesPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out _, CatalogSource.Prototype);
            ((AssetCatalog)catalog.PackObject).prototypeCatalogId = "any-id";
            AddWriteStep(catalog);

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("設為原型來源"));
        }

        [Test]
        public void Verify_FailsWhenPrototypeCatalogHasNoKey()
        {
            CatalogNode(out GraphNode cell, CatalogSource.Prototype);
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有指定目錄"));
        }

        /// <summary>目錄是包，一般清單欄位接不上它——只有格子才是取值端點。</summary>
        [Test]
        public void Verify_FailsWhenOrdinaryFieldTakesTheCatalog()
        {
            GraphNode catalog = CatalogNode(out _);
            AddStep(new ReadStep(catalog));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("收不下包"));
        }

        [Test]
        public void Verify_FailsWhenStepSlotHasNoContent()
        {
            root.Steps.Add(new APActionSlot());

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有接任何步驟內容"));
        }

        [Test]
        public void Verify_IgnoresDisabledStep()
        {
            var slot = new APActionSlot();
            slot.Disabled = true;
            root.Steps.Add(slot);

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        /// <summary>格子的結果型別跟著它接的篩選公式走，不再是整包目錄。</summary>
        [Test]
        public void Verify_FailsWhenFilteredCellDoesNotMatchTheField()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            SetFilter(cell, new CountFilter());
            AddWriteStep(catalog);
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("型別不相容"));
        }

        /// <summary>停用篩選公式＝沒有篩選：格子回到整包目錄的型別，原本的欄位又接得上。</summary>
        [Test]
        public void Verify_PassesWhenFilterIsDisabled()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            SetFilter(cell, new CountFilter()).Disabled = true;
            AddWriteStep(catalog);
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        /// <summary>母目錄已經被刪掉、只剩格子還被欄位指著：取不到內容，要當場擋下來。</summary>
        [Test]
        public void Verify_FailsWhenCellHasNoCatalog()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            graph.Orphans.Remove(catalog);
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有母目錄"));
        }

        /// <summary>原型來源的目錄接不上產出格：拉線與落點都靠這一條擋在編輯當下。</summary>
        [Test]
        public void OutputSlot_RejectsPrototypeCatalog()
        {
            GraphNode written = CatalogNode(out _);
            GraphNode prototype = CatalogNode(out _, CatalogSource.Prototype);
            var slot = new APCatalogOutputSlot();

            Assert.That(slot.AcceptsPackObject(written.PackObject), Is.True);
            Assert.That(slot.AcceptsPackObject(prototype.PackObject), Is.False);
            Assert.That(slot.AcceptsPackObject(null), Is.False);
        }

        /// <summary>最小的篩選公式：整包有幾個。</summary>
        // 測試自備一顆而不是借用專案端那幾種：具體篩法住在使用端專案，測試組件看不到它們。
        [Serializable]
        private sealed class CountFilter : CatalogFormulaBase<int>
        {
            public override int Evaluate(List<Object> catalog) => catalog.Count;
        }

        /// <summary>只宣告「我把產出寫進這顆目錄」的假步驟，不做任何事。</summary>
        [Serializable]
        private sealed class WriteStep : APActionBase
        {
            public APCatalogOutputSlot output = new APCatalogOutputSlot();

            public WriteStep(GraphNode catalog)
            {
                output.SetNode(catalog);
            }

            public override void Execute()
            {
            }
        }

        /// <summary>只宣告「我讀這顆節點」的假步驟，不做任何事。</summary>
        [Serializable]
        private sealed class ReadStep : APActionBase
        {
            public FormulaAsset_ObjectList objects = new FormulaAsset_ObjectList();

            public ReadStep(GraphNode node)
            {
                objects.SetNode(node);
            }

            public override void Execute()
            {
            }
        }
    }
}
