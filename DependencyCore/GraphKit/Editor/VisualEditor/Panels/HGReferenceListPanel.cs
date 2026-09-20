namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>引用清單對外的命令。</summary>
public struct HGReferenceListCommands
{
    /// <summary>把所有引用者重跑一次 Core 驗證。</summary>
    public Action<ScriptableObject> VerifyAll;

    /// <summary>切換過去編某個引用者。回傳 false＝使用者取消，清單繼續畫。</summary>
    public Func<ScriptableObject, bool> Open;

    /// <summary>用視窗的通知列回報結果。</summary>
    public Action<string> Notify;
}

/// <summary>
/// 左欄第三區（只在資產焦點出現）：誰引用了目前這顆資產。
/// </summary>
// 左欄只有 160px 起跳，所以一列只放名稱與驗證狀態，整列可點＝切換過去編它；
// 兩顆維護動作縮成標題列右側的小按鈕，不吃清單高度。
public sealed class HGReferenceListPanel
{
    private const float RowHeight = 22f;

    private Vector2 scroll;

    public void Draw(Rect r, ScriptableObject asset, in HGReferenceListCommands cmd)
    {
        var users = HGReferenceIndex.Users(asset);
        int count = users.Count;

        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 74f, 18f),
            new GUIContent($"引用此資產 {count}", "專案裡已存檔、且引用這顆資產的對象；點一列切換過去編它"),
            HGStyles.PanelHeader);

        // 重掃與重驗都是低頻維護動作，縮在標題列右側。
        if (GUI.Button(new Rect(r.xMax - 68f, r.y + 2f, 40f, 18f),
            new GUIContent("重驗", "把所有引用者重跑一次 Core 驗證；只有結果翻轉的才寫檔")))
            cmd.VerifyAll(asset);
        if (GUI.Button(new Rect(r.xMax - 24f, r.y + 2f, 20f, 18f),
            EditorGUIUtility.IconContent("Refresh", "重新掃描專案的引用")))
        {
            HGReferenceIndex.Refresh();
            cmd.Notify($"找到 {HGReferenceIndex.Users(asset).Count} 個引用");
        }

        var listRect = new Rect(r.x + 2f, r.y + 22f, r.width - 4f, Mathf.Max(0f, r.yMax - r.y - 24f));
        HGStyles.Fill(listRect, HGStyles.PanelList);

        if (count == 0)
        {
            GUI.Label(new Rect(listRect.x + 6f, listRect.y + 4f, listRect.width - 12f, 30f),
                "專案裡沒有已存檔的對象引用它", HGStyles.Tiny);
            return;
        }

        var content = new Rect(0f, 0f, listRect.width - 16f, count * RowHeight + 4f);
        scroll = GUI.BeginScrollView(listRect, scroll, content);
        for (int i = 0; i < count; i++)
        {
            var so = users[i];
            var row = new Rect(0f, i * RowHeight, content.width, RowHeight - 1f);
            if (i % 2 == 1) HGStyles.Fill(row, HGStyles.RowAlt);

            if (so == null)
            {
                GUI.Label(new Rect(row.x + 4f, row.y + 2f, row.width - 8f, 17f),
                    "（已遺失的對象）", HGStyles.RowLabelError);
                continue;
            }

            bool validated = HGOwnerValidation.IsValidated(so);
            GUI.Label(new Rect(row.x + 4f, row.y + 2f, row.width - 26f, 17f),
                HGStyles.Elide(so.name, HGStyles.RowLabel, row.width - 26f), HGStyles.RowLabel);
            GUI.Label(new Rect(row.xMax - 20f, row.y + 2f, 16f, 17f),
                new GUIContent(validated ? "✓" : "✗",
                    validated ? "已驗證" : "未驗證（多半是這顆資產改過，存檔它就會重驗）"),
                validated ? HGStyles.Tiny : HGStyles.RowLabelError);

            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !row.Contains(e.mousePosition)) continue;
            e.Use();
            if (!cmd.Open(so)) continue;
            // 焦點已經不是資產了，這一區的其餘列不該再畫；ScrollView 要自己收尾才不會破版。
            GUI.EndScrollView();
            return;
        }
        GUI.EndScrollView();
    }
}

}
