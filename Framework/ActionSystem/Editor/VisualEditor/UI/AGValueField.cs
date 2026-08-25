namespace HaruFamily.Framework.ActionSystem.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

/// <summary>
/// 把任意型別的值畫成一個輸入框。畫不了的型別顯示唯讀說明，不會把內部欄位攤開誤導使用者。
/// </summary>
public static class AGValueField
{
    /// <summary>
    /// 這個型別畫不畫得出輸入框。呼叫端要拿那一格畫別的內容時先問這裡，別畫出誤導的空框。
    /// 型別清單只有這一份：Draw 由它把關，漏加的型別會直接掉進「沒有輸入介面」，加欄位時馬上看得到。
    /// </summary>
    public static bool CanDraw(Type type)
    {
        if (type == null) return false;
        if (type.IsEnum) return true;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;

        return type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(uint) || type == typeof(char) || type == typeof(bool) || type == typeof(string)
            || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
            || type == typeof(Vector2Int) || type == typeof(Vector3Int)
            || type == typeof(Color) || type == typeof(Color32)
            || type == typeof(Rect) || type == typeof(RectInt) || type == typeof(Bounds) || type == typeof(BoundsInt)
            || type == typeof(AnimationCurve) || type == typeof(Gradient)
            || type == typeof(LayerMask) || type == typeof(Quaternion);
    }

    /// <summary>按鈕排裡第 index 顆的位置：等寬、相鄰兩顆貼合，接縫交給分段樣式處理。</summary>
    private static Rect EnumButtonRect(Rect rect, int index, int count)
    {
        if (count <= 0) return rect;
        float width = rect.width / count;
        return new Rect(rect.x + width * index, rect.y, width, rect.height);
    }

    /// <summary>按鈕排的分段樣式：頭尾各圓一邊、中間方角，整排是一條被切開的框。</summary>
    // 全部用 miniButton 的話每顆都是完整圓角框，相鄰兩顆的邊框疊成一條又粗又斷的中線，很醜。
    private static GUIStyle EnumButtonStyle(int index, int count)
    {
        if (count <= 1) return EditorStyles.miniButton;
        if (index == 0) return EditorStyles.miniButtonLeft;
        if (index == count - 1) return EditorStyles.miniButtonRight;
        return EditorStyles.miniButtonMid;
    }

    /// <summary>回傳新值；沒有變更就回原值。畫不了的型別顯示唯讀說明。</summary>
    public static object Draw(Rect rect, Type type, object value, bool enumButtons = false)
    {
        if (type == null) return value;
        if (!CanDraw(type)) { DrawUnsupported(rect, type); return value; }

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
                int buttonIndex = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    long flag = Convert.ToInt64(values.GetValue(i));
                    if (flag <= 0 || (flag & (flag - 1)) != 0) continue;
                    var buttonRect = EnumButtonRect(rect, buttonIndex, buttonCount);
                    var buttonStyle = EnumButtonStyle(buttonIndex++, buttonCount);
                    bool enabled = (selectedFlags & flag) == flag;
                    if (GUI.Toggle(buttonRect, enabled, labels[i], buttonStyle) == enabled) continue;
                    selectedFlags = enabled ? selectedFlags & ~flag : selectedFlags | flag;
                }
                return Enum.ToObject(type, selectedFlags);
            }

            // 單選也走同一組 Rect，不用 GUI.Toolbar：Toolbar 畫的是連在一起的分段條，跟旁邊的 [Flags] 按鈕排長得不一樣。
            int selected = Array.IndexOf(values, current);
            object picked = current;
            for (int i = 0; i < names.Length; i++)
            {
                var buttonRect = EnumButtonRect(rect, i, names.Length);
                // 只認「關 → 開」：重複點已選中的那顆不該把值清掉，單選一定要有一個是選中的。
                if (GUI.Toggle(buttonRect, i == selected, labels[i], EnumButtonStyle(i, names.Length)) && i != selected)
                    picked = values.GetValue(i);
            }
            return picked;
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

        // CanDraw 說可以畫卻走到這裡＝兩邊的型別清單對不上，回原值不吃掉資料。
        DrawUnsupported(rect, type);
        return value;
    }

    /// <summary>畫不了就講清楚，不要假裝可以編。</summary>
    private static void DrawUnsupported(Rect rect, Type type)
    {
        // 顯示名走 ResultTypeName：企劃看到的是 [ASKind] 的族名（Entity），不是 CLR 的 List`1。
        var label = new GUIContent($"（{AGReflect.ResultTypeName(type)}：此型別沒有對應的輸入介面）",
            $"{type.FullName} 不支援在節點圖上編輯。若企劃需要，請改用支援的型別，或在 AGValueField 補一個欄位。");
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        EditorGUI.LabelField(rect, label, AGStyles.Tiny);
        GUI.color = old;
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
