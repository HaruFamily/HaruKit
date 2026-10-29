namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

public class HGPortTests
{
    [Test]
    public void ScriptLocationDistinguishesSameNamedTypesAndIgnoresComments()
    {
        string source = "namespace Other { public class HGPortTests {} }\n"
            + "// namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests { class HGPortTests {} }\n"
            + "namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests\n{\n"
            + "    // class HGPortTests {}\n"
            + "    public class HGPortTests<T> {}\n"
            + "    public class HGPortTests {}\n}";

        Assert.That(HGScriptLocator.FindDeclarationLine(source, typeof(HGPortTests)), Is.EqualTo(7));
        Assert.That(HGScriptLocator.FindDeclarationLine(
            "namespace Other { public class HGPortTests {} }", typeof(HGPortTests)), Is.Zero);
    }

    [Test]
    public void ScriptLocationResolvesFileScopedAndNestedNamespacesAndTypes()
    {
        string fileScoped = "namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests;\n"
            + "class Other { class WriteTargetSlot {} }\n"
            + "class HGPortTests\n{\n"
            + "    const string sample = @\"} class WriteTargetSlot {}\";\n"
            + "    private class WriteTargetSlot {}\n}";
        string nested = "namespace HaruFamily\n{\n"
            + "namespace DependencyCore.GraphKit.Editor.Tests\n{\n"
            + "class HGPortTests\n{\n"
            + "    private class WriteTargetSlot {}\n}\n}\n}";

        Assert.That(HGScriptLocator.FindDeclarationLine(fileScoped, typeof(WriteTargetSlot)), Is.EqualTo(6));
        Assert.That(HGScriptLocator.FindDeclarationLine(nested, typeof(WriteTargetSlot)), Is.EqualTo(7));
    }

