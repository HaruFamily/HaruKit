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
    public static T Copy<T>(T source) where T : class => Copy(source, null);

    /// <summary>
    /// 深複製，但 <paramref name="shared"/> 裡的物件原樣沿用、不跟著複製。
    /// 複製整張圖時不需要它；**複製圖裡的一小塊時需要**：那一塊裡指向具名變數的節點應該還是指向
    /// 同一個變數，跟著抄一份就會變成不在清單裡的孤兒端點——參照得到、卻永遠查不到值。
    /// </summary>
    public static T Copy<T>(T source, IEnumerable<object> shared) where T : class
    {
        var copied = new Dictionary<object, object>(ReferenceComparer.Instance);
        if (shared != null)
            foreach (var item in shared)
                if (item != null) copied[item] = item;

        try { return CopyObject(source, copied) as T; }
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
