namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 節點圖視窗的配色。欄位初始值就是預設主題；JSON 只需寫要改的鍵，沒寫的沿用預設。
/// 錯誤紅與警告琥珀不在這裡：它們是語意色，不參與配色。
/// </summary>
/// <remarks>
/// JSON 的顏色可以寫 "#RRGGBB"／"#RRGGBBAA"，也可以寫 JsonUtility 的 {"r":..,"g":..,"b":..,"a":..}。
/// 主題檔命名為 <c>*.graphtheme.json</c>，放在專案或套件任何位置都掃得到。
/// </remarks>
[Serializable]
public class HGTheme
{
    public string name = DefaultName;

    // ===== 結構：畫布與面板 =====
    public Color canvas = new(0.145f, 0.145f, 0.145f);
    public Color grid = new(0.19f, 0.19f, 0.19f);
    public Color gridBold = new(0.24f, 0.24f, 0.24f);
    public Color toolbar = new(0.18f, 0.18f, 0.18f);
    public Color panel = new(0.17f, 0.17f, 0.17f);
    public Color panelSection = new(0.21f, 0.21f, 0.21f);
    public Color panelList = new(0.13f, 0.13f, 0.13f);
    public Color console = new(0.15f, 0.15f, 0.15f);
    public Color resizeGrip = new(0.34f, 0.36f, 0.40f, 0.65f);

    // ===== 節點 =====
    public Color nodeBody = new(0.28f, 0.28f, 0.28f);
    public Color nodeBorder = new(0.11f, 0.11f, 0.11f);
    public Color nodeBorderSelected = new(1f, 0.80f, 0.38f);
    public Color headBorder = new(0.93f, 0.93f, 0.93f);
    public Color disabledVeil = new(0.08f, 0.08f, 0.08f, 0.55f);
    public Color linkDisabled = new(1f, 1f, 1f, 0.22f);
    public Color nodeNote = new(0.26f, 0.26f, 0.26f);
    public Color nodeNoteBorder = new(0.62f, 0.62f, 0.62f);
    public Color overlayPanel = new(0.10f, 0.11f, 0.13f, 0.88f);
    public Color overlayTitle = new(1f, 1f, 1f);
    public Color boxSelect = new(0.42f, 0.78f, 1f, 0.10f);

    // ===== Header 身分色 =====
    public Color headerHead = new(0.447f, 0.227f, 0.408f);
    public Color headerAction = new(0.722f, 0.231f, 0.451f);
    public Color headerFormula = new(0.750f, 0.520f, 0.200f);
    public Color headerAsset = new(0.270f, 0.450f, 0.770f);
    public Color headerToken = new(0.160f, 0.420f, 0.310f);
    public Color headerProperty = new(0.420f, 0.360f, 0.720f);
    public Color headerInk = new(0.97f, 0.93f, 0.95f);
    public Color headerOverlay = new(1f, 1f, 1f, 0.14f);

    // ===== 連線與接點 =====
    public Color link = new(0.80f, 0.80f, 0.82f);
    public Color inputPortLive = new(0.80f, 0.80f, 0.82f);
    public Color outputPortLive = new(0.80f, 0.80f, 0.82f);
    public Color outputPortColor = new(0.42f, 0.72f, 0.74f);
    public Color portHole = new(0f, 0f, 0f, 0.45f);
    public Color portGlyph = new(0.10f, 0.10f, 0.11f);

    // ===== 文字 =====
    public Color text = new(0.824f, 0.824f, 0.824f);
    public Color muted = new(0.74f, 0.74f, 0.75f);
    public Color chipText = new(0.80f, 0.68f, 1f);
    public Color slotChipText = new(0.78f, 0.78f, 0.80f);
    public Color slotChipBody = new(1f, 1f, 1f, 0.10f);
    public Color listIndex = new(0.62f, 0.62f, 0.63f);

