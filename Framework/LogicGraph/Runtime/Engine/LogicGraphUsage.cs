namespace HaruFamily.Framework.LogicGraph
{
using System;
#if UNITY_EDITOR
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
#endif

/// <summary>可選的專案設定入口。沒有實作時，LogicGraph 使用全部時機與基本圖驗證。</summary>
public interface ILogicGraphUsage<TTiming, TPack> where TTiming : Enum
{
#if UNITY_EDITOR
    void ConfigureGraph(LogicGraphUsage<TTiming, TPack> usage);
#endif
}

#if UNITY_EDITOR
/// <summary>每次查詢重新建立的使用設定；只宣告規則，不持有 Owner 或改寫文件。</summary>
public sealed class LogicGraphUsage<TTiming, TPack> where TTiming : Enum
{
    private List<TTiming> timings;
    private readonly List<(string Name, string Path)> requiredTokens = new();
    private readonly List<Action<LogicGraph<TTiming, TPack>, LogicGraphValidation>> validators = new();
    private string configurationError;

    /// <summary>null 表示使用全部時機；空集合表示不允許任何時機。</summary>
    public void AllowTimings(IEnumerable<TTiming> allowed)
        => timings = allowed == null ? null : new List<TTiming>(allowed);

    /// <summary>宣告由圖外依名稱取用的 Token；可提供來源欄位供診斷定位。</summary>
    public void RequireToken(string name, string fieldPath = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var item = (name, fieldPath);
        if (!requiredTokens.Contains(item)) requiredTokens.Add(item);
    }

    public void RequireTokens(IEnumerable<string> names, string fieldPath = null)
    {
        if (names == null) return;
        foreach (var name in names) RequireToken(name, fieldPath);
    }

    /// <summary>只在需要專案特殊規則時加入；graph 永遠是當次驗證的文件或工作副本。</summary>
    public void AddValidation(Action<LogicGraph<TTiming, TPack>, LogicGraphValidation> validate)
    {
        if (validate == null) throw new ArgumentNullException(nameof(validate));
        validators.Add(validate);
    }

    internal bool Allows(TTiming timing) => configurationError == null && (timings == null || timings.Contains(timing));
    internal void RejectConfiguration(string message) => configurationError = message;

    internal void Collect(LogicGraph<TTiming, TPack> graph, List<GraphDiagnostic> diagnostics)
    {
        var report = new LogicGraphValidation(diagnostics);
        if (configurationError != null)
        {
            report.Error("logicgraph.usage.configuration-failed", "圖的使用設定失敗：" + configurationError);
            return;
        }

        if (timings != null && graph.ActionGroups != null)
            for (int i = 0; i < graph.ActionGroups.Count; i++)
            {
                var group = graph.ActionGroups[i];
                if (group == null || Allows(group.Timing)) continue;
                report.Error("logicgraph.timing.disallowed", $"{group.Timing} 不會在此載體上觸發。",
                    $"ActionGroups[{i}].Timing");
            }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in graph.Tokens)
            if (!string.IsNullOrEmpty(token?.Name)) declared.Add(token.Name);
        foreach (var required in requiredTokens)
            if (!declared.Contains(required.Name))
                report.Error("logicgraph.external-token.missing", $"圖外引用了不存在的 Token '{required.Name}'。", required.Path);

        foreach (var validate in validators)
        {
            try { validate(graph, report); }
            catch (Exception exception)
            {
                report.Error("logicgraph.usage.validation-failed", "專案驗證失敗：" + exception.Message);
            }
        }
    }
}

/// <summary>專案驗證的最小回報介面；code 是穩定識別碼，文字只供顯示。</summary>
public sealed class LogicGraphValidation
{
    private readonly List<GraphDiagnostic> diagnostics;
    internal LogicGraphValidation(List<GraphDiagnostic> diagnostics) => this.diagnostics = diagnostics;

    public void Error(string code, string message, string fieldPath = null, string nodeId = null, string tokenId = null)
        => Add(new GraphDiagnostic(code, GraphDiagnosticSeverity.Error, message,
            new GraphDiagnosticLocation(nodeId: nodeId, tokenId: tokenId, fieldPath: fieldPath)));

    public void Warning(string code, string message, string fieldPath = null, string nodeId = null, string tokenId = null)
        => Add(new GraphDiagnostic(code, GraphDiagnosticSeverity.Warning, message,
            new GraphDiagnosticLocation(nodeId: nodeId, tokenId: tokenId, fieldPath: fieldPath)));

    /// <summary>供既有領域驗證器轉接，內容作者通常只需 Error／Warning。</summary>
    public void Add(GraphDiagnostic diagnostic)
    {
        if (diagnostic != null) diagnostics.Add(diagnostic);
    }
}
#endif
}
