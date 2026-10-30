namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class HaruGraphMcpExtensionTests
{
    private string folder;
    private readonly List<string> drafts = new();
    private readonly List<IDisposable> leases = new();
    [SetUp] public void SetUp()
    {
        folder = "Assets/__HaruGraphMcpExtensions_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring(7));
    }
    [TearDown] public void TearDown()
    {
        foreach (string id in drafts) HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "discard", draft_id = id }));
        foreach (var lease in leases) lease.Dispose();
        drafts.Clear(); leases.Clear();
        AssetDatabase.DeleteAsset(folder);
    }
    private PortableGraphOwner Owner(string name)
    {
        var owner = ScriptableObject.CreateInstance<PortableGraphOwner>();
        owner.Document.Nodes.Add(new GraphNode(new PortableGraphValue()));
        owner.Document.Nodes[0].EnsureId();
        AssetDatabase.CreateAsset(owner, folder + "/" + name + ".asset");
        AssetDatabase.SaveAssetIfDirty(owner);
        return owner;
    }
    private string Open(ScriptableObject owner, int index = 0)
    {
        string path = AssetDatabase.GetAssetPath(owner);
        var listed = Inspect(new { action = "list_documents", asset_path = path });
        string id = (string)Draft(new { action = "open", asset_path = path, document_id = (string)listed["documents"][index]["document_id"] })["draft_id"];
        drafts.Add(id); return id;
    }
    private int Generation(string id) => (int)Draft(new { action = "status", draft_id = id })["generation"];
    private JToken Request(string id) => JObject.FromObject(new { draft_id = id, generation = Generation(id) });
    private string Node(string id) => (string)Draft(new { action = "nodes", draft_id = id, generation = Generation(id) })["entries"][0]["node_id"];
    private void Amount(string id, int value) => Draft(new { action = "set_value", draft_id = id, generation = Generation(id), node_id = Node(id), field_id = typeof(PortableGraphValue).FullName + "/Amount", value });
    private int ReadAmount(string id) => (int)HaruGraphDraft.WithDraftSnapshot((JObject)Request(id), snapshot => ((PortableGraphValue)((PortableGraphDocument)snapshot.Document).Nodes[0].BodyObject).Amount);
    private static JToken Ok(object response)
    { var json = JObject.FromObject(response); Assert.That((bool)json["success"], Is.True, json.ToString()); return json["data"]; }
    private static void Fails(object response, string code)
    { var json = JObject.FromObject(response); Assert.That((bool)json["success"], Is.False, json.ToString()); Assert.That((string)json["code"], Is.EqualTo(code), json.ToString()); }
    private static JToken Draft(object request) => Ok(HaruGraphDraft.HandleCommand(JObject.FromObject(request)));
    private static JToken Inspect(object request) => Ok(HaruGraphInspect.HandleCommand(JObject.FromObject(request)));
    private static JToken Transaction(object request) => Ok(HaruGraphTransaction.HandleCommand(JObject.FromObject(request)));

    [Test]
    public void ExecutorsAreDiscoveredTypedAndIsolatedAndCanBeUnregistered()
    {
        var first = new Executor("portable.sum", false); var second = new Executor("portable.double", true);
        var lease = HaruGraphExtensions.RegisterExecutor(first); leases.Add(lease);
        leases.Add(HaruGraphExtensions.RegisterExecutor(second));
        Assert.Throws<ArgumentException>(() => HaruGraphExtensions.RegisterExecutor(first));
        string id = Open(Owner("Execution")); int generation = Generation(id);
        JToken Execute(string action, string provider = null, object input = null) => Ok(HaruGraphExecute.HandleCommand(JObject.FromObject(new { action, draft_id = id, generation, provider_id = provider, input })));
        Assert.That(Execute("providers")["providers"].Count(), Is.GreaterThanOrEqualTo(2));
        Assert.That((string)Execute("describe", first.Id)["contract"]["input"], Is.EqualTo("{add:int}"));
        Assert.That((int)Execute("run", first.Id, new { add = 3 })["result"]["value"], Is.EqualTo(4));
        Assert.That((int)Execute("run", second.Id, new { add = 3 })["result"]["value"], Is.EqualTo(8));
        Assert.That(ReadAmount(id), Is.EqualTo(1)); Assert.That(Generation(id), Is.EqualTo(generation));
        Fails(HaruGraphExecute.HandleCommand(JObject.FromObject(new { action = "run", draft_id = id, generation = -1, provider_id = first.Id, input = new { add = 3 } })), "harugraph.draft.stale_generation");
        Fails(HaruGraphExecute.HandleCommand(JObject.FromObject(new { action = "run", draft_id = id, generation, provider_id = first.Id, input = new { cancel = true } })), "harugraph.execute.cancelled");
        Assert.That(first.Disposed, Is.True);
        lease.Dispose();
        Fails(HaruGraphExecute.HandleCommand(JObject.FromObject(new { action = "describe", draft_id = id, generation, provider_id = first.Id })), "harugraph.execute.provider_unavailable");
    }
    private sealed class Executor : IHaruGraphExecutor
    {
        public string Id { get; } public string Description => "Portable arithmetic consumer";
        private readonly bool twice; public bool Disposed;
        public Executor(string id, bool twice) { Id = id; this.twice = twice; }
        public bool Supports(IGraphDocument document) => document is PortableGraphDocument;
        public JObject Describe(IGraphDocument document) => new() { ["input"] = "{add:int}", ["result"] = "{value:int}", ["isolation"] = "detached values only" };
        public JObject Run(HaruGraphDraftReadContext snapshot, JObject input, CancellationToken cancellation)
        {
            Disposed = false;
            try
            {
                if ((bool?)input["cancel"] == true) throw new OperationCanceledException();
                cancellation.ThrowIfCancellationRequested();
                var body = (PortableGraphValue)((PortableGraphDocument)snapshot.Document).Nodes[0].BodyObject;
                int result = (body.Amount + (int)input["add"]) * (twice ? 2 : 1);
                body.Amount = 999;
                return new JObject { ["value"] = result };
            }
            finally { Disposed = true; }
        }
    }

    [Test]
    public void ManagedReferenceTypesListsAndNestedBoundsUseUndoableCommands()
    {
        string id = Open(Owner("Polymorphism")); string node = Node(id);
        var path = new JArray(typeof(PortableGraphValue).FullName + "/Options");
        JToken Command(string action, JArray target, object extra = null)
        {
            var request = JObject.FromObject(new { action, draft_id = id, generation = Generation(id), node_id = node, value_path = target });
            if (extra != null) request.Merge(JObject.FromObject(extra));
            return Ok(HaruGraphDraft.HandleCommand(request));
        }
        Assert.That(Command("structured_types", path)["entries"].Any(item => (string)item["type"] == typeof(PortableScale).AssemblyQualifiedName), Is.True);
        Command("replace_structured_value", path, new { type = typeof(PortableScale).AssemblyQualifiedName });
        var scale = new JArray(path[0], typeof(PortableScale).FullName + "/Scale");
        Command("set_structured_value", scale, new { value = 7 });
        Assert.That((int)Command("structured_fields", path)["entries"][0]["value"], Is.EqualTo(7));
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "set_structured_value", draft_id = id, generation = Generation(id), node_id = node, value_path = scale, value = 100 })), "harugraph.draft.invalid_value");
        Command("replace_structured_value", path, new { type = typeof(PortableLabel).AssemblyQualifiedName });
        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        Assert.That((int)Command("structured_fields", path)["entries"][0]["value"], Is.EqualTo(7));
        var list = new JArray(typeof(PortableGraphValue).FullName + "/Items");
        Command("insert_value_item", list, new { index = 0, type = typeof(PortableScale).AssemblyQualifiedName });
        Command("replace_structured_value", new JArray(list[0], 0), new { type = typeof(PortableLabel).AssemblyQualifiedName });
        Assert.That((string)Command("structured_fields", new JArray(list[0], 0))["entries"][0]["value"], Is.EqualTo("label"));
        Command("replace_structured_value", path, new { type = typeof(PortableLinked).AssemblyQualifiedName });
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "replace_structured_value", draft_id = id, generation = Generation(id), node_id = node, value_path = path, type = typeof(PortableScale).AssemblyQualifiedName })), "harugraph.draft.item_connected");
    }

    [Test]
    public void DependencyTransactionRequiresClosureAndRejectsStalePreviewThenPersistsBothFiles()
    {
        var source = Owner("Source"); var dependent = Owner("Dependent");
        dependent.Dependency = source; dependent.Document.Validated = true; EditorUtility.SetDirty(dependent); AssetDatabase.SaveAssetIfDirty(dependent);
        string a = Open(source), b = Open(dependent); Amount(a, 41);
        var deps = Transaction(new { action = "dependencies", drafts = new JArray(Request(a)) });
        Assert.That(deps["files"].Count(), Is.EqualTo(2));
        Fails(HaruGraphTransaction.HandleCommand(JObject.FromObject(new { action = "preview", drafts = new JArray(Request(a)) })), "harugraph.draft.dependencies_required");
        string Preview() => (string)Transaction(new { action = "preview", drafts = new JArray(Request(a), Request(b)) })["preview_token"];
        string stale = Preview(); Amount(a, 42);
        Fails(HaruGraphTransaction.HandleCommand(JObject.FromObject(new { action = "commit", preview_token = stale })), "harugraph.draft.transaction_changed");
        Assert.That(((PortableGraphValue)source.Document.Nodes[0].BodyObject).Amount, Is.EqualTo(1));
        Transaction(new { action = "commit", preview_token = Preview() });
        Assert.That(((PortableGraphValue)source.Document.Nodes[0].BodyObject).Amount, Is.EqualTo(42));
        Assert.That(dependent.Document.Validated, Is.False);
        string sourcePath = AssetDatabase.GetAssetPath(source), dependentPath = AssetDatabase.GetAssetPath(dependent);
        Resources.UnloadAsset(source); Resources.UnloadAsset(dependent);
        Assert.That(((PortableGraphValue)AssetDatabase.LoadAssetAtPath<PortableGraphOwner>(sourcePath).Document.Nodes[0].BodyObject).Amount, Is.EqualTo(42));
        Assert.That(AssetDatabase.LoadAssetAtPath<PortableGraphOwner>(dependentPath).Document.Validated, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CustomNestedBindingsMetadataAndMultiDocumentCommitOrRollback(bool failWrite)
    {
        var owner = ScriptableObject.CreateInstance<PortableNestedOwner>();
        owner.Data.First.Nodes.Add(new GraphNode(new PortableGraphValue()));
        owner.Data.Second.Nodes.Add(new GraphNode(new PortableGraphValue()));
        owner.Data.First.Nodes[0].EnsureId(); owner.Data.Second.Nodes[0].EnsureId();
        string path = folder + "/Nested.asset"; AssetDatabase.CreateAsset(owner, path); AssetDatabase.SaveAssetIfDirty(owner);
        var context = new HGEditorExtensionContext(new MetadataProvider(), new MetadataProvider());
        leases.Add(HaruGraphExtensions.RegisterDocuments("portable.nested", target => target is PortableNestedOwner ? new[] {
            new HaruGraphDocumentEntry("first", new HGDocumentBinding<IGraphDocument>("first", value => ((PortableNestedOwner)value).Data.First,
                (value, document) => ((PortableNestedOwner)value).Data.First = (PortableGraphDocument)document), context),
            new HaruGraphDocumentEntry("second", new HGDocumentBinding<IGraphDocument>("second", value => ((PortableNestedOwner)value).Data.Second,
                (value, document) => { ((PortableNestedOwner)value).Data.Second = (PortableGraphDocument)document; if (failWrite) throw new InvalidOperationException("Injected second write failure"); }), context)
        } : Array.Empty<HaruGraphDocumentEntry>()));
        string a = Open(owner), b = Open(owner, 1);
        string node = Node(a);
        var fields = Draft(new { action = "metadata_fields", draft_id = a, generation = Generation(a), node_id = node });
        Assert.That((string)fields["entries"][0]["field_id"], Is.EqualTo("portable.amount"));
        Draft(new { action = "set_metadata_value", draft_id = a, generation = Generation(a), node_id = node, field_id = "portable.amount", value = 31 });
        Assert.That(ReadAmount(a), Is.EqualTo(31));
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "set_value", draft_id = a, generation = Generation(a), node_id = node,
            field_id = typeof(PortableGraphValue).FullName + "/Amount", value = 99 })), "harugraph.draft.field_unavailable");
        var structured = Draft(new { action = "structured_fields", draft_id = a, generation = Generation(a), node_id = node });
        Assert.That((string)structured["entries"][0]["value_path"][0], Is.EqualTo("portable.amount"));
        Draft(new { action = "set_structured_value", draft_id = a, generation = Generation(a), node_id = node,
            value_path = new[] { "portable.amount" }, value = 30 });
        Draft(new { action = "undo", draft_id = a, generation = Generation(a) });
        Assert.That(ReadAmount(a), Is.EqualTo(31));
        Draft(new { action = "set_metadata_value", draft_id = b, generation = Generation(b), node_id = Node(b), field_id = "portable.amount", value = 32 });
        Draft(new { action = "replace_structured_value", draft_id = a, generation = Generation(a), node_id = node,
            value_path = new[] { "portable.options" }, type = typeof(PortableScale).AssemblyQualifiedName });
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "set_structured_value", draft_id = a, generation = Generation(a), node_id = node,
            value_path = new[] { "portable.readonly" }, value = 999 })), "harugraph.draft.invalid_value_path");
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "set_metadata_value", draft_id = a, generation = Generation(a), node_id = node,
            field_id = "portable.hidden", value = 999 })), "harugraph.draft.field_unavailable");
        string token = (string)Transaction(new { action = "preview", drafts = new JArray(Request(a), Request(b)) })["preview_token"];
        byte[] before = File.ReadAllBytes(path);
        object response = HaruGraphTransaction.HandleCommand(JObject.FromObject(new { action = "commit", preview_token = token }));
        if (failWrite)
        {
            Fails(response, "harugraph.draft.transaction_failed");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(((PortableGraphValue)owner.Data.First.Nodes[0].BodyObject).Amount, Is.EqualTo(1));
            Assert.That(((PortableGraphValue)owner.Data.Second.Nodes[0].BodyObject).Amount, Is.EqualTo(1));
            Assert.That((bool)Draft(new { action = "status", draft_id = a })["requires_reopen"], Is.True);
        }
        else
        {
            Ok(response);
            Resources.UnloadAsset(owner);
            var stored = AssetDatabase.LoadAssetAtPath<PortableNestedOwner>(path);
            Assert.That(((PortableGraphValue)stored.Data.First.Nodes[0].BodyObject).Amount, Is.EqualTo(31));
            Assert.That(((PortableGraphValue)stored.Data.Second.Nodes[0].BodyObject).Amount, Is.EqualTo(32));
        }
    }
    private sealed class MetadataProvider : IHGEditorExtensionProvider, IHGEditorMetadataProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document) => document is PortableGraphDocument;
        public void AddPorts(HGPortBuildContext context) { }
        public bool TryGetNodeDescriptor(Type type, out HGNodeDescriptor descriptor)
        {
            descriptor = type == typeof(PortableGraphValue) ? new HGNodeDescriptor(type, new[] {
                HGFieldDescriptor.Create<PortableGraphValue, int>("portable.amount", value => value.Amount, (value, number) => value.Amount = number),
                HGFieldDescriptor.Create<PortableGraphValue, PortableOptions>("portable.options", value => value.Options, (value, options) => value.Options = options, managedReference: true),
                HGFieldDescriptor.Create<PortableGraphValue, int>("portable.readonly", value => value.Amount),
                HGFieldDescriptor.Create<PortableGraphValue, int>("portable.hidden", value => value.Amount, (value, number) => value.Amount = number, isVisible: _ => false) }) : null;
            return descriptor != null;
        }
        public bool TryGetValueDrawer(Type type, out IHGValueDrawer drawer) { drawer = null; return false; }
    }
}
}
