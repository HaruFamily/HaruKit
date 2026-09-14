namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次Token庫需要的全部資料。面板不認識 model 或 focus，只認這份快照。</summary>
public struct HGTokenLibraryView
{
    /// <summary>目前這張畫布的對外端點。資產焦點下是那個資產的參數介面。</summary>
    public List<HGToken> Tokens;

    /// <summary>正在編輯哪一個Token；null＝不在Token焦點。</summary>
    public GraphToken FocusedToken;
}

/// <summary>
/// Token庫對外的命令。面板只回報使用者做了什麼，怎麼改圖由視窗決定。
/// </summary>
public struct HGTokenLibraryCommands
{
    /// <summary>改名。回傳 false＝名稱不合法，就地改名會維持編輯狀態。</summary>
    public Func<GraphToken, string, bool> Rename;

    /// <summary>點一筆：進去編它，或再點目前這筆＝退出。進出判斷在視窗。</summary>
    public Action<GraphToken> Activate;

    /// <summary>拖到「＋」上放開：複製這一個（內容一起複製）。</summary>
    public Action<GraphToken> Duplicate;

    /// <summary>拖到「－」上放開，或直接按「－」刪掉目前編輯中的那一個。</summary>
    public Action<GraphToken> Remove;

    /// <summary>按「＋」：開型別選單。</summary>
    public Action Create;

    /// <summary>這個Token有沒有驗證問題；reason 為 null＝沒有。</summary>
    public Func<HGToken, (string reason, bool isError)> IssueOf;
}

/// <summary>
/// 左欄Token庫：這張圖有哪些對外端點。新增、改名、刪除都在這裡，點一筆進入它自己的畫布。
/// </summary>
// 拖放區面板。搜尋字與捲動自己持有；「正在拖誰」與「誰正在被改名」向框架借。
// 兩顆按鈕同時是拖曳落點（拖Token上去＝複製／刪除），所以它們也得走框架的拖曳狀態。
public sealed class HGTokenLibraryPanel
{
    private const float CellHeight = 30f;
    private const float CellCorner = 3f;

    private string search = "";
    private Vector2 scroll;

    /// <summary>換編輯對象時把面板自己的視圖狀態歸零。</summary>
    public void Reset()
    {
        search = "";
        scroll = Vector2.zero;
    }

