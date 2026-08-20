namespace HaruFamily.Framework.ActionSystem.Editor
{
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 資產工作副本的一份快照。內容、候選與變數必須是**同一次深複製**的產物，
/// 分次抄會把同一顆端點抄成幾份不相干的物件，變數節點指到的就不是清單裡那一顆。
/// </summary>
public class AGAssetSnapshot
{
    public GraphNode Root;
    public List<GraphNode> Orphans;
    public List<GraphEndpoint> Endpoints;
}

/// <summary>
/// 資產焦點的復原歷程。資產是獨立存檔交易、不進 Owner 的工作副本，所以 <see cref="AGModel"/> 的
/// Undo 堆疊蓋不到它，得自己記一份。行為刻意與那邊一致：快照式、0.4 秒內連續修改合併成一步、上限 40 步。
/// </summary>
// 快照式而不是逐項記錄：圖是 SerializeReference 多型樹，節點數是幾十個等級，整份抄最直接。
public class AGAssetHistory
{
    private const int UndoLimit = 40;
    private const double MergeWindow = 0.4;

    private readonly List<AGAssetSnapshot> undoStack = new();
    private readonly List<AGAssetSnapshot> redoStack = new();
    private AGAssetSnapshot baseline;                 // 上一個記錄點的狀態（＝本次修改前的狀態）
    private double lastPushTime;

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    /// <summary>進出資產時重開歷程。current 傳進資產當下的狀態，第一次修改才有東西可以退回。</summary>
    public void Reset(AGAssetSnapshot current)
    {
        undoStack.Clear();
        redoStack.Clear();
        baseline = current;
        lastPushTime = 0d;
    }

    /// <summary>強制切一個記錄點，讓下一次修改不會跟前一次合併。</summary>
    public void BreakMerge() => lastPushTime = 0d;

    /// <summary>記一步。current 是**修改後**的狀態；推進堆疊的是上一份 baseline（修改前）。</summary>
    public void Record(AGAssetSnapshot current)
    {
        if (current == null) return;
        double now = EditorApplication.timeSinceStartup;

        if (baseline != null && now - lastPushTime >= MergeWindow)
        {
            undoStack.Add(baseline);
            if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
            redoStack.Clear();
            lastPushTime = now;
        }
        else if (baseline == null)
        {
            lastPushTime = now;
        }

        baseline = current;
    }

    /// <summary>退一步。current 是目前狀態（進 redo 堆疊）；回傳要套用的快照，沒得退回 null。</summary>
    public AGAssetSnapshot Undo(AGAssetSnapshot current)
    {
        if (undoStack.Count == 0 || current == null) return null;
        redoStack.Add(current);

        var restored = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        // 套用後那份快照就是活的物件，baseline 不能跟它共用參考，否則下一次修改會連歷程一起改掉。
        baseline = null;
        lastPushTime = 0d;
        return restored;
    }

    /// <summary>前進一步。規則與 <see cref="Undo"/> 對稱。</summary>
    public AGAssetSnapshot Redo(AGAssetSnapshot current)
    {
        if (redoStack.Count == 0 || current == null) return null;
        undoStack.Add(current);

        var restored = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        baseline = null;
        lastPushTime = 0d;
        return restored;
    }

    /// <summary>套用快照之後由視窗回報新的 baseline（＝套用後現場再抄一份），歷程才不會跟活資料共用參考。</summary>
    public void Rebase(AGAssetSnapshot current) => baseline = current;
}

}
