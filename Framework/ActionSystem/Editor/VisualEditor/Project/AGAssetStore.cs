namespace HaruFamily.Framework.ActionSystem.Editor
{
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ActionSystem 共用資產的存放位置與命名規則。
///
/// 位置由使用端專案決定，不由套件寫死：套件若硬指一個 Assets 底下的路徑，等於替每個安裝它的專案
/// 決定資料夾配置，而且刪掉那個資料夾後下次抽出又會自己長回來。這裡改成第一次抽出時問使用者，
/// 選擇存在 EditorPrefs（key 綁專案，換專案不互相污染）。
/// </summary>
public static class AGAssetStore
{
    private static readonly string PrefKey = $"ActionGraph.AssetFolder.{Application.dataPath.GetHashCode():X8}";

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

    /// <summary>取得新資產的唯一路徑；資料夾未設定時開資料夾選擇器問一次。使用者取消則回傳 false。</summary>
    public static bool TryGetUniquePath(string assetName, out string path)
    {
        path = null;

        string folder = Folder;
        if (string.IsNullOrEmpty(folder) && !TryPickFolder(out folder)) return false;

        string fileName = SanitizeFileName(assetName);
        path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
        return true;
    }

    /// <summary>開資料夾選擇器讓使用者指定存放位置，成功時寫回 EditorPrefs。</summary>
    public static bool TryPickFolder(out string folder)
    {
        folder = null;

        string picked = EditorUtility.OpenFolderPanel("選擇 ActionSystem 共用資產存放資料夾", Application.dataPath, string.Empty);
        if (string.IsNullOrEmpty(picked)) return false;

        // AssetDatabase 只認 "Assets/" 開頭的相對路徑；選到專案外的資料夾一律擋掉。
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        picked = picked.Replace('\\', '/');
        if (!picked.StartsWith($"{projectRoot}/Assets", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"[ActionGraph] 共用資產資料夾必須在 Assets 底下：{picked}");
            return false;
        }

        string relative = picked.Substring(projectRoot.Length + 1);
        if (!AssetDatabase.IsValidFolder(relative))
        {
            Debug.LogError($"[ActionGraph] 不是有效的專案資料夾：{relative}");
            return false;
        }

        Folder = relative;
        folder = relative;
        return true;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "ActionSystemAsset";

        var chars = value.Trim().ToCharArray();
        const string invalid = "/\\:*?\"<>|";
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i]) || invalid.IndexOf(chars[i]) >= 0) chars[i] = '_';

        string result = new string(chars).Trim('.', ' ');
        return string.IsNullOrEmpty(result) ? "ActionSystemAsset" : result;
    }
}
}
