namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 讓節點圖內的 Unity 原生控制項（輸入框、按鈕、下拉、勾選框、標籤字色）跟著 <see cref="HGTheme"/>，
/// 不隨 Unity 的 Light／Dark 主題變化。
/// </summary>
/// <remarks>
/// 做法是在 Repaint 期間暫時改寫共用的 EditorStyles／GUI.skin 樣式，離開範圍立刻還原。
/// 只在 Repaint 生效：樣式的外觀只在 Repaint 被讀，而 Repaint 期間不會開對話框或讓別的視窗重繪，
/// 改寫不會外漏到其他視窗。呼叫端一律 <c>using (HGSkin.Scope())</c>，例外也會在 finally 還原。
/// ObjectField 的選取圓鈕、Color／Curve／Gradient 欄位與 AdvancedDropdown 由 Unity 內部樣式繪製，不在涵蓋範圍。
/// </remarks>
public static class HGSkin
{
    private enum Look { Label, Field, Button, Popup, Toggle }

    private sealed class StateBackup
    {
        public Texture2D Background;
        public Texture2D[] Scaled;
        public Color TextColor;
    }

    private sealed class StyleBackup
    {
        public GUIStyle Style;
        public RectOffset Border;
        public readonly StateBackup[] States = new StateBackup[8];
    }

    private static readonly List<StyleBackup> backups = new();
    private static readonly List<Texture2D> textures = new();
    private static int depth;
    private static int textureVersion = -1;
    private static Color savedSelection, savedCursor;

    private static Texture2D fieldTex, fieldFocusTex, buttonTex, buttonHoverTex, buttonPressedTex, buttonOnTex;
    private static Texture2D popupTex, popupHoverTex, popupPressedTex, toggleOffTex, toggleOnTex;

    public readonly struct ScopeHandle : IDisposable
    {
        private readonly bool active;
        public ScopeHandle(bool active) { this.active = active; }
        public void Dispose() { if (active) End(); }
    }