    // ===== 清單與庫格 =====
    public Color rowAlt = new(1f, 1f, 1f, 0.04f);
    public Color libraryCellBorder = new(0.11f, 0.11f, 0.11f);
    public Color listBand = new(0f, 0f, 0f, 0.24f);
    public Color listStripeEven = new(1f, 1f, 1f, 0.07f);
    public Color listStripeOdd = new(0f, 0f, 0f, 0.084f);
    public Color listRule = new(1f, 1f, 1f, 0.13f);
    public Color listHeader = new(0f, 0f, 0f, 0.176f);
    public Color listRowHover = new(1f, 1f, 1f, 0.07f);
    public Color listRowDragging = new(1f, 1f, 1f, 0.13f);
    public Color focusBanner = new(0.45f, 0.32f, 0.18f);
    public Color dropCreate = new(0.24f, 0.50f, 0.34f, 0.75f);
    public Color dropRemove = new(0.62f, 0.24f, 0.26f, 0.75f);

    // ===== 工具列按鈕染色（乘在按鈕底色上） =====
    public Color toolbarLocked = new(1f, 0.85f, 0.5f);
    public Color saveHighlight = new(0.85f, 0.28f, 0.28f);

    // ===== 執行狀態 =====
    public Color executionNotVisited = new(0.20f, 0.22f, 0.23f);
    public Color executionRunning = new(0.25f, 0.86f, 1f);
    public Color executionCompleted = new(0.34f, 0.65f, 0.43f);
    public Color executionCancelled = new(0.55f, 0.57f, 0.60f);

    // ===== 輸入框與按鈕（取代 Unity 原生外觀） =====
    public Color fieldBackground = new(0.165f, 0.165f, 0.165f);
    public Color fieldBorder = new(0.10f, 0.10f, 0.10f);
    public Color fieldFocused = new(0.23f, 0.47f, 0.73f);
    public Color fieldText = new(0.824f, 0.824f, 0.824f);
    public Color buttonBackground = new(0.345f, 0.345f, 0.345f);
    public Color buttonHover = new(0.40f, 0.40f, 0.40f);
    public Color buttonPressed = new(0.27f, 0.27f, 0.27f);
    public Color buttonOn = new(0.27f, 0.38f, 0.49f);
    public Color buttonBorder = new(0.14f, 0.14f, 0.14f);
    public Color buttonText = new(0.85f, 0.85f, 0.85f);
    public Color buttonOnText = new(0.95f, 0.95f, 0.95f);
    public Color selection = new(0.24f, 0.37f, 0.59f);
    public Color cursor = new(0.85f, 0.85f, 0.85f);

    public const string DefaultName = "預設";
    public const string FileSuffix = ".graphtheme.json";
    private const string PrefKey = "HaruGraph.Theme";

    private static HGTheme current;
    private static string currentGuid;

    /// <summary>目前套用的主題。第一次讀取時依 EditorPrefs 載入；載入失敗會 Log 並退回預設。</summary>
    public static HGTheme Current => current ??= LoadSelected();

    /// <summary>目前主題檔的 GUID；預設主題為空字串。</summary>
    public static string CurrentGuid
    {
        get { _ = Current; return currentGuid; }
    }

    /// <summary>主題換過一次就加一；需要跟著換的快取拿它比對。</summary>
    public static int Version { get; private set; }

