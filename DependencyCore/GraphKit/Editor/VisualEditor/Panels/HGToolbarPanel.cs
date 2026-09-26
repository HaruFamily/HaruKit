namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次工具列需要的全部資料。存檔能不能按、標籤寫什麼，都由視窗算好再交進來。</summary>
public struct HGToolbarView
{
    /// <summary>麵包屑：對象名（型別）。兼任換對象鈕的標籤。</summary>
    public string Crumb;

    /// <summary>麵包屑的 tooltip：對象的資產路徑。</summary>
    public string CrumbTooltip;

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

    /// <summary>開節點搜尋，與 Ctrl+F 相同。</summary>
    public Action Search;
}

/// <summary>
/// 最上方資訊列：目前對誰工作，以及存檔／取消／復原這些與圖內容無關的全域動作。
/// 由左往右分三區：對象（鎖定、麵包屑兼換對象鈕、待切換）｜視圖（搜尋、介面顯示）｜文件（復原重做、取消、存檔）。
/// </summary>
// 回報／命令區，不接受拖放。這個面板沒有任何自己的視圖狀態，所以是 static——
// 面板契約要的是「狀態不要散在視窗」，不是「每個面板都得有狀態」。
public static class HGToolbarPanel
{
    private const float ButtonHeight = 19f;
    private const float Gap = 4f;

    /// <summary>區與區之間的間距，中間畫一條分隔線。</summary>
    private const float GroupGap = 13f;

    private const float MinCrumbWidth = 80f;

    public static void Draw(Rect r, in HGToolbarView view, in HGToolbarCommands cmd)
    {
        HGStyles.Fill(r, HGStyles.Toolbar);
        float y = r.y + 1f;

        // 右區先排，左區的麵包屑才知道能用多寬。由右往左：存檔 → 取消 → 復原重做 → 介面顯示 → 搜尋。
        float x = r.xMax - 6f;

        x -= 94f;
        var saveRect = new Rect(x, y, 94f, ButtonHeight);
        GUI.enabled = view.SaveEnabled;
        var saveColor = GUI.backgroundColor;
        if (view.SaveHighlight) GUI.backgroundColor = HGStyles.SaveHighlight;
        if (GUI.Button(saveRect, new GUIContent(view.SaveLabel, view.SaveTooltip))) cmd.Save();
        GUI.backgroundColor = saveColor;
        GUI.enabled = true;

        // 取消會捨棄全部修改，與存檔之間多留一點距離，避免順手按錯。
        x -= 60f + 8f;
        if (GUI.Button(new Rect(x, y, 60f, ButtonHeight), new GUIContent(view.BackLabel, view.BackTooltip)))
            cmd.Back();

        x = Separator(r, x);

        // 資產焦點也有復原：它走自己的歷程（HGAssetHistory），與 Owner 那份互不干擾。
        // 復原與重做用相連的分段鈕，讀起來是一組，不會和取消／存檔混在一起。
        x -= 44f;
        GUI.enabled = view.CanRedo;
        if (GUI.Button(new Rect(x, y, 44f, ButtonHeight), new GUIContent("重做", "Ctrl+Y / Ctrl+Shift+Z"), EditorStyles.miniButtonRight))
            cmd.Redo();
        x -= 44f;
        GUI.enabled = view.CanUndo;
        if (GUI.Button(new Rect(x, y, 44f, ButtonHeight), new GUIContent("復原", "Ctrl+Z"), EditorStyles.miniButtonLeft))
            cmd.Undo();
        GUI.enabled = true;

        // 中區：怎麼看這張圖。介面顯示在右、搜尋在左。
        x = Separator(r, x);
        if (view.HasLibraries)
        {
            x -= 76f;
            var libraryRect = new Rect(x, y, 76f, ButtonHeight);
            if (GUI.Button(libraryRect, new GUIContent("介面顯示", "顯示或隱藏庫區"), EditorStyles.toolbarDropDown))
                cmd.ShowLibraries?.Invoke(libraryRect);
            x -= Gap;
        }
        x -= 52f;
        if (GUI.Button(new Rect(x, y, 52f, ButtonHeight), new GUIContent("搜尋", "搜尋這張畫布上的節點（含被收起的）\n快捷鍵 Ctrl+F")))
            cmd.Search?.Invoke();
        float rightStart = x - GroupGap;

        // 鎖放在最左：它和麵包屑都在回答「現在對誰工作」。
        // 不用內建的 "IN LockButton" 樣式與鎖頭圖示：樣式名在不同 Unity 版本會變，
        // 找不到時每一幀都吐錯誤；純文字按鈕沒有這個風險，狀態也直接寫在臉上。
        var lockRect = new Rect(r.x + 4f, y, 52f, ButtonHeight);
        var lockColor = GUI.backgroundColor;
        if (view.Locked) GUI.backgroundColor = HGStyles.ToolbarLocked;
        if (GUI.Button(lockRect, new GUIContent(view.Locked ? "已鎖定" : "鎖定",
                view.Locked
                    ? "已鎖定：在 Project／Hierarchy 點別的東西不會把這個視窗切走。點此解鎖"
                    : "鎖住目前的編輯對象，之後在 Project／Hierarchy 點別的東西都不會切走")))
            cmd.ToggleLock();
        GUI.backgroundColor = lockColor;

        float crumbX = lockRect.xMax + Gap;
        float pendingWidth = view.PendingTargetName == null ? 0f : 130f + Gap;
        float crumbSpace = rightStart - crumbX - pendingWidth;
        if (crumbSpace < MinCrumbWidth) return;

        // 麵包屑本身就是換對象鈕：點名字換對象，比旁邊另放一顆小鈕好找也好按。
        // 資產焦點下不給換（資產是獨立一層），停用時仍照常顯示名字。
        string crumbTip = string.IsNullOrEmpty(view.CrumbTooltip) ? "換編輯對象" : $"{view.CrumbTooltip}\n點此換編輯對象";
        float crumbWidth = Mathf.Min(crumbSpace, EditorStyles.popup.CalcSize(new GUIContent(view.Crumb)).x);
        var crumbRect = new Rect(crumbX, y, crumbWidth, ButtonHeight);
        GUI.enabled = view.OwnerPickerEnabled;
        if (EditorGUI.DropdownButton(crumbRect, HGStyles.Elide(view.Crumb, EditorStyles.popup, crumbWidth, crumbTip),
                FocusType.Passive, EditorStyles.popup))
            cmd.PickOwner(crumbRect);
        GUI.enabled = true;

        if (view.PendingTargetName == null) return;

        var switchRect = new Rect(crumbRect.xMax + Gap, y, 130f, ButtonHeight);
        var label = HGStyles.Elide($"切換→{view.PendingTargetName}", GUI.skin.button, switchRect.width,
            "剛才選取了別的對象，按此切換（目前的修改會依提示處理）");
        var old = GUI.backgroundColor;
        GUI.backgroundColor = HGStyles.ToolbarLocked;
        if (GUI.Button(switchRect, label)) cmd.SwitchTarget();
        GUI.backgroundColor = old;
    }

    /// <summary>在 x 左邊留出區間距並畫一條分隔線，回傳下一區的右緣。</summary>
    private static float Separator(Rect r, float x)
    {
        float lineX = x - GroupGap * 0.5f;
        HGStyles.Fill(new Rect(lineX, r.y + 4f, 1f, r.height - 8f), HGStyles.ListRule);
        return x - GroupGap;
    }
}

}