    private sealed class WriteTargetSlot : PropertySlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override Type FamilyType => typeof(TestFormulaSlot);
    }

    [Test]
    public void PropertyWriterConnectsToLowerInputAndRebuildsWithoutUsingHeader()
    {
        var property = new GraphNode();
        property.SetProperty(new GraphProperty("Value", new TestFormulaSlot()));
        var writer = new WriteTargetSlot();
        writer.SetNode(property);
        var source = new HGPropertyWriteSource(writer, new GraphNode());
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 4);
        var writerKey = new HGPortKey("action", "/output", HGPortRole.Output);
        var inputKey = new HGPortKey("property", "/property/input", HGPortRole.Input);
        var headerKey = new HGPortKey("property", "", HGPortRole.Output);
        Assert.That(build.AddOutput(writerKey, source, new HGDelegatePortPolicy(() => true), Presentation(new object())), Is.True);
        Assert.That(build.AddInput(inputKey, property.PropertyInput,
            new HGDelegatePortPolicy(() => true, checkAcceptance: candidate =>
                candidate is HGPropertyWriteSource write ? write.CheckTarget(property) : HGPortConnectionResult.IncompatibleType),
            Presentation(new object())), Is.True);
        Assert.That(build.AddOutput(headerKey, new HGDelegatePortSource(property, null, _ => true),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), true), Is.True);

        var output = graph.PortsByKey[writerKey];
        var input = graph.PortsByKey[inputKey];
        Assert.That(HGPortConnection.Check(output, input, 4), Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(HGPortConnection.Check(output, graph.PortsByKey[headerKey], 4), Is.EqualTo(HGPortConnectionResult.SameRole));
        var link = new HGLink
        {
            ParentRow = new HGRow { OwnerNodeId = "action", Path = "/output", InputSlot = writer },
            OutputOwner = new HGNodeView { Id = "property", Carrier = property },
        };
        Assert.That(HGLinkPortResolver.Resolve(link, graph.PortsByKey, graph.PrimaryOutputs, 4, graph.PrimaryInputs),
            Is.EqualTo(HGLinkPortResolution.Resolved));
        Assert.That(link.InputPort, Is.SameAs(input));
        Assert.That(link.OutputPort, Is.SameAs(output));
        property.SetProperty(null);
        Assert.That(input.Presentation.Visible, Is.True);
        Assert.That(HGPortConnection.Check(output, input, 4), Is.EqualTo(HGPortConnectionResult.MissingBinding));
    }

    [Test]
    public void CreatingAPropertyRequiresATypeAndRegistersOneLibraryDefinition()
    {
        var model = new HGModel();
        var library = new List<GraphProperty>();
        Assert.That(model.CreateProperty(library, typeof(string), out var invalidTypeError), Is.Null);
        Assert.That(invalidTypeError, Is.Not.Null.And.Not.Empty);
        Assert.That(model.CreateProperty(library, null, out _), Is.Null);
        Assert.That(model.CreateProperty(library, typeof(FormulaSlotBase), out _), Is.Null);
        Assert.That(library, Is.Empty);
        var property = model.CreateProperty(library, typeof(TestFormulaSlot), out var error);
        Assert.That(error, Is.Null);
        Assert.That(library, Is.EqualTo(new[] { property }));
        Assert.That(property.RetainValue, Is.False);
    }

    [Test]
    public void ResettingNodeIdsDoesNotChangePropertyDefinitionIds()
    {
        var local = new GraphProperty(null, new TestFormulaSlot());
        var proto = new GraphProperty("Shared", new TestFormulaSlot());
        string localId = local.EnsureId();
        string protoId = proto.EnsureId();
        var localNode = new GraphNode();
        localNode.SetProperty(local);
        var protoNode = new GraphNode();
        protoNode.SetProperty(proto);

        HGModel.ResetNodeIds(localNode);
        HGModel.ResetNodeIds(protoNode);

        Assert.That(local.Id, Is.EqualTo(localId), "庫定義的識別碼不隨節點重設。");
        Assert.That(proto.Id, Is.EqualTo(protoId), "Property 是共用的庫定義，複製節點不可改到它。");
    }

    [Test]
    public void DuplicatingATokenKeepsThePropertyDefinitionShared()
    {
        var proto = new GraphProperty("Shared", new TestFormulaSlot());
        proto.EnsureId();
        var propertyNode = new GraphNode();
        propertyNode.SetProperty(proto);
        var reader = new TestFormulaSlot();
        reader.SetNode(propertyNode);
        var token = new GraphToken("Source", reader);
        var scope = new List<GraphToken> { token };

        GraphToken copy = new HGModel().DuplicateToken(token, scope, out string error);

        Assert.That(error, Is.Null);
        Assert.That(copy?.Slot?.Node, Is.Not.Null);
        Assert.That(copy.Slot.Node, Is.Not.SameAs(propertyNode), "載體本身仍要複製。");
        Assert.That(copy.Slot.Node.Property, Is.SameAs(proto), "庫定義必須沿用，不可抄出不在庫裡的第二份。");
    }

    [Test]
    public void ExtractedAssetCopiesDefinitionsOnceAndPreservesInputAndOriginalLibrary()
    {
        var original = new GraphProperty("Shared", new TestFormulaSlot()) { RetainValue = true };
        original.EnsureId();
        var first = new GraphNode();
        var second = new GraphNode();
        first.SetProperty(original);
        second.SetProperty(original);
        var input = new GraphNode();
        first.SetPropertyInput(input);
        var library = new List<GraphProperty>();

        HaruGraphWindow.CopyPropertyDefinitions(new[] { first, second }, library);

        Assert.That(library, Has.Count.EqualTo(1));
        Assert.That(first.Property, Is.SameAs(library[0]).And.SameAs(second.Property));
        Assert.That(first.Property, Is.Not.SameAs(original));
        Assert.That(first.Property.Id, Is.Not.EqualTo(original.Id));
        Assert.That(first.Property.RetainValue, Is.True);
        Assert.That(first.PropertyInput.Node, Is.SameAs(input));
        Assert.That(original.Name, Is.EqualTo("Shared"));
    }

    [Test]
    public void DuplicatingAPropertyCreatesANewStorageWithAFreshIdAndName()
    {
        var source = new GraphProperty("Shared", new TestFormulaSlot());
        source.EnsureId();
        var scope = new List<GraphProperty> { source };

        GraphProperty copy = new HGModel().DuplicateProperty(source, scope, out string error);

        Assert.That(error, Is.Null);
        Assert.That(copy, Is.Not.Null.And.Not.SameAs(source));
        Assert.That(copy.Id, Is.Not.Null.And.Not.EqualTo(source.Id));
        Assert.That(copy.Slot, Is.Not.SameAs(source.Slot), "初始內容要是獨立的一份，改複本不能動到原件。");
        Assert.That(copy.Name, Is.EqualTo("Shared 複本"));
        Assert.That(scope, Is.EqualTo(new[] { source, copy }));
    }

    [Test]
    public void CopyingPropertyNodesSharesEveryLibraryDefinition()
    {
        var local = new GraphProperty(null, new TestFormulaSlot());
        string localId = local.EnsureId();
        var localNode = new GraphNode();
        localNode.EnsureId();
        localNode.SetProperty(local);

        var proto = new GraphProperty("Shared", new TestFormulaSlot());
        proto.EnsureId();
        var protoNode = new GraphNode();
        protoNode.EnsureId();
        protoNode.SetProperty(proto);

        List<GraphNode> copies = HaruGraphWindow.CloneSubgraph(new List<GraphNode> { localNode, protoNode }, out _);

        Assert.That(copies, Has.Count.EqualTo(2));
        Assert.That(copies[0].Property, Is.SameAs(local));
        Assert.That(copies[0].Property.Id, Is.EqualTo(localId));
        Assert.That(copies[1].Property, Is.SameAs(proto), "Property 是庫定義，複本必須沿用同一顆。");
    }

    [Test]
    public void SwitchingAConnectedPropertyNodeBreaksTheLinkButKeepsTheNode()
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var document = new PropertyDocument();
            // 庫裡只有另一族的 Property：切換定義之後寫入線必然不相容，一定會走斷線那條路。
            var proto = new GraphProperty("Other", new TestFormulaSlotB());
            proto.EnsureId();
            document.Properties.Add(proto);
            var targetReference = new GraphNode();
            targetReference.SetProperty(proto);
            string targetId = targetReference.EnsureId();
            document.Orphans.Add(targetReference);

            var local = new GraphProperty(null, new TestFormulaSlot());
            local.EnsureId();
            local.Name = "Original";
            document.Properties.Add(local);
            var propertyNode = new GraphNode();
            string propertyId = propertyNode.EnsureId();
            propertyNode.SetProperty(local);

            // Property 節點刻意不放候選池：它只被寫入欄位牽著，斷線後若沒有回收就會從整張圖失聯。
            var writer = new PropertyWriterBody();
            writer.Target.SetNode(propertyNode);
            var writerNode = new GraphNode(writer);
            string writerId = writerNode.EnsureId();
            document.Orphans.Add(writerNode);
            owner.Document = document;

            Assert.That(window.BindDocument(owner, PropertyBinding(), PropertyContext()), Is.True);
            Assert.That(window.GetDocumentCommands().Query(), Is.Not.Null);
            HGNodeView view = window.NodeOfId(propertyId);
            Assert.That(view?.Carrier, Is.Not.Null, "綁定後應該找得到 Property 節點。");

            window.ChangeNodeToProperty(view, window.NodeOfId(targetId).Property);
            Assert.That(window.GetDocumentCommands().Query(), Is.Not.Null);

            Assert.That(window.NodeOfId(propertyId)?.Carrier, Is.Not.Null, "換模式只斷線，節點必須留在圖上。");
            var writerCopy = window.NodeOfId(writerId)?.Carrier?.BodyObject as PropertyWriterBody;
            Assert.That(writerCopy, Is.Not.Null);
            Assert.That(writerCopy.Target.Node, Is.Null, "族不相容的寫入線要斷開。");
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void UnboundPropertyNodeKeepsItsIdentityAndRejectsWriters()
    {
        var node = new GraphNode();
        string id = node.EnsureId();
        node.SetProperty(null);
        Assert.That(node.Kind, Is.EqualTo(NodeKind.Property));
        Assert.That(node.Id, Is.EqualTo(id));
        Assert.That(new HGPropertyWriteSource(new WriteTargetSlot(), new GraphNode()).CheckTarget(node),
            Is.EqualTo(HGPortConnectionResult.MissingBinding));
    }

    [Test]
    public void PropertyNamesUseChipAndRejectCrossFamilyDuplicates()
    {
        var model = new HGModel();
        var scope = new List<GraphProperty>();

        GraphProperty first = model.CreateProperty(scope, typeof(TestFormulaSlot), out string firstError);
        GraphProperty second = model.CreateProperty(scope, typeof(TestFormulaSlotB), out string secondError);

        Assert.That(firstError, Is.Null);
        Assert.That(secondError, Is.Null);
        Assert.That(first.RetainValue, Is.False, "編輯器新建 Property 預設只在本次執行使用。");
        Assert.That(new GraphProperty().RetainValue, Is.False);
        string chip = HGReflect.SlotKindName(typeof(TestFormulaSlot));
        Assert.That(first.Name, Is.EqualTo(chip + "1"));
        Assert.That(second.Name, Is.EqualTo(chip + "2"), "不同族若 chip 名稱相同，仍須避開庫內重名。");
        Assert.That(model.RenameProperty(second, first.Name, scope, out string renameError), Is.False,
            "名稱在庫作用域內全域唯一，不同族也不可同名。");
        Assert.That(renameError, Is.Not.Null);
    }

    [Test]
    public void DeletingADefinitionClearsItsNodesWhileClearingANodeLeavesTheLibraryAlone()
    {
        var model = new HGModel();
        var scope = new List<GraphProperty>();
        GraphProperty proto = model.CreateProperty(scope, typeof(TestFormulaSlot), out _);
        var protoNode = new GraphNode();
        protoNode.SetProperty(proto);
        GraphProperty local = proto;
        var localNode = new GraphNode();
        localNode.SetProperty(local);

        localNode.Clear();

        Assert.That(scope, Has.Count.EqualTo(1), "刪掉引用節點不影響庫定義。");
        Assert.That(localNode.Kind, Is.EqualTo(NodeKind.Empty));

        model.DeleteProperty(proto, scope, new[] { protoNode });

        Assert.That(scope, Is.Empty);
        Assert.That(protoNode.Kind, Is.EqualTo(NodeKind.Empty), "刪庫定義時所有引用節點一併清空。");
    }



    private static HGDocumentBinding<PropertyDocument> PropertyBinding()
        => new("Property.Document", target => ((PropertyDocumentOwner)target).Document,
            (target, document) => ((PropertyDocumentOwner)target).Document = document);

    [Test]
    public void SelectionSwitchResolvesTheNewOwnerInsteadOfReusingItsPredecessorsBinding()
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var nextOwner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        var previousSelection = UnityEditor.Selection.activeObject;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            owner.Document = new PropertyDocument();
            nextOwner.Document = new TestDocument();
            Assert.That(window.BindDocument(owner, PropertyBinding(), PropertyContext()), Is.True);
            var previousCommands = window.GetDocumentCommands();

            UnityEditor.Selection.activeObject = nextOwner;
            typeof(HaruGraphWindow).GetMethod("OnSelectionChange", flags).Invoke(window, null);

            var model = (HGModel)typeof(HaruGraphWindow).GetField("model", flags).GetValue(window);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.Owner, Is.SameAs(nextOwner));
            Assert.That(model.Data, Is.TypeOf<TestDocument>());
            Assert.That(previousCommands.Query(), Is.Null, "成功切換後舊命令必須失效。");
            Assert.That(window.GetDocumentCommands().Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEditor.Selection.activeObject = previousSelection;
            UnityEngine.Object.DestroyImmediate(nextOwner);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void FailedWindowBindPreservesThePreviousDocumentAndCommands()
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var nextOwner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.Document = new PropertyDocument();
            Assert.That(window.BindDocument(owner, PropertyBinding(), PropertyContext()), Is.True);
            var commands = window.GetDocumentCommands();
            var before = commands.Query();
            var failingBinding = new HGDocumentBinding<TestDocument>("Failing.Document",
                _ => throw new InvalidOperationException("read failed"),
                (target, document) => ((TestDocumentOwner)target).Document = document);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[GraphKit] 文件 'Failing.Document' 重載失敗，目前工作副本與歷程保留：read failed");
            Assert.That(window.BindDocument(nextOwner, failingBinding, HGEditorExtensionContext.Default), Is.False);

            Assert.That(commands.Query(), Is.Not.Null, "失敗綁定不能讓原命令失效。");
            Assert.That(commands.Query().Generation, Is.EqualTo(before.Generation));
            Assert.That(commands.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed), "取消仍應讀取原 Owner 的 binding。");
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(nextOwner);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TokenCandidatesUseSourceSlotPolicyRegardlessOfSharedReaderOrder(bool strictReaderFirst)
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var document = new PropertyDocument();
            var original = new GraphToken("Original", new CompatibleTestFormulaSlot());
            var foreign = new GraphToken("Foreign", new TestFormulaSlotB());
            document.Tokens.Add(original);
            document.Tokens.Add(foreign);
            var source = new GraphNode();
            source.SetToken(original);
            string sourceId = source.EnsureId();
            var open = new CompatibleTokenReaderBody();
            var strict = new CompatibleTokenReaderBody();
            strict.Input.CompatibleResults = false;
            open.Input.SetNode(source);
            strict.Input.SetNode(source);
            var openNode = new GraphNode(open);
            var strictNode = new GraphNode(strict);
            string openId = openNode.EnsureId();
            string strictId = strictNode.EnsureId();
            document.Orphans.Add(strictReaderFirst ? strictNode : openNode);
            document.Orphans.Add(strictReaderFirst ? openNode : strictNode);
            owner.Document = document;
            var context = new HGEditorExtensionContext(new PropertyDocumentProvider(),
                profile: new HGEditorProfile(HGCapabilities.Properties | HGCapabilities.Tokens));
            Assert.That(window.BindDocument(owner, PropertyBinding(), context), Is.True);
            var commands = window.GetDocumentCommands();
            commands.Query();

            var options = window.NodeSourceOptions(window.NodeOfId(sourceId));
            Assert.That(options.Exists(option => option.Group == "Token" && option.Name == "Foreign"), Is.True);
            options.Find(option => option.Group == "Token" && option.Name == "Foreign").Apply();
            commands.Query();
            var changed = window.NodeOfId(sourceId).Carrier;
            Assert.That(changed.Token.Name, Is.EqualTo("Foreign"));
            Assert.That(((CompatibleTokenReaderBody)window.NodeOfId(openId).Obj).Input.Node, Is.SameAs(changed));
            Assert.That(((CompatibleTokenReaderBody)window.NodeOfId(strictId).Obj).Input.Node, Is.Null);

            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            commands.Query();
            Assert.That(window.NodeOfId(sourceId).Carrier.Token.Name, Is.EqualTo("Original"));
            Assert.That(((CompatibleTokenReaderBody)window.NodeOfId(strictId).Obj).Input.Node,
                Is.SameAs(window.NodeOfId(sourceId).Carrier));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ReplacingPropertyWithBodyPreservesReadersAndUndoRestoresWriters(bool connectedWriter)
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var document = new PropertyDocument();
            var property = new GraphNode();
            string id = property.EnsureId();
            property.SetProperty(new GraphProperty("Value", new TestFormulaSlot()));
            document.Properties.Add(property.Property);
            property.Pos = new Vector2(120, 160);
            property.Note = "keep";
            var reader = new PropertyReaderBody();
            reader.Source.SetNode(property);
            var readerNode = new GraphNode(reader);
            string readerId = readerNode.EnsureId();
            var writer = new PropertyWriterBody();
            if (connectedWriter) writer.Target.SetNode(property);
            var writerNode = new GraphNode(writer);
            string writerId = writerNode.EnsureId();
            document.Orphans.Add(readerNode);
            document.Orphans.Add(writerNode);
            owner.Document = document;
            Assert.That(window.BindDocument(owner, PropertyBinding(), PropertyContext()), Is.True);
            var commands = window.GetDocumentCommands();
            commands.Query();

            window.ReplaceNodeType(window.NodeOfId(id), typeof(PropertyReaderBody));
            commands.Query();
            var changed = window.NodeOfId(id).Carrier;
            Assert.That(changed.Kind, Is.EqualTo(NodeKind.Inline));
            Assert.That(changed.Pos, Is.EqualTo(new Vector2(120, 160)));
            Assert.That(changed.Note, Is.EqualTo("keep"));
            Assert.That(((PropertyReaderBody)window.NodeOfId(readerId).Obj).Source.Node, Is.SameAs(changed));
            Assert.That(((PropertyWriterBody)window.NodeOfId(writerId).Obj).Target.Node, Is.Null);
            Assert.That(window.NodeSourceOptions(window.NodeOfId(id)).Exists(option => option.Name == "Property"), Is.True);

            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            commands.Query();
            var restored = window.NodeOfId(id).Carrier;
            Assert.That(restored.Kind, Is.EqualTo(NodeKind.Property));
            Assert.That(((PropertyReaderBody)window.NodeOfId(readerId).Obj).Source.Node, Is.SameAs(restored));
            var restoredTarget = ((PropertyWriterBody)window.NodeOfId(writerId).Obj).Target.Node;
            if (connectedWriter) Assert.That(restoredTarget, Is.SameAs(restored));
            else Assert.That(restoredTarget, Is.Null);

            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            commands.Query();
            window.NodeSourceOptions(window.NodeOfId(id)).Find(option => option.Name == "Property").Apply();
            commands.Query();
            Assert.That(window.NodeOfId(id).Carrier.Kind, Is.EqualTo(NodeKind.Property));
            Assert.That(((PropertyReaderBody)window.NodeOfId(readerId).Obj).Source.Node,
                Is.SameAs(window.NodeOfId(id).Carrier));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void PublicReplacementOnlyAcceptsDefinitionsOwnedByTheWorkingLibrary()
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        try
        {
            owner.Document = new PropertyDocument();
            owner.Document.Properties.Add(new GraphProperty("Shared", new TestFormulaSlot()));
            owner.Document.Orphans.Add(new GraphNode());
            Assert.That(HGDocumentSession<PropertyDocument>.TryOpen(owner, PropertyBinding(), out var session), Is.True);
            var carrier = session.Document.Orphans[0];
            Assert.That(session.ReplaceSource(carrier, HGCarrierSource.NamedProperty(owner.Document.Properties[0])),
                Is.EqualTo(HGSessionCommandResult.Rejected), "Owner 的定義不是工作副本的定義。");
            Assert.That(session.IsDirty, Is.False);
            var proto = session.Document.Properties[0];
            Assert.That(session.ReplaceSource(carrier, HGCarrierSource.NamedProperty(proto)), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(carrier.Property, Is.SameAs(proto));

            var local = new GraphProperty(null, new TestFormulaSlot());
            string id = local.EnsureId();
            Assert.That(session.ReplaceSource(carrier, HGCarrierSource.NamedProperty(local)), Is.EqualTo(HGSessionCommandResult.Rejected));
            Assert.That(carrier.Property, Is.Not.SameAs(local));
            Assert.That(carrier.Property, Is.SameAs(proto));
            Assert.That(session.Document.Properties[0], Is.SameAs(proto));
            Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(session.Document.Orphans[0].Property, Is.Null);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    private static HGEditorExtensionContext PropertyContext()
        => new(new PropertyDocumentProvider(), profile: new HGEditorProfile(HGCapabilities.Properties));

    [Test]
    public void KeyUsesStableOwnerPathAndRole()
    {
        var first = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var rebuilt = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var output = new HGPortKey("node", "/steps[2]", HGPortRole.Output);

        Assert.That(rebuilt, Is.EqualTo(first));
        Assert.That(output, Is.Not.EqualTo(first));
    }

    [Test]
    public void RegistryRejectsPortFromOldGeneration()
    {
        var registry = new HGPortRegistry(4);

        Assert.That(registry.Add(AggregatePort(3)), Is.False);
        Assert.That(registry.Add(AggregatePort(4)), Is.True);
        Assert.That(registry.Ports, Has.Count.EqualTo(1));
    }

    [Test]
    public void RoleSpecificBuilderAssignsGenerationAndRejectsIncompletePorts()
    {
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 4);
        var presentation = Presentation(new object());

        Assert.That(build.AddInput(new HGPortKey("input", "/value", HGPortRole.Output), new TestSlot(),
            new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(graph.Ports[0].Key.Role, Is.EqualTo(HGPortRole.Input));
        Assert.That(graph.Ports[0].Generation, Is.EqualTo(4));
        Assert.That(build.AddOutput(new HGPortKey("output", "", HGPortRole.Output),
            new HGDelegatePortSource(null, null, _ => true), new HGDelegatePortPolicy(() => true), presentation, true),
            Is.False);
        Assert.That(build.LastResult, Is.EqualTo(HGPortBuildResult.InvalidBinding));
    }

    [Test]
    public void BuildContextExposesReadOnlyNodeFieldAndPortSnapshots()
    {
        var graph = new HGGraphView();
        var node = new HGNodeView { Id = "node", Pos = new Vector2(10f, 20f) };
        var row = new HGRow
        {
            OwnerNodeId = "node",
            Path = "/value",
            Kind = HGRowKind.InputPort,
            InputPortPosition = new Vector2(12f, 34f),
        };
        node.Rows.Add(row);
        graph.Nodes.Add(node);
        var build = new HGPortBuildContext(graph, 4);
        var key = new HGPortKey("node", "/value", HGPortRole.Input);

        Assert.That(build.Nodes, Has.Count.EqualTo(1));
        Assert.That(build.Nodes[0].Id, Is.EqualTo("node"));
        Assert.That(build.Fields, Has.Count.EqualTo(1));
        Assert.That(build.Fields[0].InputAnchor.NodeId, Is.EqualTo("node"));
        Assert.That(build.Fields[0].InputAnchor.Position, Is.EqualTo(new Vector2(12f, 34f)));
        var presentation = new HGDelegatePortPresentation(new object(), build.Fields[0].InputAnchor,
            () => Vector2.zero, () => Rect.zero, () => true, () => false);
        var locator = (IHGPortPresentationLocator)presentation;
        Assert.That(locator.NodeId, Is.EqualTo("node"));
        Assert.That(locator.FieldPath, Is.EqualTo("/value"));
        var providerKey = new HGPortKey("provider", "/adapter", HGPortRole.Input);
        Assert.That(build.AddInput(providerKey, new TestSlot(), new HGDelegatePortPolicy(() => true), presentation), Is.True);
        Assert.That(build.TryGetDescriptor(providerKey, out var port), Is.True);
        Assert.That(port.Anchor.NodeId, Is.EqualTo("node"));
        Assert.That(port.Anchor.FieldPath, Is.EqualTo("/value"));
        Assert.That(port.IsPrimaryOutput, Is.False);
    }

    [Test]
    public void LinkOwnersExposeRoleSpecificEndpoints()
    {
        var input = new HGNodeView();
        var output = new HGNodeView();
        var link = new HGLink { InputOwner = input, OutputOwner = output };

        Assert.That(link.InputOwner, Is.SameAs(input));
        Assert.That(link.OutputOwner, Is.SameAs(output));
    }

    [Test]
    public void SlotAndTokenExposeFamilyType()
    {
        var slot = new TestFormulaSlot();
        var token = new GraphToken("value", slot);

        Assert.That(slot.FamilyType, Is.EqualTo(typeof(TestFormulaSlot)));
        Assert.That(token.FamilyType, Is.EqualTo(typeof(TestFormulaSlot)));
    }

    [Test]
    public void RegistryRequiresOneExplicitPrimaryOutputPerSource()
    {
        var registry = new HGPortRegistry(4);
        var source = new GraphNode();
        var first = new HGPortKey("node", "/first", HGPortRole.Output);
        var second = new HGPortKey("node", "/second", HGPortRole.Output);
        var portSource = new HGDelegatePortSource(source, source, _ => true);

        Assert.That(registry.AddOutput(first, portSource, new HGDelegatePortPolicy(() => true), Presentation(new object()), true), Is.True);
        Assert.That(registry.TryGetPrimaryOutputDescriptor(source, out var primary), Is.True);
        Assert.That(primary.Key, Is.EqualTo(first));
        Assert.That(primary.IsPrimaryOutput, Is.True);
        Assert.That(registry.AddOutput(second, portSource, new HGDelegatePortPolicy(() => true), Presentation(new object()), true), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.DuplicatePrimaryOutput));
        Assert.That(registry.Descriptors, Has.Count.EqualTo(1));
    }

    [Test]
    public void RegistryRejectsDuplicateMissingOwnerAndEmptySourceWithoutMutatingIndexes()
    {
        var registry = new HGPortRegistry(4);
        var inputKey = new HGPortKey("node", "/input", HGPortRole.Input);
        var policy = new HGDelegatePortPolicy(() => true);

        Assert.That(registry.AddInput(inputKey, new TestSlot(), policy, Presentation(new object())), Is.True);
        Assert.That(registry.AddInput(inputKey, new TestSlot(), policy, Presentation(new object())), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.DuplicateKey));
        Assert.That(registry.AddInput(new HGPortKey("missing", "/input", HGPortRole.Input), new TestSlot(), policy,
            Presentation(null)), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.MissingOwner));
        Assert.That(registry.AddOutput(new HGPortKey("output", "/source", HGPortRole.Output),
            new HGDelegatePortSource(null, null, _ => true), policy, Presentation(new object())), Is.False);
        Assert.That(registry.LastResult, Is.EqualTo(HGPortBuildResult.InvalidBinding));
        Assert.That(registry.Ports, Has.Count.EqualTo(1));
        Assert.That(registry.Descriptors, Has.Count.EqualTo(1));
        Assert.That(registry.TryGet(inputKey, out _), Is.True);
    }

    [Test]
    public void ExplicitProviderAddsAdapterThroughBuildSeam()
    {
        var extension = new HGEditorExtensionContext(new TestProvider());
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 9);

        extension.Provider.AddPorts(build);

        Assert.That(graph.Ports, Has.Count.EqualTo(1));
        Assert.That(graph.Ports[0].Key.OwnerId, Is.EqualTo("test-adapter"));
        Assert.That(graph.Ports[0].Binding, Is.InstanceOf<IHGAggregatePortBinding>());
    }

    [Test]
    public void ExtensionContextCollectsOptionalDomainDiagnostics()
    {
        var extension = new HGEditorExtensionContext(new TestProvider());
        var diagnostics = new List<GraphDiagnostic>();

        extension.CollectDiagnostics(null, null, diagnostics);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Code, Is.EqualTo("test.domain.invalid"));
    }

    [Test]
    public void ExplicitEditorProfileOverridesLegacyDocumentCapabilities()
    {
        var context = new HGEditorExtensionContext(new TestProvider(), profile: new HGEditorProfile(HGCapabilities.Properties));
        var legacyContext = new HGEditorExtensionContext(new TestProvider());
        var legacyDocument = new TestDocument(HGCapabilities.Tokens);

        Assert.That(context.CapabilitiesOf(new TestDocument()), Is.EqualTo(HGCapabilities.Properties));
        Assert.That(HGGraph.Has(context, new TestDocument(), HGCapabilities.Properties), Is.True);
        Assert.That(HGGraph.Has(context, new TestDocument(), HGCapabilities.Tokens), Is.False);
        Assert.That(legacyContext.CapabilitiesOf(legacyDocument), Is.EqualTo(HGCapabilities.Tokens));
    }

    [Test]
    public void ModelRoutesRootOperationsThroughItsExplicitAdapter()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var adapter = new TestRootAdapter();
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        model.SetRootAdapter(adapter);

        Assert.That(model.AvailableRootKeys, Is.EqualTo(new object[] { "custom" }));
        Assert.That(model.RootItemSlotType, Is.EqualTo(typeof(TestSlot)));
        Assert.That(model.ReadRootGroups()[0].Root, Is.SameAs(adapter.Root));
        Assert.That(model.HasRoot("custom"), Is.True);

        var added = model.AddRoot("new-root");
        Assert.That(adapter.AddedKey, Is.EqualTo("new-root"));
        Assert.That(model.NewRootItem(added.Items), Is.TypeOf<TestSlot>());
        model.RemoveRoot(added);
        Assert.That(adapter.RemovedRoot, Is.SameAs(added.Root));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WindowRootRemovalPreservesSourcesAndSlotOrphansThroughHistoryAndCommit(bool keepSharedRoot)
    {
        var owner = ScriptableObject.CreateInstance<RemovalDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var child = new GraphNode(new RemovalBody()) { Pos = new Vector2(800, 120) };
            var body = new RemovalBody();
            body.Source.SetNode(child);
            var unique = new GraphNode(body) { Pos = new Vector2(400, 120), Note = "keep me" };
            var shared = new GraphNode(new RemovalBody()) { Pos = new Vector2(400, 300) };
            var loose = new GraphNode(new RemovalBody()) { Pos = new Vector2(800, 300) };
            string uniqueId = unique.EnsureId(), childId = child.EnsureId(), sharedId = shared.EnsureId(), looseId = loose.EnsureId();
            var firstSlot = new RemovalSlot();
            firstSlot.SetNode(unique);
            firstSlot.Orphans.Add(loose);
            var sharedSlot = new RemovalSlot();
            sharedSlot.SetNode(shared);
            owner.Document = new RemovalDocument();
            owner.Document.Groups.Add(new RemovalRoot { Key = "first", Items = new() { firstSlot, sharedSlot } });
            // 同一個 Slot 也可能被保留中的 root 共用，移除容器不能 SetNode(null) 改掉它。
            if (keepSharedRoot) owner.Document.Groups.Add(new RemovalRoot { Key = "second", Items = new() { sharedSlot } });
            Assert.That(window.BindDocument(owner, RemovalBinding(), HGEditorExtensionContext.Default), Is.True);
            var commands = window.GetDocumentCommands();
            Assert.That(commands.Query(), Is.Not.Null);
            var model = RemovalModel(window);
            var working = (RemovalDocument)model.Data;
            var rootNode = window.NodeOfId(HGGraph.GroupHeadId(model, working.Groups[0]));

            InvokeRemoval(window, "RemoveTimingGroup", rootNode);
            Assert.That(commands.Query(), Is.Not.Null);
            working = (RemovalDocument)model.Data;
            Assert.That(working.Groups, Has.Count.EqualTo(keepSharedRoot ? 1 : 0));
            Assert.That(working.Orphans, Has.Count.EqualTo(keepSharedRoot ? 2 : 3));
            Assert.That(working.Orphans.Exists(node => node.Id == uniqueId), Is.True);
            Assert.That(working.Orphans.Exists(node => node.Id == looseId), Is.True);
            if (keepSharedRoot) Assert.That(working.Groups[0].Items[0].Node.Id, Is.EqualTo(sharedId));
            else Assert.That(working.Orphans.Exists(node => node.Id == sharedId), Is.True);
            Assert.That(window.NodeOfId(uniqueId).Carrier.Note, Is.EqualTo("keep me"));
            Assert.That(window.NodeOfId(uniqueId).Carrier.Pos, Is.EqualTo(new Vector2(400, 120)));
            Assert.That(window.NodeOfId(childId).Carrier.Pos, Is.EqualTo(new Vector2(800, 120)));
            Assert.That(((RemovalBody)window.NodeOfId(uniqueId).Carrier.BodyObject).Source.Node,
                Is.SameAs(window.NodeOfId(childId).Carrier));

            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Query(), Is.Not.Null);
            Assert.That(((RemovalDocument)model.Data).Groups, Has.Count.EqualTo(keepSharedRoot ? 2 : 1));
            Assert.That(((RemovalDocument)model.Data).Orphans, Is.Empty);
            Assert.That(commands.Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Commit(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(owner.Document.Groups, Has.Count.EqualTo(keepSharedRoot ? 1 : 0));
            Assert.That(owner.Document.Orphans, Has.Count.EqualTo(keepSharedRoot ? 2 : 3));
            Assert.That(window.BindDocument(owner, RemovalBinding(), HGEditorExtensionContext.Default), Is.True);
            Assert.That(window.GetDocumentCommands().Query(), Is.Not.Null);
            Assert.That(window.NodeOfId(uniqueId), Is.Not.Null);
            Assert.That(window.NodeOfId(childId), Is.Not.Null);
            Assert.That(window.NodeOfId(sharedId), Is.Not.Null);
            Assert.That(window.NodeOfId(looseId), Is.Not.Null);
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WindowListRemovalPreservesSourceSubtreesAndOtherUsers(bool nestedEntry)
    {
        var owner = ScriptableObject.CreateInstance<RemovalDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            var leaf = new GraphNode(new RemovalBody()) { Pos = new Vector2(1000, 100) };
            var childBody = new RemovalBody();
            childBody.Source.SetNode(leaf);
            var unique = new GraphNode(childBody) { Pos = new Vector2(700, 100) };
            var shared = new GraphNode(new RemovalBody()) { Pos = new Vector2(700, 300) };
            var uniqueSlot = new RemovalSlot();
            uniqueSlot.SetNode(unique);
            var sharedSlot = new RemovalSlot();
            sharedSlot.SetNode(shared);
            var body = new RemovalListBody();
            body.Shared.SetNode(shared);
            if (nestedEntry) body.Entries.Add(new RemovalEntry { First = uniqueSlot, Second = sharedSlot });
            else body.Actions.AddRange(new[] { uniqueSlot, sharedSlot });
            var container = new GraphNode(body) { Pos = new Vector2(300, 100) };
            string containerId = container.EnsureId(), uniqueId = unique.EnsureId(), sharedId = shared.EnsureId(), leafId = leaf.EnsureId();
            owner.Document = new RemovalDocument();
            owner.Document.Orphans.Add(container);
            Assert.That(window.BindDocument(owner, RemovalBinding(), HGEditorExtensionContext.Default), Is.True);
            var commands = window.GetDocumentCommands();
            Assert.That(commands.Query(), Is.Not.Null);
            var workingBody = (RemovalListBody)window.NodeOfId(containerId).Carrier.BodyObject;
            var items = nestedEntry
                ? new HGListItemSource(workingBody.Entries, typeof(RemovalEntry))
                : new HGListItemSource(workingBody.Actions, typeof(RemovalSlot));

            InvokeRemoval(window, "RemoveListItem", items, 0);
            Assert.That(commands.Query(), Is.Not.Null);
            Assert.That(items.Count, Is.EqualTo(nestedEntry ? 0 : 1));
            Assert.That(window.NodeOfId(uniqueId), Is.Not.Null);
            Assert.That(window.NodeOfId(leafId), Is.Not.Null);
            Assert.That(((RemovalBody)window.NodeOfId(uniqueId).Carrier.BodyObject).Source.Node,
                Is.SameAs(window.NodeOfId(leafId).Carrier));
            Assert.That(workingBody.Shared.Node, Is.SameAs(window.NodeOfId(sharedId).Carrier));
            Assert.That(((RemovalDocument)RemovalModel(window).Data).Orphans, Has.Count.EqualTo(2));
            Assert.That(window.NodeOfId(uniqueId).Carrier.Pos, Is.EqualTo(new Vector2(700, 100)));
            Assert.That(commands.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
            Assert.That(commands.Query(), Is.Not.Null);
            var restored = (RemovalListBody)window.NodeOfId(containerId).Carrier.BodyObject;
            Assert.That(nestedEntry ? restored.Entries.Count : restored.Actions.Count, Is.EqualTo(nestedEntry ? 1 : 2));
            Assert.That(((RemovalDocument)RemovalModel(window).Data).Orphans, Has.Count.EqualTo(1));
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    private static HGModel RemovalModel(HaruGraphWindow window) =>
        (HGModel)typeof(HaruGraphWindow).GetField("model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(window);

    private static void InvokeRemoval(HaruGraphWindow window, string method, params object[] args) =>
        typeof(HaruGraphWindow).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(window, args);

    private static HGDocumentBinding<RemovalDocument> RemovalBinding() =>
        new("Removal.Document", owner => ((RemovalDocumentOwner)owner).Document,
            (owner, document) => ((RemovalDocumentOwner)owner).Document = document);

    [Test]
    public void ModelKeepsRootKeyIdentityWhenDisplayTextCollides()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var first = new SameLabelRootKey();
        var second = new SameLabelRootKey();
        var adapter = new TestRootAdapter(TestRootKind.Enum, "same", first, second);
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        model.SetRootAdapter(adapter);

        Assert.That(model.AvailableRootKeys, Is.EqualTo(new object[] { TestRootKind.Enum, "same", first, second }));
        Assert.That(model.RootKeyTitle(TestRootKind.Enum), Is.EqualTo(TestRootKind.Enum.ToString()));
        Assert.That(model.RootKeyTitle("same"), Is.EqualTo("same"));
        Assert.That(model.RootKeyTitle(first), Is.EqualTo(first.ToString()));
        Assert.That(model.RootKeyTitle(second), Is.EqualTo(second.ToString()));
        Assert.That(model.HasRoot(TestRootKind.Enum), Is.True);
        Assert.That(model.HasRoot("same"), Is.True);
        Assert.That(model.HasRoot(first), Is.True);
        Assert.That(model.HasRoot(second), Is.True);
        Assert.That(model.HasRoot(new SameLabelRootKey()), Is.False);
        var rootObjects = new List<object>();
        var ids = new HashSet<string>();
        foreach (var root in model.ReadRootGroups())
        {
            rootObjects.Add(root.Root);
            Assert.That(ids.Add(HGGraph.GroupHeadId(model, root.Root)), Is.True);
        }
        var built = HGGraph.Build(model, rootObjects, null, "roots", "Roots");
        Assert.That(built.Nodes.Count, Is.EqualTo(4));
        Assert.That(built.Diagnostics, Is.Empty);
        model.AddRoot(second);
        Assert.That(adapter.AddedKey, Is.SameAs(second));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void DocumentRootAdapterKeepsNonSlotRootsAndFixedItemsReadable()
    {
        var document = new NonSlotDocument();
        var firstRoot = new NonSlotRoot { Items = new[] { 3, 5 } };
        document.Roots.Add(firstRoot);
        var adapter = HGModel.HGDocumentRootAdapter.Instance;

        var roots = adapter.ReadRoots(document);

        Assert.That(roots, Has.Count.EqualTo(1));
        Assert.That(roots[0].Root, Is.SameAs(firstRoot));
        Assert.That(roots[0].Items, Is.SameAs(firstRoot.Items));
        Assert.That(roots[0].Items.IsFixedSize, Is.True);
        Assert.That(adapter.CreateItem(document), Is.EqualTo(0));
        Assert.That(adapter.AddRoot(document, "second").Root, Is.TypeOf<NonSlotRoot>());
        Assert.That(adapter.RemoveRoot(document, firstRoot), Is.True);
        Assert.That(adapter.RemoveRoot(document, firstRoot), Is.False);
    }

    [Test]
    public void ListItemSourceRejectsStructuralChangesForArraysButAllowsValueWrites()
    {
        var values = new[] { 1, 2 };
        var source = new HGListItemSource(values, typeof(int));

        Assert.That(source.CanEditStructure, Is.False);
        Assert.That(source.Add(3), Is.False);
        Assert.That(source.Insert(0, 3), Is.False);
        Assert.That(source.RemoveAt(0), Is.False);
        Assert.That(source.Move(0, 1), Is.False);
        Assert.That(source.Set(1, 7), Is.True);
        Assert.That(values, Is.EqualTo(new[] { 1, 7 }));
    }

    [Test]
    public void AssetListElementIsAnEmptyReferenceNotANewSceneObject()
    {
        var source = new HGListItemSource(new List<GameObject>(), typeof(GameObject));

        Assert.That(source.TryCreateElement(out object item), Is.True);
        Assert.That(item, Is.Null);
    }

    [Test]
    public void TokenFreeDocumentBindsWithoutAnITokenOwner()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var model = new HGModel();

        Assert.That(owner.Document, Is.Not.InstanceOf<ITokenOwner>());
        Assert.That(model.Bind(owner), Is.True);
        Assert.That(model.OwnerTokens, Is.Empty);

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void ConnectionCoordinatorAppliesGenerationVisibilityLockAndPolicy()
    {
        const int generation = 5;
        var slot = new TestSlot();
        bool locked = false;
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input),
            new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, source => source.Accepts(slot)),
            new HGDelegatePortPresentation(slot, () => Vector2.zero, () => Rect.zero,
                () => true, () => locked), generation);
        var source = new HGDelegatePortSource(new GraphNode(), null, candidate => ReferenceEquals(candidate, slot));
        var output = new HGPort(new HGPortKey("output", "", HGPortRole.Output),
            new HGOutputPortBinding(source),
            new HGDelegatePortPolicy(() => true),
            new HGDelegatePortPresentation(new object(), () => Vector2.one, () => Rect.zero,
                () => true, () => false), generation);

        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.True);
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        var external = new HGDelegatePortSource(null, null, candidate => ReferenceEquals(candidate, slot));
        Assert.That(HGPortConnection.CheckInputSource(input, external, generation),
            Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(HGPortConnection.CanConnect(input, output, generation + 1), Is.False);
        Assert.That(HGPortConnection.Check(input, output, generation + 1),
            Is.EqualTo(HGPortConnectionResult.StaleGeneration));
        locked = true;
        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.False);
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Locked));
    }

    [Test]
    public void ConnectionCoordinatorPreservesDetailedPolicyRejection()
    {
        const int generation = 5;
        var slot = new TestSlot();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, checkAcceptance: _ => HGPortConnectionResult.DomainRejected),
            Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "/value", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);

        Assert.That(HGPortConnection.Check(input, output, generation),
            Is.EqualTo(HGPortConnectionResult.DomainRejected));
    }

    [Test]
    public void ConnectionCoordinatorRejectsSameRoleAggregateAndHiddenPorts()
    {
        const int generation = 5;
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var secondInput = new HGPort(new HGPortKey("other", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
                () => false, () => false), generation);

        Assert.That(HGPortConnection.Check(input, secondInput, generation), Is.EqualTo(HGPortConnectionResult.SameRole));
        Assert.That(HGPortConnection.Check(input, AggregatePort(generation), generation),
            Is.EqualTo(HGPortConnectionResult.AggregateOnly));
        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Hidden));
    }

    [Test]
    public void ConnectionCoordinatorIsSymmetricAndAggregateCannotStartOrConnect()
    {
        const int generation = 5;
        var slot = new TestSlot();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, source => source.Accepts(slot)), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("output", "/value", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(new GraphNode(), null, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var aggregate = AggregatePort(generation);

        Assert.That(HGPortConnection.Check(input, output, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(HGPortConnection.Check(output, input, generation), Is.EqualTo(HGPortConnectionResult.Allowed));
        Assert.That(aggregate.CanStart, Is.False);
        Assert.That(HGPortConnection.Check(input, aggregate, generation), Is.EqualTo(HGPortConnectionResult.AggregateOnly));
        Assert.That(HGPortConnection.Check(aggregate, input, generation), Is.EqualTo(HGPortConnectionResult.AggregateOnly));
    }

    [Test]
    public void LinkPortResolverUsesCustomPrimaryOutputAndClassifiesUnresolvedEndpoints()
    {
        const int generation = 6;
        var inputRow = new HGRow { OwnerNodeId = "input", Path = "/value" };
        var source = new GraphNode();
        source.EnsureId();
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input), new HGInputPortBinding(new TestSlot()),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation);
        var output = new HGPort(new HGPortKey("provider", "/custom", HGPortRole.Output),
            new HGOutputPortBinding(new HGDelegatePortSource(source, source, _ => true)),
            new HGDelegatePortPolicy(() => true), Presentation(new object()), generation, true);
        var ports = new Dictionary<HGPortKey, HGPort> { [input.Key] = input, [output.Key] = output };
        var primaryOutputs = new Dictionary<GraphNode, HGPort> { [source] = output };
        var resolved = new HGLink { ParentRow = inputRow, OutputOwner = new HGNodeView { Carrier = source } };

        Assert.That(HGLinkPortResolver.Resolve(resolved, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.Resolved));
        Assert.That(resolved.InputPort, Is.SameAs(input));
        Assert.That(resolved.OutputPort, Is.SameAs(output));

        var missingInput = new HGLink { ParentRow = new HGRow { OwnerNodeId = "missing", Path = "/value" } };
        Assert.That(HGLinkPortResolver.Resolve(missingInput, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.InputUnresolved));

        var missingSource = new HGLink { ParentRow = inputRow };
        Assert.That(HGLinkPortResolver.Resolve(missingSource, ports, primaryOutputs, generation),
            Is.EqualTo(HGLinkPortResolution.OutputUnresolved));

        var missingPrimary = new HGLink { ParentRow = inputRow, OutputOwner = new HGNodeView { Carrier = source } };
        Assert.That(HGLinkPortResolver.Resolve(missingPrimary, ports, new Dictionary<GraphNode, HGPort>(), generation),
            Is.EqualTo(HGLinkPortResolution.PrimaryOutputMissing));
        Assert.That(missingPrimary.InputPort, Is.SameAs(input));
        Assert.That(missingPrimary.OutputPort, Is.Null);
    }

    [Test]
    public void SourceAcceptancePreservesDetailedRejection()
    {
        var slot = new TestSlot();
        var source = new HGDelegatePortSource(null, null, _ => false,
            _ => HGPortConnectionResult.IncompatibleFamily);

        Assert.That(HGPortConnection.CheckSourceAcceptance(source, slot),
            Is.EqualTo(HGPortConnectionResult.IncompatibleFamily));
    }

    [Test]
    public void TypedDocumentBindingClonesAndWritesOnlyItsDocumentType()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var binding = new HGDocumentBinding<TestDocument>("test-document",
            target => (target as TestDocumentOwner)?.Document,
            (target, document) => (target as TestDocumentOwner).Document = document,
            () => new TestDocument());

        Assert.That(binding.TryRead(owner, out var source), Is.True);
        Assert.That(binding.TryClone(source, out var copy), Is.True);
        Assert.That(copy, Is.TypeOf<TestDocument>());
        Assert.That(copy, Is.Not.SameAs(source));
        Assert.That(binding.TryWrite(owner, copy), Is.True);
        Assert.That(owner.Document, Is.SameAs(copy));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void LegacyDocumentDiscoveryRejectsMultipleCandidates()
    {
        Assert.That(HGModel.FindSystemField(typeof(TestDocumentOwner)), Is.Not.Null);
        Assert.That(HGModel.FindSystemField(typeof(TestMultiDocumentOwner)), Is.Null);
    }

    [Test]
    public void LegacyDocumentDiscoveryRejectsMissingCandidate()
    {
        Assert.That(HGModel.FindSystemField(typeof(TestNoDocumentOwner)), Is.Null);
    }

    [Test]
    public void TypedDocumentBindingRejectsAliasedClone()
    {
        var document = new TestDocument(deepCopy: current => current);
        var binding = new HGDocumentBinding<TestDocument>("test-document", _ => document, (owner, copy) => { });

        Assert.That(binding.TryClone(document, out _), Is.False);
    }

    [Test]
    public void TypedDocumentBindingRejectsNullAndWrongTypeClone()
    {
        var binding = new HGDocumentBinding<TestDocument>("test-document", _ => null, (owner, copy) => { });

        Assert.That(binding.TryClone(new TestDocument(deepCopy: _ => null), out _), Is.False);
        Assert.That(binding.TryClone(new TestDocument(deepCopy: _ => new object()), out _), Is.False);
    }

    [Test]
    public void ModelBindsAnIsolatedTypedWorkingDocument()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        owner.Document = new TestDocument();
        var model = new HGModel();

        Assert.That(model.Bind(owner), Is.True);
        Assert.That(model.Data, Is.TypeOf<TestDocument>());
        Assert.That(model.Data, Is.Not.SameAs(owner.Document));
        Assert.That(model.Doc, Is.SameAs(model.Data));

        UnityEngine.Object.DestroyImmediate(owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StoredValidationReadsTheWrittenDocumentForExplicitAndLegacyBindings(bool legacy)
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        try
        {
            owner.Document = new TestDocument();
            var model = new HGModel();
            var binding = new HGDocumentBinding<TestDocument>("Document", target => ((TestDocumentOwner)target).Document,
                (target, document) => ((TestDocumentOwner)target).Document = document);
            Assert.That(legacy ? model.Bind(owner) : model.Bind(owner, binding), Is.True);
            Assert.That(model.IsStoredDocumentValidated, Is.False);

            Assert.That(model.Save(), Is.True);

            Assert.That(owner.Document.IsValidated, Is.False, "保存不執行驗證。");
            owner.Document.Verify();
            Assert.That(owner.Document.IsValidated, Is.True);
            Assert.That(model.Doc.IsValidated, Is.False, "未驗證的工作副本不能取代已寫回文件的旗標。");
            Assert.That(model.IsStoredDocumentValidated, Is.True);
            var reopened = new HGModel();
            Assert.That(legacy ? reopened.Bind(owner) : reopened.Bind(owner, binding), Is.True);
            Assert.That(reopened.IsStoredDocumentValidated, Is.True, "重開後仍須讀到已寫回的驗證結果。");
            model.MarkContentChanged();
            Assert.That(model.IsStoredDocumentValidated, Is.True, "工作副本未存修改不會改變已儲存文件。");
            owner.Document.InvalidateValidation();
            Assert.That(model.IsStoredDocumentValidated, Is.False);
            owner.Document.Verify();
            Assert.That(model.IsStoredDocumentValidated, Is.True);
            owner.Document = null;
            Assert.That(model.IsStoredDocumentValidated, Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void RawReferenceLocationsKeepHostScopeAndIgnoreNullsDeclarationsAndScalarText()
    {
        const string yaml = @"%YAML 1.1
--- !u!114 &11400000
MonoBehaviour:
  graph:
    _orphans:
    - rid: 294645405547233762
    - rid: -2
    - {rid: 294645405547233762}
  note: |
    rid: 294645405547233762
  references:
    version: 2
    RefIds:
    - rid: 760
      type: {class: ActionSlot, ns: Test, asm: Test}
      data:
        _node:
          rid: 294645405547233762
        ordinaryNull:
          rid: -2
    - rid: 761
      type: {class: GraphToken, ns: Test, asm: Test}
      data:
        _slot: {rid: 294645405547233762}
    - rid: 294645405547233762
      type: {class: Missing, ns: Test, asm: Test}
      data:
        value: 42
--- !u!114 &11400001
MonoBehaviour:
  other:
    rid: 294645405547233762
";
        var paths = HGSerializedReferenceLocations.Parse(yaml.Split('\n'), 11400000,
            new HashSet<long> { 294645405547233762 });
        Assert.That(paths, Is.EqualTo(new[]
        {
            "graph._orphans.Array.data[0]", "graph._orphans.Array.data[2]",
            "managedReferences[760]._node", "managedReferences[761]._slot"
        }));
        Assert.Throws<System.IO.InvalidDataException>(() => HGSerializedReferenceLocations.Parse(
            new[] { "not a Unity text asset" }, 11400000, new HashSet<long> { 1 }));
    }

    [Test]
    public void NodeDescriptorUsesTypedFieldAccessAndRejectsDuplicateIds()
    {
        var descriptor = HGFieldDescriptor.Create<TestValueOwner, int>("count",
            target => target.Count, (target, value) => target.Count = value, "Count");
        var target = new TestValueOwner { Count = 2 };

        Assert.That(descriptor.Read(target), Is.EqualTo(2));
        descriptor.Write(target, 7);
        Assert.That(target.Count, Is.EqualTo(7));
        Assert.Throws<ArgumentException>(() => new HGNodeDescriptor(typeof(TestValueOwner),
            new[] { descriptor, descriptor }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ModelCommitGuardsExplicitAndLegacyBindingsAgainstOwnerReplacement(bool legacy)
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        try
        {
            owner.Document = new TestDocument();
            var model = new HGModel();
            var binding = new HGDocumentBinding<TestDocument>("Document", x => ((TestDocumentOwner)x).Document,
                (x, document) => ((TestDocumentOwner)x).Document = document);
            Assert.That(legacy ? model.Bind(owner) : model.Bind(owner, binding), Is.True);
            model.MarkContentChanged();
            var working = model.Data;
            var external = new TestDocument();
            owner.Document = external;
            Assert.That(model.Save(), Is.False);
            Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
            Assert.That(model.Data, Is.SameAs(working));
            Assert.That(owner.Document, Is.SameAs(external));
            Assert.That(model.Dirty, Is.True);
            Assert.That(model.CanUndo, Is.True);
            Assert.That(model.TryReload(), Is.True);
            Assert.That(model.Save(), Is.True);
            Assert.That(model.Save(), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void ModelFailedReloadKeepsWorkingCopyHistoryAndConflictBaseline()
    {
        var owner = ScriptableObject.CreateInstance<TestDocumentOwner>();
        try
        {
            owner.Document = new TestDocument();
            bool failRead = false;
            var binding = new HGDocumentBinding<TestDocument>("Document",
                x => failRead ? throw new InvalidOperationException("read failed") : ((TestDocumentOwner)x).Document,
                (x, document) => ((TestDocumentOwner)x).Document = document);
            var model = new HGModel();
            Assert.That(model.Bind(owner, binding), Is.True);
            model.MarkContentChanged();
            var working = model.Data;
            owner.Document = new TestDocument();
            failRead = true;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[GraphKit] 文件 'Document' 重載失敗，目前工作副本與歷程保留：read failed");
            Assert.That(model.TryReload(), Is.False);
            Assert.That(model.Data, Is.SameAs(working));
            Assert.That(model.Dirty, Is.True);
            Assert.That(model.CanUndo, Is.True);
            failRead = false;
            Assert.That(model.Save(), Is.False);
            Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void DescriptorRejectsInvalidWritesWithoutChangingTheTarget()
    {
        var writable = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value);
        var readOnly = HGFieldDescriptor.Create<TestValueOwner, int>("readonly", target => target.Count);
        var text = HGFieldDescriptor.Create<TestValueOwner, string>("text", target => target.Text,
            (target, value) => target.Text = value);
        var target = new TestValueOwner { Count = 2, Text = "before" };

        Assert.That(writable.TryWrite(target, "wrong", out var wrongType), Is.False);
        Assert.That(wrongType, Is.Null);
        Assert.That(writable.TryWrite(target, null, out var nullValue), Is.False);
        Assert.That(nullValue, Is.Null);
        Assert.That(readOnly.TryWrite(target, 7, out var readOnlyValue), Is.False);
        Assert.That(readOnlyValue, Is.Null);
        Assert.That(text.TryWrite(target, null, out var nullableValue), Is.True);
        Assert.That(nullableValue, Is.Null);
        Assert.That(target.Count, Is.EqualTo(2));
        Assert.That(target.Text, Is.Null);
    }

    [Test]
    public void DescriptorReplacesReflectionRowsAndMeasuresItsValueDrawer()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count",
            target => target.Count, (target, value) => target.Count = value, "Count");
        var drawer = new TestValueDrawer();
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner { Count = 2, Ignored = 3 } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(view.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(drawer));
        Assert.That(view.Nodes[0].Rows[0].Height, Is.EqualTo(38f));
        Assert.That(drawer.MeasuredTarget.Count, Is.EqualTo(2));
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(180f));
    }

    [Test]
    public void HiddenLabelDrawerMeasuresTheActualDrawWidthAndReportsMeasureFailure()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, hideLabel: true);
        var drawer = new TestValueDrawer();
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        var row = view.Nodes[0].Rows[0];
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(HGGraph.ValueFieldRect(new Rect(0, 0, 300, row.Height), row).width));
        Assert.That(drawer.MeasuredWidth, Is.EqualTo(276f));
        metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), new ThrowingDrawer());
        view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        Assert.That(view.Diagnostics.Exists(issue => issue.Code == "graphkit.metadata.drawer-failed"), Is.True);
        Assert.That(view.Nodes[0].Rows[0].DrawerError, Is.Not.Empty);
    }

    [Test]
    public void RegistryRemainsAtomicWhenPresentationThrowsAndCollectionsAreReadOnly()
    {
        var registry = new HGPortRegistry(1);
        var key = new HGPortKey("node", "/input", HGPortRole.Input);
        var broken = new HGDelegatePortPresentation(new object(), () => throw new InvalidOperationException("position"),
            () => Rect.zero, () => true, () => false);
        Assert.That(registry.AddInput(key, new TestSlot(), new HGDelegatePortPolicy(() => true), broken), Is.False);
        Assert.That(registry.Ports, Is.Empty);
        Assert.That(registry.TryGet(key, out _), Is.False);
        Assert.That(registry.Descriptors, Is.Empty);
        Assert.Throws<NotSupportedException>(() => ((IList<HGPort>)registry.Ports).Clear());
        Assert.That(typeof(HGPortBuildContext).GetMethod("TryGet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), Is.Null);
    }

    private sealed class ThrowingDrawer : IHGValueDrawer
    {
        public float Measure(in HGValueDrawerContext context, float width) => throw new InvalidOperationException("measure");
        public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value) => throw new InvalidOperationException("draw");
    }

    [Test]
    public void DescriptorCarriesExistingRowPresentationMetadata()
    {
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, hideLabel: true, labelWidthUnits: 4, labelWidthRatio: 0.5f,
            forceEnumButtons: true);
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);
        var row = view.Nodes[0].Rows[0];

        Assert.That(row.HideLabel, Is.True);
        Assert.That(row.LabelWidthUnits, Is.EqualTo(4));
        Assert.That(row.LabelWidthRatio, Is.EqualTo(0.5f));
        Assert.That(row.ForceEnumButtons, Is.True);
    }

    [Test]
    public void SlotClassEnumAttributeForcesButtonsWithoutFieldAttribute()
    {
        var marked = HGGraph.Build(new HGModel(), new object[] { new EnumButtonSlotOwner() }, null, "test", "Test");
        var plain = HGGraph.Build(new HGModel(), new object[] { new TestSlotOwner() }, null, "test", "Test");

        Assert.That(marked.Nodes[0].Rows[0].ForceEnumButtons, Is.True);
        Assert.That(plain.Nodes[0].Rows[0].ForceEnumButtons, Is.False);
    }

    [Test]
    public void BoolAttributeCarriesLabelsAndEnumAttributeDoesNotTouchBool()
    {
        var view = HGGraph.Build(new HGModel(), new object[] { new BoolButtonOwner() }, null, "test", "Test");
        var rows = view.Nodes[0].Rows;

        Assert.That(rows[0].BoolButtons, Is.Not.Null);
        Assert.That(rows[0].BoolButtons.TrueLabel, Is.EqualTo("區分"));
        Assert.That(rows[0].BoolButtons.FalseLabel, Is.EqualTo("忽略"));
        Assert.That(rows[1].BoolButtons, Is.Null);
    }

    [Test]
    public void FoldoutsGroupMembersUnderTheirHeaderAndCollapsedFoldoutsFoldOntoIt()
    {
        var model = new HGModel();
        var owner = new FoldoutOwner();
        var view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test");
        var rows = view.Nodes[0].Rows;

        // 每組各一列，插在第一個成員的位置；同名的後續成員收進同一組。
        Assert.That(rows.ConvertAll(row => row.Kind),
            Is.EqualTo(new[] { HGRowKind.NoPort, HGRowKind.Foldout, HGRowKind.Foldout, HGRowKind.NoPort }));
        var a = rows[1];
        var b = rows[2];
        Assert.That(a.Label, Is.EqualTo("A"));
        Assert.That(b.Label, Is.EqualTo("B"));
        Assert.That(a.Children.Count, Is.EqualTo(2));
        Assert.That(b.Children.Count, Is.EqualTo(1));
        Assert.That(a.Children.TrueForAll(row => ReferenceEquals(row.FoldoutOwnerRow, a)), Is.True);
        // 成員縮一層，組內的清單框才落在群組導引線右邊。
        Assert.That(a.Children.TrueForAll(row => row.Depth == a.Depth + 1), Is.True);
        // 預設展開：成員接在標題列下面，群組高度含成員。
        Assert.That(a.Collapsed, Is.False);
        Assert.That(a.Children[0].LocalY, Is.EqualTo(a.LocalY + HGGraph.RowHeight));
        Assert.That(a.Height, Is.EqualTo(3 * HGGraph.RowHeight));
        Assert.That(b.LocalY, Is.EqualTo(a.LocalY + a.Height));
        Assert.That(view.Diagnostics.Exists(d => d.Code == "graphkit.metadata.tab-invalid"), Is.True);

        string key = HGGraph.CollapseKey(view.Nodes[0].Id, a);
        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test",
            new Dictionary<string, bool> { [key] = true });
        a = view.Nodes[0].Rows[1];
        b = view.Nodes[0].Rows[2];

        // 收起的一組成員壓到標題列並隱藏；其他組照常展開，可同時展開多組。
        Assert.That(a.Collapsed, Is.True);
        Assert.That(a.Height, Is.EqualTo(HGGraph.RowHeight));
        Assert.That(a.Children.TrueForAll(row => row.Hidden && row.LocalY == a.LocalY), Is.True);
        Assert.That(b.Collapsed, Is.False);
        Assert.That(b.LocalY, Is.EqualTo(a.LocalY + HGGraph.RowHeight));
        Assert.That(b.Children[0].Hidden, Is.False);
    }

    [Test]
    public void SlotSettingsPersistIndependentlyAndKeepDefaultsAndConnectionsOnMainRows()
    {
        var owner = new SlotSettingsOwner();
        owner.A.DefaultObject = 9;
        var source = new GraphNode(new PropertyReaderBody());
        owner.A.SetNode(source);
        owner.B.SetNode(source);
        var model = new HGModel();
        var view = HGGraph.Build(model, new object[] { owner }, null, "settings", "Settings");
        Assert.That(view.Diagnostics, Is.Empty);
        var a = view.Nodes[0].Rows[0];
        var b = view.Nodes[0].Rows[1];
        float mainY = a.LocalY;
        Assert.That(a.SlotSettingsRow.Collapsed, Is.True);
        Assert.That(b.SlotSettingsRow.Collapsed, Is.True);
        Assert.That(a.SlotSettingsRow.Children, Has.Count.EqualTo(1));
        Assert.That(a.SlotSettingsRow.Children[0].Hidden, Is.True);
        Assert.That(a.Hidden, Is.False);
        Assert.That(view.Links, Has.Count.EqualTo(2));

        var state = new GraphViewState();
        string key = HGGraph.CollapseKey(view.Nodes[0].Id, a.SlotSettingsRow);
        state.SetFolded(key, false);
        state = JsonUtility.FromJson<GraphViewState>(JsonUtility.ToJson(GraphDeepCopy.Copy(state)));
        var folds = new Dictionary<string, bool>();
        foreach (var folded in state.Folded) folds[folded] = true;
        foreach (var unfolded in state.Unfolded) folds[unfolded] = false;
        view = HGGraph.Build(model, new object[] { owner }, null, "settings", "Settings", folds);
        a = view.Nodes[0].Rows[0];
        b = view.Nodes[0].Rows[1];
        Assert.That(a.SlotSettingsRow.Collapsed, Is.False);
        Assert.That(b.SlotSettingsRow.Collapsed, Is.True);
        Assert.That(a.LocalY, Is.EqualTo(mainY));
        Assert.That(a.Height, Is.EqualTo(HGGraph.RowHeight));
        Assert.That(a.SlotSettingsRow.Children[0].LocalY, Is.EqualTo(mainY + a.Height));
        Assert.That(b.LocalY, Is.EqualTo(mainY + 2 * HGGraph.RowHeight));
        Assert.That(a.InputSlot, Is.SameAs(owner.A));
        Assert.That(((FormulaSlotBase)a.InputSlot).DefaultObject, Is.EqualTo(9));
        Assert.That(a.SlotSettingsRow.Children[0].Target, Is.SameAs(owner.A));
        Assert.That(view.Links, Has.Count.EqualTo(2));
        Assert.That(view.Nodes[0].Rows[2].SlotSettingsRow, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GroupPathsNestTabsAndFoldoutsAndRestoreIndependentViewState(bool useDescriptor)
    {
        var owner = new NestedFieldGroupOwner();
        var shared = new GraphNode(new PropertyReaderBody());
        owner.Left.SetNode(shared);
        owner.Right.SetNode(shared);
        IHGEditorMetadataProvider metadata = useDescriptor
            ? new TestMetadataProvider(new HGNodeDescriptor(typeof(NestedFieldGroupOwner), new[]
            {
                HGFieldDescriptor.CreateSlot<NestedFieldGroupOwner, TestFormulaSlot>("Left", target => target.Left,
                    tab: "Basic/Settings/Left"),
                HGFieldDescriptor.CreateSlot<NestedFieldGroupOwner, TestFormulaSlot>("Right", target => target.Right,
                    tab: "Basic/Settings/Right"),
                HGFieldDescriptor.Create<NestedFieldGroupOwner, int>("Declaration", target => target.Declaration,
                    isVisible: target => target.Show, tab: "Basic", foldout: "Basic/Settings"),
                HGFieldDescriptor.Create<NestedFieldGroupOwner, int>("Other", target => target.Other, tab: "Other"),
            }), null) : null;
        var model = new HGModel();
        var view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata);
        Assert.That(view.Diagnostics, Is.Empty);
        var node = view.Nodes[0];
        var outer = node.Rows[0];
        var basic = outer.Children[0];
        var foldout = basic.Children[0];
        var inner = foldout.Children[0];
        var left = inner.Children[0].Children[0];
        var right = inner.Children[1].Children[0];
        Assert.That(outer.Kind, Is.EqualTo(HGRowKind.Tabs));
        Assert.That(basic.Label, Is.EqualTo("Basic"));
        Assert.That(foldout.Kind, Is.EqualTo(HGRowKind.Foldout));
        Assert.That(foldout.Label, Is.EqualTo("Settings"));
        Assert.That(inner.Kind, Is.EqualTo(HGRowKind.Tabs));
        Assert.That(left.FoldoutOwnerRow, Is.SameAs(foldout));
        Assert.That(left.TabPageOwnerRow.TabPageOwnerRow, Is.SameAs(basic));
        Assert.That(left.Depth, Is.EqualTo(foldout.Depth + 2));
        Assert.That(left.IsInputPortVisible, Is.True);
        Assert.That(right.IsTabHidden, Is.True);
        Assert.That(view.Links, Has.Count.EqualTo(2));
        float height = node.Height;
        string outerKey = HGGraph.CollapseKey(node.Id, outer);
        string innerKey = HGGraph.CollapseKey(node.Id, inner);
        string foldoutKey = HGGraph.CollapseKey(node.Id, foldout);
        Assert.That(outerKey, Is.Not.EqualTo(innerKey));
        var selection = new Dictionary<string, string> { [outerKey] = "Other", [innerKey] = "Right" };
        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata, fieldTabs: selection);
        inner = view.Nodes[0].Rows[0].Children[0].Children[0].Children[0];
        Assert.That(inner.Children[1].Children[0].IsTabHidden, Is.True);
        Assert.That(view.Nodes[0].Height, Is.EqualTo(height));

        selection[outerKey] = "Basic";
        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test",
            new Dictionary<string, bool> { [foldoutKey] = true }, metadata: metadata, fieldTabs: selection);
        foldout = view.Nodes[0].Rows[0].Children[0].Children[0];
        Assert.That(foldout.Collapsed, Is.True);
        foreach (var row in HGGraph.AllRows(foldout.Children)) Assert.That(row.Hidden, Is.True);
        Assert.That(view.Links, Has.Count.EqualTo(2));

        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata, fieldTabs: selection);
        inner = view.Nodes[0].Rows[0].Children[0].Children[0].Children[0];
        Assert.That(inner.Children[1].Children[0].IsInputPortVisible, Is.True);
        Assert.That(inner.Children[0].Children[0].IsTabHidden, Is.True);
        Assert.That(owner.Left.Node, Is.SameAs(owner.Right.Node));
    }

    [Test]
    public void GroupPathConflictsFallbackWithoutLosingFieldsAndImplicitParentsNest()
    {
        var view = HGGraph.Build(new HGModel(), new object[] { new ConflictingFieldGroupOwner() }, null, "test", "Test");
        var rows = view.Nodes[0].Rows;
        Assert.That(rows, Has.Count.EqualTo(5));
        for (int i = 0; i < 4; i++) Assert.That(rows[i].Kind, Is.EqualTo(HGRowKind.NoPort));
        Assert.That(view.Diagnostics, Has.Count.EqualTo(4));
        Assert.That(rows[4].Label, Is.EqualTo("Valid"));
        Assert.That(rows[4].Children[0].Label, Is.EqualTo("Child"));
        Assert.That(rows[4].Children[0].Children[0].Depth, Is.EqualTo(rows[4].Depth + 2));
    }

    [Test]
    public void ListDefaultSlotGetsAnEditableConstantListSection()
    {
        var owner = new ListDefaultSlotOwner();
        owner.Input.DefaultObject = new List<TestRootKind> { default, default };
        var view = HGGraph.Build(new HGModel(), new object[] { owner }, null, "test", "Test");
        var rows = view.Nodes[0].Rows;

        Assert.That(rows[0].InputSlot, Is.SameAs(owner.Input));
        Assert.That(rows[1].Kind, Is.EqualTo(HGRowKind.List));
        Assert.That(((HGListItemSource)rows[1].Items).List, Is.SameAs(owner.Input.DefaultObject));
        Assert.That(rows[1].Children, Has.Count.EqualTo(2));
        Assert.That(rows[1].Children[0].ForceEnumButtons, Is.True);
        Assert.That(rows[1].HeaderRow, Is.SameAs(rows[0]));
        Assert.That(rows[0].DefaultListRow, Is.SameAs(rows[1]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FieldTabsHideRowsWithoutDroppingSharedLinksAndKeepStableLayout(bool useDescriptor)
    {
        var owner = new FieldTabOwner();
        var shared = new GraphNode(new PropertyReaderBody());
        owner.First.SetNode(shared);
        owner.Second.SetNode(shared);
        IHGEditorMetadataProvider metadata = useDescriptor
            ? new TestMetadataProvider(new HGNodeDescriptor(typeof(FieldTabOwner), new[]
            {
                HGFieldDescriptor.Create<FieldTabOwner, bool>("Show", target => target.Show),
                HGFieldDescriptor.CreateSlot<FieldTabOwner, TestFormulaSlot>("First", target => target.First, tab: "A"),
                HGFieldDescriptor.CreateSlot<FieldTabOwner, TestFormulaSlot>("Second", target => target.Second,
                    isVisible: target => target.Show, tab: "B"),
                HGFieldDescriptor.Create<FieldTabOwner, int>("Third", target => target.Third, tab: "A"),
            }), null) : null;
        var model = new HGModel();
        var view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata);
        Assert.That(view.Diagnostics, Is.Empty);
        var node = view.Nodes[0];
        var tabs = node.Rows[1];
        Assert.That(node.Rows.Count, Is.EqualTo(2));
        Assert.That(tabs.Kind, Is.EqualTo(HGRowKind.Tabs));
        Assert.That(tabs.ActiveTab, Is.EqualTo("A"));
        Assert.That(tabs.Children[0].Children.Count, Is.EqualTo(2));
        Assert.That(tabs.Children[1].Children[0].IsInputPortVisible, Is.False);
        Assert.That(view.BySlot[owner.First], Is.SameAs(view.BySlot[owner.Second]));
        Assert.That(view.Links.Count, Is.EqualTo(2));
        float height = node.Height;
        string key = HGGraph.CollapseKey(node.Id, tabs);
        var selection = new Dictionary<string, string> { [key] = "B" };

        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata, fieldTabs: selection);
        tabs = view.Nodes[0].Rows[1];
        Assert.That(tabs.ActiveTab, Is.EqualTo("B"));
        Assert.That(tabs.Children[0].Children.TrueForAll(row => row.Hidden && row.IsTabHidden), Is.True);
        Assert.That(tabs.Children[1].Children[0].IsInputPortVisible, Is.True);
        Assert.That(view.Nodes[0].Height, Is.EqualTo(height));
        Assert.That(view.Links.Count, Is.EqualTo(2));
        Assert.That(owner.First.Node, Is.SameAs(shared));
        Assert.That(owner.Second.Node, Is.SameAs(shared));

        // 條件把作用中頁整頁藏掉時回第一頁，不改寫已儲存的選擇；重新出現會回 B。
        owner.Show = false;
        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata, fieldTabs: selection);
        Assert.That(view.Nodes[0].Rows[1].ActiveTab, Is.EqualTo("A"));
        Assert.That(view.Nodes[0].Rows[1].Children.Count, Is.EqualTo(1));
        owner.Show = true;
        view = HGGraph.Build(model, new object[] { owner }, null, "test", "Test", metadata: metadata, fieldTabs: selection);
        Assert.That(view.Nodes[0].Rows[1].ActiveTab, Is.EqualTo("B"));
    }

    [Test]
    public void FrameZoomFitsALargeGraphBelowTheFormerMinimum()
    {
        var viewport = new Vector2(1000f, 600f);
        var bounds = new Rect(-30000f, 20000f, 80000f, 120000f);
        float zoom = HaruGraphWindow.CalculateFrameZoom(viewport, bounds);
        Assert.That(zoom, Is.GreaterThan(0f).And.LessThan(0.01f));
        Assert.That((bounds.width + 80f) * zoom, Is.LessThanOrEqualTo(viewport.x + 0.001f));
        Assert.That((bounds.height + 80f) * zoom, Is.LessThanOrEqualTo(viewport.y + 0.001f));
        Assert.That(HaruGraphWindow.CalculateFrameZoom(viewport, new Rect(0f, 0f, 1f, 1f)),
            Is.EqualTo(1.4f));
    }

    [TestCase(1f)]
    [TestCase(0.1f)]
    [TestCase(0.005f)]
    public void WheelZoomIsProportionalAndReversibleAtOverviewScales(float start)
    {
        float smaller = HaruGraphWindow.CalculateWheelZoom(start, 3f);
        Assert.That(smaller, Is.GreaterThan(0f).And.LessThan(start));
        Assert.That(smaller / start,
            Is.EqualTo(HaruGraphWindow.CalculateWheelZoom(1f, 3f)).Within(0.000001f));
        Assert.That(HaruGraphWindow.CalculateWheelZoom(smaller, -3f), Is.EqualTo(start).Within(start * 0.00001f));
        Assert.That(HaruGraphWindow.CalculateWheelZoom(1.8f, -3f), Is.EqualTo(1.8f));
    }

    [Test]
    public void LayoutKeepsSharedDescendantsUniqueAndRestoresAuthoredPositions()
    {
        var owner = new FieldTabOwner();
        var shared = new GraphNode(new PropertyReaderBody());
        var branchBody = new PropertyReaderBody();
        branchBody.Source.SetNode(shared);
        var branch = new GraphNode(branchBody);
        var orphan = new GraphNode(new PropertyReaderBody());
        owner.First.SetNode(branch);
        owner.Second.SetNode(shared);
        var model = new HGModel();
        var roots = new object[] { owner };
        var orphans = new List<GraphNode> { orphan };

        var view = HGGraph.Build(model, roots, orphans, "layout", "Layout");
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(view.Nodes, Has.Count.EqualTo(4));
        Assert.That(view.Links, Has.Count.EqualTo(3));
        Assert.That(view.BySlot[owner.Second], Is.SameAs(view.BySlot[branchBody.Source]));
        var branchView = view.ByCarrier[branch];
        var sharedView = view.ByCarrier[shared];
        Assert.That(branchView.Pos.x, Is.GreaterThan(view.Nodes[0].Pos.x));
        Assert.That(sharedView.Pos.x, Is.GreaterThan(branchView.Pos.x));
        Assert.That(view.ByCarrier[orphan].Pos.y, Is.GreaterThan(sharedView.Pos.y + sharedView.Height));

        var authoredPosition = new Vector2(120f, 380f);
        shared.Pos = authoredPosition;
        view = HGGraph.Build(model, roots, orphans, "layout", "Layout");
        Assert.That(view.Diagnostics, Is.Empty);
        Assert.That(view.ByCarrier[shared].Pos, Is.EqualTo(authoredPosition));
        Assert.That(view.BySlot[owner.Second], Is.SameAs(view.BySlot[branchBody.Source]));
        Assert.That(view.Links, Has.Count.EqualTo(3));
    }

    [Test]
    public void ListFieldEnumAttributeReachesValueElements()
    {
        var owner = new EnumListOwner { Values = new List<TestRootKind> { default } };
        var view = HGGraph.Build(new HGModel(), new object[] { owner }, null, "test", "Test");
        var list = view.Nodes[0].Rows[0];

        Assert.That(list.Kind, Is.EqualTo(HGRowKind.List));
        Assert.That(list.Children[0].ForceEnumButtons, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TabWidthsCombineFixedAndAutomaticMetadataOnTheNodeGrid(bool useDescriptor)
    {
        var owner = new TabWidthOwner();
        IHGEditorMetadataProvider metadata = useDescriptor
            ? new TestMetadataProvider(new HGNodeDescriptor(typeof(TabWidthOwner), new[]
            {
                HGFieldDescriptor.Create<TabWidthOwner, int>("Fixed", target => target.Fixed, tab: "Fixed", tabWidthUnits: 4),
                HGFieldDescriptor.Create<TabWidthOwner, int>("Wider", target => target.Wider, tab: "Wider", tabWidthUnits: 5),
                HGFieldDescriptor.Create<TabWidthOwner, int>("Automatic", target => target.Automatic, tab: "Automatic"),
            }), null) : null;
        var view = HGGraph.Build(new HGModel(), new object[] { owner }, null, "test", "Test", metadata: metadata);
        Assert.That(view.Diagnostics, Is.Empty);
        var tabs = view.Nodes[0].Rows[0];
        Assert.That(tabs.TabHeaderHeight, Is.EqualTo(HGGraph.RowHeight), "三格寬的節點不預留接點空間，三頁各占一格。");
        Assert.That(tabs.Children[0].LocalY, Is.EqualTo(tabs.LocalY + tabs.TabHeaderHeight));
        HGGraph.MeasureTabWidths(tabs, 400f);
        Assert.That(tabs.Children[0].TabLayoutWidth, Is.EqualTo(80f).Within(0.001f));
        Assert.That(tabs.Children[1].TabLayoutWidth, Is.EqualTo(100f).Within(0.001f));
        Assert.That(tabs.Children[2].TabLayoutWidth, Is.EqualTo(220f).Within(0.001f));
        HGGraph.MeasureTabWidths(tabs, 229f);
        Assert.That(tabs.Children.ConvertAll(page => page.TabLayoutWidth), Is.EqualTo(new[] { 80f, 100f, 40f }));

        var rootStrip = HGGraph.TabStripRect(new HGRow(), 300f);
        Assert.That(rootStrip.xMin, Is.Zero);
        Assert.That(rootStrip.xMax, Is.EqualTo(300f));
        var nested = new HGRow { LeftPad = 30f, Depth = 1 };
        var strip = HGGraph.TabStripRect(nested, 300f);
        Assert.That(strip.xMin % HGGraph.GridSize, Is.Zero);
        Assert.That(strip.xMax % HGGraph.GridSize, Is.Zero);
        Assert.That(strip.xMin, Is.GreaterThanOrEqualTo(nested.LeftPad + HGGraph.IndentWidth));
        Assert.That(strip.xMax, Is.EqualTo(300f));
        var smallest = HGGraph.TabStripRect(nested, HGGraph.GridSize);
        Assert.That(smallest.xMin, Is.Zero);
        Assert.That(smallest.width, Is.EqualTo(HGGraph.GridSize));
    }

    [Test]
    public void TabWidthsDistributeWholeCellsAndWrapInsteadOfSplittingCells()
    {
        var tabs = new HGRow { Kind = HGRowKind.Tabs };
        tabs.Children.Add(new HGRow { Kind = HGRowKind.TabPage });
        tabs.Children.Add(new HGRow { Kind = HGRowKind.TabPage });
        tabs.Children.Add(new HGRow { Kind = HGRowKind.TabPage });
        HGGraph.MeasureTabWidths(tabs, 300f);
        Assert.That(tabs.Children.ConvertAll(page => page.TabLayoutWidth), Is.EqualTo(new[] { 100f, 100f, 100f }));
        HGGraph.MeasureTabWidths(tabs, 209f);
        Assert.That(tabs.Children.ConvertAll(page => page.TabLayoutWidth), Is.EqualTo(new[] { 80f, 60f, 60f }),
            "十格分三頁為 4／3／3，尾端不足一格的 9px 留白。");

        tabs.Children[0].TabWidthUnits = 10;
        tabs.Children[1].TabWidthUnits = 20;
        HGGraph.MeasureTabWidths(tabs, 200f);
        // 三頁各保留一格，剩餘七格按 9／19 配額取整，餘格先補第一頁。
        Assert.That(tabs.Children[0].TabLayoutWidth, Is.EqualTo(80f));
        Assert.That(tabs.Children[1].TabLayoutWidth, Is.EqualTo(100f));
        Assert.That(tabs.Children[2].TabLayoutWidth, Is.EqualTo(20f).Within(0.001f));
        HGGraph.MeasureTabWidths(tabs, 30f);
        Assert.That(tabs.Children.ConvertAll(page => page.TabLayoutWidth), Is.EqualTo(new[] { 20f, 20f, 20f }));
        Assert.That(tabs.TabHeaderHeight, Is.EqualTo(3 * HGGraph.RowHeight));
        Assert.That(tabs.Children[0].TabHeaderOffset, Is.EqualTo(Vector2.zero));
        Assert.That(tabs.Children[1].TabHeaderOffset, Is.EqualTo(new Vector2(0f, HGGraph.RowHeight)));
        Assert.That(tabs.Children[2].TabHeaderOffset, Is.EqualTo(new Vector2(0f, 2 * HGGraph.RowHeight)));

        foreach (var page in tabs.Children) page.TabWidthUnits = 1;
        HGGraph.MeasureTabWidths(tabs, 200f);
        Assert.That(tabs.Children.ConvertAll(page => page.TabLayoutWidth), Is.EqualTo(new[] { 20f, 20f, 20f }),
            "全部明確指定時不擅自拉寬，未用滿的空間保留在列尾。");
    }

    [TestCase("Input", false)]
    [TestCase("Amount", false)]
    [TestCase("Input", true)]
    public void DiagnosticNavigationRevealsTheExactFieldAndItsContainers(string field, bool legacySlot)
    {
        var owner = ScriptableObject.CreateInstance<PropertyDocumentOwner>();
        var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
        try
        {
            owner.Document = new PropertyDocument();
            var other = new GraphNode(new DiagnosticFieldBody());
            var target = new GraphNode(new DiagnosticFieldBody());
            string otherId = other.EnsureId();
            string targetId = target.EnsureId();
            owner.Document.Orphans.Add(other);
            owner.Document.Orphans.Add(target);
            foreach (string id in new[] { otherId, targetId })
            {
                var group = new GraphNodeGroup("tim:*", "Group", new Rect(0, 0, 400, 300), 0);
                group.AddTab("A");
                group.AddTab("B");
                group.SetMember(id, true);
                group.SetActiveTab(1);
                group.SetCollapsed(true);
                owner.Document.ViewState.AddNodeGroup(group);
                owner.Document.ViewState.SetFolded(id + "#/Details/#Settings", true);
                owner.Document.ViewState.SetFolded(id + "#/Details/Values", true);
            }
            Assert.That(window.BindDocument(owner, PropertyBinding(), PropertyContext()), Is.True);
            var commands = window.GetDocumentCommands();
            commands.Query();
            string path = "/Details/Values[0]/" + field;
            var view = window.NodeOfId(targetId);
            var before = System.Linq.Enumerable.Single(HGGraph.AllRows(view.Rows), row => row.Path == path);
            Assert.That(view.Hidden, Is.True);
            Assert.That(before.Hidden, Is.True);
            var diagnostic = new GraphDiagnostic("test.field", GraphDiagnosticSeverity.Error, "Field error",
                new GraphDiagnosticLocation(focusId: "tim:*", nodeId: targetId, fieldPath: legacySlot ? null : path));
            var issue = new HGIssue(diagnostic, "Field", null, legacySlot ? before.InputSlot : null, null);

            window.JumpTo(issue);
            commands.Query();
            view = window.NodeOfId(targetId);
            var revealed = System.Linq.Enumerable.Single(HGGraph.AllRows(view.Rows), row => row.Path == path);
            Assert.That(view.Hidden, Is.False);
            Assert.That(revealed.Hidden, Is.False);
            Assert.That(revealed.TabPageOwnerRow.Label, Is.EqualTo("Advanced"));
            Assert.That(revealed.TabPageOwnerRow.TabStripRow.ActiveTab, Is.EqualTo("Advanced"));
            Assert.That(revealed.ItemOwnerRow.Collapsed, Is.False);
            Assert.That(revealed.FoldoutOwnerRow.Collapsed, Is.False);
            Assert.That(window.NodeOfId(otherId).Hidden, Is.True, "同名欄位不可展開到另一顆節點。");
            Assert.That(commands.Query().IsDirty, Is.True);
            Assert.That(owner.Document.ViewState.NodeGroups[1].Collapsed, Is.True, "定位只改工作副本。");
            Assert.That(commands.Cancel(), Is.EqualTo(HGSessionCommandResult.Changed));
            commands.Query();
            Assert.That(window.NodeOfId(targetId).Hidden, Is.True);
            Assert.That(System.Linq.Enumerable.Single(HGGraph.AllRows(window.NodeOfId(targetId).Rows), row => row.Path == path).Hidden, Is.True);
        }
        finally
        {
            window.GetDocumentCommands()?.Cancel();
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void MetadataDiagnosticsWithMatchingFieldPathsKeepTheirOwningNodeIds()
    {
        var first = new GraphNode(new DiagnosticMetadataBody());
        var second = new GraphNode(new DiagnosticMetadataBody());
        var view = HGGraph.Build(new HGModel(), Array.Empty<object>(), new List<GraphNode> { first, second }, "test", "Test");
        var issues = view.Diagnostics.FindAll(diagnostic => diagnostic.Code == "graphkit.metadata.tab-invalid");
        Assert.That(issues.Count, Is.EqualTo(2));
        Assert.That(issues[0].Location.NodeId, Is.EqualTo(first.Id));
        Assert.That(issues[1].Location.NodeId, Is.EqualTo(second.Id));
        Assert.That(issues[0].Location.FieldPath, Is.EqualTo("/Value"));
        Assert.That(issues[1].Location.FieldPath, Is.EqualTo("/Value"));
    }

    [Test]
    public void FormulaSlotListElementsHideLabel()
    {
        var owner = new FormulaSlotListOwner { Values = new List<TestFormulaSlot> { new() } };
        var view = HGGraph.Build(new HGModel(), new object[] { owner }, null, "test", "Test");
        var element = view.Nodes[0].Rows[0].Children[0];

        Assert.That(element.InputSlot, Is.SameAs(owner.Values[0]));
        Assert.That(element.HideLabel, Is.True);
    }

    [Test]
    public void GraphViewStateReportsOnlyRealChangesAndKeepsFoldOverridesExclusive()
    {
        var state = new GraphViewState();

        Assert.That(state.SetHidden("node#/a", true), Is.True);
        Assert.That(state.SetHidden("node#/a", true), Is.False);
        Assert.That(state.Hidden, Is.EquivalentTo(new[] { "node#/a" }));
        Assert.That(state.SetHidden("node#/a", false), Is.True);
        Assert.That(state.Hidden, Is.Empty);

        Assert.That(state.SetFolded("node#/list", false), Is.True);
        Assert.That(state.Unfolded, Is.EquivalentTo(new[] { "node#/list" }));
        Assert.That(state.SetFolded("node#/list", true), Is.True);
        Assert.That(state.Folded, Is.EquivalentTo(new[] { "node#/list" }));
        Assert.That(state.Unfolded, Is.Empty);

        Assert.That(state.SetNoteCollapsed("node", true), Is.True);
        Assert.That(state.SetNoteCollapsed("", true), Is.False);
        Assert.That(state.NotesCollapsed, Is.EquivalentTo(new[] { "node" }));
        Assert.That(state.SetFieldTab("node#/#tabs/", "B"), Is.True);
        Assert.That(state.SetFieldTab("node#/#tabs/", "B"), Is.False);
        Assert.That(state.SetFieldTab("", "A"), Is.False);
        Assert.That(state.FieldTabs.Count, Is.EqualTo(1));
    }

    [Test]
    public void GraphViewStateSurvivesDocumentDeepCopyAsAnIndependentCopy()
    {
        var document = new ViewStateDocument();
        ((IGraphViewStateOwner)document).ViewState.SetHidden("node#/a", true);
        ((IGraphViewStateOwner)document).ViewState.SetFieldTab("node#/#tabs/", "B");

        var copy = GraphDeepCopy.Copy(document);
        var copied = ((IGraphViewStateOwner)copy).ViewState;
        Assert.That(copied.Hidden, Is.EquivalentTo(new[] { "node#/a" }));

        copied.SetHidden("node#/b", true);
        Assert.That(((IGraphViewStateOwner)document).ViewState.Hidden, Has.Count.EqualTo(1));
        Assert.That(copied.FieldTabs[0].Tab, Is.EqualTo("B"));
        copied.SetFieldTab("node#/#tabs/", "A");
        Assert.That(((IGraphViewStateOwner)document).ViewState.FieldTabs[0].Tab, Is.EqualTo("B"));
        var restored = JsonUtility.FromJson<GraphViewState>(JsonUtility.ToJson(copied));
        Assert.That(restored.FieldTabs[0].Key, Is.EqualTo("node#/#tabs/"));
        Assert.That(restored.FieldTabs[0].Tab, Is.EqualTo("A"));
    }

    [Test]
    public void NodeGroupsSurviveDocumentDeepCopyAsIndependentCopies()
    {
        var document = new ViewStateDocument();
        var group = new GraphNodeGroup("tim:*", "群組", new Rect(20f, 40f, 400f, 240f), 2);
        Assert.That(group.SetMember("node-a", true), Is.True);
        Assert.That(group.SetMember("node-a", true), Is.False);
        ((IGraphViewStateOwner)document).ViewState.AddNodeGroup(group);

        var copy = GraphDeepCopy.Copy(document);
        var copied = ((IGraphViewStateOwner)copy).ViewState.NodeGroups[0];
        Assert.That(copied, Is.Not.SameAs(group));
        Assert.That(copied.Id, Is.EqualTo(group.Id));
        Assert.That(copied.Rect, Is.EqualTo(group.Rect));
        Assert.That(copied.ColorIndex, Is.EqualTo(2));
        Assert.That(copied.UseCustomColor, Is.False);
        Assert.That(copied.Collapsed, Is.False);
        Assert.That(copied.SetCollapsed(true), Is.True);
        Assert.That(copied.SetCollapsed(true), Is.False);
        Assert.That(group.Collapsed, Is.False);
        Assert.That(copied.Members, Is.EquivalentTo(new[] { "node-a" }));

        Assert.That(copied.SetCustomColor(new Color(0.2f, 0.4f, 0.6f, 0.5f)), Is.True);
        Assert.That(copied.CustomColor.a, Is.EqualTo(1f));
        Assert.That(group.UseCustomColor, Is.False);
        copied.SetMember("node-b", true);
        copied.SetRect(new Rect(0f, 0f, 200f, 100f));
        Assert.That(group.Members, Has.Count.EqualTo(1));
        Assert.That(group.Rect, Is.EqualTo(new Rect(20f, 40f, 400f, 240f)));
    }

    [Test]
    public void NodeGroupTabsPlaceNewMembersOnActiveTabAndReturnThemToFirstTabOnRemove()
    {
        var group = new GraphNodeGroup("tim:*", "群組", new Rect(0f, 0f, 400f, 240f), 0);
        group.SetMember("node-a", true);
        Assert.That(group.HasTabs, Is.False);
        Assert.That(group.IsOnActiveTab("node-a"), Is.True);

        group.AddTab("分頁 1");
        int second = group.AddTab("分頁 2");
        Assert.That(group.HasTabs, Is.True);
        Assert.That(group.SetActiveTab(second), Is.True);
        Assert.That(group.SetMember("node-b", true), Is.True);
        Assert.That(group.TabOf("node-a"), Is.EqualTo(0));
        Assert.That(group.TabOf("node-b"), Is.EqualTo(second));
        Assert.That(group.IsOnActiveTab("node-a"), Is.False);

        var document = new ViewStateDocument();
        ((IGraphViewStateOwner)document).ViewState.AddNodeGroup(group);
        var copied = ((IGraphViewStateOwner)GraphDeepCopy.Copy(document)).ViewState.NodeGroups[0];
        Assert.That(copied.ActiveTab, Is.EqualTo(second));
        Assert.That(copied.TabOf("node-b"), Is.EqualTo(second));
        Assert.That(copied.SetMemberTab("node-b", 0), Is.True);
        Assert.That(group.TabOf("node-b"), Is.EqualTo(second));

        Assert.That(group.RemoveTab(second), Is.True);
        Assert.That(group.HasTabs, Is.False);
        Assert.That(group.Tabs, Has.Count.EqualTo(1));
        Assert.That(group.Tabs[0].Name, Is.EqualTo("分頁 1"));
        Assert.That(group.RemoveTab(0), Is.False);
        Assert.That(group.TabOf("node-b"), Is.EqualTo(0));
        Assert.That(group.Members, Is.EquivalentTo(new[] { "node-a", "node-b" }));
    }

    [Test]
    public void ListAppendRegistersOnlyForEditableSlotLists()
    {
        var registry = new HGPortRegistry(1);
        var list = new List<TestFormulaSlot>();
        var key = new HGPortKey("owner", "/items", HGPortRole.Input);

        Assert.That(registry.AddListAppend(key, list, typeof(TestFormulaSlot), null, Presentation(new object())), Is.True);
        Assert.That(registry.TryGet(key, out var port), Is.True);
        Assert.That(port.InputSlot, Is.InstanceOf<TestFormulaSlot>());
        Assert.That(port.CanStart, Is.True);
        Assert.That(list, Is.Empty);

        Assert.That(registry.AddListAppend(new HGPortKey("owner", "/array", HGPortRole.Input), new TestFormulaSlot[0],
            typeof(TestFormulaSlot), null, Presentation(new object())), Is.False);
        Assert.That(registry.AddListAppend(new HGPortKey("owner", "/values", HGPortRole.Input), new List<int>(),
            typeof(int), null, Presentation(new object())), Is.False);
        Assert.That(registry.AddListAppend(new HGPortKey("owner", "/writes", HGPortRole.Input), new List<PropertyWriteSlot>(),
            typeof(PropertyWriteSlot), null, Presentation(new object())), Is.False);
    }

    [Test]
    public void ListAppendCommitAddsThePrototypeOnce()
    {
        var list = new List<TestFormulaSlot>();
        var prototype = new TestFormulaSlot();
        var binding = new HGListAppendPortBinding(prototype, new HGListItemSource(list, typeof(TestFormulaSlot)));

        Assert.That(binding.Commit(), Is.True);
        Assert.That(binding.Commit(), Is.True);
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0], Is.SameAs(prototype));
    }

    [Test]
    public void ListAppendCycleAsksWhetherTheSourceReachesTheListOwner()
    {
        var owner = new GraphNode();
        var unrelated = new GraphNode();

        Assert.That(HGListAppend.WouldCreateCycle(owner, owner), Is.True);
        Assert.That(HGListAppend.WouldCreateCycle(owner, unrelated), Is.False);
        Assert.That(HGListAppend.WouldCreateCycle(null, owner), Is.False);
    }

    [Test]
    public void PropertySlotListAppendIsAWriteOutput()
    {
        var registry = new HGPortRegistry(1);
        var owner = new GraphNode();
        owner.EnsureId();
        var items = new HGListItemSource(new List<PropertyWriteSlot>(), typeof(PropertyWriteSlot));
        var binding = new HGListAppendWriteBinding(new PropertyWriteSlot(), owner, items);
        var key = new HGPortKey("owner", "/writes", HGPortRole.Input);

        Assert.That(registry.AddListAppend(key, binding, new HGDelegatePortPolicy(() => true), Presentation(new object())), Is.True);
        Assert.That(registry.TryGet(new HGPortKey("owner", "/writes", HGPortRole.Output), out var port), Is.True);
        Assert.That(port.IsOutput, Is.True);
        Assert.That(port.Source, Is.InstanceOf<HGPropertyWriteSource>());
        Assert.That(items.Count, Is.EqualTo(0));
    }

    [Test]
    public void DescriptorDrawerMeasuresWithItsConfiguredLabelWidth()
    {
        var drawer = new TestValueDrawer();
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, labelWidthUnits: 4);
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { field }), drawer);
        HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test", metadata: metadata);

        Assert.That(drawer.MeasuredWidth, Is.EqualTo(200f));
    }

    [Test]
    public void MetadataProvidersKeepTheirDrawersScopedToTheirOwnContext()
    {
        var firstDrawer = new TestValueDrawer();
        var secondDrawer = new TestValueDrawer();
        var field = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value);
        var descriptor = new HGNodeDescriptor(typeof(TestValueOwner), new[] { field });

        var first = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "first", "First",
            metadata: new TestMetadataProvider(descriptor, firstDrawer));
        var second = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "second", "Second",
            metadata: new TestMetadataProvider(descriptor, secondDrawer));

        Assert.That(first.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(firstDrawer));
        Assert.That(second.Nodes[0].Rows[0].ValueDrawer, Is.SameAs(secondDrawer));
        Assert.That(first.Nodes[0].Rows[0].ValueDrawer, Is.Not.SameAs(second.Nodes[0].Rows[0].ValueDrawer));
    }

    [Test]
    public void DescriptorVisibilityHidesRowsAndFailsOpenWithOneDiagnostic()
    {
        var hidden = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, isVisible: _ => false);
        var hiddenView = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { hidden }), null));
        Assert.That(hiddenView.Nodes[0].Rows, Is.Empty);

        var failing = HGFieldDescriptor.Create<TestValueOwner, int>("count", target => target.Count,
            (target, value) => target.Count = value, isVisible: _ => throw new InvalidOperationException("broken"));
        var failingView = HGGraph.Build(new HGModel(), new object[] { new TestValueOwner() }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestValueOwner), new[] { failing }), null));
        var report = new HGReport();
        report.ReplaceGraphViewDiagnostics(failingView.Diagnostics);

        Assert.That(failingView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(failingView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(failingView.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.visibility-failed"));
        Assert.That(report.ErrorCount, Is.EqualTo(1));
    }

    [Test]
    public void ReflectionShowIfUsesCachedConditionsAndReportsFailuresInTheGraph()
    {
        var hiddenView = HGGraph.Build(new HGModel(), new object[] { new ShowIfOwner() }, null, "test", "Test");
        var shownView = HGGraph.Build(new HGModel(), new object[] { new ShowIfOwner { Show = true } }, null, "test", "Test");
        var invalidView = HGGraph.Build(new HGModel(), new object[] { new InvalidShowIfOwner() }, null, "test", "Test");
        var nonBooleanView = HGGraph.Build(new HGModel(), new object[] { new NonBooleanShowIfOwner() }, null, "test", "Test");
        var throwingView = HGGraph.Build(new HGModel(), new object[] { new ThrowingShowIfOwner() }, null, "test", "Test");

        Assert.That(hiddenView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(shownView.Nodes[0].Rows, Has.Count.EqualTo(2));
        Assert.That(invalidView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(invalidView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(nonBooleanView.Diagnostics[0].Message, Does.Contain("Visible"));
        Assert.That(throwingView.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(throwingView.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(throwingView.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.visibility-failed"));
    }

    [Test]
    public void DescriptorSlotUsesTheExistingInputPortRow()
    {
        var slot = new TestSlot();
        var field = HGFieldDescriptor.CreateSlot<TestSlotOwner, TestSlot>("input", target => target.Input, "Input");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestSlotOwner { Input = slot } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.InputPort));
        Assert.That(view.Nodes[0].Rows[0].InputSlot, Is.SameAs(slot));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(field.ReadOnly, Is.False);
    }

    [Test]
    public void DescriptorSlotFactoryNormalizesOnlyTheWorkingTarget()
    {
        var field = HGFieldDescriptor.CreateSlotWithFactory<TestSlotOwner, TestSlot>("input", target => target.Input,
            (target, value) => target.Input = value, _ => new TestSlot(), "Input");
        var target = new TestSlotOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null));

        Assert.That(target.Input, Is.Not.Null);
        Assert.That(view.Normalized, Is.True);
        Assert.That(view.Nodes[0].Rows[0].InputSlot, Is.SameAs(target.Input));
    }

    [Test]
    public void DescriptorFactoryFailureReportsOneLocationAndDoesNotWrite()
    {
        var field = HGFieldDescriptor.CreateSlotWithFactory<TestSlotOwner, TestSlot>("input", target => target.Input,
            (target, value) => target.Input = value, _ => throw new InvalidOperationException("broken"), "Input");
        var target = new TestSlotOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestSlotOwner), new[] { field }), null));

        Assert.That(target.Input, Is.Null);
        Assert.That(view.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(view.Diagnostics[0].Code, Is.EqualTo("graphkit.metadata.factory-failed"));
        Assert.That(view.Diagnostics[0].Location.FieldPath, Is.EqualTo("/input"));
    }

    [Test]
    public void DescriptorGroupControlsExpansionWithoutLeafTypeInference()
    {
        var field = HGFieldDescriptor.CreateGroup<TestGroupOwner, TestValueOwner>("child", target => target.Child, "Child");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestGroupOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestGroupOwner { Child = new TestValueOwner { Count = 2 } } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.Group));
        Assert.That(view.Nodes[0].Rows[0].Descriptor, Is.SameAs(field));
        Assert.That(view.Nodes[0].Rows[0].Children, Is.Not.Empty);
    }

    [Test]
    public void DescriptorListUsesTheExistingListItemSource()
    {
        var field = HGFieldDescriptor.CreateList<TestListOwner, int>("values", target => target.Values, "Values");
        var metadata = new TestMetadataProvider(new HGNodeDescriptor(typeof(TestListOwner), new[] { field }), null);
        var view = HGGraph.Build(new HGModel(), new object[] { new TestListOwner { Values = new List<int> { 2 } } }, null,
            "test", "Test", metadata: metadata);

        Assert.That(view.Nodes[0].Rows, Has.Count.EqualTo(1));
        Assert.That(view.Nodes[0].Rows[0].Kind, Is.EqualTo(HGRowKind.List));
        Assert.That(view.Nodes[0].Rows[0].Items, Is.TypeOf<HGListItemSource>());
        Assert.That(((HGListItemSource)view.Nodes[0].Rows[0].Items).ElementType, Is.EqualTo(typeof(int)));
        Assert.That(field.ReadOnly, Is.False);
    }

    [Test]
    public void DescriptorListFactoryNormalizesOnlyTheWorkingTarget()
    {
        var field = HGFieldDescriptor.CreateListWithFactory<TestListOwner, List<int>, int>("values", target => target.Values,
            (target, value) => target.Values = value, _ => new List<int>(), "Values");
        var target = new TestListOwner();
        var view = HGGraph.Build(new HGModel(), new object[] { target }, null, "test", "Test",
            metadata: new TestMetadataProvider(new HGNodeDescriptor(typeof(TestListOwner), new[] { field }), null));

        Assert.That(target.Values, Is.Not.Null);
        Assert.That(view.Normalized, Is.True);
        Assert.That(view.Nodes[0].Rows[0].Items, Is.TypeOf<HGListItemSource>());
    }

    [Test]
    public void GraphDiagnosticKeepsOnlyStableLocationIdentifiers()
    {
        var location = new GraphDiagnosticLocation("document", "focus", "node", "token", "/field");
        var diagnostic = new GraphDiagnostic("test.invalid", GraphDiagnosticSeverity.Error, "Invalid value", location, "Fix it");

        Assert.That(diagnostic.Code, Is.EqualTo("test.invalid"));
        Assert.That(diagnostic.Severity, Is.EqualTo(GraphDiagnosticSeverity.Error));
        Assert.That(diagnostic.Location.FieldPath, Is.EqualTo("/field"));
        Assert.Throws<ArgumentException>(() => new GraphDiagnostic("", GraphDiagnosticSeverity.Error, "Invalid value"));
    }

    [Test]
    public void ReportReplacesPortResolutionDiagnosticsWithoutRemovingStructuralIssues()
    {
        var report = new HGReport();
        report.Issues.Add(new HGIssue(new GraphDiagnostic("graphkit.slot.invalid", GraphDiagnosticSeverity.Error,
            "Structural failure"), "Slot", null, null, null));
        report.ReplaceGraphViewDiagnostics(new[]
        {
            new GraphDiagnostic("graphkit.port-resolution.input-unresolved", GraphDiagnosticSeverity.Error,
                "Input unresolved", new GraphDiagnosticLocation(focusId: "focus", fieldPath: "/input")),
        });
        report.ReplaceGraphViewDiagnostics(new[]
        {
            new GraphDiagnostic("graphkit.metadata.visibility-failed", GraphDiagnosticSeverity.Warning,
                "Visibility failed", new GraphDiagnosticLocation(fieldPath: "/field")),
        });

        Assert.That(report.Issues, Has.Count.EqualTo(2));
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.slot.invalid"), Is.True);
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.port-resolution.input-unresolved"), Is.False);
        Assert.That(report.Issues.Exists(issue => issue.Code == "graphkit.metadata.visibility-failed"), Is.True);
    }

    private static HGPort AggregatePort(int generation, string owner = "aggregate")
        => new HGPort(new HGPortKey(owner, "/items", HGPortRole.Aggregate),
            HGAggregatePortBinding.Instance,
            new HGDelegatePortPolicy(() => false),
            new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
                () => true, () => false), generation);

    private static IHGPortPresentation Presentation(object owner)
        => new HGDelegatePortPresentation(owner, () => Vector2.zero, () => Rect.zero,
            () => true, () => false);

    private sealed class TestProvider : IHGEditorExtensionProvider, IHGEditorDiagnosticProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context)
            => context.AddAggregate(new HGPortKey("test-adapter", "/aggregate", HGPortRole.Aggregate),
                 HGPortTests.Presentation(new object()));

        public void CollectDiagnostics(UnityEngine.Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
            => diagnostics.Add(new GraphDiagnostic("test.domain.invalid", GraphDiagnosticSeverity.Error, "Invalid test domain"));
    }

    private sealed class TestRootAdapter : IHGRootAdapter
    {
        private readonly ArrayList items = new();
        private readonly List<HGRootGroupView> roots = new();

        public object Root { get; }
        public object AddedKey { get; private set; }
        public object RemovedRoot { get; private set; }

        public TestRootAdapter(params object[] rootKeys)
        {
            if (rootKeys == null || rootKeys.Length == 0) rootKeys = new object[] { "custom" };
            foreach (object key in rootKeys)
                roots.Add(new HGRootGroupView { Root = new object(), RootKey = key, Items = items,
                    StableId = key is SameLabelRootKey ? Guid.NewGuid().ToString("N") : null });
            Root = roots[0].Root;
        }

        public IReadOnlyList<object> RootKeys(IGraphDocument document, UnityEngine.Object owner)
        {
            var keys = new List<object>();
            foreach (var root in roots) keys.Add(root.RootKey);
            return keys;
        }

        public IReadOnlyList<HGRootGroupView> ReadRoots(IGraphDocument document)
            => roots;

        public HGRootGroupView AddRoot(IGraphDocument document, object rootKey)
        {
            AddedKey = rootKey;
            return new HGRootGroupView { Root = Root, RootKey = rootKey, Items = items };
        }

        public bool RemoveRoot(IGraphDocument document, object root)
        {
            RemovedRoot = root;
            return ReferenceEquals(root, Root);
        }

        public Type ItemType(IGraphDocument document) => typeof(TestSlot);
        public object CreateItem(IGraphDocument document) => new TestSlot();
    }

    private enum TestRootKind
    {
        Enum,
    }

    private sealed class SameLabelRootKey
    {
        public override string ToString() => "same";
    }

    private sealed class TestMetadataProvider : IHGEditorMetadataProvider
    {
        private readonly HGNodeDescriptor descriptor;
        private readonly IHGValueDrawer drawer;

        public TestMetadataProvider(HGNodeDescriptor descriptor, IHGValueDrawer drawer)
        {
            this.descriptor = descriptor;
            this.drawer = drawer;
        }

        public bool TryGetNodeDescriptor(Type nodeType, out HGNodeDescriptor result)
        {
            result = nodeType == descriptor.NodeType ? descriptor : null;
            return result != null;
        }

        public bool TryGetValueDrawer(Type valueType, out IHGValueDrawer result)
        {
            result = valueType == typeof(int) ? drawer : null;
            return result != null;
        }
    }

    private sealed class TestValueDrawer : IHGValueDrawer
    {
        public TestValueOwner MeasuredTarget;
        public float MeasuredWidth;

        public float Measure(in HGValueDrawerContext context, float width)
        {
            MeasuredTarget = context.Target as TestValueOwner;
            MeasuredWidth = width;
            return 35f;
        }

        public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value)
            => HGValueDrawerResult.Unchanged(value);
    }

    private sealed class TestSlot : GraphSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }

    [HGEnum]
    private sealed class EnumButtonSlot : GraphSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }

    private sealed class EnumButtonSlotOwner
    {
        public EnumButtonSlot Input;
    }

    [HGEnum]
    private sealed class ListDefaultSlot : FormulaSlotBase
    {
        private GraphNode node;
        private object defaultValue;

        public override GraphNode Node => node;
        public override Type ResultType => typeof(List<TestRootKind>);
        public override Type PackType => typeof(object);
        public override object DefaultObject { get => defaultValue; set => defaultValue = value; }
        public override Type BodyBaseType => null;
        public override Type AssetBaseType => null;
        public override void SetNode(GraphNode value) => node = value;
    }

    private sealed class ListDefaultSlotOwner
    {
        public ListDefaultSlot Input = new();
    }

    private sealed class EnumListOwner
    {
        [HGEnum] public List<TestRootKind> Values;
    }

    private sealed class FormulaSlotListOwner
    {
        public List<TestFormulaSlot> Values;
    }

    [Serializable]
    private sealed class ViewStateDocument : IGraphViewStateOwner
    {
        [SerializeField] private GraphViewState viewState = new();
        GraphViewState IGraphViewStateOwner.ViewState => viewState;
    }

    private class TestFormulaSlot : FormulaSlotBase
    {
        private GraphNode node;
        private object defaultValue;

        public override bool AcceptsProperty(GraphProperty property) => AcceptsCompatibleSlot(property?.Slot);
        public override bool AcceptsToken(GraphToken token) => AcceptsCompatibleSlot(token?.Slot);
        public override bool AcceptsBody(GraphNodeContent body) => body is PropertyReaderBody;

        public override GraphNode Node => node;
        public override Type ResultType => typeof(int);
        public override Type PackType => typeof(object);
        public override object DefaultObject { get => defaultValue; set => defaultValue = value; }
        public override Type BodyBaseType => null;
        public override Type AssetBaseType => null;
        public override void SetNode(GraphNode value) => node = value;
    }

    private sealed class SettingsTestSlot : TestFormulaSlot
    {
        [HGSlotSetting] public bool RoundUp;
    }

    private sealed class SlotSettingsOwner
    {
        public SettingsTestSlot A = new();
        public SettingsTestSlot B = new();
        public TestFormulaSlot Plain = new();
    }

    private sealed class CompatibleTestFormulaSlot : TestFormulaSlot
    {
        public bool CompatibleResults = true;
        protected override bool AllowCompatibleResult => CompatibleResults;
    }

    private sealed class CompatibleTokenReaderBody : GraphNodeContent
    {
        public CompatibleTestFormulaSlot Input = new();
    }

    private sealed class TestDocumentOwner : ScriptableObject
    {
        public TestDocument Document;
    }

    private sealed class TestMultiDocumentOwner : ScriptableObject
    {
        public TestDocument First;
        public TestDocument Second;
    }

    private sealed class TestNoDocumentOwner : ScriptableObject { }

    private sealed class BoolButtonOwner
    {
        [HGBool("區分", "忽略")] public bool CaseSensitive;
        [HGEnum] public bool EnumMarked;
    }

    private sealed class FoldoutOwner
    {
        public int Plain;
        [HGFoldout("A")] public int First;
        [HGFoldout("B")] public int Second;
        [HGFoldout("A")] public int Third;
        [HGFoldout("x//y")] public int Reserved;
    }

    private sealed class NestedFieldGroupOwner
    {
        [HGTab("Basic/Settings/Left")] public TestFormulaSlot Left = new();
        [HGTab("Basic/Settings/Right")] public TestFormulaSlot Right = new();
        // 型別宣告在子路徑後面，且欄位不可見時仍須保持 Basic 是頁面。
        [HGTab("Basic"), HGFoldout("Basic/Settings"), HGShowIf(nameof(Show))] public int Declaration;
        [HGTab("Other")] public int Other;
        public bool Show => false;
    }

    private sealed class ConflictingFieldGroupOwner
    {
        [HGTab("Conflict")] public int Tab;
        [HGFoldout("Conflict")] public int Foldout;
        [HGFoldout("Conflict/Child")] public int Descendant;
        [HGTab("A"), HGFoldout("B")] public int Unrelated;
        [HGFoldout("Valid/Child")] public int Nested;
    }

    private sealed class FieldTabOwner
    {
        public bool Show = true;
        [HGTab("A")] public TestFormulaSlot First = new();
        [HGTab("B"), HGShowIf(nameof(Show))] public TestFormulaSlot Second = new();
        [HGTab("A")] public int Third;
    }

    [HGNode("Tab Width Test", Width = 3)]
    private sealed class TabWidthOwner
    {
        [HGTab("Fixed", Width = 4)] public int Fixed;
        [HGTab("Wider", Width = 5)] public int Wider;
        [HGTab("Automatic")] public int Automatic;
    }

    private sealed class TestValueOwner
    {
        public int Count;
        public int Ignored;
        public string Text;
    }

    private sealed class ShowIfOwner
    {
        public bool Show;
        [HGShowIf(nameof(Show))] public int Conditional;
    }

    private sealed class InvalidShowIfOwner
    {
        [HGShowIf("Missing")] public int Conditional;
    }

    private sealed class NonBooleanShowIfOwner
    {
        [HGShowIf(nameof(Visible))] public int Conditional;
        private string Visible => "not a bool";
    }

    private sealed class ThrowingShowIfOwner
    {
        [HGShowIf(nameof(Throws))] public int Conditional;
        private bool Throws => throw new InvalidOperationException("broken");
    }

    private sealed class TestSlotOwner
    {
        public TestSlot Input;
    }

    private sealed class TestGroupOwner
    {
        public TestValueOwner Child;
    }

    private sealed class TestListOwner
    {
        public List<int> Values;
    }

    private sealed class RemovalDocumentOwner : ScriptableObject
    {
        public RemovalDocument Document;
    }

    [Serializable]
    private sealed class RemovalDocument : IGraphDocument
    {
        public List<RemovalRoot> Groups = new();
        [SerializeReference] private List<GraphNode> orphans = new();
        public List<GraphNode> Orphans => orphans;
        public IList Roots => Groups;
        public bool IsValidated { get; private set; }
        public Type PackType => typeof(object);
        public Type ItemSlotType => typeof(RemovalSlot);
        public string RootChip => "Timing";
        public string RootNoun => "Timing";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Removal Test";
        public void InvalidateValidation() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => GraphDeepCopy.Copy(this);
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { "first", "second" };
        public object KeyOf(object root) => ((RemovalRoot)root).Key;
        public string TitleOf(object root) => ((RemovalRoot)root).Key;
        public IList ItemsOf(object root) => ((RemovalRoot)root).Items;
        public object AddRoot(object key)
        {
            var root = new RemovalRoot { Key = (string)key };
            Groups.Add(root);
            return root;
        }
    }

    [Serializable]
    private sealed class RemovalRoot
    {
        public string Key;
        [SerializeReference] public List<RemovalSlot> Items = new();
    }

    [Serializable]
    private sealed class RemovalSlot : GraphSlotBase, IOrphanPool
    {
        [SerializeReference] private GraphNode node;
        [SerializeReference] private List<GraphNode> orphans = new();
        public List<GraphNode> Orphans => orphans;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }

    [Serializable]
    private sealed class RemovalBody : GraphNodeContent
    {
        public RemovalSlot Source = new();
    }

    [Serializable]
    private sealed class RemovalListBody : GraphNodeContent
    {
        public List<RemovalSlot> Actions = new();
        public List<RemovalEntry> Entries = new();
        public RemovalSlot Shared = new();
    }

    [Serializable]
    private sealed class RemovalEntry
    {
        public RemovalSlot First;
        public RemovalSlot Second;
    }

    private sealed class NonSlotRoot
    {
        public int[] Items;
    }

    private sealed class NonSlotDocument : IGraphDocument
    {
        public List<GraphNode> Orphans { get; } = new();
        public IList Roots { get; } = new ArrayList();
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(int);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Non-slot roots";

        public void InvalidateValidation() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => new NonSlotDocument();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { "first", "second" };
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => (root as NonSlotRoot)?.Items;
        public object AddRoot(object key)
        {
            var root = new NonSlotRoot { Items = Array.Empty<int>() };
            Roots.Add(root);
            return root;
        }
    }

    /// <summary>另一個族：用來製造「切換定義之後寫入線必然不相容」的情境。</summary>
    private sealed class TestFormulaSlotB : FormulaSlotBase
    {
        private GraphNode node;
        private object defaultValue;

        public override GraphNode Node => node;
        public override Type ResultType => typeof(int);
        public override Type PackType => typeof(object);
        public override object DefaultObject { get => defaultValue; set => defaultValue = value; }
        public override Type BodyBaseType => null;
        public override Type AssetBaseType => null;
        public override void SetNode(GraphNode value) => node = value;
    }

    private sealed class PropertyWriteSlot : PropertySlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override Type FamilyType => typeof(TestFormulaSlot);
    }

    private sealed class PropertyWriterBody : GraphNodeContent
    {
        public PropertyWriteSlot Target = new PropertyWriteSlot();
    }

    private sealed class PropertyReaderBody : GraphNodeContent
    {
        public TestFormulaSlot Source = new TestFormulaSlot();
    }

    private sealed class PropertyDocumentOwner : ScriptableObject
    {
        public PropertyDocument Document;
    }

    private sealed class PropertyDocumentProvider : IHGEditorExtensionProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => document is PropertyDocument;
        public void AddPorts(HGPortBuildContext context) { }
    }

    /// <summary>有 Property 能力的最小文件。欄位刻意不用唯讀自動屬性：GraphDeepCopy 會跳過 readonly 欄位。</summary>
    private sealed class PropertyDocument : IGraphDocument, IPropertyOwner, ITokenOwner, IGraphViewStateOwner
    {
        [SerializeField] private GraphViewState viewState = new();
        public GraphViewState ViewState => viewState;
        private List<GraphNode> orphans = new List<GraphNode>();
        private List<GraphProperty> properties = new List<GraphProperty>();
        private List<GraphToken> tokens = new List<GraphToken>();
        private ArrayList roots = new ArrayList();

        public List<GraphNode> Orphans => orphans;
        public List<GraphProperty> Properties => properties;
        public List<GraphToken> Tokens => tokens;
        public IList Roots => roots;
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(TestSlot);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => HGCapabilities.Properties;
        public string WindowTitle => "Property Test";

        public void InvalidateValidation() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => GraphDeepCopy.Copy(this);
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new List<object>();
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => null;
        public object AddRoot(object key) => null;
    }

    private sealed class DiagnosticFieldBody : GraphNodeContent
    {
        [HGTab("Basic")] public int Plain;
        [HGTab("Advanced")] public DiagnosticDetails Details = new();
    }

    private sealed class DiagnosticDetails
    {
        [HGFoldout("Settings")] public List<DiagnosticEntry> Values = new() { new DiagnosticEntry() };
    }

    private sealed class DiagnosticEntry
    {
        public TestFormulaSlot Input = new();
        public int Amount;
    }

    private sealed class DiagnosticMetadataBody : GraphNodeContent
    {
        [HGTab("bad//path")] public int Value;
    }

    private sealed class TestDocument : IGraphDocument
    {
        private readonly HGCapabilities capabilities;

        private readonly Func<TestDocument, object> deepCopy;

        public TestDocument(HGCapabilities capabilities = HGCapabilities.None, Func<TestDocument, object> deepCopy = null)
        {
            this.capabilities = capabilities;
            this.deepCopy = deepCopy;
        }

        public List<GraphNode> Orphans { get; } = new();
        public IList Roots { get; } = new ArrayList();
        public bool IsValidated { get; private set; }
        public Type PackType => null;
        public Type ItemSlotType => typeof(TestSlot);
        public string RootChip => "";
        public string RootNoun => "Root";
        public HGCapabilities Capabilities => capabilities;
        public string WindowTitle => "Test";

        public void InvalidateValidation() => IsValidated = false;
        public void Verify() => IsValidated = true;
        public object DeepCopy() => deepCopy != null ? deepCopy(this) : new TestDocument();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new List<object>();
        public object KeyOf(object root) => root;
        public string TitleOf(object root) => "Root";
        public IList ItemsOf(object root) => null;
        public object AddRoot(object key) => null;
    }
}
}
