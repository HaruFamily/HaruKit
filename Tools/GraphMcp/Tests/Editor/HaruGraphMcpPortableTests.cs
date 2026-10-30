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

public sealed class HaruGraphMcpPortableTests
{
    [Test]
    public void DiscoverEditUndoRedoCommitAndReloadWithoutConsumerPackages()
    {
        string folder = "Assets/__HaruGraphMcpPortable_" + Guid.NewGuid().ToString("N");
        string path = folder + "/Owner.asset", draft = null;
        var snapshots = new List<string>();
        AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        try
        {
            var owner = ScriptableObject.CreateInstance<PortableGraphOwner>();
            var node = new GraphNode(new PortableGraphValue());
            string nodeId = node.EnsureId();
            owner.Document.Nodes.Add(node);
            AssetDatabase.CreateAsset(owner, path);
            AssetDatabase.SaveAssetIfDirty(owner);
            var found = Ok(HaruGraphInspect.HandleCommand(JObject.FromObject(new
                { action = "search_documents", search = folder, page_size = 100 })));
            var entry = found["entries"].Single(item => (string)item["asset_path"] == path);
            string document = (string)entry["documents"][0]["document_id"];
            draft = (string)Call(new { action = "open", asset_path = path, document_id = document })["draft_id"];
            int Generation() => (int)Call(new { action = "status", draft_id = draft })["generation"];
            int Amount() => (int)HaruGraphDraft.WithDraftSnapshot(JObject.FromObject(new
                { draft_id = draft, generation = Generation() }), context =>
                ((PortableGraphValue)((PortableGraphDocument)context.Document).Nodes.Single().BodyObject).Amount);
            int initialGeneration = Generation();
            Call(new { action = "set_value", draft_id = draft, generation = initialGeneration, node_id = nodeId,
                field_id = typeof(PortableGraphValue).FullName + "/Amount", value = 42 });
            Assert.That(Amount(), Is.EqualTo(42));
            Assert.That(((PortableGraphValue)owner.Document.Nodes.Single().BodyObject).Amount, Is.EqualTo(1));
            var stale = JObject.FromObject(HaruGraphDraft.HandleCommand(JObject.FromObject(new
                { action = "undo", draft_id = draft, generation = initialGeneration })));
            Assert.That((string)stale["code"], Is.EqualTo("harugraph.draft.stale_generation"));
            Call(new { action = "undo", draft_id = draft, generation = Generation() });
            Assert.That(Amount(), Is.EqualTo(1));
            Call(new { action = "redo", draft_id = draft, generation = Generation() });
            Assert.That(Amount(), Is.EqualTo(42));
            Call(new { action = "validate", draft_id = draft, generation = Generation() });
            var preview = Call(new { action = "preview", draft_id = draft, generation = Generation() });
            snapshots.Add((string)preview["before_snapshot_id"]);
            snapshots.Add((string)preview["after_snapshot_id"]);
            Call(new { action = "commit", draft_id = draft, generation = Generation(), preview_token = (string)preview["preview_token"] });
            Call(new { action = "discard", draft_id = draft });
            draft = null;
            Resources.UnloadAsset(owner);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<PortableGraphOwner>(path);
            Assert.That(((PortableGraphValue)reloaded.Document.Nodes.Single().BodyObject).Amount, Is.EqualTo(42));
            draft = (string)Call(new { action = "open", asset_path = path, document_id = document })["draft_id"];
            Assert.That(Amount(), Is.EqualTo(42));
        }
        finally
        {
            if (draft != null) HaruGraphDraft.HandleCommand(JObject.FromObject(new { action = "discard", draft_id = draft }));
            foreach (string snapshot in snapshots)
                HaruGraphInspect.HandleCommand(JObject.FromObject(new { action = "release", snapshot_id = snapshot }));
            AssetDatabase.DeleteAsset(folder);
        }
    }

    private static JToken Call(object args) => Ok(HaruGraphDraft.HandleCommand(JObject.FromObject(args)));
    private static JToken Ok(object response)
    {
        var json = JObject.FromObject(response);
        Assert.That((bool)json["success"], Is.True, json.ToString());
        return json["data"];
    }
}
}
