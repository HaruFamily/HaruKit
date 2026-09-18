namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

public partial class HaruGraphWindow
{
    private void RefreshExecutionSource()
    {
        IGraphExecutionDocument document = null;
        executionReadError = null;
        if (model != null && model.TryReadOwnerDocument(out var live, out executionReadError))
            document = live as IGraphExecutionDocument;
        if (!string.IsNullOrEmpty(executionReadError))
        {
            executionRevision = null;
            return; // A failed getter must not silently release the currently held execution.
        }
        GraphExecutionSource source;
        try
        {
            source = document?.ExecutionSource;
            executionRevision = document?.ExecutionRevision;
        }
        catch (Exception exception)
        {
            executionReadError = "ExecutionSource / ExecutionRevision：" + exception.Message;
            executionRevision = null;
            return;
        }
        if (!ReferenceEquals(source, executionSource))
        {
            executionObservation?.Dispose();
            executionSource = source;
            executionObservation = source?.Observe();
            selectedExecution = null;
            lastExecutionId = 0;
            followNextExecution = true;
        }
        if (source == null) return;
        var sessions = source.Sessions;
        if (sessions.Count == 0) return;
        var latest = sessions[sessions.Count - 1];
        if (latest.Id > lastExecutionId)
        {
            if (followNextExecution) { selectedExecution = latest; followNextExecution = false; }
            lastExecutionId = latest.Id;
        }
    }

    private void UpdateExecutionView()
    {
        if (EditorApplication.timeSinceStartup < nextExecutionRepaint) return;
        nextExecutionRepaint = EditorApplication.timeSinceStartup + 0.1;
        RefreshExecutionSource();
        if (executionSource != null) Repaint();
    }

    private void CancelObservedExecutions()
    {
        executionSource?.CancelAll();
    }

    private void ExecutionPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            CancelObservedExecutions();
    }

    private string ExecutionScope => focus.Kind == HGFocusKind.Asset && focus.AssetObject != null
        ? "asset:" + focus.AssetObject.GetInstanceID() : "";

    private bool ExecutionMatchesView => executionSource != null && !model.Dirty && !assetDirty
        && string.IsNullOrEmpty(executionReadError)
        && selectedExecution?.MappingDiagnostic == null
        && (selectedExecution == null || selectedExecution.Revision == executionRevision)
        && (selectedExecution == null || focus.Kind != HGFocusKind.Asset
            || focus.AssetObject is IGraphAsset asset && selectedExecution.MatchesScope(ExecutionScope, asset.Root));

    private GraphNodeExecutionSnapshot? NodeExecution(HGNodeView node)
    {
        if (!ExecutionMatchesView || selectedExecution == null || node.Carrier == null || string.IsNullOrEmpty(node.Carrier.Id)) return null;
        return selectedExecution.Query(new GraphExecutionNodeKey(node.Carrier.Id, ExecutionScope));
    }

    private bool NodeHasHold(HGNodeView node)
    {
        if (executionSource == null || node.Carrier == null || string.IsNullOrEmpty(node.Carrier.Id)) return false;
        var key = new GraphExecutionNodeKey(node.Carrier.Id, ExecutionScope);
        return selectedExecution != null ? selectedExecution.IsHeld(key)
            : executionSource.IsHeldForNextExecution(key, executionRevision);
    }

    private void ToggleNodeHold(HGNodeView node)
    {
        bool held = NodeHasHold(node);
        if (!held && (!ExecutionMatchesView || !EditorApplication.isPlaying || selectedExecution?.IsFinished == true)) return;
        var key = new GraphExecutionNodeKey(node.Carrier.Id, ExecutionScope);
        if (selectedExecution != null) selectedExecution.SetHold(key, !held);
        else executionSource.SetHoldForNextExecution(key, executionRevision, !held);
        Repaint(); // Observation state never invalidates the authored graph or its Undo history.
    }

    private void DrawExecutionPanel(Rect rect)
    {
        string description = !string.IsNullOrEmpty(executionReadError) ? "無法讀取執行文件：" + executionReadError
            : !EditorApplication.isPlaying ? "Play Mode 中可標記 Hold；只暫停抵達的執行鏈。"
            : selectedExecution?.MappingDiagnostic != null ? selectedExecution.MappingDiagnostic.Message
            : !ExecutionMatchesView ? "文件有未儲存修改或版本不符；隱藏執行顏色，仍可解除 Hold／取消執行。"
            : selectedExecution == null ? "準備下一次執行：點節點的 Ⅱ 設定 Hold，命中後點 ▶ 繼續。"
            : selectedExecution.IsFinished ? $"{HGNodeStatus.StateLabel(selectedExecution.State)}；保留結果供檢查，可選擇準備下一次執行。"
            : $"等待 {selectedExecution.WaitingCount} · 執行中 {selectedExecution.RunningCount}；其他執行鏈照常運作。";
        var view = new HGExecutionView
        {
            Title = selectedExecution == null ? "執行觀察 · 準備下一次執行" : $"執行 #{selectedExecution.Id} · {selectedExecution.Name}",
            Description = description,
            CanRelease = executionSource != null,
            CanCancel = selectedExecution != null && !selectedExecution.IsFinished,
        };
        HGExecutionPanel.Draw(rect, view, new HGExecutionCommands
        {
            Pick = ShowExecutionPicker,
            Release = () =>
            {
                if (selectedExecution != null) selectedExecution.ReleaseAllHolds();
                else executionSource.ClearNextHolds();
                Repaint();
            },
            Cancel = () => { selectedExecution?.Cancel(); Repaint(); },
        });
    }

    private void ShowExecutionPicker(Rect anchor)
    {
        if (executionSource == null) return;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("準備下一次執行"), selectedExecution == null, () =>
        { selectedExecution = null; followNextExecution = true; Repaint(); });
        var sessions = executionSource.Sessions;
        for (int i = sessions.Count - 1; i >= 0; i--)
        {
            var session = sessions[i];
            string state = session.IsFinished ? HGNodeStatus.StateLabel(session.State) : session.WaitingCount > 0 ? "Hold 等待中" : "執行中";
            menu.AddItem(new GUIContent($"#{session.Id} · {session.Name.Replace('/', '／')} · {state}"), ReferenceEquals(selectedExecution, session),
                () => { selectedExecution = session; followNextExecution = false; Repaint(); });
        }
        menu.DropDown(anchor);
    }
}
}
