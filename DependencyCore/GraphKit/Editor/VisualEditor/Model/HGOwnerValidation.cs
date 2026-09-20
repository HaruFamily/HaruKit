namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>依既有文件欄位探索驗證 Owner；內容類別不需轉發 IGraphOwner。</summary>
public static class HGOwnerValidation
{
    public static IEnumerable<FieldInfo> DocumentFields(Type ownerType)
    {
        if (ownerType == null) yield break;
        foreach (var field in HGReflect.Fields(ownerType))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false)
                && !field.IsDefined(typeof(SerializeReference), false)) continue;
            if (typeof(IGraphDocument).IsAssignableFrom(field.FieldType)) yield return field;
        }
    }

    public static bool HasDocuments(Type ownerType)
    {
        foreach (var field in DocumentFields(ownerType)) return true;
        return false;
    }

    public static bool CanVerify(Object owner) => owner != null && (owner is IGraphOwner || HasDocuments(owner.GetType()));

    public static bool IsValidated(Object owner)
    {
        if (owner == null) return false;
        try
        {
            if (owner is IGraphOwner legacy) return legacy.IsGraphValidated();
            bool found = false;
            foreach (var field in DocumentFields(owner.GetType()))
            {
                found = true;
                if (field.GetValue(owner) is not IGraphDocument document || !document.IsValidated) return false;
            }
            return found;
        }
        catch (Exception) { return false; }
    }

    /// <summary>驗證傳入文件；可以是工作副本，不會轉而驗證 Owner 上另一份文件。</summary>
    public static void VerifyDocument(IGraphDocument document, Object owner)
    {
        if (document is IGraphDocumentValidation validation) validation.Verify(owner);
        else document.Verify();
    }

    /// <summary>Inspector 指定的單一欄位；null 文件只在這個明確操作中建立。</summary>
    public static bool VerifyField(Object owner, FieldInfo field, bool markDirty = false)
    {
        if (owner == null || field == null || field.IsStatic
            || !typeof(IGraphDocument).IsAssignableFrom(field.FieldType)) return false;
        IGraphDocument document = null;
        bool created = false;
        bool was = false;
        try
        {
            document = field.GetValue(owner) as IGraphDocument;
            if (document == null)
            {
                document = Activator.CreateInstance(field.FieldType) as IGraphDocument;
                if (document == null) return false;
                field.SetValue(owner, document);
                created = true;
            }
            was = document.IsValidated;
            if (markDirty) document.MarkDirty();
            VerifyDocument(document, owner);
            return document.IsValidated;
        }
        catch (Exception exception)
        {
            document?.MarkDirty();
            Debug.LogError($"[GraphKit] '{owner.name}.{field.Name}' 驗證失敗：{exception.Message}", owner);
            return false;
        }
        finally
        {
            if (created || document != null && was != document.IsValidated) MarkOwnerDirty(owner);
        }
    }

    /// <summary>全部文件重驗；保留既有 IGraphOwner 的自訂整合行為。</summary>
    public static bool Verify(Object owner, bool markDirty = false)
        => Verify(owner, out _, markDirty);

    /// <summary>changed 依逐文件的狀態變更計算，不以 Owner 的彙總 bool 推測。</summary>
    public static bool Verify(Object owner, out bool changed, bool markDirty = false)
    {
        changed = false;
        if (owner == null) return false;
        if (owner is IGraphOwner legacy)
        {
            bool was = legacy.IsGraphValidated();
            if (markDirty) legacy.MarkGraphDirty();
            legacy.VerifyGraph();
            bool now = legacy.IsGraphValidated();
            changed = was != now;
            if (changed) MarkOwnerDirty(owner);
            return now;
        }

        bool found = false, valid = true;
        foreach (var field in DocumentFields(owner.GetType()))
        {
            found = true;
            // 批次驗證不初始化未配置的文件。
            if (field.GetValue(owner) is not IGraphDocument document) { valid = false; continue; }
            bool was = document.IsValidated;
            bool now = VerifyField(owner, field, markDirty);
            if (!now) valid = false;
            if (was != document.IsValidated) changed = true;
        }
        return found && valid;
    }

    private static void MarkOwnerDirty(Object owner)
    {
        EditorUtility.SetDirty(owner);
        if (owner is Component component && component.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
    }
}
}