    /// <summary>專案與套件內所有 <c>*.graphtheme.json</c>，依顯示名排序。只在開選單時呼叫。</summary>
    public static List<(string Guid, string Name)> Available()
    {
        var list = new List<(string Guid, string Name)>();
        foreach (var guid in AssetDatabase.FindAssets("graphtheme t:TextAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add((guid, ReadDisplayName(path)));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return list;
    }

    /// <summary>切換主題並記進 EditorPrefs（本機視圖偏好）。guid 為空＝預設主題。</summary>
    public static void Select(string guid)
    {
        guid ??= "";
        try { EditorPrefs.SetString(PrefKey, guid); }
        catch (Exception ex) { Debug.LogError($"[GraphKit] 主題偏好寫入失敗（{PrefKey}={guid}），這次仍套用但下次開啟會回到舊選擇：{ex.Message}"); }
        Apply(LoadGuid(guid), guid);
    }

    /// <summary>重讀目前主題檔；主題 JSON 被修改時呼叫。</summary>
    public static void Reload() => Apply(LoadGuid(CurrentGuid), CurrentGuid);

    /// <summary>把目前主題的完整鍵值寫成 JSON，當作自訂主題的範本。</summary>
    public static bool Export(string path, out string error)
    {
        error = null;
        try
        {
            File.WriteAllText(path, ToHexJson(JsonUtility.ToJson(Current, true)));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Apply(HGTheme theme, string guid)
    {
        current = theme;
        currentGuid = guid;
        Version++;
        HGStyles.ResetCache();
        HGSkin.ResetCache();
        foreach (var window in Resources.FindObjectsOfTypeAll<HaruGraphWindow>()) window.Repaint();
    }

    private static HGTheme LoadSelected()
    {
        string guid = "";
        try { guid = EditorPrefs.GetString(PrefKey, ""); }
        catch (Exception ex) { Debug.LogWarning($"[GraphKit] 讀取主題偏好失敗（{PrefKey}），改用預設主題：{ex.Message}"); }
        currentGuid = guid;
        return LoadGuid(guid);
    }

    private static HGTheme LoadGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return new HGTheme();

        string path = AssetDatabase.GUIDToAssetPath(guid);
        // 走 AssetDatabase 讀：套件內的 Packages/ 路徑是虛擬路徑，System.IO 找不到。
        var file = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (file == null)
        {
            Debug.LogWarning($"[GraphKit] 找不到主題檔（GUID {guid}，路徑 '{path}'），節點圖改用預設主題。");
            return new HGTheme();
        }

        try
        {
            var theme = new HGTheme();
            JsonUtility.FromJsonOverwrite(FromHexJson(file.text), theme);
            if (string.IsNullOrEmpty(theme.name)) theme.name = Path.GetFileName(path);
            return theme;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GraphKit] 主題檔解析失敗：{path}，節點圖改用預設主題。{ex.Message}");
            return new HGTheme();
        }
    }

    private static string ReadDisplayName(string path)
    {
        string fallback = Path.GetFileName(path);
        fallback = fallback.Substring(0, fallback.Length - FileSuffix.Length);
        try
        {
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (file == null) return fallback;
            var probe = new NameProbe();
            JsonUtility.FromJsonOverwrite(file.text, probe);
            return string.IsNullOrEmpty(probe.name) ? fallback : probe.name;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GraphKit] 主題檔讀不到名稱：{path}，以檔名顯示。{ex.Message}");
            return fallback;
        }
    }

    [Serializable]
    private class NameProbe { public string name; }

    private static readonly Regex HexValue = new("\"#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\"");
    private static readonly Regex ColorObject = new(
        @"\{\s*""r""\s*:\s*([-0-9.eE]+)\s*,\s*""g""\s*:\s*([-0-9.eE]+)\s*,\s*""b""\s*:\s*([-0-9.eE]+)\s*,\s*""a""\s*:\s*([-0-9.eE]+)\s*\}");

    // JsonUtility 只認 {"r":..} 物件；先把 "#hex" 換成物件，作者才能用 hex 寫主題。
    private static string FromHexJson(string json) => HexValue.Replace(json, m =>
    {
        ColorUtility.TryParseHtmlString("#" + m.Groups[1].Value, out var c);
        return string.Format(CultureInfo.InvariantCulture, "{{\"r\":{0},\"g\":{1},\"b\":{2},\"a\":{3}}}", c.r, c.g, c.b, c.a);
    });

    private static string ToHexJson(string json) => ColorObject.Replace(json, m =>
    {
        var c = new Color(Parse(m.Groups[1].Value), Parse(m.Groups[2].Value), Parse(m.Groups[3].Value), Parse(m.Groups[4].Value));
        return c.a >= 1f ? $"\"#{ColorUtility.ToHtmlStringRGB(c)}\"" : $"\"#{ColorUtility.ToHtmlStringRGBA(c)}\"";
    });

    private static float Parse(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>主題 JSON 存檔後自動重讀，改顏色時不必手動重新整理。</summary>
internal sealed class HGThemePostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        string guid = HGTheme.CurrentGuid;
        if (string.IsNullOrEmpty(guid)) return;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return;
        if (Array.IndexOf(imported, path) < 0 && Array.IndexOf(deleted, path) < 0) return;
        HGTheme.Reload();
    }
}

}
