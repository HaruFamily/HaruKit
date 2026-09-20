namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>一個可編輯對象（含 LogicGraph 欄位的 SO）。</summary>
public class HGOwnerEntry
{
    public ScriptableObject Owner;
    public string Name;
    public string TypeName;
    public string Path;
}

/// <summary>
/// 專案中所有「支援的類型」索引。用 GetMainAssetTypeAtPath 先過濾型別，只載入真的有 LogicGraph 欄位的資產。
/// </summary>
public static class HGOwnerIndex
{
    private static List<HGOwnerEntry> cache;
    private static List<HGOwnerEntry> allCache;

    // 選擇器只列能隱式開啟的單文件 Owner；引用索引也包含須指定 binding 的多文件 Owner。
    internal static List<HGOwnerEntry> AllEntries => allCache ??= Scan();
    public static List<HGOwnerEntry> Entries => cache ??= AllEntries.FindAll(entry => HGModel.CanEdit(entry.Owner));

    public static bool HasCache => cache != null;

    public static void Refresh() { allCache = Scan(); cache = null; }

    /// <summary>下次取用時才重掃。專案內容變動時由 <see cref="HGReferenceIndex"/> 呼叫。</summary>
    public static void Invalidate() { cache = null; allCache = null; }

    private static List<HGOwnerEntry> Scan()
    {
        var result = new List<HGOwnerEntry>();
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
                if (type == null || !HGOwnerValidation.HasDocuments(type)) continue;

                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null) continue;

                result.Add(new HGOwnerEntry
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
        var dropdown = new HGOwnerDropdown(new AdvancedDropdownState(), Entries, onPick);
        dropdown.Show(rect);
    }
}

/// <summary>可編輯對象的選擇下拉：資料夾＝型別，項目＝資產名稱。</summary>
public class HGOwnerDropdown : AdvancedDropdown
{
    private class OwnerItem : AdvancedDropdownItem
    {
        public readonly ScriptableObject Owner;
        public OwnerItem(string name, ScriptableObject owner) : base(name) => Owner = owner;
    }

    private readonly List<HGOwnerEntry> entries;
    private readonly Action<ScriptableObject> onPick;

    public HGOwnerDropdown(AdvancedDropdownState state, List<HGOwnerEntry> entries, Action<ScriptableObject> onPick)
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
            root.AddChild(new AdvancedDropdownItem("（專案裡找不到含節點圖的資產）"));
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
