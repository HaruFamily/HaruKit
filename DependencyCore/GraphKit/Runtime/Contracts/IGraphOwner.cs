namespace HaruFamily.DependencyCore.GraphKit
{
#if UNITY_EDITOR
using System.Collections.Generic;
#endif

public interface IGraphOwner
{
    void MarkGraphDirty();
    bool IsGraphValidated();
#if UNITY_EDITOR
    void VerifyGraph();

    /// <summary>
    /// 本載體實際會被觸發的時機。編輯器的時機選單只列這些值，接不到不會跑的時機。
    /// null＝不限制（列出 TTiming 的全部成員）。型別是中性 Enum：泛型層不認識專案的時機列舉。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<System.Enum> AllowedTimings { get; }
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
