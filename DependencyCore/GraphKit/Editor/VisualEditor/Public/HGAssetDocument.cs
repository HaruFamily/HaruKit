namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A graph-only asset working document. Never serialized into the asset itself.</summary>
[Serializable]
public sealed class HGAssetDocument : IGraphDocument, ITokenOwner, IPropertyOwner, IGraphViewStateOwner
{
    public GraphAssetEditState State;
    public string Scope;
    public HGAssetDocument(GraphAssetEditState state, string scope) { State = state; Scope = scope; state.RequireComplete(); }
    public IList Roots => new object[] { State.Input };
    public List<GraphNode> Orphans => State.Orphans;
    public List<GraphToken> Tokens => State.Tokens;
    public List<GraphProperty> Properties => State.Properties;
    public GraphViewState ViewState => State.ViewState;
    public bool IsValidated => false;
    public void InvalidateValidation() { }
    public void Verify() => throw new NotSupportedException("Use structural diagnostics; this adapter does not execute asset content.");
    public object DeepCopy() => GraphDeepCopy.Copy(this);
    public Type PackType => State.Input is FormulaSlotBase formula ? formula.PackType : (State.Input as ActionSlotBase)?.PackType;
    public Type ItemSlotType => State.Input.GetType();
    public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => Array.Empty<object>();
    public object KeyOf(object root) => "asset-input";
    public string TitleOf(object root) => "Asset input";
    public string RootChip => "Asset";
    public string RootNoun => "Asset input";
    public string WindowTitle => "Asset draft";
    public HGCapabilities Capabilities => HGCapabilities.None;
    public IList ItemsOf(object root) => new object[] { State.Input };
    public object AddRoot(object key) => throw new NotSupportedException("An asset has exactly one fixed input.");
}
}
