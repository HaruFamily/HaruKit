namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>一個可編輯對象（含 ActionSystem 欄位的 SO）。</summary>
public class AGOwnerEntry
{
    public ScriptableObject Owner;
    public string Name;
    public string TypeName;
    public string Path;
}

/// <summary>
/// 專案中所有「支援的類型」索引。用 GetMainAssetTypeAtPath 先過濾型別，只載入真的有 ActionSystem 欄位的資產。
/// </summary>
public static class AGOwnerIndex
{
    private static List<AGOwnerEntry> cache;

    public static List<AGOwnerEntry> Entries => cache ??= Scan();

    public static bool HasCache => cache != null;

    public static void Refresh() => cache = Scan();

    /// <summary>下次取用時才重掃。專案內容變動時由 <see cref="AGReferenceIndex"/> 呼叫。</summary>
    public static void Invalidate() => cache = null;

    private static List<AGOwnerEntry> Scan()
    {
        var result = new List<AGOwnerEntry>();
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                // 小專案掃很快，進度條反而閃一下礙眼；資產多才顯示。
                if (guids.Length > 400 && i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "掃描可編輯對象", $"{i + 1}/{guids.Length}", (float)i / Mathf.Max(1, guids.Length)))
                    break;

                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                // 先看型別再決定要不要載入：整個專案的 SO 全載一次太慢。
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type == null || AGModel.FindSystemField(type) == null) continue;

                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null) continue;

                result.Add(new AGOwnerEntry
                {
                    Owner = so,
                    Name = so.name,
                    TypeName = type.Name,
                    Path = path,
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        result.Sort((a, b) =>
        {
            int c = string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
        return result;
    }

    /// <summary>開啟「選擇編輯對象」下拉（依型別分組，內建搜尋）。</summary>
    public static void ShowPicker(Rect rect, Action<ScriptableObject> onPick)
    {
        var dropdown = new AGOwnerDropdown(new AdvancedDropdownState(), Entries, onPick);
        dropdown.Show(rect);
    }
}

/// <summary>可編輯對象的選擇下拉：資料夾＝型別，項目＝資產名稱。</summary>
public class AGOwnerDropdown : AdvancedDropdown
{
    private class OwnerItem : AdvancedDropdownItem
    {
        public readonly ScriptableObject Owner;
        public OwnerItem(string name, ScriptableObject owner) : base(name) => Owner = owner;
    }

    private readonly List<AGOwnerEntry> entries;
    private readonly Action<ScriptableObject> onPick;

    public AGOwnerDropdown(AdvancedDropdownState state, List<AGOwnerEntry> entries, Action<ScriptableObject> onPick)
        : base(state)
    {
        this.entries = entries;
        this.onPick = onPick;
        minimumSize = new Vector2(300f, 360f);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem("選擇編輯對象");
        if (entries.Count == 0)
        {
            root.AddChild(new AdvancedDropdownItem("（專案裡找不到含 ActionSystem 的資產）"));
            return root;
        }

        var folders = new Dictionary<string, AdvancedDropdownItem>();
        foreach (var e in entries)
        {
            if (!folders.TryGetValue(e.TypeName, out var folder))
            {
                folder = new AdvancedDropdownItem(e.TypeName);
                folders[e.TypeName] = folder;
                root.AddChild(folder);
            }
            folder.AddChild(new OwnerItem(e.Name, e.Owner));
        }
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is OwnerItem oi && oi.Owner != null) onPick?.Invoke(oi.Owner);
    }
}

}
