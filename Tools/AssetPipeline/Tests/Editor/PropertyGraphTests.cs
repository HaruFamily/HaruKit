using NUnit.Framework;
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    /// <summary>
    /// Property 的讀寫語意與驗證規則：寫入替換、未寫入回初始內容、族不符拒絕、寫回同一顆不算循環。
    /// </summary>
    // 測的是 GraphVerifier 與 Slot 的接線，不涉及資產寫入，所以不建交易也不碰 AssetDatabase。
    public sealed class PropertyGraphTests
    {
        private Graph graph;
        private ActionGroup root;

        [SetUp]
        public void SetUp()
        {
            graph = new Graph();
            root = (ActionGroup)((IGraphDocument)graph).AddRoot(Graph.PipelineKey);
        }

        /// <summary>建一顆 Property 定義與指著它的節點。節點先放候選池，由呼叫端決定接到哪個欄位。</summary>
        private GraphNode PropertyNode(out GraphProperty property, bool proto = false, string name = "Objects")
        {
            property = new GraphProperty(name, new ObjectListSlot(), proto);
            property.EnsureId();
            if (proto) graph.Properties.Add(property);

            var node = new GraphNode();
            node.EnsureId();
            node.SetProperty(property);
            graph.Orphans.Add(node);
            return node;
        }

        private void AddAction(ActionBase action) => root.Actions.Add(new ActionSlot(action));

        [Test]
        public void Write_ReplacesCurrentValue_AndReaderSeesIt()
        {
            GraphNode node = PropertyNode(out GraphProperty property);
            var writer = new WriteAction(node);
            var reader = new ReadAction(node);

            var first = new List<Object>();
            var second = new List<Object>();

            writer.output.Write(first);
            Assert.AreSame(first, reader.objects.Evaluate(), "讀取端應取得剛寫入的那一份引用。");

            writer.output.Write(second);
            Assert.AreSame(second, reader.objects.Evaluate(), "第二次寫入應替換目前值，不追加也不合併。");
            Assert.AreSame(second, property.CurrentValue);
        }

        [Test]
        public void PlainProperty_WithoutWrite_ReadsNull()
        {
            GraphNode node = PropertyNode(out GraphProperty property);
            var reader = new ReadAction(node);

            Assert.IsFalse(property.HasValue);
            Assert.IsNull(reader.objects.Evaluate(), "一般 Property 未寫入應回 default(T)，清單不自動 new。");
        }

        [Test]
        public void GenericPropertySlot_DeepCopyPreservesConnectionAndIsolatesLocalProperty()
        {
            GraphNode node = PropertyNode(out GraphProperty property);
            var writer = new WriteAction(node);
            var copy = GraphDeepCopy.Copy(writer);

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.output, Is.TypeOf<PropertySlot<List<Object>, ObjectListSlot>>());
            Assert.That(copy.output.Target, Is.Not.Null);
            Assert.That(copy.output.Target, Is.Not.SameAs(property));
            Assert.That(copy.output.FamilyType, Is.EqualTo(typeof(ObjectListSlot)));
            var value = new List<Object>();
            Assert.That(copy.output.Write(value), Is.True);
            Assert.That(copy.output.Target.CurrentValue, Is.SameAs(value));
            Assert.That(property.HasValue, Is.False);
            AddAction(copy);
            Assert.That(GraphVerifier.Collect(graph), Is.Empty);
        }

        [Test]
        public void ProtoProperty_WithoutWrite_ReadsInitialContent()
        {
            var initial = new List<Object>();
            GraphNode node = PropertyNode(out GraphProperty property, proto: true);
            ((ObjectListSlot)property.Slot).Default = initial;

            var reader = new ReadAction(node);

            Assert.AreSame(initial, reader.objects.Evaluate(), "ProtoProperty 不必先 Set 就讀得到初始內容。");

            // Set 之後回到目前值，且不回寫初始設定。
            var written = new List<Object>();
            property.SetValue(written);
            Assert.AreSame(written, reader.objects.Evaluate());
            Assert.AreSame(initial, ((ObjectListSlot)property.Slot).Default);

            // 明確初始化回到當時的初始內容。
            property.Initialize();
            Assert.AreSame(initial, reader.objects.Evaluate());
        }

        [Test]
        public void CaptureAndRestore_ReturnsToTheUnwrittenState()
        {
            GraphNode node = PropertyNode(out GraphProperty property);
            var reader = new ReadAction(node);

            (object value, bool written) before = property.CaptureValue();
            property.SetValue(new List<Object>());
            Assert.IsTrue(property.HasValue);

            property.RestoreValue(before);

            Assert.IsFalse(property.HasValue, "回復後應退回未寫入狀態，不是留著一個空清單。");
            Assert.IsNull(reader.objects.Evaluate());
        }

        [Test]
        public void ReadThenWriteBack_SameProperty_IsNotACycle()
        {
            GraphNode node = PropertyNode(out _);
            AddAction(new ReadWriteAction(node));

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            Assert.IsEmpty(diagnostics, Describe(diagnostics));
        }

        [Test]
        public void PropertyInput_DoesNotOverwriteSharedDefinitionOrEvaluateOnRead()
        {
            var sourceValue = new List<Object>();
            GraphNode source = new GraphNode(new ListFormula(sourceValue));
            GraphNode propertyNode = PropertyNode(out GraphProperty property, proto: true);
            propertyNode.SetPropertyInput(source);

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            CollectionAssert.DoesNotContain(Codes(diagnostics), "assetpipeline.property.slot-linked");
            Assert.IsNull(property.Slot.Node);
            // ProtoProperty 未寫入時回它自己的初始內容；來源公式不求值，所以拿到的不會是來源那一份。
            // 不可斷言 null：Proto 的初始內容就是型別欄位的常數，ObjectListSlot 的常數是一個空清單。
            List<Object> read = new ReadAction(propertyNode).objects.Evaluate();
            Assert.AreSame(property.InitialValue, read);
            Assert.AreNotSame(sourceValue, read);
            var second = new GraphNode();
            second.SetProtoProperty(property);
            second.Clear();
            Assert.IsNull(property.Slot.Node);
        }

        [Test]
        public void IncompatibleFamily_IsRejectedEvenWhenTheResultTypeWouldFit()
        {
            GraphNode node = PropertyNode(out _);   // ObjectListSlot 族
            AddAction(new IntReadAction(node));     // IntSlot 族

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            CollectionAssert.Contains(Codes(diagnostics), "assetpipeline.property.target-incompatible");
        }

        [Test]
        public void DuplicateNameInSameFamily_IsRejected()
        {
            PropertyNode(out _, proto: true, name: "Objects");
            PropertyNode(out _, proto: true, name: "Objects");

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            CollectionAssert.Contains(Codes(diagnostics), "assetpipeline.property.duplicate");
        }

        [Test]
        public void PlainProperty_WithoutName_IsValid()
        {
            PropertyNode(out GraphProperty property, name: null);

            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(graph);

            CollectionAssert.DoesNotContain(Codes(diagnostics), "assetpipeline.property.name-missing");
        }

        [Test]
        public void LocalProperty_IsNotAddedToTheDocumentDefinitions()
        {
            PropertyNode(out GraphProperty property);

            Assert.IsEmpty(graph.Properties);
            Assert.IsFalse(property.Proto);
        }

        [Test]
        public void ProtoProperty_WithoutKey_PreservesThePropertyNode()
        {
            var node = new GraphNode();

            node.SetProtoProperty(null);

            Assert.AreEqual(NodeKind.Property, node.Kind);
            Assert.IsTrue(node.IsProtoProperty);
            Assert.IsNull(node.Property);
        }

        private static List<string> Codes(List<GraphDiagnostic> diagnostics)
        {
            var codes = new List<string>(diagnostics.Count);
            foreach (GraphDiagnostic diagnostic in diagnostics) codes.Add(diagnostic.Code);
            return codes;
        }

        private static string Describe(List<GraphDiagnostic> diagnostics)
        {
            if (diagnostics.Count == 0) return string.Empty;

            var text = new System.Text.StringBuilder("預期沒有診斷，實際收到：");
            foreach (GraphDiagnostic diagnostic in diagnostics) text.Append('\n').Append(diagnostic.Message);
            return text.ToString();
        }

        /// <summary>只宣告「我寫這顆 Property」的假動作。</summary>
        [Serializable]
        private sealed class WriteAction : ActionBase
        {
            public PropertySlot<List<Object>, ObjectListSlot> output = new();

            public WriteAction(GraphNode node) => output.SetNode(node);

            public bool Write(List<Object> value) => output.Write(value);

            protected override void OnExecute(PipelineActionContext context) { }
        }

        /// <summary>讀取端刻意用另一族：族不同就不該接得上，不看結果型別。</summary>
        [Serializable]
        private sealed class IntReadAction : ActionBase
        {
            public IntSlot value = new IntSlot();

            public IntReadAction(GraphNode node) => value.SetNode(node);

            protected override void OnExecute(PipelineActionContext context) { }
        }

        /// <summary>只宣告「我讀這顆 Property」的假動作。</summary>
        [Serializable]
        private sealed class ReadAction : ActionBase
        {
            public ObjectListSlot objects = new ObjectListSlot();

            public ReadAction(GraphNode node) => objects.SetNode(node);

            protected override void OnExecute(PipelineActionContext context) { }
        }

        [Serializable]
        private sealed class ListFormula : Formula_ObjectList<NullPack>
        {
            public List<Object> Value;

            public ListFormula(List<Object> value) => Value = value;

            protected override List<Object> OnEvaluate(NullPack pack) => Value;
        }

        /// <summary>同一顆 Property 既讀又寫：圖上成一圈，但不是求值依賴。</summary>
        [Serializable]
        private sealed class ReadWriteAction : ActionBase
        {
            public ObjectListSlot objects = new ObjectListSlot();
            public PropertySlot<List<Object>, ObjectListSlot> output = new();

            public ReadWriteAction(GraphNode node)
            {
                objects.SetNode(node);
                output.SetNode(node);
            }

            protected override void OnExecute(PipelineActionContext context) { }
        }
    }
}
