namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class HGAssetEntry
{
    public ScriptableObject Asset;
    public string Name;
    public string TypeName;
    public string Path;
    public bool IsAction;
    public Type ResultType;
}

/// <summary>固定資產資料夾內的共用公式／動作資產快取。</summary>
[InitializeOnLoad]
public static class HGAssetIndex
{
    private static List<HGAssetEntry> cache;

    static HGAssetIndex() => EditorApplication.projectChanged += Invalidate;

    public static List<HGAssetEntry> Entries => cache ??= Scan();

    public static void Refresh() => cache = Scan();

    private static void Invalidate() => cache = null;

    // GUID 排列是本機的庫視圖偏好，不改共用資產本體，也不加入圖的 Undo。
    private static string OrderKey => "HaruGraph.AssetLib.Order." + Application.dataPath + ":" + HGAssetStore.Folder;

    public static bool Move(ScriptableObject asset, ScriptableObject target)
    {
        var entries = Entries;
        int from = entries.FindIndex(candidate => candidate.Asset == asset);
        int to = entries.FindIndex(candidate => candidate.Asset == target);
        if (asset == null || target == null || from < 0 || to < 0 || from == to) return false;

        var ordered = new List<HGAssetEntry>(entries);
        var entry = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, entry);
        var guids = new List<string>(ordered.Count);
        foreach (var item in ordered)
            guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item.Asset)));
        try { EditorPrefs.SetString(OrderKey, string.Join("\n", guids)); }
        catch (Exception exception)
        {
            Debug.LogError($"[GraphKit] 儲存資產庫排序失敗（{HGAssetStore.Folder}），保留原排列：{exception}");
            return false;
        }
        entries.Clear();
        entries.AddRange(ordered);
        return true;
    }

    private static void ApplyOrder(List<HGAssetEntry> entries)
    {
        string saved = EditorPrefs.GetString(OrderKey, "");
        if (string.IsNullOrEmpty(saved)) return;

        var byGuid = new Dictionary<string, HGAssetEntry>();
        foreach (var entry in entries)
            byGuid[AssetDatabase.AssetPathToGUID(entry.Path)] = entry;

        var ordered = new List<HGAssetEntry>(entries.Count);
        var used = new HashSet<HGAssetEntry>();
        foreach (string guid in saved.Split('\n'))
            if (byGuid.TryGetValue(guid, out var entry) && used.Add(entry)) ordered.Add(entry);
        // 新資產按預設排序接在末端；刪除與暫時不在資料夾內的 GUID 不建立空列。
        foreach (var entry in entries)
            if (used.Add(entry)) ordered.Add(entry);
        entries.Clear();
        entries.AddRange(ordered);
    }

    private static List<HGAssetEntry> Scan()
    {
        var result = new List<HGAssetEntry>();
        if (!AssetDatabase.IsValidFolder(HGAssetStore.Folder)) return result;

        var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { HGAssetStore.Folder });
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids.Length > 400 && i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "掃描共用資產", $"{i + 1}/{guids.Length}", (float)i / Mathf.Max(1, guids.Length)))
                    break;

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                // 判定只走 Contracts 的兩個介面，不認任何具體資產基底：編輯器不知道使用端有哪幾種圖資產。
                bool isGraphAsset = type != null && typeof(IGraphAsset).IsAssignableFrom(type);
                bool isAction = isGraphAsset && typeof(IActionGraphAsset).IsAssignableFrom(type);
                if (!isGraphAsset) continue;

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;
                result.Add(new HGAssetEntry
                {
                    Asset = asset,
                    Name = asset.name,
                    TypeName = type.Name,
                    Path = path,
                    IsAction = isAction,
                    ResultType = HGReflect.AssetResultType(asset),
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        result.Sort((a, b) =>
        {
            int kind = a.IsAction.CompareTo(b.IsAction);
            if (kind != 0) return kind;
            int type = string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal);
            return type != 0 ? type : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
        ApplyOrder(result);
        return result;
    }

}
}
