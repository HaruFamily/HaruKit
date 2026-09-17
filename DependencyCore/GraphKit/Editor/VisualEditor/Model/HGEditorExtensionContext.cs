namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using UnityEngine;

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

    public HGEditorExtensionContext(IHGEditorExtensionProvider provider)
    {
        Provider = provider ?? throw new System.ArgumentNullException(nameof(provider));
    }

    public bool Supports(Object owner, IGraphDocument document)
        => Provider.Supports(owner, document);
}
}
