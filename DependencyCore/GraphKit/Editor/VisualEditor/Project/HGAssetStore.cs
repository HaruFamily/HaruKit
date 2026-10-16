namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 共用資產的存放位置與命名規則。
///
/// 位置由使用端專案決定，不由套件寫死：套件若硬指一個 Assets 底下的路徑，等於替每個安裝它的專案
/// 決定資料夾配置。使用者從資產庫選擇資料夾，選擇存在 EditorPrefs（key 綁專案，換專案不互相污染）。
/// </summary>
public static class HGAssetStore
{
    private static readonly string PrefKey = $"HaruGraph.AssetFolder.{Application.dataPath.GetHashCode():X8}";

    /// <summary>共用資產資料夾（"Assets/..." 相對路徑）。未設定或已被刪除時回傳空字串。</summary>
    public static string Folder
    {
        get
        {
            string value = EditorPrefs.GetString(PrefKey, string.Empty);
            return AssetDatabase.IsValidFolder(value) ? value : string.Empty;
        }
        set => EditorPrefs.SetString(PrefKey, value ?? string.Empty);
    }

    /// <summary>取得新資產的唯一路徑。資料夾未設定時直接失敗，不在這裡問——見 <see cref="TryPickFolder"/>。</summary>
    public static bool TryGetUniquePath(string assetName, out string path)
    {
        path = null;

        // 抽出當下不彈選擇器：使用者那一刻在想的是節點，不是專案的資料夾配置，
        // 中途插一個 modal 會把操作打斷，而且選錯了還得再抽一次才發現。
        // 落點是開工前就該決定好的設定，只有資產庫那顆「資料夾」鈕能改。
        string folder = Folder;
        if (string.IsNullOrEmpty(folder)) return false;

        string fileName = SanitizeFileName(assetName);
        path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
        return true;
    }

    /// <summary>開資料夾選擇器讓使用者指定存放位置，成功時寫回 EditorPrefs。</summary>
    public static bool TryPickFolder(out string folder, System.Action<string> onError = null)
    {
        folder = null;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string current = Folder;
        string startFolder = string.IsNullOrEmpty(current) ? Application.dataPath : Path.Combine(projectRoot, current);
        string picked = EditorUtility.OpenFolderPanel("選擇共用資產存放資料夾", startFolder, string.Empty);
        if (string.IsNullOrEmpty(picked)) return false;

        // AssetDatabase 只認 "Assets/" 開頭的相對路徑；選到專案外的資料夾一律擋掉。
        string assetsRoot = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        picked = Path.GetFullPath(picked).Replace('\\', '/').TrimEnd('/');
        var comparison = Application.platform == RuntimePlatform.WindowsEditor
            ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
        if (!string.Equals(picked, assetsRoot, comparison) && !picked.StartsWith(assetsRoot + "/", comparison))
        {
            return FolderSelectionFailed($"請選擇目前專案 Assets 內的資料夾：{picked}", onError);
        }

        if (!Directory.Exists(picked))
            return FolderSelectionFailed($"選擇的資料夾已不存在：{picked}", onError);

        string relative = "Assets" + picked.Substring(assetsRoot.Length);
        // 原生選擇器中剛建立的資料夾可能尚未匯入；只在需要時同步刷新，再驗證登記結果。
        if (!AssetDatabase.IsValidFolder(relative))
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch (System.Exception exception)
            {
                return FolderSelectionFailed($"匯入資產庫資料夾失敗：{relative}\n{exception.Message}", onError);
            }
        }
        if (!AssetDatabase.IsValidFolder(relative))
        {
            return FolderSelectionFailed($"Unity 尚未識別資料夾：{relative}\n請查看 Console 的匯入錯誤，修正後重新選擇。", onError);
        }

        Folder = relative;
        folder = relative;
        return true;
    }

    private static bool FolderSelectionFailed(string message, System.Action<string> onError)
    {
        Debug.LogError($"[GraphKit] {message}\n資產庫設定未變更。");
        onError?.Invoke(message);
        return false;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "GraphAsset";

        var chars = value.Trim().ToCharArray();
        const string invalid = "/\\:*?\"<>|";
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i]) || invalid.IndexOf(chars[i]) >= 0) chars[i] = '_';

        string result = new string(chars).Trim('.', ' ');
        return string.IsNullOrEmpty(result) ? "GraphAsset" : result;
    }
}
}
