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

    /// <summary>Tool-owned root access for one document shape. GraphKit never assumes the runtime root type.</summary>
    public interface IHGRootAdapter
    {
        IReadOnlyList<object> RootKeys(IGraphDocument document, Object owner);
        IReadOnlyList<HGRootGroupView> ReadRoots(IGraphDocument document);
        HGRootGroupView AddRoot(IGraphDocument document, object rootKey);
        bool RemoveRoot(IGraphDocument document, object root);
        Type ItemType(IGraphDocument document);
        object CreateItem(IGraphDocument document);
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
        public HGEditorProfile Profile { get; }

        public HGEditorExtensionContext(IHGEditorExtensionProvider provider, IHGEditorMetadataProvider metadata = null,
            HGEditorProfile profile = null)
        {
            Provider = provider ?? throw new System.ArgumentNullException(nameof(provider));
            Metadata = metadata;
            Profile = profile;
        }

    public bool Supports(Object owner, IGraphDocument document)
        => Provider.Supports(owner, document);

    /// <summary>Collects optional Tool diagnostics without making GraphKit depend on any Tool assembly.</summary>
        public void CollectDiagnostics(Object owner, IGraphDocument document, List<GraphDiagnostic> diagnostics)
        {
            if (Provider is IHGEditorDiagnosticProvider provider)
                provider.CollectDiagnostics(owner, document, diagnostics);
        }

        /// <summary>Returns Tool-declared capabilities, or the document's legacy declaration when no profile is supplied.</summary>
        public HGCapabilities CapabilitiesOf(IGraphDocument document)
            => Profile?.Capabilities ?? document?.Capabilities ?? HGCapabilities.None;
    }

    /// <summary>Editor-only capability profile supplied by a Tool for one graph session.</summary>
    public sealed class HGEditorProfile
    {
        public HGCapabilities Capabilities { get; }

        public IHGRootAdapter RootAdapter { get; }

        public HGEditorProfile(HGCapabilities capabilities, IHGRootAdapter rootAdapter = null)
        {
            Capabilities = capabilities;
            RootAdapter = rootAdapter;
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
