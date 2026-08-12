namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>可建立的節點型別清單，依 ActionSystem [ActionNode] 的分類分組。</summary>
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
}

/// <summary>型別選擇下拉：分類為資料夾，選項名稱用 [ActionNode.Name]。</summary>
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
