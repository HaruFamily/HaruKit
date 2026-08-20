namespace HaruFamily.Framework.ActionSystem.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector 上的 ActionSystem 欄位：畫成一張「節點圖入口」卡片，不展開圖的任何內容。
/// </summary>
// 註冊在開放泛型上，任何含 ActionSystem<,> 欄位的 Owner 都吃得到，不必逐個 Owner 寫 CustomEditor。
// 節點圖是唯一編輯點：Inspector 展開巢狀清單只會提供第二條會打架的編輯路徑。
// 畫成卡片而不是一顆裸按鈕，是因為這一格背後是整套編輯系統，外觀要對得起它的份量。
[CustomPropertyDrawer(typeof(ActionSystem<,>), true)]
public class ActionSystemDrawer : PropertyDrawer
{
    private const float Pad = 6f;
    private const float AccentWidth = 3f;
    private const float TitleHeight = 18f;
    private const float SummaryHeight = 14f;
    private const float ButtonHeight = 26f;
    private const float VerifyWidth = 64f;
    private const float Gap = 4f;

    private static GUIStyle titleStyle;
    private static GUIStyle summaryStyle;
    private static GUIStyle statusStyle;

    private static readonly Color OkColor = new Color(0.36f, 0.90f, 0.52f);
    private static readonly Color FailColor = new Color(1f, 0.42f, 0.42f);
    private static readonly Color IdleColor = new Color(0.55f, 0.55f, 0.55f);

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => Pad + TitleHeight + 2f + SummaryHeight + Gap + ButtonHeight + Pad;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EnsureStyles();

        var target = property.serializedObject.targetObject;
        bool multi = property.serializedObject.isEditingMultipleObjects;
        var validated = property.FindPropertyRelative("_validated");

        bool known = !multi && validated != null && !validated.hasMultipleDifferentValues;
        bool ok = known && validated.boolValue;
        int timings = 0, actions = 0, tokens = 0;
        if (!multi) timings = CountTimings(property, out actions, out tokens);

        // 空圖沒有「未驗證」可言，色條轉灰，免得一顆全新的資產一開就紅著臉。
        Color accent = !known ? IdleColor : timings == 0 ? IdleColor : ok ? OkColor : FailColor;
        DrawCard(position, accent);

        float x = position.x + AccentWidth + Pad;
        float width = position.xMax - Pad - x;

        // ===== 標題列：名稱 ｜ 狀態 =====
        var titleRect = new Rect(x, position.y + Pad, width, TitleHeight);
        GUI.Label(titleRect, "◈  ActionSystem 節點圖", titleStyle);

        statusStyle.normal.textColor = accent;
        GUI.Label(titleRect, StatusText(known, ok, timings, multi), statusStyle);

        // ===== 摘要列 =====
        var summaryRect = new Rect(x, titleRect.yMax + 2f, width, SummaryHeight);
        GUI.Label(summaryRect, SummaryText(multi, timings, actions, tokens), summaryStyle);

        // ===== 開啟 ｜ 驗證 =====
        var openRect = new Rect(x, summaryRect.yMax + Gap, width - VerifyWidth - Gap, ButtonHeight);
        var verifyRect = new Rect(openRect.xMax + Gap, openRect.y, VerifyWidth, ButtonHeight);

        using (new EditorGUI.DisabledScope(multi))
        {
            var open = new GUIContent("開啟節點圖編輯器",
                "節點圖是唯一的編輯入口；Inspector 不展開圖的內容。");
            if (GUI.Button(openRect, open)) ActionGraphWindow.OpenFor(target);
        }

        var owner = target as IActionSystemOwner;
        using (new EditorGUI.DisabledScope(multi || owner == null))
        {
            var verify = owner != null
                ? new GUIContent("驗證", "跑一次完整驗證，結果輸出到 Console。")
                : new GUIContent("驗證",
                    $"'{(target != null ? target.name : "?")}' 沒有實作 IActionSystemOwner，無法從 Inspector 驗證。");
            if (GUI.Button(verifyRect, verify)) Verify(property, owner);
        }
    }

    private static string StatusText(bool known, bool ok, int timings, bool multi)
    {
        if (multi) return "多重選取";
        if (!known) return "狀態未知";
        if (timings == 0) return "空的";
        return ok ? "✔ 已驗證" : "✘ 未驗證";
    }

    private static string SummaryText(bool multi, int timings, int actions, int tokens)
    {
        if (multi) return "多個對象：內容摘要不顯示";
        if (timings == 0 && tokens == 0) return "尚未建立任何時機——開啟編輯器新增第一個";
        return $"{timings} 個時機 · {actions} 個動作 · {tokens} 個變數";
    }

    /// <summary>只讀 SerializedProperty 的長度，不碰實體物件；Inspector 每幀跑得起。</summary>
    private static int CountTimings(SerializedProperty property, out int actions, out int tokens)
    {
        actions = 0;
        tokens = 0;

        var endpoints = property.FindPropertyRelative("_endpoints");
        if (endpoints != null && endpoints.isArray) tokens = endpoints.arraySize;

        var groups = property.FindPropertyRelative("ActionGroups");
        if (groups == null || !groups.isArray) return 0;

        for (int i = 0; i < groups.arraySize; i++)
        {
            // SerializeReference 元素可能是 null（型別遺失或手動清空），FindPropertyRelative 會回 null。
            var list = groups.GetArrayElementAtIndex(i)?.FindPropertyRelative("Actions");
            if (list != null && list.isArray) actions += list.arraySize;
        }
        return groups.arraySize;
    }

    private static void DrawCard(Rect rect, Color accent)
    {
        if (Event.current.type != EventType.Repaint) return;

        bool pro = EditorGUIUtility.isProSkin;
        var background = pro ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.80f, 0.80f, 0.80f);
        var border = pro ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.62f, 0.62f, 0.62f);

        EditorGUI.DrawRect(rect, background);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);

        // 左緣狀態色條：整張卡的驗證狀態一眼可見，不必讀文字。
        EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, AccentWidth, rect.height - 2f), accent);
    }

    // Verify() 改的是 C# 物件上的 _validated，不經 SerializedProperty，所以前後都要手動同步一次。
    private static void Verify(SerializedProperty property, IActionSystemOwner owner)
    {
        if (owner == null) return;

        property.serializedObject.ApplyModifiedProperties();
        owner.VerifyActionSystem();
        EditorUtility.SetDirty(property.serializedObject.targetObject);
        property.serializedObject.Update();
    }

    private static void EnsureStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.alignment = TextAnchor.MiddleLeft;

        summaryStyle = new GUIStyle(EditorStyles.miniLabel);
        summaryStyle.alignment = TextAnchor.MiddleLeft;

        statusStyle = new GUIStyle(EditorStyles.miniLabel);
        statusStyle.alignment = TextAnchor.MiddleRight;
    }
}

}
