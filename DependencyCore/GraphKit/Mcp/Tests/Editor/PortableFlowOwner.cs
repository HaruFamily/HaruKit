namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using System.Collections;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using UnityEngine;

/// <summary>Nonexecuting consumer fixture for the background flow protocol.</summary>
public sealed class PortableFlowOwner : ScriptableObject
{
    [SerializeReference] public PortableFlowDocument Document = new();
}

[Serializable]
public sealed class PortableFlowDocument : IGraphDocument, IGraphViewStateOwner
{
    [SerializeReference] public List<PortableFlowRoot> Entries = new();
    [SerializeReference] public List<GraphNode> Candidates = new();
    public GraphViewState View = new();
    public bool Validated;
    public List<GraphNode> Orphans => Candidates;
    public IList Roots => Entries;
    public GraphViewState ViewState => View;
    public bool IsValidated => Validated;
    public void InvalidateValidation() => Validated = false;
    public void Verify() => Validated = true;
    public object DeepCopy() => GraphDeepCopy.Copy(this);
    public Type PackType => typeof(object);
    public Type ItemSlotType => typeof(PortableActionSlot);
    public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => Array.Empty<object>();
    public object KeyOf(object root) => Entries.IndexOf((PortableFlowRoot)root);
    public string TitleOf(object root) => "Portable Flow";
    public string RootChip => "Flow";
    public string RootNoun => "Root";
    public HGCapabilities Capabilities => HGCapabilities.None;
    public string WindowTitle => "Portable Flow";
    public IList ItemsOf(object root) => ((PortableFlowRoot)root).Actions;
    public object AddRoot(object key) => null;
}

[Serializable]
public sealed class PortableFlowRoot : IGraphFlowContainer, ISequentialActionContainer
{
    public List<PortableActionSlot> Actions = new();
    public IReadOnlyList<ActionSlotBase> SequentialActions => Actions;
    public GraphFlowDescription DescribeFlow() => GraphFlowDescription.Sequence(Actions);
}

[Serializable]
public sealed class PortableActionSlot : ActionSlotBase
{
    [SerializeReference] public GraphNode Source;
    [SerializeField] private string id;
    [SerializeField] private bool disabled;
    [SerializeField] private string label;
    [SerializeField] private Vector2 pos;
    [SerializeField] private bool hasPos;
    [SerializeReference] private List<GraphNode> orphans = new();
    public override GraphNode Node => Source;
    public override void SetNode(GraphNode node) => Source = node;
    public override bool AcceptsBody(GraphNodeContent body) => body is PortableFlowAction;
    public override bool Disabled { get => disabled; set => disabled = value; }
    public override string Label { get => label; set => label = value; }
    public override string Id => id;
    public override string EnsureId() => string.IsNullOrEmpty(id) ? id = Guid.NewGuid().ToString("N") : id;
    public override void ResetId() => id = Guid.NewGuid().ToString("N");
    public override Vector2 Pos { get => pos; set { pos = value; hasPos = true; } }
    public override bool HasPos => hasPos;
    public override void ClearPos() { pos = default; hasPos = false; }
    public override List<GraphNode> Orphans => orphans;
    public override Type PackType => typeof(object);
    public override Type BodyBaseType => typeof(PortableFlowAction);
    public override Type AssetBaseType => typeof(ScriptableObject);
}

[Serializable] public class PortableFlowAction : GraphNodeContent { public int Amount = 1; }

[Serializable]
public sealed class PortableOpaqueFlow : PortableFlowAction
{
    public List<PortableActionSlot> HiddenActions = new();
}

[Serializable]
public sealed class PortableFlowLoop : PortableFlowAction, IGraphFlowContainer
{
    public PortableActionSlot Source = new();
    public List<PortableActionSlot> Body = new();
    public GraphFlowDescription DescribeFlow() => new(GraphFlowKind.Loop,
        new[] { new GraphFlowSection("body", "Body", GraphFlowSectionRole.Body, Body) },
        new[] { new GraphFlowInput("source", "Source", Source) });
}
}
