namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次目錄庫需要的全部資料。面板不認識 model 或 Owner，只認這份快照。</summary>
public struct HGCatalogLibraryView
{
    /// <summary>Owner 上的全部目錄。Owner 沒實作 <see cref="ICatalogOwner"/> 時為 null。</summary>
    public IReadOnlyList<IGraphCatalog> Catalogs;
}

/// <summary>目錄庫要用到的命令。超過兩條就收成結構，不排成一長串位置參數。</summary>
// cmd 傳值不傳 in：改名與移除都在 lambda 裡呼叫，C# 不允許 lambda 捕捉 in 參數（CS1628）。
public struct HGCatalogLibraryCommands
{
    /// <summary>建一個新目錄，回傳它的 Id；建不出來回 null。</summary>
    public Func<string> Create;

    /// <summary>改名。失敗回 false，錯誤訊息由呼叫端顯示。</summary>
    public Func<string, string, bool> Rename;

    /// <summary>刪掉整個目錄。</summary>
    public Action<string> Remove;

    /// <summary>把 Project 拖進來的資產加進這個目錄。</summary>
    public Action<string, IReadOnlyList<UnityEngine.Object>> Add;

    /// <summary>從目錄移除單一資產。</summary>
    public Action<string, UnityEngine.Object> RemoveItem;
}

/// <summary>
/// 左欄目錄庫：手動蒐集的資產分組。先建目錄，再從 Project 把資產拖進某一列。
/// </summary>
// 拖放區面板。展開狀態、搜尋字與捲動是自己的視圖狀態；就地改名向框架借 HGInlineRename，
// 理由與資產庫相同：同一個目錄可能同時出現在別的區，各自記就會兩格一起進編輯。
//
// **內容住在 Owner，不進圖的工作副本**：這裡的每一個命令都是立即寫檔，沒有「取消就還原」。
// 這與共用資產庫一致（轉存資產也是立刻寫進 Project），與 Token 庫相反。
public sealed class HGCatalogLibraryPanel
{
    private const float RowHeight = 24f;
    private const float ItemHeight = 20f;
    private const float Corner = 3f;

    /// <summary>展開時整塊（群組列＋項目）的外框圓角與上下內距。</summary>
    private const float BlockCorner = 4f;
    private const float BlockPad = 6f;

    /// <summary>項目的縮排，以及把它們串回群組列的那條縱線的位置。</summary>
    private const float ItemIndent = 18f;
    private const float SpineX = 11f;

    private const string PrefExpanded = "HaruGraph.CatalogLib.Expanded";

    private string search = "";
    private Vector2 scroll;

    /// <summary>展開中的目錄 Id。一次只展開一個：左欄很窄，同時攤開兩個等於誰都看不完。</summary>
    private string expanded;

    /// <summary>這一輪從 Project 來的拖曳還在進行中。</summary>
    // 必須自己記一個旗標，不能每次問 DragAndDrop.objectReferences：
    // 那份清單只在拖曳事件裡有意義，Repaint 時問到的是上一次拖曳留下的殘值——
    // 結果是落點高亮只在 DragUpdated 那一幀出現（閃爍），拖完之後按鈕還一直顯示「放入」。
    private bool projectDrag;

    private bool prefsLoaded;

    /// <summary>換編輯對象時把面板自己的視圖狀態歸零。展開狀態留著，它跨對象仍然有意義。</summary>
    public void Reset()
    {
        search = "";
        scroll = Vector2.zero;
    }

