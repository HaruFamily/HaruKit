using NUnit.Framework;
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
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
        private GraphNode CatalogNode(AssetCatalogBase catalog, out GraphNode cell)
        {
            var node = new GraphNode();
            node.EnsureId();

            node.SetCatalog(catalog);
            graph.Orphans.Add(node);

            cell = ((IGraphNodeOwner)catalog).CreateChild();
            return node;
        }

        private GraphNode DynamicCatalog(out GraphNode cell)
            => CatalogNode(new DynamicAssetCatalog(), out cell);

        private GraphNode PrototypeCatalog(out GraphNode cell, string catalogId = "any-id")
            => CatalogNode(new PrototypeAssetCatalog { catalogId = catalogId }, out cell);

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
            DynamicCatalog(out GraphNode cell);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        [Test]
        public void CollectDiagnostics_ReportsStableCodeAndFieldPath()
        {
            DynamicCatalog(out GraphNode cell);
            AddAction(new ReadAction(cell));

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            GraphDiagnostic diagnostic = diagnostics.Find(item =>
                item.Code == "assetpipeline.action.dynamic-catalog-read-before-write");
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
            Assert.That(diagnostic.Location.FieldPath, Is.EqualTo("動作[0]"));
        }

        [Test]
        public void Verify_PassesWhenCatalogIsWrittenInOrder()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            AddWriteAction(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.None.Contains(OrderError));
        }

        [Test]
        public void Verify_FailsWhenWriterComesAfterReader()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            AddAction(new ReadAction(cell));
            AddWriteAction(catalog);

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains(OrderError));
        }

        [Test]
        public void Verify_SequentialChildrenWriteThenReadWithoutAnOrphanCatalog()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            graph.Orphans.Remove(catalog);
            AddAction(new SequentialAction(new WriteAction(catalog), new ReadAction(cell)));

            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
        }

        [Test]
        public void Verify_SequentialChildrenReadThenWriteReportsTheChildPath()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            graph.Orphans.Remove(catalog);
            AddAction(new SequentialAction(new ReadAction(cell), new WriteAction(catalog)));

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("assetpipeline.action.dynamic-catalog-read-before-write"));
            Assert.That(diagnostics[0].Location.FieldPath, Is.EqualTo("動作[0].SequentialActions[0]"));
        }

        [Test]
        public void Verify_NestedSequencesShareOutputsWithSiblingsAndFollowingRoots()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            AddAction(new SequentialAction(
                new SequentialAction(new WriteAction(catalog)),
                new SequentialAction(new ReadAction(cell))));
            AddAction(new ReadAction(cell));

            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Verify_DisabledChildWriterDoesNotProduceCatalogs(bool disableNode)
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            var sequence = new SequentialAction(new WriteAction(catalog), new ReadAction(cell));
            if (disableNode) sequence.actions[0].Node.Disabled = true;
            else sequence.actions[0].Disabled = true;
            AddAction(sequence);

            Assert.That(GraphVerifier.Collect(graph), Has.Some.Contains(OrderError));
        }

        [Test]
        public void Verify_SequentialCyclesAreReportedWithoutRejectingRepeatedSharedChildren()
        {
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            var sequence = new SequentialAction(new WriteAction(catalog), new ReadAction(cell));
            sequence.actions.Add(sequence.actions[1]);
            AddAction(sequence);
            Assert.That(GraphVerifier.Collect(graph), Is.Empty);

            sequence.actions.Add(root.Actions[0]);
            Assert.That(GraphVerifier.CollectDiagnostics(graph).Exists(item => item.Code == "assetpipeline.node.cycle"), Is.True);
        }

        /// <summary>原型來源隨時都有內容，不受動作順序影響。</summary>
        // 這顆目錄沒有任何動作寫得進去，節點只在候選池裡：驗證器得自己走到那裡把格子接回母目錄，
        // 否則整條原型路徑會停在「沒有母目錄」，執行期也取不到內容。所以這裡斷言的是整份無錯。
        [Test]
        public void Verify_IgnoresOrderForPrototypeCatalog()
        {
            PrototypeCatalog(out GraphNode cell);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Verify_FailsWhenOutputSlotTakesPrototypeCatalog()
        {
            GraphNode catalog = PrototypeCatalog(out _);
            AddWriteAction(catalog);

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("只接得上動態目錄"));
        }

        [Test]
        public void Verify_FailsWhenPrototypeCatalogHasNoKey()
        {
            PrototypeCatalog(out GraphNode cell, string.Empty);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有指定目錄"));
        }

        /// <summary>目錄是包，一般清單欄位接不上它——只有格子才是取值端點。</summary>
        [Test]
        public void Verify_FailsWhenOrdinaryFieldTakesTheCatalog()
        {
            GraphNode catalog = DynamicCatalog(out _);
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
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
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
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
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
            GraphNode catalog = DynamicCatalog(out GraphNode cell);
            graph.Orphans.Remove(catalog);
            AddAction(new ReadAction(cell));

            List<string> errors = GraphVerifier.Collect(graph);

            Assert.That(errors, Has.Some.Contains("沒有母目錄"));
        }

        /// <summary>原型來源的目錄接不上產出格：拉線與落點都靠這一條擋在編輯當下。</summary>
        [Test]
        public void OutputSlot_RejectsPrototypeCatalog()
        {
            GraphNode written = DynamicCatalog(out _);
            GraphNode prototype = PrototypeCatalog(out _);
            var slot = new CatalogOutputSlot();

            Assert.That(slot.WritesToCatalog, Is.True);
            Assert.That(slot.AcceptsCatalogObject(written.CatalogObject), Is.True);
            Assert.That(slot.AcceptsCatalogObject(prototype.CatalogObject), Is.False);
            Assert.That(slot.AcceptsCatalogObject(null), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CatalogSourceReplacement_PreservesCellsFiltersAndCarrier(bool toDynamic)
        {
            GraphNode carrier = toDynamic ? PrototypeCatalog(out _) : DynamicCatalog(out _);
            var original = (AssetCatalogBase)carrier.CatalogObject;
            var first = original.Cells[0];
            var second = ((IGraphNodeOwner)original).CreateChild();
            var filter = SetFilter(first, new CountFilter());
            original.SyncCells();
            var downstream = new ReadAction(second);
            carrier.Pos = new UnityEngine.Vector2(80f, 120f);
            carrier.Note = "保留備註";
            carrier.Disabled = true;
            string id = carrier.Id;
            string cellId = first.Id;
            // 其他目錄的寫入端不應阻擋這顆的替換。
            var unrelated = new CatalogOutputSlot();
            unrelated.SetNode(DynamicCatalog(out _));

            bool changed = ((IHGCatalogSourceSelector)original).TryReplaceSource(carrier,
                toDynamic ? typeof(DynamicAssetCatalog) : typeof(PrototypeAssetCatalog),
                new GraphSlotBase[] { downstream.objects, unrelated }, out string error);

            Assert.That(changed, Is.True, error);
            var replacement = (AssetCatalogBase)carrier.CatalogObject;
            Assert.That(replacement.GetType(), Is.EqualTo(toDynamic ? typeof(DynamicAssetCatalog) : typeof(PrototypeAssetCatalog)));
            Assert.That(replacement.Cells, Is.Not.SameAs(original.Cells));
            Assert.That(replacement.Cells.Count, Is.EqualTo(2));
            Assert.That(replacement.Cells[0], Is.SameAs(first));
            Assert.That(replacement.Cells[1], Is.SameAs(second));
            Assert.That(downstream.objects.Node, Is.SameAs(second));
            Assert.That(((CatalogCell)first.BodyObject).InputSlot.Node, Is.SameAs(filter));
            Assert.That(((CatalogCell)first.BodyObject).Owner, Is.SameAs(replacement));
            Assert.That(((CatalogCell)second.BodyObject).Owner, Is.SameAs(replacement));
            Assert.That(first.Id, Is.EqualTo(cellId));
            Assert.That(carrier.Id, Is.EqualTo(id));
            Assert.That(carrier.Pos, Is.EqualTo(new UnityEngine.Vector2(80f, 120f)));
            Assert.That(carrier.Note, Is.EqualTo("保留備註"));
            Assert.That(carrier.Disabled, Is.True);
            if (replacement is PrototypeAssetCatalog prototype) Assert.That(prototype.catalogId, Is.Empty);
            else Assert.That(replacement.Read(), Is.Empty);
        }

        [Test]
        public void CatalogSourceReplacement_RejectsWriterWithoutChangingAnyReferences()
        {
            GraphNode carrier = DynamicCatalog(out GraphNode cell);
            var original = (DynamicAssetCatalog)carrier.CatalogObject;
            original.SyncCells();
            var filter = SetFilter(cell, new CountFilter());
            var firstWriter = new CatalogOutputSlot();
            var secondWriter = new CatalogOutputSlot();
            firstWriter.SetNode(carrier);
            secondWriter.SetNode(carrier);
            carrier.Disabled = true;

            bool changed = ((IHGCatalogSourceSelector)original).TryReplaceSource(carrier,
                typeof(PrototypeAssetCatalog), new GraphSlotBase[] { firstWriter, secondWriter }, out string error);

            Assert.That(changed, Is.False);
            Assert.That(error, Does.Contain("先解除產出連線"));
            Assert.That(carrier.CatalogObject, Is.SameAs(original));
            Assert.That(original.Cells[0], Is.SameAs(cell));
            Assert.That(((CatalogCell)cell.BodyObject).Owner, Is.SameAs(original));
            Assert.That(((CatalogCell)cell.BodyObject).InputSlot.Node, Is.SameAs(filter));
            Assert.That(firstWriter.Node, Is.SameAs(carrier));
            Assert.That(secondWriter.Node, Is.SameAs(carrier));
        }

        [Test]
        public void CatalogSourceReplacement_RejectsStaleSelector()
        {
            GraphNode carrier = PrototypeCatalog(out _);
            var stale = (IHGCatalogSourceSelector)carrier.CatalogObject;
            Assert.That(stale.TryReplaceSource(carrier, typeof(DynamicAssetCatalog),
                Array.Empty<GraphSlotBase>(), out _), Is.True);
            var current = carrier.CatalogObject;

            Assert.That(stale.TryReplaceSource(carrier, typeof(PrototypeAssetCatalog),
                Array.Empty<GraphSlotBase>(), out _), Is.False);
            Assert.That(carrier.CatalogObject, Is.SameAs(current));
        }

        [Test]
        public void CatalogReferenceResultPreservesNullInsteadOfReturningTheWholeListOrDefault()
        {
            var cell = new CatalogCell();
            cell.InputSlot.SetNode(new GraphNode(new EmptyObjectFilter()));
            var slot = new ObjectSlot();
            var fallback = new UnityEngine.GameObject("Fallback");
            try
            {
                slot.Default = fallback;
                slot.SetNode(new GraphNode(cell));
                Assert.That(cell.EvaluateObject(), Is.Null);
                Assert.That(slot.Evaluate(), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(fallback); }
        }

        [Test]
        public void FormulaFamiliesKeepNullPackAndCatalogPackSeparate()
        {
            var ordinary = new ConstantIntFormula();
            var packed = new CountFilter();
            var input = new IntSlot();
            var filter = new CatalogFormulaSlot();

            Assert.That(input.AcceptsBody(ordinary), Is.True);
            Assert.That(input.AcceptsBody(packed), Is.False);
            Assert.That(filter.AcceptsBody(ordinary), Is.False);
            Assert.That(filter.AcceptsBody(packed), Is.True);
            input.SetNode(new GraphNode(ordinary));
            Assert.That(input.Evaluate(), Is.EqualTo(7));
            Assert.That(ordinary.ReceivedNull, Is.True);
            filter.SetNode(new GraphNode(packed));
            Assert.That(filter.Evaluate(new List<Object> { null, null }), Is.EqualTo(2));
        }

        [Test]
        public void RenamedIntSlotLoadsItsPreviousManagedReferenceName()
        {
            var input = new IntSlot(42);
            input.SetNode(new GraphNode(new ConstantIntFormula()));
            string json = UnityEngine.JsonUtility.ToJson(new SlotEnvelope { slot = input });
            Assert.That(json, Does.Contain("\"class\":\"IntSlot\""));
            string previous = json.Replace("\"class\":\"IntSlot\"", "\"class\":\"FormulaAsset_Int\"");

            var restored = UnityEngine.JsonUtility.FromJson<SlotEnvelope>(previous);

            Assert.That(restored.slot, Is.TypeOf<IntSlot>());
            var result = (IntSlot)restored.slot;
            Assert.That(result.Default, Is.EqualTo(42));
            Assert.That(result.Evaluate(), Is.EqualTo(7));
        }

        [Serializable]
        private sealed class SlotEnvelope
        {
            [UnityEngine.SerializeReference] public GraphSlotBase slot;
        }

        [Test]
        public void TypedObjectListFormulaKeepsOneResultForOrdinaryAndCatalogOutput()
        {
            var asset = new UnityEngine.GameObject("Typed result");
            try
            {
                var ordinary = new ConstantGameObjects { value = asset };
                var carrier = new GraphNode(ordinary);
                Assert.That(HGModel.CarrierResultType(carrier), Is.EqualTo(typeof(List<UnityEngine.GameObject>)));
                var typedInput = new GameObjectListSlot();
                var objectInput = new ObjectListSlot();
                Assert.That(typedInput.AcceptsBody(ordinary), Is.True);
                Assert.That(objectInput.AcceptsBody(ordinary), Is.False);
                typedInput.SetNode(carrier);
                Assert.That(typedInput.Evaluate(), Is.EqualTo(new[] { asset }));
                Assert.That(HGReflect.FormulaResultType(ordinary.GetType()), Is.EqualTo(typeof(List<UnityEngine.GameObject>)));

                var catalog = new DynamicAssetCatalog();
                catalog.Write(new Object[] { asset }, true);
                var cellNode = ((IGraphNodeOwner)catalog).CreateChild();
                catalog.SyncCells();
                var cell = (CatalogCell)cellNode.BodyObject;
                cell.InputSlot.SetNode(new GraphNode(new GameObjectsFilter()));
                Assert.That(cell.ResultType, Is.EqualTo(typeof(List<UnityEngine.GameObject>)));
                Assert.That(typedInput.AcceptsBody(cell), Is.True);
                Assert.That(objectInput.AcceptsBody(cell), Is.False);
                typedInput.SetNode(cellNode);
                Assert.That(typedInput.Evaluate(), Is.EqualTo(new[] { asset }));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Serializable]
        private sealed class ConstantIntFormula : Formula_Int<NullPack>
        {
            [NonSerialized] public bool ReceivedNull;
            protected override int OnEvaluate(NullPack pack) { ReceivedNull = pack == null; return 7; }
        }

        [Serializable]
        private sealed class ConstantGameObjects : Formula_GameObjectList<NullPack>
        {
            public UnityEngine.GameObject value;
            protected override List<UnityEngine.GameObject> OnEvaluate(NullPack pack) => new() { value };
        }

        [Serializable]
        private sealed class GameObjectsFilter : Formula_GameObjectList<List<Object>>
        {
            protected override List<UnityEngine.GameObject> OnEvaluate(List<Object> pack)
            {
                var result = new List<UnityEngine.GameObject>();
                foreach (Object asset in pack)
                    if (asset is UnityEngine.GameObject gameObject) result.Add(gameObject);
                return result;
            }
        }

        [Serializable]
        private sealed class EmptyObjectFilter : Formula_Object<List<Object>>
        {
            protected override Object OnEvaluate(List<Object> catalog) => null;
        }

        /// <summary>最小的篩選公式：整包有幾個。</summary>
        // 測試自備一顆而不是借用專案端那幾種：具體篩法住在使用端專案，測試組件看不到它們。
        [Serializable]
        private sealed class CountFilter : Formula_Int<List<Object>>
        {
            protected override int OnEvaluate(List<Object> catalog) => catalog.Count;
        }

        [Serializable]
        private sealed class SequentialAction : ActionBase, ISequentialActionContainer
        {
            public List<ActionSlot> actions = new();
            public IReadOnlyList<ActionSlotBase> SequentialActions => actions;

            public SequentialAction(params ActionBase[] children)
            {
                foreach (ActionBase child in children) actions.Add(new ActionSlot(child));
            }

            protected override void OnExecute(PipelineActionContext context)
            {
                foreach (ActionSlot child in actions)
                {
                    child.Execute(context);
                    if (context.Result.HasFailure) return;
                }
            }
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

            protected override void OnExecute(PipelineActionContext context)
            {
            }
        }

        /// <summary>只宣告「我讀這顆節點」的假動作，不做任何事。</summary>
        [Serializable]
        private sealed class ReadAction : ActionBase
        {
            public ObjectListSlot objects = new ObjectListSlot();

            public ReadAction(GraphNode node)
            {
                objects.SetNode(node);
            }

            protected override void OnExecute(PipelineActionContext context)
            {
            }
        }
    }
}
