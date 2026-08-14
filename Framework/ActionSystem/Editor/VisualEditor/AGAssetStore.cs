namespace PinPlugin.ActionSystem.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>ActionSystem 共用資產的固定儲存位置與命名規則。</summary>
public static class AGAssetStore
{
    public const string Folder = "Assets/ActionSystemAssets";

    public static bool TryGetUniquePath(string assetName, out string path)
    {
        path = null;
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            string guid = AssetDatabase.CreateFolder("Assets", "ActionSystemAssets");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[ActionGraph] 無法建立共用資產資料夾：{Folder}");
                return false;
            }
        }

        string fileName = SanitizeFileName(assetName);
        path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{fileName}.asset");
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
