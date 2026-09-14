namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>焦點資訊列的四種樣子。版面隨型態變，所以型態要明講，不從其他欄位反推。</summary>
public enum HGFocusHeaderKind
{
    /// <summary>純標題 + 說明（時機畫布、沒有編輯對象）。</summary>
    Plain,

    /// <summary>共用資產本體：橫幅 + 可改名的檔名。</summary>
    Asset,

    /// <summary>共用資產底下的某個變數：橫幅 + 可改名的變數名。</summary>
    AssetVariable,

    /// <summary>變數畫布：可改名的變數名 + 說明。</summary>
    Variable,

    /// <summary>動作畫布：可改名的動作標籤 + 說明。</summary>
    Action,
}

/// <summary>畫一次焦點資訊列需要的全部資料。焦點判斷在視窗，這裡只剩版面。</summary>
public struct HGFocusHeaderView
{
    public HGFocusHeaderKind Kind;

    /// <summary>可就地改名的目標；null＝這一型沒有改名入口，標題畫成純文字。</summary>
    public object NameTarget;

    public string NameDisplay;
    public string NameTooltip;

    /// <summary>改名提交。回傳 false＝名稱不合法，維持編輯狀態。</summary>
    public Func<string, bool> NameSubmit;

    /// <summary>不可改名時顯示的標題。</summary>
    public string Title;

    /// <summary>標題底下那行小字；null 或空＝不畫。</summary>
    public string Description;
}

/// <summary>
/// 畫布上方框：目前焦點畫布的身分與資訊。使用者在這裡知道「我現在在編誰」，並且對那個對象本身改名。
/// </summary>
// 回報／命令區：可以點、可以改名（那是真的改資料），但不接受拖放——這才是它與左右框的分界。
// 沒有自己的視圖狀態，所以是 static；改名狀態向框架的 HGInlineRename 借。
public static class HGFocusHeaderPanel
{
    private const string AssetBanner = "　共用資產：修改會影響所有引用它的對象。存檔是獨立的一次交易。";

    public static void Draw(Rect r, in HGFocusHeaderView view, HGInlineRename inlineName)
    {
        HGStyles.Fill(r, HGStyles.PanelSection);
        HGStyles.Frame(r, HGStyles.NodeBorder);

        switch (view.Kind)
        {
            case HGFocusHeaderKind.AssetVariable:
                DrawBanner(r);
                // 移除／返回都在左欄變數庫：資產焦點下那一區列的就是這個資產的變數，
                // 點同一格退出、拖到「－ 移除變數」刪除，標頭不重複第二個入口（與 Variable 焦點一致）。
                if (view.NameTarget != null)
                    DrawName(new Rect(r.x, r.y + 20f, r.width - 12f, 22f), view, inlineName);
                else
                    GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 18f), view.Title, EditorStyles.boldLabel);
                return;

            case HGFocusHeaderKind.Asset:
                DrawBanner(r);
                if (view.NameTarget != null)
                    // 資產本體的標題就是檔名，和變數標題同一套手勢：雙擊改名、Enter 提交。
                    inlineName.Draw(new Rect(r.x + 6f, r.y + 21f, r.width - 12f, 20f), view.NameTarget,
                        HGInlineRename.SiteFocus, view.NameDisplay, view.NameDisplay,
                        HGStyles.FocusTitle, view.NameTooltip, view.NameSubmit);
                else
                    GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 18f), view.Title, EditorStyles.boldLabel);
                return;

            case HGFocusHeaderKind.Variable:
                DrawName(new Rect(r.x, r.y, r.width - 12f, 22f), view, inlineName);
                DrawDescription(r, r.y + 22f, view.Description);
                return;

            case HGFocusHeaderKind.Action:
                DrawName(r, view, inlineName);
                DrawDescription(r, r.y + 24f, view.Description);
                return;

            default:
                GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 18f), view.Title, EditorStyles.boldLabel);
                DrawDescription(r, r.y + 24f, view.Description);
                return;
        }
    }

    private static void DrawBanner(Rect r)
    {
        var banner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 18f);
        HGStyles.Fill(banner, new Color(0.45f, 0.32f, 0.18f));
        GUI.Label(banner, AssetBanner, HGStyles.RowLabel);
    }

    /// <summary>焦點標題：雙擊就地改名，Enter 提交、Esc 取消。和節點上的動作標籤同一套手勢。</summary>
    private static void DrawName(Rect header, in HGFocusHeaderView view, HGInlineRename inlineName)
    {
        if (view.NameTarget == null) return;
        var nameRect = new Rect(header.x + 6f, header.y + 2f, header.width - 12f, 22f);
        inlineName.Draw(nameRect, view.NameTarget, HGInlineRename.SiteFocus,
            view.NameDisplay, view.NameDisplay, HGStyles.FocusTitle, view.NameTooltip, view.NameSubmit);
    }

    private static void DrawDescription(Rect r, float y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        GUI.Label(new Rect(r.x + 6f, y, r.width - 12f, 16f), text, HGStyles.Tiny);
    }
}

}
