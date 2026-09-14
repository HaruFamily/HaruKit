namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 畫一次 Console 需要的全部資料。面板不認識 focus、model 或圖，只認這份快照，
/// 所以它落在哪個框、服務哪個領域都不影響它怎麼畫。
/// </summary>
public struct HGConsoleView
{
    /// <summary>要列出的問題清單與計數。</summary>
    public HGReport Report;

    /// <summary>這張圖驗證過至少一次。false 時狀態欄顯示「尚未驗證」。</summary>
    public bool VerifiedOnce;

    /// <summary>目前的報告是完整驗證的結果，不是編輯途中的即時結果。</summary>
    public bool Fresh;

    /// <summary>標頭右側的警告字串；null 或空＝不顯示。</summary>
    public string OwnerWarning;
}

/// <summary>
/// 下方框：驗證回報。不接受拖放，對外只有一條單向命令——跳到某個問題所在的位置。
/// 高度、收合、捲動與分頁都由自己持有，連 EditorPrefs 的讀寫也在這裡，視窗只負責給它一塊 Rect。
/// </summary>
// 這是分區契約的第一個實作：面板持有視圖狀態、視窗持有版面。面板不得反過來呼叫視窗，
// 要影響畫布只能透過 Draw 傳進來的命令委派——這條界線一鬆，跨區耦合就會從這裡長回去。
public sealed class HGConsolePanel
{
    /// <summary>收合後只剩標題列。這個高度同時是版面計算的下限。</summary>
    public const float HeaderHeight = 22f;

    private const float RowHeight = 20f;
    private const float TabCornerRadius = 3f;
    private const float MinDragHeight = 60f;
    private const string PrefHeight = "HaruGraph.ConsoleHeight";
    private const string PrefCollapsed = "HaruGraph.ConsoleCollapsed";

    private float height = 150f;
    private bool collapsed;
    private Vector2 scroll;
    private int tab;                 // 0 全部 / 1 錯誤 / 2 警告
    private bool resizing;

    /// <summary>拖曳中。視窗用它決定分隔把手要不要畫成按住的樣子。</summary>
    public bool IsResizing => resizing;

    public void LoadPrefs()
    {
        height = EditorPrefs.GetFloat(PrefHeight, 150f);
        collapsed = EditorPrefs.GetBool(PrefCollapsed, false);
    }

    public void SavePrefs()
    {
        EditorPrefs.SetFloat(PrefHeight, height);
        EditorPrefs.SetBool(PrefCollapsed, collapsed);
    }

    /// <summary>驗證抓到錯誤時展開並切到錯誤頁。呼叫端不必知道分頁編號。</summary>
    public void RevealErrors()
    {
        collapsed = false;
        tab = 1;
    }

    /// <summary>本幀要佔多高。收合時就是標題列高度。</summary>
    public float LayoutHeight(float maxHeight)
        => collapsed ? HeaderHeight : Mathf.Clamp(height, HeaderHeight, Mathf.Max(HeaderHeight, maxHeight));

    /// <summary>回傳 true 代表高度變了，呼叫端要 Repaint。</summary>
    public bool HandleResize(Rect handle, float maxHeight)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;

        if (e.type == EventType.MouseDown && e.button == 0 && !collapsed && handle.Contains(e.mousePosition))
        {
            resizing = true;
            e.Use();
            return false;
        }
        if (e.type == EventType.MouseDrag && resizing)
        {
            height = Mathf.Clamp(height - e.delta.y, MinDragHeight, Mathf.Max(MinDragHeight, maxHeight));
            e.Use();
            return true;
        }
        if (e.type == EventType.MouseUp && resizing)
        {
            resizing = false;
            e.Use();
        }
        return false;
    }

    /// <summary><paramref name="jump"/> 是這個面板唯一的對外影響力：點一列問題就發一次。</summary>
    public void Draw(Rect r, in HGConsoleView view, Action<HGIssue> jump)
    {
        HGStyles.Fill(r, HGStyles.Console);
        HGStyles.Frame(r, HGStyles.NodeBorder);

        var report = view.Report;
        var head = new Rect(r.x, r.y, r.width, HeaderHeight);
        if (GUI.Button(new Rect(head.x + 2f, head.y + 2f, 18f, 17f), collapsed ? "▸" : "▾", EditorStyles.miniButton))
            collapsed = !collapsed;

        float tx = head.x + 24f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"全部 {report.Issues.Count}", tab == 0)) tab = 0;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"錯誤 {report.ErrorCount}", tab == 1)) tab = 1;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"警告 {report.WarningCount}", tab == 2)) tab = 2;

        string verifyStatus = view.Fresh
            ? $"完整驗證 {report.Time:HH:mm:ss}"
            : view.VerifiedOnce
                ? $"即時驗證 {report.Time:HH:mm:ss}"
                : "尚未驗證";
        GUI.Label(new Rect(head.xMax - 274f, head.y + 3f, 270f, 16f), verifyStatus, HGStyles.Tiny);

        if (!string.IsNullOrEmpty(view.OwnerWarning))
            GUI.Label(new Rect(head.xMax - 470f, head.y + 3f, 192f, 16f), view.OwnerWarning, HGStyles.RowLabelError);

        if (collapsed) return;

        var listRect = new Rect(r.x + 2f, r.y + HeaderHeight, r.width - 4f, r.height - HeaderHeight - 2f);
        var shown = new List<HGIssue>();
        foreach (var issue in report.Issues)
        {
            if (tab == 1 && !issue.IsError) continue;
            if (tab == 2 && issue.IsError) continue;
            shown.Add(issue);
        }

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * RowHeight + 4f);
        scroll = GUI.BeginScrollView(listRect, scroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var issue = shown[i];
            var row = new Rect(0f, i * RowHeight, content.width, RowHeight - 1f);
            if (i % 2 == 1) HGStyles.Fill(row, HGStyles.RowAlt);

            var icon = new Rect(row.x + 4f, row.y + 5f, 9f, 9f);
            HGStyles.Fill(icon, issue.IsError ? HGStyles.Error : HGStyles.Warning);
            GUI.Label(new Rect(row.x + 18f, row.y + 1f, row.width - 22f, 17f), issue.Line, HGStyles.ConsoleRow);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                jump?.Invoke(issue);
                Event.current.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>Console 分頁：選中才給滿色。分頁沒有身分，走中性灰。</summary>
    private static bool DrawTab(Rect r, string label, bool active)
    {
        HGStyles.RoundedFill(r, HGStyles.CellTint(HGStyles.NodeBody, false, active), TabCornerRadius);
        GUI.Label(r, label, HGStyles.Tiny);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }
}

}
