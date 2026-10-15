namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>候選來源的編輯期索引。只快取成員，不快取來源值；每次開啟選單重新取得完整快照。</summary>
public static class HGOptionsCatalog
{
    public sealed class Entry
    {
        public object Value { get; }
        public string Label { get; }
        public string Group { get; }
        public string Source { get; }
        public string Tooltip { get; }

        internal Entry(object value, string group, string source, string label = null, string tooltip = null)
        {
            Value = value;
            Label = string.IsNullOrWhiteSpace(label) ? Display(value) : label;
            Group = group ?? "";
            Source = source;
            Tooltip = tooltip ?? "";
        }
    }

    private static List<MemberInfo> sources;

    public static string Display(object value)
        => value is UnityEngine.Object asset ? (asset != null ? asset.name : "<Missing>")
            : value?.ToString() ?? "<null>";

    /// <summary>僅 List&lt;T&gt; 為多選，其餘支援的值型別為單選；Slot 不在本介面處理。</summary>
    public static bool TryValueType(Type fieldType, out Type valueType, out bool multiple)
    {
        multiple = fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>);
        valueType = multiple ? fieldType.GetGenericArguments()[0] : fieldType;
        return HGValueField.CanDraw(valueType) && (valueType.IsValueType || valueType == typeof(string)
            || typeof(UnityEngine.Object).IsAssignableFrom(valueType));
    }

    public static bool TryGet(string library, Type valueType, out List<Entry> entries, out string error)
    {
        entries = new List<Entry>();
        error = null;
        string sourceName = library;
        try
        {
            if (string.IsNullOrWhiteSpace(library)) throw new ArgumentException("候選庫 ID 不可為空。");
            sources ??= Discover();
            var seen = new HashSet<object>();
            Type libraryType = null;
            bool found = false;
            foreach (var member in sources)
            {
                var attribute = member.GetCustomAttribute<HGCatalogAttribute>();
                if (attribute.Library != library) continue;
                found = true;
                sourceName = member.DeclaringType.FullName + "." + member.Name;
                Type resultType = ResultType(member);
                Type elementType = EnumerableElement(resultType);
                if (elementType == null) throw new InvalidOperationException("來源必須宣告 IEnumerable<T> 或其實作型別。");
                bool described = elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(HGCatalogEntry<>);
                Type candidateType = described ? elementType.GetGenericArguments()[0] : elementType;
                if (libraryType != null && libraryType != candidateType)
                    throw new InvalidOperationException($"同庫型別衝突：{libraryType} / {candidateType}。");
                libraryType = candidateType;
                if (!valueType.IsAssignableFrom(candidateType))
                    throw new InvalidOperationException($"候選 {candidateType} 無法寫入 {valueType}。");
                if (Read(member) is not IEnumerable values) throw new InvalidOperationException("來源回傳 null。");
                // 先完成列舉再交給 UI；iterator 的例外亦屬來源失敗，不呈現半份候選。
                foreach (object item in values)
                {
                    if (item == null || !elementType.IsInstanceOfType(item)) throw new InvalidOperationException("候選值不符合宣告型別。");
                    var presentation = described ? (IHGCatalogEntry)item : null;
                    object value = described ? presentation.Value : item;
                    if (value == null || value is UnityEngine.Object asset && asset == null)
                        throw new InvalidOperationException("候選不可為 null 或 Missing 物件；空值由欄位 AllowEmpty 提供。");
                    if (!candidateType.IsInstanceOfType(value)) throw new InvalidOperationException("候選值不符合宣告型別。");
                    if (seen.Add(value)) entries.Add(new Entry(value, attribute.Group, sourceName, presentation?.Label, presentation?.Tooltip));
                }
            }
            if (!found) throw new InvalidOperationException($"找不到候選庫 '{library}'。");
            return true;
        }
        catch (Exception exception)
        {
            entries.Clear();
            error = $"{sourceName}: {(exception is TargetInvocationException invocation ? invocation.InnerException?.Message : exception.Message)}";
            return false;
        }
    }

    private static List<MemberInfo> Discover()
    {
        var result = new List<MemberInfo>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types; }
            foreach (var type in types)
            {
                if (type == null) continue;
                foreach (var member in type.GetMembers(flags))
                    if (member.IsDefined(typeof(HGCatalogAttribute), false)) result.Add(member);
            }
        }
        result.Sort((a, b) => StringComparer.Ordinal.Compare(
            a.DeclaringType.AssemblyQualifiedName + "." + a.Name, b.DeclaringType.AssemblyQualifiedName + "." + b.Name));
        return result;
    }

    private static Type ResultType(MemberInfo member)
    {
        if (member.DeclaringType.ContainsGenericParameters) throw new InvalidOperationException("來源不可位於未封閉泛型。");
        switch (member)
        {
            case FieldInfo field when field.IsStatic: return field.FieldType;
            case PropertyInfo property when property.GetGetMethod(true)?.IsStatic == true && property.GetIndexParameters().Length == 0:
                return property.PropertyType;
            case MethodInfo method when method.IsStatic && !method.ContainsGenericParameters && method.GetParameters().Length == 0:
                return method.ReturnType;
            default: throw new InvalidOperationException("來源必須是靜態欄位、可讀屬性或無參數方法。");
        }
    }

    private static object Read(MemberInfo member) => member switch
    {
        FieldInfo field => field.GetValue(null),
        PropertyInfo property => property.GetValue(null),
        MethodInfo method => method.Invoke(null, null),
        _ => throw new InvalidOperationException("不支援的來源成員。"),
    };

    private static Type EnumerableElement(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
        Type element = null;
        foreach (var contract in type.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IEnumerable<>)) continue;
            Type candidate = contract.GetGenericArguments()[0];
            if (element != null && element != candidate) throw new InvalidOperationException("來源實作多種 IEnumerable<T>，無法確定值型別。");
            element = candidate;
        }
        return element;
    }
}
}
