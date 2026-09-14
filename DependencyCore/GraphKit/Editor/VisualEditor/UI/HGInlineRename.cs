namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 就地改名：平常畫成一般標籤，雙擊變輸入框，Enter 提交、Esc 取消、點到別處也提交。
/// </summary>
// 框架級服務，不屬於任何一個面板：同一個端點／資產會同時出現在焦點列與左欄，
// 兩格共用同一份「誰正在被改名」的狀態，各自記就會兩格一起進編輯。面板向框架借用它。
public sealed class HGInlineRename
{
    /// <summary>焦點資訊列那一格。</summary>
    public const string SiteFocus = "focus";

    /// <summary>左欄變數庫的清單格。</summary>
    public const string SiteTokenLib = "tokenLib";

    /// <summary>左欄資產庫的清單格。</summary>
    public const string SiteAssetLib = "assetLib";

    /// <summary>節點參數列的動作標籤。</summary>
    public const string SiteRow = "row";

    private const string ControlPrefix = "agInlineName";

    private readonly Action repaint;

    private object target;
    private string site;
    private string draft = "";
    private Func<string, bool> submit;

    public HGInlineRename(Action repaint) => this.repaint = repaint;

    /// <summary>目前有沒有一格開著編輯。</summary>
    public bool IsEditing => target != null;

    /// <summary>
    /// site 是這一格的所在區塊，同一個 target 會同時出現在兩個區塊。
    /// display 是平常顯示的字（可能是自動名），editSeed 是進入編輯時填進去的字（實際存的名字）。
    /// submit 回傳 false＝名稱不合法，維持編輯狀態讓使用者改。回傳 true 代表這一格正在編輯，
    /// 呼叫端要跳過自己的點擊處理，否則同一下會又改名又切焦點。
    /// </summary>
    public bool Draw(Rect rect, object target, string site, string display, string editSeed,
        GUIStyle style, string tooltip, Func<string, bool> submit)
    {
        var e = Event.current;
        if (!ReferenceEquals(this.target, target) || this.site != site)
        {
            GUI.Label(rect, HGStyles.Elide(display, style, rect.width, tooltip), style);
            if (e.type != EventType.MouseDown || e.button != 0 || e.clickCount != 2) return false;
            if (!rect.Contains(e.mousePosition)) return false;

            this.target = target;
            this.site = site;
            draft = editSeed ?? "";
            this.submit = submit;
            GUI.FocusControl(null);
            e.Use();
            repaint?.Invoke();
            return true;
        }

        // 每幀重存：submit 是 closure，換一份資料就是換一個委派，留舊的會寫到上一輪的物件上。
        this.submit = submit;

        // 鍵盤事件要在畫欄位**之前**判斷：TextField 會把 Return 吃掉，畫完再問就永遠問不到。
        bool enter = e.type == EventType.KeyDown
            && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool escape = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

        // 控制項名稱帶 site：同一個 target 在焦點標題與左欄各有一格，同名的兩個控制項會互搶鍵盤焦點。
        string control = ControlPrefix + site;
        GUI.SetNextControlName(control);
        draft = EditorGUI.TextField(rect, draft);
        // 只在還沒拿到焦點時搶：每幀都搶的話，滑鼠點進去放游標或拉選取會被下一幀重設。
        if (GUI.GetNameOfFocusedControl() != control) EditorGUI.FocusTextInControl(control);

        // 點到別的地方＝提交：改名是小編輯，留著一個開著的輸入框比直接收掉更容易誤觸。
        bool clickedAway = e.type == EventType.MouseDown && !rect.Contains(e.mousePosition);
        if (!clickedAway && !enter && !escape) return true;

        if (escape) Cancel();
        else Commit();
        if (enter || escape) e.Use();             // 點走的那一下要留給底下的控制項處理
        repaint?.Invoke();
        return true;
    }

    /// <summary>
    /// 提交目前開著的就地改名（沒有就什麼都不做）。名稱不合法時保持編輯狀態。
    /// **畫布也要呼叫它**：`HandleCanvasInput` 會把點擊 `e.Use()` 掉，畫在它後面的左欄與焦點資訊列
    /// 因此看不到那一下 MouseDown，自己收不了尾。
    /// </summary>
    public void Commit()
    {
        if (target == null) return;
        if (submit != null && !submit(draft.Trim())) return;
        Cancel();
    }

    public void Cancel()
    {
        target = null;
        site = null;
        draft = "";
        submit = null;
        GUI.FocusControl(null);
    }
}

}
