namespace PinPlugin.ActionSystem.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>編輯器共用的顏色與 GUIStyle。GUIStyle 只能在 OnGUI 期間建立，全部走 lazy。</summary>
public static class AGStyles
{
    public static readonly Color Canvas = new(0.16f, 0.17f, 0.19f);
    public static readonly Color Grid = new(0.21f, 0.22f, 0.25f);
    public static readonly Color GridBold = new(0.26f, 0.27f, 0.31f);

    public static readonly Color NodeBody = new(0.24f, 0.25f, 0.28f);
    public static readonly Color NodeHeader = new(0.30f, 0.33f, 0.39f);
    public static readonly Color NodeHeaderRoot = new(0.24f, 0.40f, 0.44f);
    public static readonly Color NodeHeaderOrphan = new(0.40f, 0.34f, 0.24f);
    public static readonly Color NodeHeaderToken = new(0.36f, 0.28f, 0.48f);
    public static readonly Color NodeHeaderAsset = new(0.26f, 0.36f, 0.34f);
    public static readonly Color NodeBorder = new(0.10f, 0.10f, 0.12f);
    public static readonly Color NodeBorderSelected = new(1f, 0.78f, 0.30f);

    public static readonly Color PortEmpty = new(0.45f, 0.47f, 0.52f);
    public static readonly Color PortLive = new(0.42f, 0.78f, 1f);
    public static readonly Color PortToken = new(0.69f, 0.52f, 0.92f);
    public static readonly Color PortError = new(1f, 0.42f, 0.42f);

    public static readonly Color Link = new(0.42f, 0.78f, 1f);
    public static readonly Color Error = new(1f, 0.42f, 0.42f);
    public static readonly Color Warning = new(1f, 0.78f, 0.34f);
    public static readonly Color Muted = new(0.65f, 0.66f, 0.70f);
    public static readonly Color RowAlt = new(1f, 1f, 1f, 0.03f);

    private static GUIStyle nodeTitle, nodeDesc, rowLabel, rowLabelError, chip, panelHeader, consoleRow, tiny;

    public static GUIStyle NodeTitle => nodeTitle ??= new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 12,
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 6, 0, 0),
        normal = { textColor = Color.white },
    };

    public static GUIStyle NodeDesc => nodeDesc ??= new GUIStyle(EditorStyles.miniLabel)
    {
        padding = new RectOffset(6, 6, 0, 0),
        normal = { textColor = Muted },
        wordWrap = false,
    };

    public static GUIStyle RowLabel => rowLabel ??= new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(4, 2, 0, 0),
    };

    public static GUIStyle RowLabelError => rowLabelError ??= new GUIStyle(RowLabel)
    {
        normal = { textColor = Error },
    };

    public static GUIStyle Chip => chip ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 4, 0, 0),
        normal = { textColor = new Color(0.80f, 0.68f, 1f) },
    };

    public static GUIStyle PanelHeader => panelHeader ??= new GUIStyle(EditorStyles.boldLabel)
    {
        padding = new RectOffset(6, 6, 2, 2),
    };

    public static GUIStyle ConsoleRow => consoleRow ??= new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(6, 4, 1, 1),
    };

    public static GUIStyle Tiny => tiny ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        normal = { textColor = Muted },
    };

    public static void Fill(Rect r, Color c) => EditorGUI.DrawRect(r, c);

    /// <summary>畫外框（四條線，避免額外貼圖）。</summary>
    public static void Frame(Rect r, Color c, float thickness = 1f)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
    }

    /// <summary>接點：實心小方塊（IMGUI 沒有便宜的圓形，方塊在小尺寸下更清楚）。</summary>
    public static void Port(Rect r, Color c)
    {
        EditorGUI.DrawRect(r, c);
        Frame(r, new Color(0f, 0f, 0f, 0.6f));
    }
}

}
