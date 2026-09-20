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

/// <summary>文件可選的 Owner-aware 驗證能力；由文件實作，持有文件的內容類別不需轉發驗證。</summary>
public interface IGraphDocumentValidation
{
#if UNITY_EDITOR
    IReadOnlyList<GraphDiagnostic> CollectDiagnostics(UnityEngine.Object owner);
    void Verify(UnityEngine.Object owner);
#endif
}

}
