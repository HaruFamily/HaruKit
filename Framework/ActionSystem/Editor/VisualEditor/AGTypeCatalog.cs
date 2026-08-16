namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>可建立的節點型別清單，依 ActionSystem [ASNode] 的分組整理。</summary>
public static class AGTypeCatalog
{
    private static readonly Dictionary<Type, List<Type>> cache = new();

    /// <summary>某個 base 底下所有可實例化的具體型別。</summary>
    public static List<Type> Concrete(Type baseType)
    {
        if (baseType == null) return new List<Type>();
        if (cache.TryGetValue(baseType, out var cached)) return cached;

        var list = new List<Type>();
        foreach (var t in TypeCache.GetTypesDerivedFrom(baseType))
        {
            if (!Usable(t, baseType)) continue;
            list.Add(t);
        }

        // TypeCache 對「封閉泛型 base」不保證有結果；掃一次已載入組件補齊。
        if (list.Count == 0)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (Exception) { continue; }
                foreach (var t in types)
                    if (Usable(t, baseType)) list.Add(t);
            }
        }
        list.Sort((a, b) =>
        {
            int c = string.Compare(AGReflect.TypeCategory(a), AGReflect.TypeCategory(b), StringComparison.Ordinal);
            if (c != 0) return c;
            c = AGReflect.TypePriority(a).CompareTo(AGReflect.TypePriority(b));
            return c != 0 ? c : string.Compare(AGReflect.TypeName(a), AGReflect.TypeName(b), StringComparison.Ordinal);
        });
        cache[baseType] = list;
        return list;
    }

    private static bool Usable(Type t, Type baseType)
    {
        if (t == null || t.IsAbstract || t.IsGenericTypeDefinition) return false;
        if (!baseType.IsAssignableFrom(t)) return false;
        return t.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>開啟型別選擇下拉（內建關鍵字搜尋 + 分類分組）。</summary>
    public static void ShowPicker(Rect rect, Type baseType, string title, Action<Type> onPick)
    {
        var types = Concrete(baseType);
        if (types.Count == 0)
        {
            Debug.LogWarning($"[ActionGraph] 找不到 {baseType?.Name} 的可用型別。");
            return;
        }
        var dropdown = new AGTypeDropdown(new AdvancedDropdownState(), types, title, onPick);
        dropdown.Show(rect);
    }

    public static void ShowSourcePicker(Rect rect, List<AGSourceOption> options, string title = "變更來源")
    {
        if (options == null || options.Count == 0)
        {
            Debug.LogWarning("[ActionGraph] 找不到可用的 Node 來源。");
            return;
        }
        new AGSourceDropdown(new AdvancedDropdownState(), options, title).Show(rect);
    }
}

public class AGSourceOption
{
    public string Group;
    public string Name;
    public bool IsCurrent;
    public Action Apply;
}

/// <summary>統一選擇 inline Node、Token 與 Asset 的搜尋下拉。</summary>
public class AGSourceDropdown : AdvancedDropdown
{
    private class SourceItem : AdvancedDropdownItem
    {
        public readonly AGSourceOption Option;
        public SourceItem(AGSourceOption option) : base(option.IsCurrent ? "✓ " + option.Name : option.Name)
            => Option = option;
    }

    private readonly List<AGSourceOption> options;
    private readonly string title;

    public AGSourceDropdown(AdvancedDropdownState state, List<AGSourceOption> options, string title) : base(state)
    {
        this.options = options;
        this.title = title;
        minimumSize = new Vector2(280f, 360f);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem(title);
        var folders = new Dictionary<string, AdvancedDropdownItem>();
        foreach (var option in options)
        {
            var parent = root;
            string path = "";
            // 沒有分組的選項直接掛在根，不要為它生一個空資料夾。
            foreach (string part in (option.Group ?? "").Split('/'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                path = string.IsNullOrEmpty(path) ? part : path + "/" + part;
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new AdvancedDropdownItem(part);
                    folders[path] = folder;
                    parent.AddChild(folder);
                }
                parent = folder;
            }
            parent.AddChild(new SourceItem(option));
        }
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is SourceItem source) source.Option.Apply?.Invoke();
    }
}

/// <summary>型別選擇下拉：分組為資料夾，選項名稱用 [ASNode.Name]。</summary>
public class AGTypeDropdown : AdvancedDropdown
{
    private class TypeItem : AdvancedDropdownItem
    {
        public readonly Type Type;
        public TypeItem(string name, Type type) : base(name) => Type = type;
    }

    private readonly List<Type> types;
    private readonly string title;
    private readonly Action<Type> onPick;

    public AGTypeDropdown(AdvancedDropdownState state, List<Type> types, string title, Action<Type> onPick) : base(state)
    {
        this.types = types;
        this.title = title;
        this.onPick = onPick;
        minimumSize = new Vector2(260f, 320f);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem(title);
        var folders = new Dictionary<string, AdvancedDropdownItem>();

        foreach (var t in types)
        {
            string category = AGReflect.TypeCategory(t);
            if (!folders.TryGetValue(category, out var folder))
            {
                folder = new AdvancedDropdownItem(category);
                folders[category] = folder;
                root.AddChild(folder);
            }
            folder.AddChild(new TypeItem(AGReflect.TypeName(t), t));
        }
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is TypeItem ti) onPick?.Invoke(ti.Type);
    }
}

}
