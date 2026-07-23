namespace PinPlugin.ActionSystem
{
#if UNITY_EDITOR
using System.IO;
using UnityEditor;

internal static class ActionSystemSavePathPrefs
{
    private const string PrefKey = "ActionSystem.LastAssetSaveDir";

    public static string GetInitialDir()
    {
        var pref = EditorPrefs.GetString(PrefKey, "");
        if (!string.IsNullOrEmpty(pref) && AssetDatabase.IsValidFolder(pref)) return pref;

        var sel = Selection.activeObject;
        if (sel != null)
        {
            var p = AssetDatabase.GetAssetPath(sel);
            if (!string.IsNullOrEmpty(p))
            {
                var dir = Path.GetDirectoryName(p)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && AssetDatabase.IsValidFolder(dir)) return dir;
            }
        }
        return "Assets";
    }

    public static void RememberDir(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return;
        var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir)) EditorPrefs.SetString(PrefKey, dir);
    }
}
#endif

}