    // cmd 不用 in：底下的改名要在 lambda 裡叫它，而 in／ref 參數不能被 lambda 捕捉（CS1628）。
    public void Draw(Rect r, float top, in HGTokenLibraryView view, HGTokenLibraryCommands cmd,
        HGInlineRename inlineName, HGLibraryDrag drag)
    {
        DrawCreateButton(new Rect(r.x + 4f, top, r.width - 8f, 20f), cmd, drag);
        DrawRemoveButton(new Rect(r.x + 4f, top + 22f, r.width - 8f, 20f), view, cmd, drag);

        var searchRect = new Rect(r.x + 4f, top + 46f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋 Token"));
        search = EditorGUI.TextField(
            new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), search);

        // r 已經是這一區的範圍，yMax 就是分隔線；高度夾 0 以上，視窗擠到極限時不會出現負高度的 ScrollView。
        var listRect = new Rect(r.x + 2f, top + 70f, r.width - 4f, Mathf.Max(0f, r.yMax - top - 72f));
        var shown = new List<HGToken>();
        foreach (var t in view.Tokens)
            if (string.IsNullOrWhiteSpace(search)
                || t.Key?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || t.TypeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                shown.Add(t);

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * CellHeight + 4f);
        scroll = GUI.BeginScrollView(listRect, scroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var token = shown[i];
            var endpoint = token.Token;
            var row = new Rect(2f, i * CellHeight + 2f, content.width - 4f, CellHeight - 3f);
            bool isFocus = ReferenceEquals(view.FocusedToken, endpoint);
            // 深綠→琥珀，和畫布上的Token節點同一條漸層。
            HGStyles.CellBackground(row, HGStyles.HeaderToken, HGStyles.HeaderFormula, i % 2 == 1, isFocus);

            var nameRect = new Rect(row.x + 8f, row.y + 2f, row.width - 70f, 18f);
            bool renaming = inlineName.Draw(nameRect, endpoint, HGInlineRename.SiteTokenLib,
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, token.Key ?? "",
                HGStyles.RowLabel, "雙擊可改名；外部（Inspector）用這個名字查它的值",
                name => cmd.Rename(endpoint, name));

            var typeRect = new Rect(row.xMax - 58f, row.y + 6f, 42f, 15f);
            HGStyles.RoundedFill(typeRect, HGStyles.HeaderFormula, CellCorner);
            GUI.Label(typeRect, HGStyles.Elide(token.TypeName, HGStyles.NodeChip, typeRect.width), HGStyles.NodeChip);

            var (reason, isError) = cmd.IssueOf(token);
            if (reason != null)
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                HGStyles.Fill(dot, isError ? HGStyles.Error : HGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            if (renaming) continue;               // 正在改名的這一格不吃點擊，否則同一下會又改名又切焦點

            var e = Event.current;
            // 右鍵不做事：改名雙擊、刪除是上面那顆「－ 移除Token」，選單只是多一層要記的東西。
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                drag.BeginToken(endpoint);
                e.Use();
            }
            if (e.type == EventType.MouseDrag && drag.IsSource(endpoint)) drag.PromoteOnDrag();
            // 名字那一格不切焦點：雙擊改名的第一下否則會先跳進（或跳出）這個Token的畫布。
            // MouseDown 仍照收，拖曳複製／移除要能從名字上起拖。
            if (e.type == EventType.MouseUp && drag.IsPendingClick(endpoint)
                && row.Contains(e.mousePosition) && !nameRect.Contains(e.mousePosition))
            {
                drag.ClearToken();
                cmd.Activate(endpoint);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>
    /// 「＋ 新增Token」：單擊開型別選單；把Token格**拖到這顆按鈕上放開＝複製那一個**（內容一起複製）。
    /// </summary>
    private static void DrawCreateButton(Rect rect, HGTokenLibraryCommands cmd, HGLibraryDrag drag)
    {
        var e = Event.current;
        bool dropping = drag.DroppingToken;
        bool hover = rect.Contains(e.mousePosition);

        if (dropping && hover) HGStyles.Fill(rect, new Color(0.24f, 0.50f, 0.34f, 0.75f));

        bool clicked = GUI.Button(rect, new GUIContent(
            dropping ? "複製 Token" : "＋ 新增 Token",
            "新增一個 Token；把左邊的 Token 拖到這裡＝複製它"));

        // 拖曳放開不會讓 GUI.Button 回 true（它沒在自己身上收到 MouseDown），所以自己判。
        if (dropping && hover && e.rawType == EventType.MouseUp)
        {
            cmd.Duplicate(drag.Token);
            drag.Clear();
            e.Use();
            return;
        }
        if (clicked && !dropping) cmd.Create();
    }

    /// <summary>
    /// 「－ 移除Token」：單擊刪掉**目前正在編輯**的那一個，或把Token格**拖到這顆按鈕上放開**刪掉被拖的那一個。
    /// 兩條路都要先表態（先點開它，或把它拖過來），所以不再問一次確認框——刪完用提示說明怎麼救回來。
    /// </summary>
    private static void DrawRemoveButton(Rect rect, in HGTokenLibraryView view,
        HGTokenLibraryCommands cmd, HGLibraryDrag drag)
    {
        var e = Event.current;
        bool dropping = drag.DroppingToken;
        bool hover = rect.Contains(e.mousePosition);

        // 拖曳中鋪一層紅底當落點：拖著Token在畫面上跑時，看得到「放這裡會刪掉」才敢放手。
        // 字只拿掉開頭的「－」，不改寫成一句話——按鈕上的字換來換去比底色還吵。
        if (dropping && hover) HGStyles.Fill(rect, new Color(0.62f, 0.24f, 0.26f, 0.75f));

        bool hasFocusToken = view.FocusedToken != null;
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && (dropping || hasFocusToken);
        bool clicked = GUI.Button(rect, new GUIContent(
            dropping ? "移除 Token" : "－ 移除 Token",
            hasFocusToken
                ? "移除目前編輯中的 Token；也可以把左邊的 Token 直接拖到這裡"
                : "先點一個 Token 進去，或把 Token 拖到這裡"));
        GUI.enabled = wasEnabled;

        if (dropping && hover && e.rawType == EventType.MouseUp)
        {
            cmd.Remove(drag.Token);
            drag.Clear();
            e.Use();
            return;
        }
        if (clicked && hasFocusToken) cmd.Remove(view.FocusedToken);
    }
}

}
