namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>Detached graph data for trusted consumer tools. External Unity assets remain references; consumers own execution and side-effect isolation.</summary>
public sealed class HaruGraphDraftReadContext
{
    public int Generation { get; private set; }
    public IGraphDocument Document { get; private set; }
    public Dictionary<string, GraphSlotBase> Slots { get; private set; }
    public Dictionary<string, GraphNode> Nodes { get; private set; }

    internal HaruGraphDraftReadContext(HGDocumentSession<IGraphDocument> session)
    {
        var endpoints = new HaruGraphDraftConnections(session);
        Generation = session.Generation;
        Document = session.Document;
        Slots = endpoints.Slots;
        Nodes = endpoints.Nodes;
    }
}

public static partial class HaruGraphDraft
{
    /// <summary>Validates draft lifetime/generation and gives a consumer a detached document and matching endpoint map. Never supplies the session or Owner write API.</summary>
    public static object WithDraftSnapshot(JObject parameters, Func<HaruGraphDraftReadContext, object> consume)
    {
        if (parameters == null || consume == null) return Error("invalid_request", "A request and consumer are required.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Error("editor_busy", "Read draft snapshots in idle Edit Mode.");
        var input = new ToolParams(parameters);
        string id = input.Get("draft_id");
        if (id == null || !drafts.TryGetValue(id, out var draft) || draft.Session.Owner == null || draft.Expires <= DateTime.UtcNow)
        {
            ReleaseDraft(id);
            return Error("draft_unavailable", "Draft expired or is unavailable. Open again.");
        }
        if (input.GetRaw("generation")?.ToObject<int>() != draft.Session.Generation)
            return Error("stale_generation", "Read status and resolve targets again before requesting a snapshot.");
        if (draft.RequiresReopen) return Error("reopen_required", "Inspect Owner and open a fresh draft.");
        draft.Expires = DateTime.UtcNow.AddMinutes(30);
        // Copy the graph and maps together so endpoint keys address objects in this same copy.
        var snapshot = GraphDeepCopy.Copy(new HaruGraphDraftReadContext(draft.Session));
        if (snapshot?.Document == null) return Error("snapshot_failed", "Could not copy draft data.");
        return consume(snapshot);
    }
}
}
