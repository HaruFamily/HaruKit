namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次工具列需要的全部資料。存檔能不能按、標籤寫什麼，都由視窗算好再交進來。</summary>
public struct HGToolbarView
{
    /// <summary>麵包屑：對象名（型別）（路徑）。</summary>
    public string Crumb;

    /// <summary>資產焦點下不給換對象——資產是獨立一層，未儲存狀態與外層各記各的。</summary>
    public bool OwnerPickerEnabled;

    /// <summary>鎖定中：在 Project／Hierarchy 點別的東西不會把這個視窗切走。</summary>
    public bool Locked;
    public bool HasLibraries;

    public bool SaveEnabled;

    /// <summary>可存檔時把鈕染紅，讓「有東西還沒存」在餘光裡看得到。</summary>
    public bool SaveHighlight;

    public string SaveLabel;
    public string SaveTooltip;
    public string BackLabel;
    public string BackTooltip;
    public bool CanUndo;
    public bool CanRedo;

    /// <summary>剛才在 Project／Hierarchy 選了別的對象；null＝沒有待切換的。</summary>
    public string PendingTargetName;
}

/// <summary>工具列對外的命令。</summary>
public struct HGToolbarCommands
{
    /// <summary>開換對象選單，錨點是那顆鈕。</summary>
    public Action<Rect> PickOwner;

    public Action Save;
    public Action Back;
    public Action Undo;
    public Action Redo;
    public Action SwitchTarget;

    /// <summary>切換鎖定。</summary>
    public Action ToggleLock;
    public Action<Rect> ShowLibraries;
}

/// <summary>
/// 最上方資訊列：目前對誰工作，以及存檔／取消／復原這些與圖內容無關的全域動作。
/// </summary>
// 回報／命令區，不接受拖放。這個面板沒有任何自己的視圖狀態，所以是 static——
// 面板契約要的是「狀態不要散在視窗」，不是「每個面板都得有狀態」。
public static class HGToolbarPanel
{
    public static void Draw(Rect r, in HGToolbarView view, in HGToolbarCommands cmd)
    {
        HGStyles.Fill(r, HGStyles.Toolbar);

        var ownerPickerRect = new Rect(r.x + 4f, r.y + 2f, 18f, 18f);
        GUI.enabled = view.OwnerPickerEnabled;
        if (GUI.Button(ownerPickerRect, new GUIContent("", "換編輯對象"), EditorStyles.popup))
            cmd.PickOwner(ownerPickerRect);
        GUI.enabled = true;

        // 鎖緊挨著換對象鈕：兩顆都在回答「現在對誰工作」，擺在一起才讀得出是同一件事。
        // 不用內建的 "IN LockButton" 樣式與鎖頭圖示：樣式名在不同 Unity 版本會變，
        // 找不到時每一幀都吐錯誤；純文字按鈕沒有這個風險，狀態也直接寫在臉上。
        var lockRect = new Rect(ownerPickerRect.xMax + 4f, r.y + 1f, 58f, 19f);
        var lockColor = GUI.backgroundColor;
        if (view.Locked) GUI.backgroundColor = HGStyles.ToolbarLocked;
        if (GUI.Button(lockRect, new GUIContent(view.Locked ? "已鎖定" : "鎖定",
                view.Locked
                    ? "已鎖定：在 Project／Hierarchy 點別的東西不會把這個視窗切走。點此解鎖"
                    : "鎖住目前的編輯對象，之後在 Project／Hierarchy 點別的東西都不會切走")))
            cmd.ToggleLock();
        GUI.backgroundColor = lockColor;

        float crumbX = lockRect.xMax + 4f;
        if (view.HasLibraries)
        {
            var libraryRect = new Rect(crumbX, r.y + 1f, 48f, 19f);
            if (GUI.Button(libraryRect, new GUIContent("庫 ▾", "顯示或隱藏庫區"), EditorStyles.toolbarDropDown))
                cmd.ShowLibraries?.Invoke(libraryRect);
            crumbX = libraryRect.xMax + 4f;
        }
        GUI.Label(new Rect(crumbX, r.y + 2f, Mathf.Max(0f, r.width - 502f - (crumbX - lockRect.xMax - 4f)), 18f),
            view.Crumb, EditorStyles.boldLabel);

        float x = r.xMax - 6f;

        x -= 96f;
        var saveRect = new Rect(x, r.y + 1f, 94f, 19f);
        GUI.enabled = view.SaveEnabled;
        var saveColor = GUI.backgroundColor;
        if (view.SaveHighlight) GUI.backgroundColor = HGStyles.SaveHighlight;
        if (GUI.Button(saveRect, new GUIContent(view.SaveLabel, view.SaveTooltip))) cmd.Save();
        GUI.backgroundColor = saveColor;
        GUI.enabled = true;

        x -= 62f;
        if (GUI.Button(new Rect(x, r.y + 1f, 60f, 19f), new GUIContent(view.BackLabel, view.BackTooltip)))
            cmd.Back();

        // 資產焦點也有復原：它走自己的歷程（HGAssetHistory），與 Owner 那份互不干擾。
        x -= 48f;
        GUI.enabled = view.CanRedo;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("重做", "Ctrl+Y / Ctrl+Shift+Z"))) cmd.Redo();
        GUI.enabled = true;

        x -= 48f;
        GUI.enabled = view.CanUndo;
        if (GUI.Button(new Rect(x, r.y + 1f, 46f, 19f), new GUIContent("復原", "Ctrl+Z"))) cmd.Undo();
        GUI.enabled = true;

        x -= 132f;
        if (view.PendingTargetName == null) return;

        var switchRect = new Rect(x, r.y + 1f, 130f, 19f);
        var label = new GUIContent($"切換→{view.PendingTargetName}",
            "剛才選取了別的對象，按此切換（目前的修改會依提示處理）");
        var old = GUI.backgroundColor;
        GUI.backgroundColor = HGStyles.ToolbarLocked;
        if (GUI.Button(switchRect, label)) cmd.SwitchTarget();
        GUI.backgroundColor = old;
    }
}

}
