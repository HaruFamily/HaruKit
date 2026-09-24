namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 節點群組的顏色面板：上面是主題調色盤（點一格就選定並關閉），下面是自訂色的色相／飽和／明度滑桿（拖動即時套用）。
/// Esc 或點到別處關閉。
/// </summary>
// 自訂色不用 EditorGUI.ColorField：它會另開 Unity 的顏色選擇器，PopupWindow 一失焦就自己關掉，
// 選好的顏色回不到這裡。滑桿畫在面板裡，不離開這個視窗。
public sealed class HGNodeGroupColorPopup : PopupWindowContent
{
    private const float Cell = 20f;
    private const float Gap = 4f;
    private const float Padding = 8f;
    private const int Columns = 6;
    private const float LineHeight = 18f;

    private readonly Color[] palette;
    private readonly int selectedIndex;
    private readonly Action<int> onPalette;
    private readonly Action<Color> onCustom;
    private bool useCustom;
    private float hue;
    private float saturation;
    private float value;

    public HGNodeGroupColorPopup(Color[] palette, int selectedIndex, bool useCustom, Color custom,
        Action<int> onPalette, Action<Color> onCustom)
    {
        this.palette = palette ?? new Color[0];
        this.selectedIndex = selectedIndex;
        this.useCustom = useCustom;
        this.onPalette = onPalette;
        this.onCustom = onCustom;
        Color.RGBToHSV(useCustom ? custom : SelectedPaletteColor(), out hue, out saturation, out value);
    }

    private int Rows => Mathf.Max(1, Mathf.CeilToInt(palette.Length / (float)Columns));

    public override Vector2 GetWindowSize()
    {
        float width = Padding * 2f + Columns * Cell + (Columns - 1) * Gap;
        float height = Padding + Rows * (Cell + Gap) + Padding + LineHeight * 4f + Padding;
        return new Vector2(Mathf.Max(width, 220f), height);
    }

    public override void OnGUI(Rect rect)
    {
        using (HGSkin.Scope())
        {
            if (Event.current.type == EventType.Repaint) HGStyles.Fill(rect, HGStyles.Panel);
            DrawContent(rect);
        }
    }

    private void DrawContent(Rect rect)
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { editorWindow.Close(); e.Use(); return; }

        for (int i = 0; i < palette.Length; i++)
        {
            int row = i / Columns, column = i % Columns;
            var cell = new Rect(Padding + column * (Cell + Gap), Padding + row * (Cell + Gap), Cell, Cell);
            bool selected = !useCustom && i == selectedIndex;
            HGStyles.Fill(cell, palette[i]);
            HGStyles.Frame(cell, selected ? HGStyles.NodeBorderSelected : HGStyles.NodeBorder, selected ? 2f : 1f);
            if (!GUI.Button(cell, GUIContent.none, GUIStyle.none)) continue;
            onPalette?.Invoke(i);
            editorWindow.Close();
            return;
        }

        float y = Padding + Rows * (Cell + Gap) + Padding;
        float width = rect.width - Padding * 2f;
        var preview = new Rect(Padding, y, 36f, LineHeight - 2f);
        HGStyles.Fill(preview, Color.HSVToRGB(hue, saturation, value));
        HGStyles.Frame(preview, useCustom ? HGStyles.NodeBorderSelected : HGStyles.NodeBorder, useCustom ? 2f : 1f);
        GUI.Label(new Rect(preview.xMax + 6f, y, width - preview.width - 6f, LineHeight), "自訂", EditorStyles.miniLabel);
        y += LineHeight;

        EditorGUI.BeginChangeCheck();
        hue = Slider(ref y, width, "色相", hue);
        saturation = Slider(ref y, width, "飽和", saturation);
        value = Slider(ref y, width, "明度", value);
        if (!EditorGUI.EndChangeCheck()) return;
        useCustom = true;
        onCustom?.Invoke(Color.HSVToRGB(hue, saturation, value));
    }

    private static float Slider(ref float y, float width, string label, float current)
    {
        var line = new Rect(Padding, y, width, LineHeight - 2f);
        y += LineHeight;
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 36f;
        try
        {
            return EditorGUI.Slider(line, label, current, 0f, 1f);
        }
        finally
        {
            EditorGUIUtility.labelWidth = labelWidth;
        }
    }

    private Color SelectedPaletteColor()
        => palette.Length == 0 ? Color.white : palette[(selectedIndex % palette.Length + palette.Length) % palette.Length];
}

}
