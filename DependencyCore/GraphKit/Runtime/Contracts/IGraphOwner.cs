namespace HaruFamily.DependencyCore.GraphKit
{
using System.Collections.Generic;

public interface IGraphOwner
{
    void MarkGraphDirty();
    bool IsGraphValidated();
#if UNITY_EDITOR
    void VerifyGraph();
#endif
}

/// <summary>Optional Runtime-owned domain validation consumed by an Editor session without a Tool-to-Editor dependency.</summary>
public interface IGraphDomainDiagnostics
{
#if UNITY_EDITOR
    void CollectDiagnostics(IGraphDocument document, List<GraphDiagnostic> diagnostics);
#endif
}

}
