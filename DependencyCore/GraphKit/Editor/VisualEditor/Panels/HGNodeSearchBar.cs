namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>搜尋列要畫的一次性快照。</summary>
public struct HGNodeSearchBarView
{
    /// <summary>命中幾顆節點。</summary>
    public int MatchCount;

    /// <summary>目前停在第幾筆（0 起算）；還沒跳過任何一筆時是 -1。</summary>
    public int Current;
}

/// <summary>
/// 畫布內的節點搜尋列（Ctrl+F）：浮在畫布右上角、root 下拉的正下方。
/// 面板只持有開關、搜尋字與鍵盤焦點；命中哪些節點、怎麼跳過去由視窗決定。
/// </summary>
public sealed class HGNodeSearchBar
{
    public const float Width = 360f;
    public const float Height = 28f;

    private const string Control = "agNodeSearch";
    private const float ButtonWidth = 22f;
    private const float CountWidth = 56f;

    private bool focusRequested;

    public bool IsOpen { get; private set; }

    /// <summary>目前的搜尋字。關閉後保留，下次打開接著用。</summary>
    public string Query { get; private set; } = "";

    /// <summary>打開並把鍵盤焦點交給輸入框；已開著時只重新搶回焦點。</summary>
    public void Open()
    {
        IsOpen = true;
        focusRequested = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ReleaseFocus();
    }

    /// <summary>
    /// 放掉輸入框的鍵盤焦點但保持開著。點回畫布時要呼叫：否則 Delete、Ctrl+C 這些畫布快捷鍵
    /// 都會被還握著焦點的輸入框吃掉。
    /// </summary>
    public void ReleaseFocus()
    {
        focusRequested = false;
        if (GUI.GetNameOfFocusedControl() == Control) GUI.FocusControl(null);
    }

    /// <summary>
    /// 在視窗座標畫搜尋列。Enter／↓＝下一筆、Shift+Enter／↑＝上一筆、Esc＝關閉。
    /// 列範圍內的滑鼠事件一律吃掉，不讓它穿到底下的畫布。
    /// </summary>
    public void Draw(Rect rect, in HGNodeSearchBarView view, Action next, Action previous)
    {
        if (!IsOpen) return;
        var e = Event.current;

        // 鍵盤事件要在畫欄位**之前**判斷：TextField 會把 Return 吃掉，畫完再問就永遠問不到。
        if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == Control)
        {
            bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if ((enter && !e.shift) || e.keyCode == KeyCode.DownArrow) { next?.Invoke(); e.Use(); }
            else if ((enter && e.shift) || e.keyCode == KeyCode.UpArrow) { previous?.Invoke(); e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
        }

        HGStyles.RoundedFill(rect, HGStyles.OverlayPanel, 4f);
        HGStyles.RoundedFrame(rect, HGStyles.NodeBorder, 4f);

        float y = rect.y + 4f;
        float h = rect.height - 8f;
        float right = rect.xMax - 4f;
        var close = new Rect(right - ButtonWidth, y, ButtonWidth, h);
        var down = new Rect(close.x - ButtonWidth - 2f, y, ButtonWidth, h);
        var up = new Rect(down.x - ButtonWidth, y, ButtonWidth, h);
        var count = new Rect(up.x - CountWidth - 4f, y, CountWidth, h);
        var field = new Rect(rect.x + 4f, y + 1f, count.x - rect.x - 8f, h - 2f);

        GUI.SetNextControlName(Control);
        Query = EditorGUI.TextField(field, Query) ?? "";
        if (focusRequested)
        {
            if (GUI.GetNameOfFocusedControl() != Control) EditorGUI.FocusTextInControl(Control);
            else focusRequested = false;
        }
        if (Query.Length == 0 && e.type == EventType.Repaint)
            GUI.Label(new Rect(field.x + 4f, field.y, field.width - 4f, field.height), "節點名稱、型別、Token…", HGStyles.NodeDesc);

        string countText = Query.Trim().Length == 0 ? ""
            : view.MatchCount == 0 ? "沒有符合"
            : $"{(view.Current < 0 ? "–" : (view.Current + 1).ToString())} / {view.MatchCount}";
        GUI.Label(count, countText, HGStyles.NodeDesc);

        using (new EditorGUI.DisabledScope(view.MatchCount == 0))
        {
            if (GUI.Button(up, new GUIContent("↑", "上一筆（Shift+Enter）"), EditorStyles.miniButtonLeft)) previous?.Invoke();
            if (GUI.Button(down, new GUIContent("↓", "下一筆（Enter）"), EditorStyles.miniButtonRight)) next?.Invoke();
        }
        if (GUI.Button(close, new GUIContent("✕", "關閉搜尋（Esc）"), EditorStyles.miniButton))
        {
            Close();
            return;
        }

        if ((e.isMouse || e.type == EventType.ScrollWheel) && rect.Contains(e.mousePosition)) e.Use();
    }
}

}
