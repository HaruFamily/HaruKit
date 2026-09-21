namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次資產庫需要的全部資料。面板不認識 model 或 focus，只認這份快照。</summary>
public struct HGAssetLibraryView
{
    /// <summary>資料夾裡掃到的共用資產。</summary>
    public List<HGAssetEntry> Entries;

    /// <summary>這張圖的欄位可以接哪些資產型別。決定一筆資產列不列得出來。</summary>
    public List<(Type acceptedAssetType, Type slotType)> SlotTypes;

    /// <summary>目前焦點停在哪個資產；null＝不在資產焦點。型別跟著 `HGFocus.AssetObject` 走。</summary>
    public UnityEngine.Object FocusedAsset;
}

/// <summary>
/// 左欄資產庫：共用公式／動作資產的清單。點一筆進去編它，拖到畫布上＝建一顆引用節點。
/// </summary>
// 拖放區面板。搜尋字與捲動是自己的，但「正在拖誰」與「誰正在被改名」都向框架借——
// 同一個面板可以落在左框或右框，這兩件事記在面板裡就會綁死在某一個框。
public sealed class HGAssetLibraryPanel
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

    /// <summary>繪製資產列；選取、改名與重排由命令接回視窗。</summary>
    public void Draw(Rect r, float top, in HGAssetLibraryView view,
        HGInlineRename inlineName, HGLibraryDrag drag, HGAssetLibraryCommands cmd)
    {
        // 重掃縮成搜尋列旁的圖示鈕：上下分區後高度是兩區共用的，整條寬按鈕不值那一列。
        var searchRect = new Rect(r.x + 4f, top, r.width - 30f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋資產"));
        search = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y,
            searchRect.width - 20f, searchRect.height), search);
        if (GUI.Button(new Rect(r.xMax - 24f, top, 20f, 20f),
            EditorGUIUtility.IconContent("Refresh", "重新掃描資產"))) HGAssetIndex.Refresh();

        var shown = new List<(HGAssetEntry entry, Type slotType)>();
        foreach (var entry in view.Entries)
        {
            Type slotType = HGReflect.SlotTypeForAsset(entry.Asset, view.SlotTypes);
            if (slotType == null) continue;
            if (!Matches(entry)) continue;
            shown.Add((entry, slotType));
        }

        var listRect = new Rect(r.x + 2f, top + 24f, r.width - 4f, Mathf.Max(0f, r.yMax - top - 26f));
        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * CellHeight + 4f);
        scroll = GUI.BeginScrollView(listRect, scroll, content);
        Action pendingMove = null;
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i].entry;
            var asset = entry.Asset;
            var row = new Rect(2f, i * CellHeight + 2f, content.width - 4f, CellHeight - 3f);
            bool isFocus = view.FocusedAsset == asset;
            // 資產＝藍→內容型別，和畫布上的資產節點同一條漸層。
            Color payload = entry.IsAction ? HGStyles.HeaderAction : HGStyles.HeaderFormula;
            HGStyles.CellBackground(row, HGStyles.HeaderAsset, payload, i % 2 == 1, isFocus);

            int direction = HGLibraryOrder.Draw(new Rect(row.x + 3f, row.y + 5f, HGLibraryOrder.Width, 18f),
                cmd.Move != null && i > 0, cmd.Move != null && i + 1 < shown.Count);
            if (direction != 0)
            {
                var target = shown[i + direction].entry.Asset;
                pendingMove = () => cmd.Move(asset, target);
            }

            var nameRect = new Rect(row.x + 8f + HGLibraryOrder.Width, row.y + 2f,
                Mathf.Max(0f, row.width - 64f - HGLibraryOrder.Width), 18f);
            bool renaming = inlineName.Draw(nameRect, asset, HGInlineRename.SiteAssetLib,
                asset.name, asset.name, HGStyles.RowLabel, "雙擊可改名（改的是 .asset 檔名）",
                name => cmd.Rename(asset, name));
            string kind = entry.IsAction ? "ACT" : HGReflect.ResultTypeName(entry.ResultType);
            var typeRect = new Rect(row.xMax - 54f, row.y + 6f, 46f, 15f);
            HGStyles.RoundedFill(typeRect, payload, CellCorner);
            GUI.Label(typeRect, HGStyles.Elide(kind, HGStyles.NodeChip, typeRect.width), HGStyles.NodeChip);

            if (renaming) continue;               // 正在改名的這一格不吃點擊

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                drag.BeginAsset(asset);
                e.Use();
            }
            if (e.type == EventType.MouseDrag && drag.IsSource(asset)) drag.PromoteOnDrag();
            // 名字那一格不切焦點（同Token庫）：雙擊改名不該順手進出這個資產的畫布。
            if (e.type == EventType.MouseUp && drag.IsPendingClick(asset)
                && row.Contains(e.mousePosition) && !nameRect.Contains(e.mousePosition))
            {
                drag.ClearAsset();
                cmd.Activate(asset, shown[i].slotType);
                e.Use();
            }
        }
        GUI.EndScrollView();
        pendingMove?.Invoke();
    }

    /// <summary>相容僅提供改名與選取命令的使用端。</summary>
    public void Draw(Rect r, float top, in HGAssetLibraryView view,
        HGInlineRename inlineName, HGLibraryDrag drag,
        Func<UnityEngine.Object, string, bool> rename, Action<ScriptableObject, Type> activate)
        => Draw(r, top, view, inlineName, drag, new HGAssetLibraryCommands { Rename = rename, Activate = activate });

    private bool Matches(HGAssetEntry entry)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        if (entry.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (entry.TypeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return entry.ResultType != null
            && HGReflect.ResultTypeName(entry.ResultType).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>共用資產庫的編輯與導覽命令。</summary>
public struct HGAssetLibraryCommands
{
    public Func<UnityEngine.Object, string, bool> Rename;
    public Action<ScriptableObject, Type> Activate;
    public Action<ScriptableObject, ScriptableObject> Move;
}

}
