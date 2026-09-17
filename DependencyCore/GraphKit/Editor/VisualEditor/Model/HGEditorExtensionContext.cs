namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A Tool-specific Editor extension provider. Providers are passed explicitly when a graph session is opened;
/// GraphKit never discovers them through reflection or global registration.
/// </summary>
public interface IHGEditorExtensionProvider
{
    /// <summary>Whether this provider can extend the bound owner and document.</summary>
    bool Supports(Object owner, IGraphDocument document);

    /// <summary>Add Tool-specific Port adapters after GraphKit has added its built-in adapters.</summary>
    void AddPorts(HGPortBuildContext context);
}

/// <summary>Optional Tool-owned domain validation for the current Editor session.</summary>
public interface IHGEditorDiagnosticProvider
{
    /// <summary>Add diagnostics for the supplied working document. Implementations must not retain Editor model objects.</summary>
    void CollectDiagnostics(Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics);
}

/// <summary>Explicit Editor-only extensions retained for the lifetime of one graph editing session.</summary>
public sealed class HGEditorExtensionContext
{
    private sealed class DefaultProvider : IHGEditorExtensionProvider
    {
        public bool Supports(Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context) { }
    }

    public static HGEditorExtensionContext Default { get; } =
        new HGEditorExtensionContext(new DefaultProvider());

    public IHGEditorExtensionProvider Provider { get; }
    public IHGEditorMetadataProvider Metadata { get; }

    public HGEditorExtensionContext(IHGEditorExtensionProvider provider, IHGEditorMetadataProvider metadata = null)
    {
        Provider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        Metadata = metadata;
    }

    public bool Supports(Object owner, IGraphDocument document)
        => Provider.Supports(owner, document);

    /// <summary>Collects optional Tool diagnostics without making GraphKit depend on any Tool assembly.</summary>
    public void CollectDiagnostics(Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
    {
        if (Provider is IHGEditorDiagnosticProvider provider)
            provider.CollectDiagnostics(owner, document, diagnostics);
    }
}

/// <summary>Explicitly binds one Owner to one editable document without exposing Editor model internals.</summary>
public abstract class HGDocumentBinding
{
    public string DocumentId { get; }

    protected HGDocumentBinding(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id is required.", nameof(documentId));
        DocumentId = documentId;
    }

    public abstract bool TryRead(Object owner, out IGraphDocument document);
    public abstract bool TryCreate(out IGraphDocument document);
    public abstract bool TryClone(IGraphDocument document, out IGraphDocument clone);
    public abstract bool TryWrite(Object owner, IGraphDocument document);
}

/// <summary>Typed document adapter for Tools that know the exact document field they intend to edit.</summary>
public sealed class HGDocumentBinding<TDocument> : HGDocumentBinding
    where TDocument : class, IGraphDocument
{
    private readonly Func<Object, TDocument> read;
    private readonly Func<TDocument> create;
    private readonly Action<Object, TDocument> write;

    public HGDocumentBinding(string documentId, Func<Object, TDocument> read, Action<Object, TDocument> write,
        Func<TDocument> create = null) : base(documentId)
    {
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.write = write ?? throw new ArgumentNullException(nameof(write));
        this.create = create;
    }

    public override bool TryRead(Object owner, out IGraphDocument document)
    {
        document = owner == null ? null : read(owner);
        return document != null;
    }

    public override bool TryCreate(out IGraphDocument document)
    {
        document = create?.Invoke();
        return document != null;
    }

    public override bool TryClone(IGraphDocument document, out IGraphDocument clone)
    {
        clone = document?.DeepCopy() as TDocument;
        return clone != null && !ReferenceEquals(document, clone);
    }

    public override bool TryWrite(Object owner, IGraphDocument document)
    {
        if (owner == null || document is not TDocument typed) return false;
        write(owner, typed);
        return true;
    }
}
}
