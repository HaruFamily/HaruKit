namespace HaruFamily.Framework.LogicGraph.Editor
{
using System;
using System.Reflection;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>LogicGraph 的正式 Editor 入口；Inspector、選單與其他工具共用同一份 context。</summary>
public static class LogicGraphEditor
{
    public static HGEditorExtensionContext Context { get; } = new HGEditorExtensionContext(
        new DocumentProvider(), profile: new HGEditorProfile(
            HGCapabilities.SharedAssets | HGCapabilities.Tokens, HGModel.HGDocumentRootAdapter.Instance));

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

    [MenuItem("PinTools/LogicGraph/開啟節點圖")]
    [MenuItem("Assets/LogicGraph/開啟節點圖", false, 30)]
    private static void OpenSelection() => Open(Selection.activeObject);

    [MenuItem("PinTools/LogicGraph/開啟節點圖", true)]
    [MenuItem("Assets/LogicGraph/開啟節點圖", true)]
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
