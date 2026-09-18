namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using UnityEngine;

/// <summary>One shared Header status strip: errors first, then the selected execution, then editor warnings.</summary>
internal static class HGNodeStatus
{
    public const float StripHeight = 4f;
    public static Color? ColorOf(bool hasIssue, bool isError, GraphNodeExecutionSnapshot? execution)
    {
        if ((hasIssue && isError) || execution?.State == GraphExecutionState.Failed) return HGStyles.Error;
        if (execution.HasValue)
            return execution.Value.State switch
            {
                GraphExecutionState.Holding => HGStyles.Warning,
                GraphExecutionState.Running => HGStyles.ExecutionRunning,
                GraphExecutionState.Completed => HGStyles.ExecutionCompleted,
                GraphExecutionState.Cancelled => HGStyles.ExecutionCancelled,
                _ => HGStyles.ExecutionNotVisited,
            };
        return hasIssue ? HGStyles.Warning : null;
    }

    public static string Describe(GraphNodeExecutionSnapshot execution)
    {
        return $"{StateLabel(execution.State)} · 抵達 {execution.Visits} 次 · 完成 {execution.Completed} 次"
            + $" · 等待 {execution.Waiting} · 執行中 {execution.Running}"
            + $" · 失敗 {execution.Failed} · 取消 {execution.Cancelled}"
            + (string.IsNullOrEmpty(execution.Error) ? "" : "\n" + execution.Error);
    }

    public static string StateLabel(GraphExecutionState state) => state switch
    {
        GraphExecutionState.Holding => "Hold 等待中",
        GraphExecutionState.Running => "執行中",
        GraphExecutionState.Completed => "已完成",
        GraphExecutionState.Failed => "執行失敗",
        GraphExecutionState.Cancelled => "已取消",
        _ => "本次尚未抵達",
    };
}
}
