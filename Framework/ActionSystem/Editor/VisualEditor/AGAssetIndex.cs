namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AGAssetEntry
{
    public ScriptableObject Asset;
    public string Name;
    public string TypeName;
    public string Path;
    public bool IsAction;
    public Type ResultType;
}

/// <summary>固定資產資料夾內的 ActionSystem 共用公式／動作資產快取。</summary>
[InitializeOnLoad]
public static class AGAssetIndex
{
    private static List<AGAssetEntry> cache;

    static AGAssetIndex() => EditorApplication.projectChanged += Invalidate;

    public static List<AGAssetEntry> Entries => cache ??= Scan();

    public static void Refresh() => cache = Scan();

    private static void Invalidate() => cache = null;

    private static List<AGAssetEntry> Scan()
    {
        var result = new List<AGAssetEntry>();
        if (!AssetDatabase.IsValidFolder(AGAssetStore.Folder)) return result;

        var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { AGAssetStore.Folder });
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids.Length > 400 && i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "掃描 ActionSystem 資產", $"{i + 1}/{guids.Length}", (float)i / Mathf.Max(1, guids.Length)))
                    break;

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                bool isFormula = type != null && typeof(FormulaAssetBase).IsAssignableFrom(type);
                bool isAction = IsActionAssetType(type);
                if (!isFormula && !isAction) continue;

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;
                result.Add(new AGAssetEntry
                {
                    Asset = asset,
                    Name = asset.name,
                    TypeName = type.Name,
                    Path = path,
                    IsAction = isAction,
                    ResultType = AGReflect.AssetResultType(asset),
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

    private static bool IsActionAssetType(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ActionAssetBase<>)) return true;
        return false;
    }
}
}
