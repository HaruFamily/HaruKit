namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using System.Collections;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using UnityEngine;

public sealed class PortableGraphOwner : ScriptableObject
{
    [SerializeReference] public PortableGraphDocument Document = new();
    public ScriptableObject Dependency;
}

[Serializable]
public sealed class PortableGraphValue : GraphNodeContent
{
    public int Amount = 1;
    [SerializeReference] public PortableOptions Options;
    [SerializeReference] public List<PortableOptions> Items = new();
}

[Serializable] public abstract class PortableOptions { }
[Serializable] public sealed class PortableScale : PortableOptions { [Range(1, 9)] public int Scale = 2; }
[Serializable] public sealed class PortableLabel : PortableOptions { public string Label = "label"; }
[Serializable] public sealed class PortableLinked : PortableOptions { public GraphNode Link = new(new PortableGraphValue()); }

/// <summary>A minimal consumer without execution framework or game dependencies.</summary>
[Serializable]
public sealed class PortableGraphDocument : IGraphDocument
{
    [SerializeReference] public List<GraphNode> Nodes = new();
    public bool Validated;
    public List<GraphNode> Orphans => Nodes;
    public IList Roots => Array.Empty<object>();
    public bool IsValidated => Validated;
    public void InvalidateValidation() => Validated = false;
    public void Verify() => Validated = true;
    public object DeepCopy() => GraphDeepCopy.Copy(this);
    public Type PackType => typeof(object);
    public Type ItemSlotType => typeof(GraphSlotBase);
    public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => Array.Empty<object>();
    public object KeyOf(object root) => null;
    public string TitleOf(object root) => "Portable";
    public string RootChip => "Portable";
    public string RootNoun => "Root";
    public HGCapabilities Capabilities => HGCapabilities.None;
    public string WindowTitle => "Portable Graph";
    public IList ItemsOf(object root) => Array.Empty<object>();
    public object AddRoot(object key) => null;
}
}
