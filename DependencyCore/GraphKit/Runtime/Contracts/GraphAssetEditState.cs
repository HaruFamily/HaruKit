#if UNITY_EDITOR
namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor-only, borrowed graph state. Clone the entire state once to preserve internal sharing.</summary>
[Serializable]
public sealed class GraphAssetEditState
{
    [SerializeReference] public GraphSlotBase Input;
    [SerializeReference] public List<GraphNode> Orphans;
    [SerializeReference] public List<GraphToken> Tokens;
    [SerializeReference] public List<GraphProperty> Properties;
    public GraphViewState ViewState;
    public Vector2 HeadPosition;
    public bool HasHeadPosition;

    public void RequireComplete()
    {
        if (Input == null || Orphans == null || Tokens == null || Properties == null || ViewState == null)
            throw new InvalidOperationException("Asset graph state is incomplete; no automatic initialization was performed.");
    }
}

/// <summary>
/// Explicit authoring contract. Capture must not initialize, clean or mutate the asset.
/// Apply must validate everything before assigning, then retain the supplied graph references as one operation;
/// it must not save, execute content, mutate the previous state or touch other assets.
/// </summary>
public interface IEditableGraphAsset : IGraphAsset
{
    GraphAssetEditState CaptureEditorState();
    void ApplyEditorState(GraphAssetEditState state);
}
}
#endif
