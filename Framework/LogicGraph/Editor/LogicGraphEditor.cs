namespace HaruFamily.Framework.LogicGraph.Editor
{
using System;
using System.Reflection;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>LogicGraph 的正式 Editor 入口；Inspector、選單與其他工具共用同一份 context。</summary>
[InitializeOnLoad]
public static class LogicGraphEditor
{
    // GraphKit 視窗在 Domain Reload 後靠這個解析器接回原文件；靜態登記每次重載都要重做。
    static LogicGraphEditor() => HaruGraphWindow.RegisterReloadResolver(ResolveAfterReload);

    public static HGEditorExtensionContext Context { get; } = new HGEditorExtensionContext(
        new DocumentProvider(), profile: new HGEditorProfile(
            HGCapabilities.SharedAssets | HGCapabilities.Tokens | HGCapabilities.Properties, HGModel.HGDocumentRootAdapter.Instance));

    public static bool HasGraph(Type ownerType)
    {
        foreach (var field in HGOwnerValidation.DocumentFields(ownerType))
            if (typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(field.FieldType)) return true;
        return false;
    }

    /// <summary>指定欄位的完整驗證，不要求 Owner 實作任何介面。</summary>
    public static bool Verify(UnityEngine.Object owner, FieldInfo field)
        => HGOwnerValidation.VerifyField(owner, field);

    /// <summary>程式建構與批次工具驗證 Owner 的全部文件。</summary>
    public static bool Verify(UnityEngine.Object owner) => HGOwnerValidation.Verify(owner);

    /// <summary>已知欄位時使用；多文件 Owner 不透過選取順序猜測文件。</summary>
    public static HGDocumentBinding CreateBinding(FieldInfo field)
    {
        if (field == null || field.IsStatic || !typeof(IGraphDocument).IsAssignableFrom(field.FieldType)
            || !typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(field.FieldType))
            throw new ArgumentException("欄位必須是 LogicGraph 文件。", nameof(field));

        return new HGDocumentBinding<IGraphDocument>($"LogicGraph.{field.DeclaringType?.FullName}.{field.Name}",
            owner => field.GetValue(owner) as IGraphDocument,
            (owner, document) => field.SetValue(owner, document),
            () => Activator.CreateInstance(field.FieldType) as IGraphDocument);
    }

    /// <summary>
    /// Domain Reload 後依文件 Id 找回原欄位並重建 binding。只認得 <see cref="CreateBinding"/> 產生的 Id；
    /// 呼叫端自訂的 binding 無法重建，回 false 由使用者從原入口重新開啟。
    /// </summary>
    private static bool ResolveAfterReload(UnityEngine.Object owner, string documentId,
        out HGDocumentBinding binding, out HGEditorExtensionContext context)
    {
        binding = null;
        context = null;
        if (owner == null) return false;

        FieldInfo match = null;
        if (documentId == null)
        {
            match = HGModel.FindSystemField(owner);
            if (match != null && !typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(match.FieldType)) match = null;
        }
        else
        {
            foreach (var field in HGOwnerValidation.DocumentFields(owner.GetType()))
            {
                if (!typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(field.FieldType)) continue;
                if ($"LogicGraph.{field.DeclaringType?.FullName}.{field.Name}" != documentId) continue;
                match = field;
                break;
            }
        }
        if (match == null) return false;

        binding = CreateBinding(match);
        context = Context;
        return true;
    }

    /// <summary>呼叫端可提供文件專屬 binding；原地修改版本須由該 binding 的 readRevision 完整維護。</summary>
    public static void Open(UnityEngine.Object owner, HGDocumentBinding binding)
        => HaruGraphWindow.OpenForDocument(owner, binding, Context);

    public static void Open(UnityEngine.Object owner, FieldInfo field) => Open(owner, CreateBinding(field));

    /// <summary>只接受恰一個文件欄位；多文件 Owner 必須指定 binding 或欄位。</summary>
    public static void Open(UnityEngine.Object owner)
    {
        var field = HGModel.FindSystemField(owner);
        if (field == null || !typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(field.FieldType))
        {
            Debug.LogError("[LogicGraph] 找不到唯一的 LogicGraph 文件；請從對應欄位的 Inspector 按鈕開啟。", owner);
            return;
        }
        Open(owner, field);
    }

    [MenuItem("HaruFamily/LogicGraph/Open Selected")]
    [MenuItem("Assets/HaruFamily/Open in LogicGraph", false, 30)]
    private static void OpenSelection() => Open(Selection.activeObject);

    [MenuItem("HaruFamily/LogicGraph/Open Selected", true)]
    [MenuItem("Assets/HaruFamily/Open in LogicGraph", true)]
    private static bool CanOpenSelection()
    {
        var field = HGModel.FindSystemField(Selection.activeObject);
        return field != null && typeof(ILogicGraphEditorDiagnostics).IsAssignableFrom(field.FieldType);
    }

    private sealed class DocumentProvider : IHGEditorExtensionProvider
    {
        public bool Supports(UnityEngine.Object owner, IGraphDocument document)
            => document is ILogicGraphEditorDiagnostics;

        public void AddPorts(HGPortBuildContext context) { }
    }
}
}
