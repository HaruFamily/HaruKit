namespace HaruFamily.Framework.LogicGraph.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class LGAssetEntry
{
    public ScriptableObject Asset;
    public string Name;
    public string TypeName;
    public string Path;
    public bool IsAction;
    public Type ResultType;
}

/// <summary>固定資產資料夾內的 LogicGraph 共用公式／動作資產快取。</summary>
[InitializeOnLoad]
public static class LGAssetIndex
{
    private static List<LGAssetEntry> cache;

    static LGAssetIndex() => EditorApplication.projectChanged += Invalidate;

    public static List<LGAssetEntry> Entries => cache ??= Scan();

    public static void Refresh() => cache = Scan();

    private static void Invalidate() => cache = null;

    private static List<LGAssetEntry> Scan()
    {
        var result = new List<LGAssetEntry>();
        if (!AssetDatabase.IsValidFolder(LGAssetStore.Folder)) return result;

        var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { LGAssetStore.Folder });
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids.Length > 400 && i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "掃描 LogicGraph 資產", $"{i + 1}/{guids.Length}", (float)i / Mathf.Max(1, guids.Length)))
                    break;

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                bool isFormula = type != null && typeof(FormulaAssetBase).IsAssignableFrom(type);
                bool isAction = IsActionAssetType(type);
                if (!isFormula && !isAction) continue;

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;
                result.Add(new LGAssetEntry
                {
                    Asset = asset,
                    Name = asset.name,
                    TypeName = type.Name,
                    Path = path,
                    IsAction = isAction,
                    ResultType = LGReflect.AssetResultType(asset),
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