    public void Draw(Rect r, float top, in HGCatalogLibraryView view,
        HGInlineRename inlineName, HGLibraryDrag drag, HGCatalogLibraryCommands cmd)
    {
        LoadPrefs();
        TrackProjectDrag();

        DrawCreateButton(new Rect(r.x + 4f, top, r.width - 8f, 20f), cmd);

        var searchRect = new Rect(r.x + 4f, top + 22f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋目錄"));
        search = EditorGUI.TextField(
            new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), search);

        float listTop = top + 46f;
        var listRect = new Rect(r.x + 2f, listTop, r.width - 4f, Mathf.Max(0f, r.yMax - listTop - 2f));

        if (view.Catalogs == null)
        {
            GUI.Label(new Rect(listRect.x + 6f, listRect.y + 4f, listRect.width - 12f, 32f),
                "這個編輯對象沒有提供目錄清單。", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        var shown = new List<IGraphCatalog>();
        foreach (var catalog in view.Catalogs)
        {
            if (catalog == null) continue;
            if (!Matches(catalog)) continue;
            shown.Add(catalog);
        }

        float contentHeight = 4f;
        foreach (var catalog in shown) contentHeight += BlockHeight(catalog) + 2f;

        var content = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
        scroll = GUI.BeginScrollView(listRect, scroll, content);

        float y = 2f;
        for (int i = 0; i < shown.Count; i++)
        {
            var catalog = shown[i];
            bool open = IsExpanded(catalog);
            float height = BlockHeight(catalog);

            // 展開時先鋪一塊容器再畫內容：群組列與它的項目要看起來是同一塊，
            // 不是上下兩段各自獨立的清單。收合時沒有內容可圈，就只是單純一列。
            if (open)
            {
                var block = new Rect(2f, y, content.width - 4f, height);
                HGStyles.RoundedFill(block, HGStyles.GroupTint(HGStyles.HeaderCatalog), BlockCorner);
                HGStyles.RoundedFrame(block, HGStyles.HeaderCatalog, BlockCorner);
            }

            float inset = open ? 2f : 0f;
            var row = new Rect(2f + inset, y + inset, content.width - 4f - inset * 2f, RowHeight - 3f);
            DrawCatalogRow(row, catalog, open, i % 2 == 1, inlineName, drag, cmd);

            if (!open)
            {
                y += height + 2f;
                continue;
            }

            float itemTop = y + inset + RowHeight;
            int count = catalog.Items.Count;

            // 左側縱線把項目串回群組列：窄欄裡光靠縮排看不出層級，有一條線就讀得出從屬。
            if (count > 0)
                HGStyles.Fill(new Rect(SpineX, itemTop - 1f, 1f, count * ItemHeight - 4f),
                    HGStyles.HeaderCatalog);

            for (int k = 0; k < count; k++)
            {
                var item = new Rect(ItemIndent, itemTop + k * ItemHeight,
                    content.width - ItemIndent - 6f, ItemHeight - 2f);
                DrawItemRow(item, catalog, catalog.Items[k], cmd);
            }

            // 空目錄要說出下一步，不然展開後只看到一個空框，看起來像壞掉。
            if (count == 0)
                GUI.Label(new Rect(ItemIndent, itemTop, content.width - ItemIndent - 6f, ItemHeight - 2f),
                    "還是空的——把 Project 的資產拖到上面那一列", HGStyles.Tiny);

            y += height + 2f;
        }

        GUI.EndScrollView();
    }

    /// <summary>更新「現在有沒有一輪 Project 拖曳在進行」。每次 Draw 最前面跑一次。</summary>
    // **清除只認 DragExited**，那是拖曳結束（放開、取消、離開視窗）之後 Unity 一定會補的事件。
    // 不在 DragPerform 清：落點的判斷跑在本函式後面，同一幀就清掉的話放開時什麼都不會發生。
    private void TrackProjectDrag()
    {
        switch (Event.current.type)
        {
            case EventType.DragUpdated:
                projectDrag = Collect().Count > 0;
                break;
            case EventType.DragExited:
                projectDrag = false;
                break;
        }
    }

    /// <summary>
    /// 「＋ 新增目錄」：單擊建一個空的，或把 Project 的資產**直接拖到這顆鈕上**＝建一個新目錄並放進去。
    /// </summary>
    // 拖到按鈕上建立，是為了少掉「先建空目錄、再拖一次」這個多餘的來回。
    // 與Token庫的「＋ 新增」同一種手勢：那顆鈕也同時是落點（拖Token到它上面＝複製）。
    private void DrawCreateButton(Rect rect, HGCatalogLibraryCommands cmd)
    {
        var e = Event.current;
        bool hover = rect.Contains(e.mousePosition);

        // 拖曳中鋪一層綠底當落點：與Token庫的新增鈕同一個顏色語意（安全累加）。
        if (projectDrag && hover) HGStyles.Fill(rect, new Color(0.24f, 0.50f, 0.34f, 0.75f));

        bool clicked = GUI.Button(rect, new GUIContent(
            projectDrag ? "新增目錄並放入" : "＋ 新增目錄",
            "建一個空目錄；把 Project 的資產拖到這裡＝直接建一個新目錄裝它們"));

        if (projectDrag && hover)
        {
            var incoming = Collect();
            DragAndDrop.visualMode = incoming.Count > 0
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (incoming.Count > 0) CreateWith(cmd, incoming);
                e.Use();
            }
            else if (e.type == EventType.DragUpdated)
            {
                e.Use();
            }
            return;
        }

        // 拖曳放開不會讓 GUI.Button 回 true（它沒在自己身上收到 MouseDown），所以上面那條自己判。
        if (clicked) CreateWith(cmd, null);
    }

    /// <summary>建一個新目錄，順手把這批資產放進去，然後展開它。</summary>
    // 建完就展開：接下來一定是看裡面有什麼，不該還要多點一下。
    private void CreateWith(HGCatalogLibraryCommands cmd, IReadOnlyList<UnityEngine.Object> assets)
    {
        string id = cmd.Create?.Invoke();
        if (string.IsNullOrEmpty(id)) return;
        if (assets != null && assets.Count > 0) cmd.Add?.Invoke(id, assets);
        SetExpanded(id);
    }

    /// <summary>
    /// 一列目錄：折疊箭頭、名稱（可就地改名）、項目數、刪除鈕。
    /// 這一列同時是兩個方向的拖曳端點——收 Project 拖進來的資產，也能被拖到畫布上變成節點。
    /// </summary>
    private void DrawCatalogRow(Rect row, IGraphCatalog catalog, bool open, bool altRow,
        HGInlineRename inlineName, HGLibraryDrag drag, HGCatalogLibraryCommands cmd)
    {
        var e = Event.current;
        bool hoverDrop = projectDrag && row.Contains(e.mousePosition);

        HGStyles.CellBackground(row, HGStyles.HeaderCatalog, HGStyles.HeaderCatalog, altRow, hoverDrop, Corner);

        var foldRect = new Rect(row.x + 4f, row.y + 3f, 14f, 14f);
        if (GUI.Button(foldRect, open ? "▾" : "▸", HGStyles.Chip)) SetExpanded(open ? null : catalog.Id);

        var nameRect = new Rect(row.x + 20f, row.y + 2f, row.width - 76f, 16f);
        string id = catalog.Id;
        var command = cmd;
        inlineName.Draw(nameRect, catalog, HGInlineRename.SiteCatalogLib,
            catalog.Name, catalog.Name, HGStyles.RowLabel, "雙擊可改名；引用它的節點存的是 Id，改名不會斷",
            name => command.Rename != null && command.Rename(id, name));

        var countRect = new Rect(row.xMax - 54f, row.y + 3f, 30f, 15f);
        HGStyles.RoundedFill(countRect, HGStyles.HeaderCatalog, Corner);
        GUI.Label(countRect, catalog.Items.Count.ToString(), HGStyles.NodeChip);

        var delRect = new Rect(row.xMax - 20f, row.y + 3f, 16f, 15f);
        if (GUI.Button(delRect, new GUIContent("×", "刪掉整個目錄"), HGStyles.Chip))
            cmd.Remove?.Invoke(catalog.Id);

        HandleProjectDrop(row, catalog, cmd);

        // 往畫布拖：面板只宣告「這一格開始拖了」，是不是來源、算點擊還是落下都在服務裡。
        // 折疊鈕、名稱格與刪除鈕已經先吃掉自己的點擊，所以這裡收到的一定是列身。
        if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
        {
            drag.BeginCatalog(catalog);
            e.Use();
        }
        if (e.type == EventType.MouseDrag && drag.IsSource(catalog)) drag.PromoteOnDrag();
        if (e.type == EventType.MouseUp && drag.IsPendingClick(catalog)) drag.ClearCatalog();
    }

    /// <summary>目錄裡的一筆資產：圖示、名字、移除鈕。點名字 ping 到 Project。</summary>
    private static void DrawItemRow(Rect row, IGraphCatalog catalog, UnityEngine.Object item,
        HGCatalogLibraryCommands cmd)
    {
        if (item == null)
        {
            GUI.Label(row, "（資產已遺失）", HGStyles.RowLabelError);
            return;
        }

        var icon = EditorGUIUtility.ObjectContent(item, item.GetType()).image;
        if (icon != null) GUI.DrawTexture(new Rect(row.x, row.y + 2f, 14f, 14f), icon);

        var nameRect = new Rect(row.x + 18f, row.y, row.width - 40f, row.height);
        if (GUI.Button(nameRect, HGStyles.Elide(item.name, HGStyles.RowLabel, nameRect.width), HGStyles.RowLabel))
            EditorGUIUtility.PingObject(item);

        var delRect = new Rect(row.xMax - 18f, row.y + 1f, 16f, 15f);
        if (GUI.Button(delRect, new GUIContent("－", "從這個目錄移除（不動 Project 裡的檔案）"), HGStyles.Chip))
            cmd.RemoveItem?.Invoke(catalog.Id, item);
    }

    /// <summary>從 Project 拖資產進這一列。只收專案裡的資產，場景物件不收。</summary>
    private static void HandleProjectDrop(Rect row, IGraphCatalog catalog, HGCatalogLibraryCommands cmd)
    {
        var e = Event.current;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
        if (!row.Contains(e.mousePosition)) return;

        var accepted = Collect();
        DragAndDrop.visualMode = accepted.Count > 0
            ? DragAndDropVisualMode.Copy
            : DragAndDropVisualMode.Rejected;

        if (e.type == EventType.DragUpdated)
        {
            e.Use();
            return;
        }

        DragAndDrop.AcceptDrag();
        if (accepted.Count > 0) cmd.Add?.Invoke(catalog.Id, accepted);
        e.Use();
    }

    /// <summary>這次拖曳裡真的能收的東西。Hierarchy 上的物件沒有資產路徑，一律排除。</summary>
    private static List<UnityEngine.Object> Collect()
    {
        var result = new List<UnityEngine.Object>();
        var refs = DragAndDrop.objectReferences;
        if (refs == null) return result;
        foreach (var obj in refs)
        {
            if (obj == null) continue;
            if (!AssetDatabase.Contains(obj)) continue;
            result.Add(obj);
        }
        return result;
    }

    /// <summary>一個目錄在清單上佔的高度。收合＝一列；展開＝群組列＋項目＋上下內距，空目錄留一列放提示。</summary>
    private float BlockHeight(IGraphCatalog catalog)
    {
        if (!IsExpanded(catalog)) return RowHeight - 3f;
        int rows = Mathf.Max(1, catalog.Items?.Count ?? 0);
        return RowHeight + rows * ItemHeight + BlockPad;
    }

    private bool Matches(IGraphCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return !string.IsNullOrEmpty(catalog.Name)
            && catalog.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsExpanded(IGraphCatalog catalog) => expanded == catalog.Id;

    private void SetExpanded(string id)
    {
        expanded = id;
        EditorPrefs.SetString(PrefExpanded, id ?? "");
    }

    private void LoadPrefs()
    {
        if (prefsLoaded) return;
        prefsLoaded = true;
        string stored = EditorPrefs.GetString(PrefExpanded, "");
        expanded = string.IsNullOrEmpty(stored) ? null : stored;
    }
}

}
