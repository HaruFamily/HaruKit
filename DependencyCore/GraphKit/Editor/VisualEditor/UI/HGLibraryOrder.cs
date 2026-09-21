namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>庫清單共用的上移／下移控制。只回傳意圖，資料交易由呼叫端處理。</summary>
public static class HGLibraryOrder
{
    public const float Width = 34f;

    public static int Draw(Rect rect, bool canMoveUp, bool canMoveDown)
    {
        int direction = 0;
        using (new EditorGUI.DisabledScope(!canMoveUp))
            if (GUI.Button(new Rect(rect.x, rect.y, 16f, rect.height),
                new GUIContent("↑", "移到上一個可見項目之前"), HGStyles.Chip)) direction = -1;
        using (new EditorGUI.DisabledScope(!canMoveDown))
            if (GUI.Button(new Rect(rect.x + 17f, rect.y, 16f, rect.height),
                new GUIContent("↓", "移到下一個可見項目之後"), HGStyles.Chip)) direction = 1;
        // 停用的邊界按鈕也佔自己的熱區，不把點擊漏給列身的選取／拖曳。
        var e = Event.current;
        if (e.button == 0 && rect.Contains(e.mousePosition)
            && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
        return direction;
    }
}
}
