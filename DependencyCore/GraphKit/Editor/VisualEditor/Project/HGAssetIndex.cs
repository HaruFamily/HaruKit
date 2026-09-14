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
        return result;
    }

}
}
