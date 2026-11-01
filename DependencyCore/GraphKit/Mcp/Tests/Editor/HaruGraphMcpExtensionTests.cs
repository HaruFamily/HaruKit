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
    public void NodeModesAreExplicitTransactionalAndRejectSharedComposition()
    {
        var owner = FlowOwner();
        string id = Open(owner), nodeId = Node(id);
        var state = Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0];
        Assert.That((string)state["node_mode"], Is.EqualTo("Composition"));
        Assert.That((int)state["read_user_count"], Is.EqualTo(2));
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "configure_node", draft_id = id,
            generation = Generation(id), node_id = nodeId, node_mode = "connection" })), "harugraph.draft.invalid_node_mode");
        Draft(new { action = "configure_node", draft_id = id, generation = Generation(id), node_id = nodeId, node_mode = "Connection" });
        state = Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0];
        Assert.That((string)state["node_mode"], Is.EqualTo("Connection"));
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "configure_node", draft_id = id,
            generation = Generation(id), node_id = nodeId, node_mode = "Composition", note = "must rollback" })), "harugraph.draft.node_mode_shared");
        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        state = Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0];
        Assert.That((string)state["node_mode"], Is.EqualTo("Composition"));
        Assert.That((int)state["read_user_count"], Is.EqualTo(2));
        Assert.That((string)state["note"], Is.Not.EqualTo("must rollback"));
        Assert.That(owner.Document.Entries[0].Actions[0].Source.Mode, Is.EqualTo(GraphNodeMode.Composition), "Owner must stay unchanged.");
    }

    [Test]
    public void PortConnectionAutomaticallySelectsConnectionMode()
    {
        string id = Open(FlowOwner()), nodeId = Node(id);
        JToken Endpoints() => Draft(new { action = "endpoints", draft_id = id, generation = Generation(id) })["entries"];
        var entries = Endpoints();
        string slotKey = (string)entries.First(item => (string)item["kind"] == "slot")["key"];
        Draft(new { action = "disconnect", draft_id = id, generation = Generation(id), slot_key = slotKey });
        entries = Endpoints();
        slotKey = (string)entries.First(item => (string)item["kind"] == "slot" && item["source_key"].Type == JTokenType.Null)["key"];
        string sourceKey = (string)entries.First(item => (string)item["node_id"] == nodeId)["key"];
        var check = Draft(new { action = "check_connection", draft_id = id, generation = Generation(id), slot_key = slotKey, source_key = sourceKey });
        Assert.That((bool)check["allowed"], Is.True);
        Draft(new { action = "connect", draft_id = id, generation = Generation(id), slot_key = slotKey, source_key = sourceKey });
        Assert.That((int)Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0]["read_user_count"], Is.EqualTo(2));
        Assert.That((string)Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0]["node_mode"], Is.EqualTo("Connection"));
        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        Assert.That((string)Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = nodeId })["entries"][0]["node_mode"], Is.EqualTo("Composition"));
    }

    private PortableFlowOwner FlowOwner()
    {
        var owner = ScriptableObject.CreateInstance<PortableFlowOwner>();
        var shared = new GraphNode(new PortableFlowAction()); shared.EnsureId();
        var first = new PortableActionSlot { Label = "first", Source = shared }; first.EnsureId();
        var second = new PortableActionSlot { Label = "second", Source = shared, Disabled = true }; second.EnsureId();
        owner.Document.Entries.Add(new PortableFlowRoot { Actions = new() { first, second } });
        AssetDatabase.CreateAsset(owner, folder + "/flow.asset");
        AssetDatabase.SaveAssetIfDirty(owner);
        return owner;
    }

    [Test]
    public void FlowProtocolUsesFreshPathsAndPreservesSharedSlotsWithAtomicRollback()
    {
        var owner = FlowOwner();
        // Test assemblies are excluded from the production catalog. A document-owned prototype is an explicit authoring choice.
        var prototype = new GraphNode(new PortableFlowLoop()); prototype.EnsureId();
        owner.Document.Candidates.Add(prototype);
        EditorUtility.SetDirty(owner); AssetDatabase.SaveAssetIfDirty(owner);
        string original = JsonUtility.ToJson(owner.Document);
        string id = Open(owner);
        var candidates = Draft(new { action = "flow_candidates", draft_id = id, generation = Generation(id), section_path = "root/0/body", page_size = 100 });
        string loopType = (string)candidates["entries"].Single(item => (string)item["type"] == typeof(PortableFlowLoop).AssemblyQualifiedName)["type"];
        var initial = Draft(new { action = "flow", draft_id = id, generation = Generation(id), page_size = 100 });
        Assert.That((int)initial["total"], Is.EqualTo(3));
        Assert.That((bool)initial["entries"][1]["shared"], Is.True);
        int generation = Generation(id);
        Draft(new { action = "flow_wrap", draft_id = id, generation, section_path = "root/0/body", index = 0, count = 2,
            type = loopType, child_section_key = "body" });
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "flow_remove", draft_id = id, generation,
            flow_path = "root/0/body/0" })), "harugraph.draft.stale_generation");
        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        var undone = Draft(new { action = "flow", draft_id = id, generation = Generation(id) });
        Assert.That((int)undone["total"], Is.EqualTo(3));
        Draft(new { action = "redo", draft_id = id, generation = Generation(id) });
        Draft(new { action = "flow_transfer", draft_id = id, generation = Generation(id), section_path = "root/0/body/0/body",
            index = 0, count = 1, target_section_path = "root/0/body", destination_index = 1 });
        var shared = (bool)HaruGraphDraft.WithDraftSnapshot((JObject)Request(id), read =>
        {
            var doc = (PortableFlowDocument)read.Document;
            var loop = (PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject;
            return ReferenceEquals(loop.Body[0].Node, doc.Entries[0].Actions[1].Node) && loop.Body[0].Disabled;
        });
        Assert.That(shared, Is.True);
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "apply_batch", draft_id = id, generation = Generation(id), commands = new object[] {
            new { action = "flow_insert", section_path = "root/0/body", index = 0, type = typeof(PortableFlowAction).AssemblyQualifiedName },
            new { action = "flow_remove", flow_path = "root/0/body/0" } } })), "harugraph.draft.batch_rejected");
        var flow = Draft(new { action = "flow", draft_id = id, generation = Generation(id) });
        Assert.That((int)flow["entries"][0]["count"], Is.EqualTo(2));
        Assert.That(JsonUtility.ToJson(owner.Document), Is.EqualTo(original));
    }

    [Test]
    public void BackgroundGeometryAndImmutablePoliciesSeparateLayoutFromContentAndProtectGroups()
    {
        var owner = FlowOwner();
        var extra = new GraphNode(new PortableFlowAction()); extra.EnsureId();
        owner.Document.Candidates.Add(extra);
        owner.Document.Validated = true;
        EditorUtility.SetDirty(owner);
        AssetDatabase.SaveAssetIfDirty(owner);
        string original = JsonUtility.ToJson(owner.Document);
        string id = Open(owner);
        var scope = Draft(new { action = "group_scopes", draft_id = id, generation = Generation(id) });
        string root = (string)scope["entries"][0]["scope_id"];
        string protectedId = owner.Document.Entries[0].Actions[0].Node.Id;
        var group = Draft(new { action = "create_group", draft_id = id, generation = Generation(id), scope_id = root, name = "Keep" });
        string groupId = (string)group["details"]["group_id"];
        Draft(new { action = "group_member", draft_id = id, generation = Generation(id), group_id = groupId, node_id = protectedId, member = true });
        Draft(new { action = "add_group_tab", draft_id = id, generation = Generation(id), group_id = groupId, name = "Second" });
        int generation = Generation(id);
        var geometry = Draft(new { action = "layout_preview", draft_id = id, generation, scope_id = root, group_id = groupId, tab_index = 1 });
        Assert.That((string)geometry["svg"], Does.StartWith("<svg"));
        var visible = Draft(new { action = "geometry", draft_id = id, generation, scope_id = root, group_id = groupId, tab_index = 0 });
        Assert.That(((JArray)visible["ports"]).Count, Is.GreaterThan(0));
        Assert.That(((JArray)visible["edges"]).Count, Is.GreaterThan(0));
        Assert.That(Generation(id), Is.EqualTo(generation));
        var tabs = Draft(new { action = "group_tabs", draft_id = id, generation, group_id = groupId });
        Assert.That((int)tabs["active_tab"], Is.EqualTo(0));
        Assert.That((bool)Draft(new { action = "layout_diff", draft_id = id, generation })["content_unchanged"], Is.True);
        Draft(new { action = "protect", draft_id = id, generation, layout_only = true, protected_group_ids = new[] { groupId } });
        Draft(new { action = "configure_node", draft_id = id, generation = Generation(id), node_id = extra.Id, position = new { x = 800, y = 200 } });
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "configure_node", draft_id = id, generation = Generation(id),
            node_id = protectedId, position = new { x = 1, y = 2 } })), "harugraph.draft.protected_edit_rejected");
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "flow_remove", draft_id = id, generation = Generation(id),
            flow_path = "root/0/body/0" })), "harugraph.draft.protected_edit_rejected");
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "protect", draft_id = id, generation = Generation(id),
            layout_only = false })), "harugraph.draft.policy_already_set");
        var diff = Draft(new { action = "layout_diff", draft_id = id, generation = Generation(id) });
        Assert.That((bool)diff["content_unchanged"], Is.True);
        Assert.That((bool)diff["protection_passed"], Is.True);
        Assert.That(JsonUtility.ToJson(owner.Document), Is.EqualTo(original));
    }

    [Test]
    public void UndeclaredCustomContainerStopsFlowExpansionWhileNodeGeometryRemainsReadable()
    {
        var owner = FlowOwner();
        var hidden = new GraphNode(new PortableFlowAction()); hidden.EnsureId();
        var opaque = new PortableOpaqueFlow();
        opaque.HiddenActions.Add(new PortableActionSlot { Source = hidden });
        var carrier = new GraphNode(opaque); carrier.EnsureId();
        owner.Document.Entries[0].Actions[0].Source = carrier;
        EditorUtility.SetDirty(owner); AssetDatabase.SaveAssetIfDirty(owner);
        string sourceBefore = JsonUtility.ToJson(owner.Document);
        string id = Open(owner);
        int generation = Generation(id);
        string DraftJson() => (string)HaruGraphDraft.WithDraftSnapshot((JObject)Request(id), read => JsonUtility.ToJson(read.Document));
        string before = DraftJson();
        var flow = Draft(new { action = "flow", draft_id = id, generation, page_size = 100 });
        var use = flow["entries"].Single(item => (string)item["node_id"] == carrier.Id);
        Assert.That((bool)use["unknown"], Is.True);
        Assert.That(flow["entries"].Any(item => (string)item["node_id"] == hidden.Id), Is.False);
        string scope = (string)Draft(new { action = "group_scopes", draft_id = id, generation })["entries"][0]["scope_id"];
        var canvas = Draft(new { action = "geometry", draft_id = id, generation, scope_id = scope });
        Assert.That(canvas["nodes"].Any(node => (string)node["node_id"] == hidden.Id), Is.True, "Unknown execution semantics do not remove ordinary Node inspection.");
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "flow_transfer", draft_id = id, generation,
            section_path = "root/0/body", index = 1, count = 1, target_section_path = "root/0/body/0/body", destination_index = 0 })),
            "harugraph.draft.command_rejected");
        Assert.That(Generation(id), Is.EqualTo(generation));
        Assert.That(DraftJson(), Is.EqualTo(before));
        Assert.That(JsonUtility.ToJson(owner.Document), Is.EqualTo(sourceBefore));
    }

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