    /// <summary>在這個範圍內繪製的原生控制項使用主題外觀。巢狀呼叫只有最外層生效。</summary>
    public static ScopeHandle Scope()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return default;
        if (depth++ > 0) return new ScopeHandle(true);
        Begin();
        return new ScopeHandle(true);
    }

    /// <summary>主題換掉時丟棄產生過的貼圖，下次繪製重做。</summary>
    public static void ResetCache()
    {
        foreach (var tex in textures) if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
        textures.Clear();
        textureVersion = -1;
    }

    private static void Begin()
    {
        EnsureTextures();
        var t = HGTheme.Current;

        Override(EditorStyles.label, Look.Label, t.text);
        Override(EditorStyles.boldLabel, Look.Label, t.text);
        Override(EditorStyles.miniLabel, Look.Label, t.text);
        Override(EditorStyles.miniBoldLabel, Look.Label, t.text);
        Override(EditorStyles.largeLabel, Look.Label, t.text);
        Override(EditorStyles.wordWrappedLabel, Look.Label, t.text);
        Override(EditorStyles.wordWrappedMiniLabel, Look.Label, t.text);
        Override(EditorStyles.foldout, Look.Label, t.text, keepBackground: true);

        Override(EditorStyles.textField, Look.Field, t.fieldText);
        Override(EditorStyles.numberField, Look.Field, t.fieldText);
        Override(EditorStyles.textArea, Look.Field, t.fieldText);
        Override(EditorStyles.objectField, Look.Field, t.fieldText);

        Override(EditorStyles.miniButton, Look.Button, t.buttonText);
        Override(EditorStyles.miniButtonLeft, Look.Button, t.buttonText);
        Override(EditorStyles.miniButtonMid, Look.Button, t.buttonText);
        Override(EditorStyles.miniButtonRight, Look.Button, t.buttonText);
        Override(EditorStyles.toolbarButton, Look.Button, t.buttonText);
        Override(EditorStyles.popup, Look.Popup, t.buttonText);
        Override(EditorStyles.miniPullDown, Look.Popup, t.buttonText);
        Override(EditorStyles.toolbarDropDown, Look.Popup, t.buttonText);
        Override(EditorStyles.toggle, Look.Toggle, t.text);

        var skin = GUI.skin;
        Override(skin.label, Look.Label, t.text);
        Override(skin.button, Look.Button, t.buttonText);
        Override(skin.textField, Look.Field, t.fieldText);
        Override(skin.textArea, Look.Field, t.fieldText);
        Override(skin.toggle, Look.Toggle, t.text);

        savedSelection = skin.settings.selectionColor;
        savedCursor = skin.settings.cursorColor;
        skin.settings.selectionColor = t.selection;
        skin.settings.cursorColor = t.cursor;
    }

    private static void End()
    {
        if (--depth > 0) return;

        // 倒序還原：同一個樣式物件可能被登記兩次（例如 GUI.skin.label 就是 EditorStyles 的同一顆），要回到最早的狀態。
        for (int i = backups.Count - 1; i >= 0; i--) Restore(backups[i]);
        backups.Clear();

        var settings = GUI.skin.settings;
        settings.selectionColor = savedSelection;
        settings.cursorColor = savedCursor;
    }

    private static GUIStyleState[] StatesOf(GUIStyle s) => new[]
    {
        s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused,
    };

    private static void Override(GUIStyle style, Look look, Color ink, bool keepBackground = false)
    {
        if (style == null) return;

        var backup = new StyleBackup { Style = style, Border = new RectOffset(style.border.left, style.border.right, style.border.top, style.border.bottom) };
        var states = StatesOf(style);
        for (int i = 0; i < states.Length; i++)
            backup.States[i] = new StateBackup { Background = states[i].background, Scaled = states[i].scaledBackgrounds, TextColor = states[i].textColor };
        backups.Add(backup);

        var t = HGTheme.Current;
        foreach (var state in states) state.textColor = ink;
        if (look == Look.Button || look == Look.Popup) style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = t.buttonOnText;
        if (look == Look.Label || keepBackground) return;

        switch (look)
        {
            case Look.Field:
                SetBackground(style, 3, fieldTex, fieldTex, fieldFocusTex, fieldFocusTex, fieldFocusTex, fieldFocusTex, fieldFocusTex, fieldFocusTex);
                break;
            case Look.Button:
                SetBackground(style, 3, buttonTex, buttonHoverTex, buttonPressedTex, buttonTex, buttonOnTex, buttonOnTex, buttonPressedTex, buttonOnTex);
                break;
            case Look.Popup:
                SetBackground(style, PopupBorder, popupTex, popupHoverTex, popupPressedTex, popupTex, popupPressedTex, popupPressedTex, popupPressedTex, popupPressedTex);
                style.border = new RectOffset(3, PopupArrowWidth, 3, 3);
                break;
            case Look.Toggle:
                SetBackground(style, 0, toggleOffTex, toggleOffTex, toggleOffTex, toggleOffTex, toggleOnTex, toggleOnTex, toggleOnTex, toggleOnTex);
                // 左、上邊框吃滿整張 14px 貼圖、中間寬高為 0：勾選框固定畫在左上角，不隨欄位寬度拉長。
                style.border = new RectOffset(ToggleSize, 0, ToggleSize, 0);
                break;
        }
    }

    private const int PopupBorder = 3;
    private const int PopupArrowWidth = 14;
    private const int ToggleSize = 14;

    private static void SetBackground(GUIStyle style, int border, params Texture2D[] perState)
    {
        var states = StatesOf(style);
        for (int i = 0; i < states.Length; i++)
        {
            states[i].background = perState[i];
            // 高 DPI 下 Unity 優先用 scaledBackgrounds，不清掉就會畫回原生貼圖。
            states[i].scaledBackgrounds = Array.Empty<Texture2D>();
        }
        style.border = new RectOffset(border, border, border, border);
    }

    private static void Restore(StyleBackup backup)
    {
        var states = StatesOf(backup.Style);
        for (int i = 0; i < states.Length; i++)
        {
            states[i].background = backup.States[i].Background;
            states[i].scaledBackgrounds = backup.States[i].Scaled;
            states[i].textColor = backup.States[i].TextColor;
        }
        backup.Style.border = backup.Border;
    }

    // ===== 貼圖 =====

    private static void EnsureTextures()
    {
        if (textureVersion == HGTheme.Version && fieldTex != null) return;
        ResetCache();
        textureVersion = HGTheme.Version;

        var t = HGTheme.Current;
        fieldTex = Box(12, 12, 3f, t.fieldBackground, t.fieldBorder);
        fieldFocusTex = Box(12, 12, 3f, t.fieldBackground, t.fieldFocused);
        buttonTex = Box(12, 12, 3f, t.buttonBackground, t.buttonBorder);
        buttonHoverTex = Box(12, 12, 3f, t.buttonHover, t.buttonBorder);
        buttonPressedTex = Box(12, 12, 3f, t.buttonPressed, t.buttonBorder);
        buttonOnTex = Box(12, 12, 3f, t.buttonOn, t.buttonBorder);
        popupTex = Popup(t.buttonBackground, t.buttonBorder, t.buttonText);
        popupHoverTex = Popup(t.buttonHover, t.buttonBorder, t.buttonText);
        popupPressedTex = Popup(t.buttonPressed, t.buttonBorder, t.buttonText);
        toggleOffTex = Box(ToggleSize, ToggleSize, 3f, t.fieldBackground, t.fieldBorder);
        toggleOnTex = Check(t.buttonOn, t.buttonBorder, t.buttonOnText);
    }

    private static Texture2D NewTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        textures.Add(tex);
        return tex;
    }

    /// <summary>圓角框：外緣 1px 邊框色、內部填色，邊緣依覆蓋率反鋸齒。</summary>
    private static Texture2D Box(int w, int h, float radius, Color fill, Color border)
    {
        var tex = NewTexture(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, BoxPixel(x, y, w, h, radius, fill, border));
        tex.Apply();
        return tex;
    }

    private static Color BoxPixel(int x, int y, int w, int h, float radius, Color fill, Color border)
    {
        // 到圓角矩形邊界的有號距離（內部為負），取像素中心。
        float px = x + 0.5f, py = y + 0.5f;
        float qx = Mathf.Max(Mathf.Abs(px - w * 0.5f) - (w * 0.5f - radius), 0f);
        float qy = Mathf.Max(Mathf.Abs(py - h * 0.5f) - (h * 0.5f - radius), 0f);
        float inside = Mathf.Min(Mathf.Max(Mathf.Abs(px - w * 0.5f) - (w * 0.5f - radius), Mathf.Abs(py - h * 0.5f) - (h * 0.5f - radius)), 0f);
        float dist = Mathf.Sqrt(qx * qx + qy * qy) + inside - radius;

        float coverage = Mathf.Clamp01(0.5f - dist);
        float borderMix = Mathf.Clamp01(dist + 1.5f);   // 最外 1px 是邊框
        var c = Color.Lerp(fill, border, borderMix);
        c.a *= coverage;
        return c;
    }

    /// <summary>下拉鈕：按鈕底加右側向下三角形。三角形放在右側不拉伸的邊框區內。</summary>
    private static Texture2D Popup(Color fill, Color border, Color arrow)
    {
        const int w = 24, h = 16;
        var tex = NewTexture(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, BoxPixel(x, y, w, h, 3f, fill, border));

        // 貼圖 y 由下往上；三角形寬 7、高 4，底邊在上、尖端朝下，垂直置中。
        int cx = w - PopupArrowWidth / 2 - 1, top = h / 2 + 2;
        for (int row = 0; row < 4; row++)
        for (int dx = -3 + row; dx <= 3 - row; dx++)
            tex.SetPixel(cx + dx, top - row, arrow);
        tex.Apply();
        return tex;
    }

    /// <summary>勾選狀態的勾選框：主題色底，中間一個勾。</summary>
    private static Texture2D Check(Color fill, Color border, Color mark)
    {
        var tex = Box(ToggleSize, ToggleSize, 3f, fill, border);
        // 勾的兩筆：左下短筆、右上長筆（貼圖 y 由下往上）。
        int[,] points = { { 3, 7 }, { 4, 6 }, { 5, 5 }, { 6, 4 }, { 7, 5 }, { 8, 6 }, { 9, 7 }, { 10, 8 }, { 11, 9 } };
        for (int i = 0; i < points.GetLength(0); i++)
        {
            int x = points[i, 0], y = points[i, 1];
            tex.SetPixel(x, y, mark);
            tex.SetPixel(x, y + 1, mark);
        }
        tex.Apply();
        return tex;
    }
}

}
