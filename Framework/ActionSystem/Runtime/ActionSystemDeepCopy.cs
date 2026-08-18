namespace PinPlugin.ActionSystem
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>ActionSystem 序列化圖的反射深複製：保留 Unity 物件參考、共享參考與循環。</summary>
public static class ActionSystemDeepCopy
{
    public static T Copy<T>(T source) where T : class
    {
        try { return CopyObject(source, new Dictionary<object, object>(ReferenceComparer.Instance)) as T; }
        catch (Exception e)
        {
            Debug.LogError($"[ActionSystem] 深複製失敗：{e.Message}");
            return null;
        }
    }

    private static object CopyObject(object source, Dictionary<object, object> copied)
    {
        if (source == null) return null;
        var type = source.GetType();
        if (type.IsValueType || type == typeof(string) || type == typeof(Type)) return source;
        if (source is UnityEngine.Object) return source;
        if (copied.TryGetValue(source, out var existing)) return existing;

        if (type.IsArray)
        {
            var sourceArray = (Array)source;
            var copyArray = Array.CreateInstance(type.GetElementType(), sourceArray.Length);
            copied[source] = copyArray;
            for (int i = 0; i < sourceArray.Length; i++) copyArray.SetValue(CopyObject(sourceArray.GetValue(i), copied), i);
            return copyArray;
        }

        if (source is IList sourceList)
        {
            var copyList = Create(type);
            if (copyList is not IList list) throw new InvalidOperationException($"{type.Name} 不是可建立的 IList。");
            copied[source] = list;
            foreach (var item in sourceList) list.Add(CopyObject(item, copied));
            return list;
        }

        if (source is IDictionary sourceDictionary)
        {
            var copyDictionary = Create(type);
            if (copyDictionary is not IDictionary dictionary) throw new InvalidOperationException($"{type.Name} 不是可建立的 IDictionary。");
            copied[source] = dictionary;
            foreach (DictionaryEntry pair in sourceDictionary)
                dictionary.Add(CopyObject(pair.Key, copied), CopyObject(pair.Value, copied));
            return dictionary;
        }

        var copy = Create(type);
        if (copy == null) throw new InvalidOperationException($"無法建立 {type.Name}。");
        copied[source] = copy;
        foreach (var field in Fields(type))
            field.SetValue(copy, CopyObject(field.GetValue(source), copied));
        return copy;
    }

    private static object Create(Type type)
    {
        try { return Activator.CreateInstance(type, true); }
        catch
        {
            try { return FormatterServices.GetUninitializedObject(type); }
            catch (Exception e)
            {
                Debug.LogError($"[ActionSystem] 無法複製 {type.Name}：{e.Message}");
                return null;
            }
        }
    }

    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized && !field.IsInitOnly) yield return field;
    }
}
}
