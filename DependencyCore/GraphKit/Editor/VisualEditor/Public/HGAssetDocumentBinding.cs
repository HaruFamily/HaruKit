namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEngine;

/// <summary>Bridges an explicit asset-state contract to the existing document commit guard.</summary>
public sealed class HGAssetDocumentBinding
{
    private readonly ScriptableObject owner;
    private readonly IEditableGraphAsset asset;
    private IGraphDocument live;
    public HGDocumentBinding<IGraphDocument> Binding { get; }
    public IGraphDocument ReadDocument() => live;

    public HGAssetDocumentBinding(ScriptableObject owner, string documentId, string scope, Func<UnityEngine.Object, string> revision)
    {
        this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
        asset = owner as IEditableGraphAsset ?? throw new ArgumentException("Asset does not implement isolated graph editing.");
        if (revision == null) throw new ArgumentNullException(nameof(revision));
        live = new HGAssetDocument(asset.CaptureEditorState(), scope);
        Binding = new HGDocumentBinding<IGraphDocument>(documentId, _ => live, Write, create: null, readRevision: revision);
    }

    private void Write(UnityEngine.Object target, IGraphDocument document)
    {
        if (target != owner || document is not HGAssetDocument next) throw new ArgumentException("Asset binding target mismatch.");
        try
        {
            next.State.RequireComplete();
            asset.ApplyEditorState(next.State);
            var stored = asset.CaptureEditorState();
            // Action inputs are temporary host Slots; their Node, libraries and layout are the persisted state.
            bool inputMatches = owner is IFormulaGraphAsset ? ReferenceEquals(stored.Input, next.State.Input)
                : ReferenceEquals(stored.Input?.Node, next.State.Input.Node);
            if (!inputMatches || !ReferenceEquals(stored.Orphans, next.State.Orphans) || !ReferenceEquals(stored.Tokens, next.State.Tokens)
                || !ReferenceEquals(stored.Properties, next.State.Properties) || !ReferenceEquals(stored.ViewState, next.State.ViewState)
                || stored.HeadPosition != next.State.HeadPosition || stored.HasHeadPosition != next.State.HasHeadPosition)
                throw new InvalidOperationException("Asset did not retain the submitted graph state.");
            live = next;
        }
        catch
        {
            // Never claim the old state is still present after a potentially partial setter.
            // The shared guard must invoke restoration and confirm it, or block further commits.
            live = null;
            throw;
        }
    }
}
}
