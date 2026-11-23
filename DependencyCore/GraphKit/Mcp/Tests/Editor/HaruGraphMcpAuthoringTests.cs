namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Block-style authoring commands: nested creation, compose, labels, stable keys, arrange and readability.</summary>
public sealed class HaruGraphMcpAuthoringTests
{
    private string folder;
    private readonly List<string> drafts = new();

    [SetUp] public void SetUp()
    {
        folder = "Assets/__HaruGraphMcpAuthoring_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring(7));
    }

    [TearDown] public void TearDown()
    {
        foreach (string id in drafts) HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "discard", draft_id = id }));
        drafts.Clear();
        AssetDatabase.DeleteAsset(folder);
    }

    private static JToken Ok(object response)
    { var json = JObject.FromObject(response); Assert.That((bool)json["success"], Is.True, json.ToString()); return json["data"]; }
    private static void Fails(object response, string code)
    { var json = JObject.FromObject(response); Assert.That((bool)json["success"], Is.False, json.ToString()); Assert.That((string)json["code"], Is.EqualTo(code), json.ToString()); }
    private static JToken Draft(object request) => Ok(HaruGraphDraft.HandleCommand(JObject.FromObject(request)));
    private int Generation(string id) => (int)Draft(new { action = "status", draft_id = id })["generation"];
    private T Read<T>(string id, Func<PortableFlowDocument, T> read)
        => (T)HaruGraphDraft.WithDraftSnapshot(JObject.FromObject(new { draft_id = id, generation = Generation(id) }),
            snapshot => read((PortableFlowDocument)snapshot.Document));

    /// <summary>One root with a labelled Loop step (empty Source and Body) and a second, empty action item.</summary>
    private string OpenLoop(out string loopId)
    {
        var owner = ScriptableObject.CreateInstance<PortableFlowOwner>();
        var loop = new GraphNode(new PortableFlowLoop()); loopId = loop.EnsureId();
        var outer = new PortableActionSlot { Label = "outer", Source = loop }; outer.EnsureId();
        var spare = new PortableActionSlot(); spare.EnsureId();
        owner.Document.Entries.Add(new PortableFlowRoot { Actions = new() { outer, spare } });
        string path = folder + "/loop.asset";
        AssetDatabase.CreateAsset(owner, path);
        AssetDatabase.SaveAssetIfDirty(owner);
        var listed = Ok(HaruGraphInspect.HandleCommand(JObject.FromObject(new { action = "list_documents", asset_path = path })));
        string id = (string)Draft(new { action = "open", asset_path = path, document_id = (string)listed["documents"][0]["document_id"] })["draft_id"];
        drafts.Add(id);
        return id;
    }

    private static string LoopType => typeof(PortableFlowLoop).AssemblyQualifiedName;

    [Test]
    public void CreateNodeWithSlotKeyNestsAndStableKeysStayValidAcrossBatchMutations()
    {
        string id = OpenLoop(out string loopId);
        // Every step after the first follows a mutation; literal generation-local keys would be rejected as stale.
        var batch = Draft(new { action = "apply_batch", draft_id = id, generation = Generation(id), commands = new object[]
        {
            new { id = "made", action = "create_node", source_kind = "inline", type = LoopType, slot_key = "@" + loopId + ".Source" },
            new { action = "label_step", slot_key = "@" + loopId + ".Source", name = "inner step" },
            new { action = "label_step", slot_key = "@use:" + loopId, name = "outer step" },
        } });
        string createdId = (string)batch["details"]["steps"][0]["data"]["details"]["node_id"];
        Assert.That(createdId, Is.Not.Null.And.Not.Empty);

        Assert.That(Read(id, doc => doc.Entries[0].Actions[0].Label), Is.EqualTo("outer step"));
        Assert.That(Read(id, doc => ((PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject).Source.Label), Is.EqualTo("inner step"));
        Assert.That(Read(id, doc => ((PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject).Source.Node.Id), Is.EqualTo(createdId));
        Assert.That(Read(id, doc => ((PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject).Source.Node.Mode), Is.EqualTo(GraphNodeMode.Composition));
        Assert.That(Read(id, doc => doc.Candidates.Count), Is.EqualTo(0), "A node created inside a slot never passes through the candidate pool.");

        var entries = Draft(new { action = "endpoints", draft_id = id, generation = Generation(id), page_size = 100 })["entries"];
        Assert.That(entries.Any(item => (string)item["stable_key"] == "@" + loopId + ".Source"), Is.True);
        Assert.That(entries.Any(item => (string)item["stable_key"] == "@" + createdId), Is.True);

        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        Assert.That(Read(id, doc => ((PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject).Source.Node), Is.Null, "The batch is one Undo step.");
        Assert.That(Read(id, doc => doc.Entries[0].Actions[0].Label), Is.EqualTo("outer"));
    }

    [Test]
    public void UnknownStableKeyIsRejectedWithoutChangingTheDraft()
    {
        string id = OpenLoop(out string loopId);
        int generation = Generation(id);
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "label_step", draft_id = id, generation,
            slot_key = "@" + loopId + ".Missing", name = "x" })), "harugraph.draft.stable_key_unavailable");
        Assert.That(Generation(id), Is.EqualTo(generation));
    }

    [Test]
    public void ComposeNestsAnUnusedCandidateAndRejectsASecondReader()
    {
        string id = OpenLoop(out string loopId);
        string candidate = (string)Draft(new { action = "create_node", draft_id = id, generation = Generation(id),
            source_kind = "inline", type = LoopType })["details"]["node_id"];
        JToken Readability() => Draft(new { action = "readability", draft_id = id, generation = Generation(id), page_size = 100 });
        Assert.That(Readability()["issues"].Any(issue => (string)issue["code"] == "unused_node" && (string)issue["node_id"] == candidate), Is.True);

        Draft(new { action = "compose", draft_id = id, generation = Generation(id), slot_key = "@" + loopId + ".Source", source_key = "@" + candidate });
        Assert.That(Read(id, doc => ((PortableFlowLoop)doc.Entries[0].Actions[0].Node.BodyObject).Source.Node.Id), Is.EqualTo(candidate));
        Assert.That(Read(id, doc => doc.Candidates.Count), Is.EqualTo(0));
        Assert.That(Readability()["issues"].Any(issue => (string)issue["code"] == "unused_node"), Is.False);

        // The spare root item is the only empty slot without a stable key.
        var entries = Draft(new { action = "endpoints", draft_id = id, generation = Generation(id), page_size = 100 })["entries"];
        string spare = (string)entries.First(item => (string)item["kind"] == "slot" && item["source_key"].Type == JTokenType.Null
            && item["stable_key"].Type == JTokenType.Null)["key"];
        Fails(HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "compose", draft_id = id, generation = Generation(id),
            slot_key = spare, source_key = "@" + candidate })), "harugraph.draft.node_shared");
    }

    [Test]
    public void ReadabilityReportsEmptyStepsAndIgnoresMissingLabels()
    {
        string id = OpenLoop(out string loopId);
        var report = Draft(new { action = "readability", draft_id = id, generation = Generation(id), page_size = 100 });
        Assert.That((bool)report["passed"], Is.False);
        Assert.That(report["issues"].Any(issue => (string)issue["code"] == "empty_step"), Is.True, "The spare item has no content.");

        Draft(new { action = "label_step", draft_id = id, generation = Generation(id), slot_key = "@use:" + loopId, name = "" });
        report = Draft(new { action = "readability", draft_id = id, generation = Generation(id), page_size = 100 });
        Assert.That(report["issues"].Any(issue => ((string)issue["code"]).Contains("label")), Is.False, "A missing Label is not a readability issue.");
    }

    [Test]
    public void ArrangeClearsManualPositionsAsOneLayoutOnlyUndoStep()
    {
        string id = OpenLoop(out string loopId);
        Draft(new { action = "configure_node", draft_id = id, generation = Generation(id), node_id = loopId, position = new { x = 500, y = 300 } });
        string scope = (string)Draft(new { action = "group_scopes", draft_id = id, generation = Generation(id) })["entries"][0]["scope_id"];
        JToken State() => Draft(new { action = "nodes", draft_id = id, generation = Generation(id), node_id = loopId })["entries"][0];
        Assert.That((bool)State()["has_position"], Is.True);

        var arranged = Draft(new { action = "arrange", draft_id = id, generation = Generation(id), scope_id = scope });
        Assert.That((int)arranged["details"]["cleared_positions"], Is.GreaterThanOrEqualTo(1));
        Assert.That((bool)State()["has_position"], Is.False);
        Assert.That((bool)Draft(new { action = "layout_diff", draft_id = id, generation = Generation(id) })["content_unchanged"], Is.True);

        Draft(new { action = "undo", draft_id = id, generation = Generation(id) });
        Assert.That((bool)State()["has_position"], Is.True);
    }
}
}
