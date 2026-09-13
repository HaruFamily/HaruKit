using HaruFamily.Framework.LogicGraph.Editor;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    /// <summary>
    /// Inspector 上的管線節點圖欄位：畫成一張「節點圖入口」卡片，不展開圖的任何內容。
    /// </summary>
    // 節點圖是唯一的編輯點：Inspector 展開巢狀步驟清單只會提供第二條會打架的編輯路徑。
    [CustomPropertyDrawer(typeof(APGraph), true)]
    public class APGraphDrawer : PropertyDrawer
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
            SerializedProperty validated = property.FindPropertyRelative("_validated");

            bool known = !multi && validated != null && !validated.hasMultipleDifferentValues;
            bool ok = known && validated.boolValue;
            int steps = 0, disabled = 0, tokens = 0;
            if (!multi) steps = CountSteps(property, out disabled, out tokens);

            // 空圖沒有「未驗證」可言，色條轉灰，免得一條全新的管線一開就紅著臉。
            Color accent = !known || steps == 0 ? IdleColor : ok ? OkColor : FailColor;
            DrawCard(position, accent);

            float x = position.x + AccentWidth + Pad;
            float width = position.xMax - Pad - x;

            var titleRect = new Rect(x, position.y + Pad, width, TitleHeight);
            GUI.Label(titleRect, "◈  管線節點圖", titleStyle);

            statusStyle.normal.textColor = accent;
            GUI.Label(titleRect, StatusText(known, ok, steps, multi), statusStyle);

            var summaryRect = new Rect(x, titleRect.yMax + 2f, width, SummaryHeight);
            GUI.Label(summaryRect, SummaryText(multi, steps, disabled, tokens), summaryStyle);

            var openRect = new Rect(x, summaryRect.yMax + Gap, width - VerifyWidth - Gap, ButtonHeight);
            var verifyRect = new Rect(openRect.xMax + Gap, openRect.y, VerifyWidth, ButtonHeight);

            using (new EditorGUI.DisabledScope(multi))
            {
                var open = new GUIContent("開啟節點圖編輯器",
                    "節點圖是唯一的編輯入口；Inspector 不展開圖的內容。");
                if (GUI.Button(openRect, open)) LogicGraphWindow.OpenFor(target);
            }

            var pipeline = target as AssetPipeline;
            using (new EditorGUI.DisabledScope(multi || pipeline == null))
            {
                var verify = pipeline != null
                    ? new GUIContent("驗證", "驗證節點圖與原型資產來源，結果輸出到 Console 與下方 Log。")
                    : new GUIContent("驗證", "這張圖不在 AssetPipeline 上，無法從 Inspector 驗證。");
                if (GUI.Button(verifyRect, verify)) Verify(property, pipeline);
            }
        }

        private static string StatusText(bool known, bool ok, int steps, bool multi)
        {
            if (multi) return "多重選取";
            if (!known) return "狀態未知";
            if (steps == 0) return "空的";
            return ok ? "✔ 已驗證" : "✘ 未驗證";
        }

        private static string SummaryText(bool multi, int steps, int disabled, int tokens)
        {
            if (multi) return "多個對象：內容摘要不顯示";
            if (steps == 0 && tokens == 0) return "尚未建立任何步驟——開啟編輯器新增第一個";
            string text = $"{steps} 個步驟 · {tokens} 個變數";
            return disabled > 0 ? $"{text} · {disabled} 個停用" : text;
        }

        /// <summary>只讀 SerializedProperty 的長度，不碰實體物件；Inspector 每幀跑得起。</summary>
        private static int CountSteps(SerializedProperty property, out int disabled, out int tokens)
        {
            disabled = 0;
            tokens = 0;

            SerializedProperty endpoints = property.FindPropertyRelative("_endpoints");
            if (endpoints != null && endpoints.isArray) tokens = endpoints.arraySize;

            SerializedProperty roots = property.FindPropertyRelative("_roots");
            if (roots == null || !roots.isArray) return 0;

            int steps = 0;
            for (int i = 0; i < roots.arraySize; i++)
            {
                // SerializeReference 元素可能是 null（型別遺失或手動清空），FindPropertyRelative 會回 null。
                SerializedProperty list = roots.GetArrayElementAtIndex(i)?.FindPropertyRelative("Steps");
                if (list == null || !list.isArray) continue;

                steps += list.arraySize;
                for (int s = 0; s < list.arraySize; s++)
                {
                    SerializedProperty flag = list.GetArrayElementAtIndex(s)?.FindPropertyRelative("_disabled");
                    if (flag != null && flag.boolValue) disabled++;
                }
            }
            return steps;
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

        // 驗證改的是 C# 物件上的旗標與 Log，不經 SerializedProperty，所以前後都要手動同步一次。
        private static void Verify(SerializedProperty property, AssetPipeline pipeline)
        {
            if (pipeline == null) return;

            property.serializedObject.ApplyModifiedProperties();
            pipeline.VerifyPipelineAssets();
            EditorUtility.SetDirty(pipeline);
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
