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
        private static GraphNode CatalogNode(out GraphNode cell, CatalogSource source = CatalogSource.Dynamic)
        {
            var node = new GraphNode();
            node.EnsureId();

            var catalog = new AssetCatalog { source = source };
            node.SetPack(catalog);

            cell = ((IGraphNodeOwner)catalog).CreateChild();
            cell.SetBody(new TestCell());
            catalog.SyncCells();
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
            AddStep(new WriteStep(catalog));
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains(OrderError));
        }

        [Test]
        public void Verify_FailsWhenWriterComesAfterReader()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell);
            AddStep(new ReadStep(cell));
            AddStep(new WriteStep(catalog));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        /// <summary>原型來源隨時都有內容，不受步驟順序影響。</summary>
        [Test]
        public void Verify_IgnoresOrderForPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out GraphNode cell, CatalogSource.Prototype);
            ((AssetCatalog)catalog.PackObject).prototypeCatalogId = "any-id";
            AddStep(new ReadStep(cell));

            List<string> errors = APGraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains(OrderError));
        }

        [Test]
        public void Verify_FailsWhenOutputSlotTakesPrototypeCatalog()
        {
            GraphNode catalog = CatalogNode(out _, CatalogSource.Prototype);
            ((AssetCatalog)catalog.PackObject).prototypeCatalogId = "any-id";
            AddStep(new WriteStep(catalog));

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

        /// <summary>最小的目錄格：原樣回傳母目錄的整包。</summary>
        // 測試自備格子而不是借用專案端那幾種：具體篩選公式住在使用端專案，測試組件看不到它們。
        [Serializable]
        private sealed class TestCell : Formula_ObjectList, ICatalogCell
        {
            [NonSerialized]
            private AssetCatalog owner;

            public AssetCatalog Owner { get => owner; set => owner = value; }

            public override List<Object> Evaluate() => new List<Object>(CatalogCell.Source(this));
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
