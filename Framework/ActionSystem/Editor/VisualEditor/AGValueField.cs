namespace PinPlugin.ActionSystem.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

/// <summary>
/// 把任意型別的值畫成一個輸入框。畫不了的型別顯示唯讀說明，不會把內部欄位攤開誤導使用者。
/// </summary>
public static class AGValueField
{
    /// <summary>回傳新值；沒有變更就回原值。</summary>
    public static object Draw(Rect rect, Type type, object value, bool enumButtons = false)
    {
        if (type == null) return value;

        // ===== 數值 =====
        if (type == typeof(int)) return EditorGUI.IntField(rect, value is int i ? i : 0);
        if (type == typeof(float)) return EditorGUI.FloatField(rect, value is float f ? f : 0f);
        if (type == typeof(double)) return EditorGUI.DoubleField(rect, value is double d ? d : 0d);
        if (type == typeof(long)) return EditorGUI.LongField(rect, value is long l ? l : 0L);

        // 小整數用 LongField 收，再夾回型別範圍：Unity 沒有 short/byte 專用欄位。
        if (type == typeof(short)) return (short)Mathf.Clamp(EditorGUI.LongField(rect, value is short s ? s : (short)0), short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)Mathf.Clamp(EditorGUI.LongField(rect, value is ushort us ? us : (ushort)0), ushort.MinValue, ushort.MaxValue);
        if (type == typeof(byte)) return (byte)Mathf.Clamp(EditorGUI.LongField(rect, value is byte b ? b : (byte)0), byte.MinValue, byte.MaxValue);
        if (type == typeof(sbyte)) return (sbyte)Mathf.Clamp(EditorGUI.LongField(rect, value is sbyte sb ? sb : (sbyte)0), sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(uint)) return (uint)Mathf.Clamp(EditorGUI.LongField(rect, value is uint ui ? ui : 0u), uint.MinValue, uint.MaxValue);

        if (type == typeof(char))
        {
            string text = EditorGUI.TextField(rect, value is char c ? c.ToString() : "");
            return string.IsNullOrEmpty(text) ? '\0' : text[0];
        }

        // ===== 基本 =====
        if (type == typeof(bool)) return EditorGUI.Toggle(rect, value is bool bo && bo);
        if (type == typeof(string)) return EditorGUI.TextField(rect, value as string ?? "");

        if (type.IsEnum)
        {
            var current = value as Enum;
            Array values = Enum.GetValues(type);
            if (current == null)
            {
                if (values.Length == 0) return value;
                current = (Enum)values.GetValue(0);
            }
            if (!enumButtons) return EditorGUI.EnumPopup(rect, current);

            var names = Enum.GetNames(type);
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var member = type.GetField(names[i]);
                var attr = member?.GetCustomAttributes(typeof(ASLabelAttribute), false);
                labels[i] = attr != null && attr.Length > 0
                    ? ((ASLabelAttribute)attr[0]).Name
                    : names[i];
            }

            if (type.IsDefined(typeof(FlagsAttribute), false))
            {
                long selectedFlags = Convert.ToInt64(current);
                int buttonCount = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    long flag = Convert.ToInt64(values.GetValue(i));
                    if (flag > 0 && (flag & (flag - 1)) == 0) buttonCount++;
                }
                if (buttonCount <= 0) return current;
                float width = rect.width / buttonCount;
                int buttonIndex = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    long flag = Convert.ToInt64(values.GetValue(i));
                    if (flag <= 0 || (flag & (flag - 1)) != 0) continue;
                    var buttonRect = new Rect(rect.x + width * buttonIndex++, rect.y, width, rect.height);
                    bool enabled = (selectedFlags & flag) == flag;
                    if (GUI.Toggle(buttonRect, enabled, labels[i], EditorStyles.miniButton) == enabled) continue;
                    selectedFlags = enabled ? selectedFlags & ~flag : selectedFlags | flag;
                }
                return Enum.ToObject(type, selectedFlags);
            }

            int selected = Array.IndexOf(values, current);
            int next = GUI.Toolbar(rect, selected, labels, EditorStyles.miniButton);
            return next >= 0 && next < values.Length ? values.GetValue(next) : current;
        }

        // ===== Unity 內建型別 =====
        if (type == typeof(Vector2)) return EditorGUI.Vector2Field(rect, GUIContent.none, value is Vector2 v2 ? v2 : Vector2.zero);
        if (type == typeof(Vector3)) return EditorGUI.Vector3Field(rect, GUIContent.none, value is Vector3 v3 ? v3 : Vector3.zero);
        if (type == typeof(Vector4)) return EditorGUI.Vector4Field(rect, GUIContent.none, value is Vector4 v4 ? v4 : Vector4.zero);
        if (type == typeof(Vector2Int)) return EditorGUI.Vector2IntField(rect, GUIContent.none, value is Vector2Int v2i ? v2i : Vector2Int.zero);
        if (type == typeof(Vector3Int)) return EditorGUI.Vector3IntField(rect, GUIContent.none, value is Vector3Int v3i ? v3i : Vector3Int.zero);
        if (type == typeof(Color)) return EditorGUI.ColorField(rect, value is Color col ? col : Color.white);
        if (type == typeof(Color32)) return (Color32)EditorGUI.ColorField(rect, value is Color32 c32 ? (Color)c32 : Color.white);
        if (type == typeof(Rect)) return EditorGUI.RectField(rect, value is Rect r ? r : new Rect());
        if (type == typeof(RectInt)) return EditorGUI.RectIntField(rect, value is RectInt ri ? ri : new RectInt());
        if (type == typeof(Bounds)) return EditorGUI.BoundsField(rect, value is Bounds bn ? bn : new Bounds());
        if (type == typeof(BoundsInt)) return EditorGUI.BoundsIntField(rect, value is BoundsInt bi ? bi : new BoundsInt());
        if (type == typeof(AnimationCurve)) return EditorGUI.CurveField(rect, value as AnimationCurve ?? new AnimationCurve());
        if (type == typeof(Gradient)) return EditorGUI.GradientField(rect, value as Gradient ?? new Gradient());

        if (type == typeof(LayerMask))
        {
            int mask = value is LayerMask lm ? lm.value : 0;
            return (LayerMask)EditorGUI.MaskField(rect, mask, UnityEditorInternal.InternalEditorUtility.layers);
        }

        // Quaternion 直接編四元數容易編壞，改成編尤拉角。
        if (type == typeof(Quaternion))
        {
            var q = value is Quaternion qt ? qt : Quaternion.identity;
            var euler = EditorGUI.Vector3Field(rect, GUIContent.none, q.eulerAngles);
            return Quaternion.Euler(euler);
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return EditorGUI.ObjectField(rect, value as UnityEngine.Object, type, false);

        // 畫不了就講清楚，不要假裝可以編。
        var label = new GUIContent($"（{AGReflect.Prettify(type.Name)}：此型別沒有對應的輸入介面）",
            $"{type.FullName} 不支援在節點圖上編輯。若企劃需要，請改用支援的型別，或在 AGValueField 補一個欄位。");
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        EditorGUI.LabelField(rect, label, AGStyles.Tiny);
        GUI.color = old;
        return value;
    }

    /// <summary>畫成不可編輯的樣子（值仍可改，只是視覺上表示它不是主要來源）。</summary>
    public static object DrawMuted(Rect rect, Type type, object value, string tooltip, bool enumButtons = false)
    {
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        var result = Draw(rect, type, value, enumButtons);
        GUI.color = old;
        if (!string.IsNullOrEmpty(tooltip)) GUI.Label(rect, new GUIContent("", tooltip));
        return result;
    }
}

}
