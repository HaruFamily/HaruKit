namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Result of a public working-copy command.</summary>
public enum HGSessionCommandResult
{
    Changed,
    NoChange,
    Rejected,
    StaleGeneration,
    ValidationFailed,
    WriteFailed,
}

/// <summary>
/// Public working-copy transaction for Tools that need commands without accessing HaruGraphWindow, HGModel, or HGGraphView.
/// A session owns only its cloned document; Owner data changes exclusively after <see cref="Commit"/> succeeds.
/// </summary>
public sealed class HGDocumentSession<TDocument>
    where TDocument : class, IGraphDocument
{
    private readonly HGDocumentBinding<TDocument> binding;
    private readonly HGEditorExtensionContext context;
    private readonly List<TDocument> undo = new List<TDocument>();
    private readonly List<TDocument> redo = new List<TDocument>();

    public Object Owner { get; }
    public string DocumentId => binding.DocumentId;
    public TDocument Document { get; private set; }
    public int Generation { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public GraphDiagnostic LastDiagnostic { get; private set; }

    private HGDocumentSession(Object owner, HGDocumentBinding<TDocument> binding, TDocument document,
        HGEditorExtensionContext context)
    {
        Owner = owner;
        this.binding = binding;
        Document = document;
        this.context = context ?? HGEditorExtensionContext.Default;
    }

    public static bool TryOpen(Object owner, HGDocumentBinding<TDocument> binding, out HGDocumentSession<TDocument> session)
        => TryOpen(owner, binding, HGEditorExtensionContext.Default, out session, out _);

    public static bool TryOpen(Object owner, HGDocumentBinding<TDocument> binding, HGEditorExtensionContext context,
        out HGDocumentSession<TDocument> session, out GraphDiagnostic diagnostic)
    {
        session = null;
        diagnostic = null;
        try
        {
            if (owner == null || binding == null) throw new InvalidOperationException("Owner and binding are required.");
            if (!binding.TryRead(owner, out IGraphDocument live) && !binding.TryCreate(out live))
                throw new InvalidOperationException("The document could not be read or created.");
            if (!binding.TryClone(live, out IGraphDocument copy) || copy is not TDocument document)
                throw new InvalidOperationException("Clone must return an isolated document of the bound type.");
            context ??= HGEditorExtensionContext.Default;
            if (!context.Supports(owner, document)) throw new InvalidOperationException("The provider does not support this document.");
            session = new HGDocumentSession<TDocument>(owner, binding, document, context);
            return true;
        }
        catch (Exception exception)
        {
            diagnostic = Failure("open", binding?.DocumentId, exception.Message);
            return false;
        }
    }

    /// <summary>Creates a registry for this exact document and generation. Rebuild it after every successful mutation.</summary>
    public HGPortRegistry CreatePortRegistry() => new HGPortRegistry(Generation, this);

    public HGSessionCommandResult Connect(HGPortRegistry registry, HGPortKey first, HGPortKey second)
        => Guard(() => ConnectCore(registry, first, second));

    private HGSessionCommandResult ConnectCore(HGPortRegistry registry, HGPortKey first, HGPortKey second)
    {
        if (!TryGetConnection(registry, first, second, out HGPort input, out HGPort output, out var result)) return result;
        return ReplaceInputSource(input, output.Source);
    }

    /// <summary>Replaces one registered input with a document-owned source through the normal session transaction.</summary>
    public HGSessionCommandResult ReplaceSource(HGPortRegistry registry, HGPortKey inputKey, IHGPortSource source)
        => ReconnectInput(registry, inputKey, source);

    /// <summary>Changes one input reference, rather than replacing the referenced carrier's content.</summary>
    public HGSessionCommandResult ReconnectInput(HGPortRegistry registry, HGPortKey inputKey, IHGPortSource source)
        => Guard(() => ReconnectInputCore(registry, inputKey, source));

    private HGSessionCommandResult ReconnectInputCore(HGPortRegistry registry, HGPortKey inputKey, IHGPortSource source)
    {
        if (!IsCurrentRegistry(registry)) return HGSessionCommandResult.StaleGeneration;
        if (!registry.TryGet(inputKey, out HGPort input) || !input.IsInput || input.InputSlot == null)
            return HGSessionCommandResult.Rejected;
        return ReplaceInputSource(input, source);
    }

    public HGSessionCommandResult Disconnect(HGPortRegistry registry, HGPortKey key)
        => Guard(() => DisconnectCore(registry, key));

    private HGSessionCommandResult DisconnectCore(HGPortRegistry registry, HGPortKey key)
    {
        if (!IsCurrentRegistry(registry)) return HGSessionCommandResult.StaleGeneration;
        if (!registry.TryGet(key, out HGPort input) || !input.IsInput || input.InputSlot == null)
            return HGSessionCommandResult.Rejected;
        if (!Owns(input.InputSlot) || input.Presentation.Locked || !input.Presentation.Visible)
            return HGSessionCommandResult.Rejected;

        GraphNode previous = input.InputSlot.Node;
        if (previous == null) return HGSessionCommandResult.NoChange;
        return Apply(() =>
        {
            List<GraphNode> orphanPool = FindOrphanPool(input.InputSlot);
            input.InputSlot.SetNode(null);
            ReturnUnreferenced(previous, orphanPool);
        });
    }

    public HGSessionCommandResult Undo()
    {
        if (undo.Count == 0) return HGSessionCommandResult.NoChange;
        if (!TryClone(Document, out TDocument current)) return HGSessionCommandResult.Rejected;
        redo.Add(current);
        Document = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        IsDirty = true;
        Generation++;
        return HGSessionCommandResult.Changed;
    }

    public HGSessionCommandResult Redo()
    {
        if (redo.Count == 0) return HGSessionCommandResult.NoChange;
        if (!TryClone(Document, out TDocument current)) return HGSessionCommandResult.Rejected;
        undo.Add(current);
        Document = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        IsDirty = true;
        Generation++;
        return HGSessionCommandResult.Changed;
    }

    public HGSessionCommandResult Commit()
        => Guard(CommitCore, HGSessionCommandResult.WriteFailed);

    private HGSessionCommandResult CommitCore()
    {
        if (Owner == null) return HGSessionCommandResult.WriteFailed;
        if (!TryClone(Document, out TDocument toStore)) return HGSessionCommandResult.WriteFailed;
        foreach (var diagnostic in CollectDiagnostics(toStore))
            if (diagnostic.Severity == GraphDiagnosticSeverity.Error)
            {
                LastDiagnostic = diagnostic;
                return HGSessionCommandResult.ValidationFailed;
            }
        toStore.MarkDirty();
        toStore.Verify();
        if (!toStore.IsValidated) return HGSessionCommandResult.ValidationFailed;
        if (!binding.TryWrite(Owner, toStore)) return HGSessionCommandResult.WriteFailed;
        EditorUtility.SetDirty(Owner);
        if (Owner is Component component && component.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        AssetDatabase.SaveAssets();
        // Keep the editing copy isolated from the instance passed to the Owner setter.
        undo.Clear();
        redo.Clear();
        IsDirty = false;
        Generation++;
        return HGSessionCommandResult.Changed;
    }

    public HGSessionCommandResult Cancel()
        => Guard(CancelCore);

    private HGSessionCommandResult CancelCore()
    {
        if (!binding.TryRead(Owner, out IGraphDocument live) || !binding.TryClone(live, out IGraphDocument copy)
            || copy is not TDocument document) return HGSessionCommandResult.Rejected;
        Document = document;
        undo.Clear();
        redo.Clear();
        IsDirty = false;
        Generation++;
        return HGSessionCommandResult.Changed;
    }

    private bool TryGetConnection(HGPortRegistry registry, HGPortKey first, HGPortKey second, out HGPort input,
        out HGPort output, out HGSessionCommandResult result)
    {
        input = output = null;
        if (!IsCurrentRegistry(registry))
        {
            result = HGSessionCommandResult.StaleGeneration;
            return false;
        }
        if (!registry.TryGet(first, out HGPort a) || !registry.TryGet(second, out HGPort b))
        {
            result = HGSessionCommandResult.Rejected;
            return false;
        }
        var acceptance = HGPortConnection.Check(a, b, Generation);
        if (acceptance != HGPortConnectionResult.Allowed)
        {
            LastDiagnostic = Failure("connection", DocumentId, acceptance.ToString());
            result = HGSessionCommandResult.Rejected;
            return false;
        }
        input = a.IsInput ? a : b;
        output = a.IsOutput ? a : b;
        result = HGSessionCommandResult.Changed;
        return true;
    }

    private bool IsCurrentRegistry(HGPortRegistry registry)
        => registry != null && registry.Generation == Generation && ReferenceEquals(registry.Scope, this);

    private HGSessionCommandResult ReplaceInputSource(HGPort input, IHGPortSource source)
    {
        if (!Owns(input.InputSlot)) return HGSessionCommandResult.Rejected;
        var acceptance = HGPortConnection.CheckInputSource(input, source, Generation);
        if (acceptance != HGPortConnectionResult.Allowed)
        {
            LastDiagnostic = Failure("connection", DocumentId, acceptance.ToString());
            return HGSessionCommandResult.Rejected;
        }

        GraphNode next = source.OutputNode;
        if (!IsDocumentOwned(next)) return HGSessionCommandResult.Rejected;
        if (ReferenceEquals(input.InputSlot.Node, next)) return HGSessionCommandResult.NoChange;

        return Apply(() =>
        {
            GraphNode previous = input.InputSlot.Node;
            List<GraphNode> orphanPool = FindOrphanPool(input.InputSlot);
            input.InputSlot.SetNode(next);
            RemoveFromOrphanPools(next);
            ReturnUnreferenced(previous, orphanPool);
        });
    }

    /// <summary>Edits a descriptor field in the current working document as one undoable command.</summary>
    public HGSessionCommandResult EditValue(object target, HGFieldDescriptor field, object value)
        => Guard(() =>
        {
            if (!Owns(target) || field == null || field.ReadOnly) return HGSessionCommandResult.Rejected;
            if (Equals(field.Read(target), value)) return HGSessionCommandResult.NoChange;
            return Apply(() =>
            {
                if (!field.TryWrite(target, value, out var error))
                    throw error ?? new ArgumentException("Value does not match the descriptor.");
            });
        });

    /// <summary>Replaces carrier content while retaining its identity, layout and all compatible references.</summary>
    public HGSessionCommandResult ReplaceSource(GraphNode carrier, HGCarrierSource source)
        => Guard(() =>
        {
            if (!IsDocumentOwned(carrier) || source == null || !source.IsValid) return HGSessionCommandResult.Rejected;
            if (source.Token != null && !Owns(source.Token)) return HGSessionCommandResult.Rejected;
            if (source.Matches(carrier)) return HGSessionCommandResult.NoChange;
            var slots = new List<GraphSlotBase>();
            var visited = new HashSet<object>(HGRefComparer.Instance);
            foreach (var root in Document.Roots) slots.AddRange(HGModel.WalkSlots(root, visited));
            foreach (var token in DocumentTokens()) slots.AddRange(HGModel.WalkSlots(token, visited));
            foreach (var pool in OrphanPools())
                foreach (var node in pool) slots.AddRange(HGModel.WalkSlots(node, visited));
            foreach (var slot in slots)
                if (ReferenceEquals(slot.Node, carrier) && !source.Accepts(slot)) return HGSessionCommandResult.Rejected;
            var shared = new HashSet<object>(ReferenceComparer.Instance);
            foreach (var slot in slots)
                if (slot.Node != null) shared.Add(slot.Node);
            foreach (var token in DocumentTokens()) shared.Add(token);
            foreach (var pool in OrphanPools())
                foreach (var node in pool) shared.Add(node);
            var pending = new Queue<GraphNode>();
            var direct = new HashSet<GraphNode>();
            CollectDirectChildren(source.Content, direct, new HashSet<object>(ReferenceComparer.Instance));
            foreach (var node in direct) pending.Enqueue(node);
            var inspected = new HashSet<GraphNode>();
            while (pending.Count > 0)
            {
                var node = pending.Dequeue();
                if (!inspected.Add(node)) continue;
                if (IsDocumentOwned(node)) { shared.Add(node); continue; }
                direct.Clear();
                var references = new HashSet<object>(ReferenceComparer.Instance);
                CollectDirectChildren(node.BodyObject, direct, references);
                CollectDirectChildren(node.CatalogObject, direct, references);
                CollectDirectChildren(node.Bindings, direct, references);
                foreach (var child in direct) pending.Enqueue(child);
            }
            return Apply(() =>
            {
                var children = new HashSet<GraphNode>();
                CollectDirectChildren(carrier.BodyObject, children, new HashSet<object>(ReferenceComparer.Instance));
                CollectDirectChildren(carrier.CatalogObject, children, new HashSet<object>(ReferenceComparer.Instance));
                CollectDirectChildren(carrier.Bindings, children, new HashSet<object>(ReferenceComparer.Instance));
                var pool = FindOrphanPool(carrier);
                source.Apply(carrier, shared);
                foreach (var input in HGModel.WalkSlots(carrier, new HashSet<object>(HGRefComparer.Instance)))
                    if (input.Node != null && !ReferenceEquals(input.Node, carrier)) RemoveFromOrphanPools(input.Node);
                foreach (var child in children) ReturnUnreferenced(child, pool);
            });
        });

    /// <summary>Deletes a document-owned carrier, disconnects its users, and returns direct child sources to the candidate pool.</summary>
    public HGSessionCommandResult DeleteNode(GraphNode node)
        => Guard(() => DeleteNodeCore(node));

    private HGSessionCommandResult DeleteNodeCore(GraphNode node)
    {
        if (node == null || !IsDocumentOwned(node))
            return HGSessionCommandResult.Rejected;
        return Apply(() => DeleteOwnedNode(node));
    }

    private void DeleteOwnedNode(GraphNode node)
    {
        List<GraphNode> orphanPool = FindOrphanPool(node);

        var children = new HashSet<GraphNode>();
        CollectDirectChildren(node.BodyObject, children, new HashSet<object>(ReferenceComparer.Instance));
        CollectDirectChildren(node.CatalogObject, children, new HashSet<object>(ReferenceComparer.Instance));
        CollectDirectChildren(node.Bindings, children, new HashSet<object>(ReferenceComparer.Instance));

        foreach (object root in Document.Roots)
            DisconnectNodeUsers(root, node, new HashSet<object>(ReferenceComparer.Instance));
        foreach (GraphToken token in DocumentTokens())
        {
            DisconnectNodeUsers(token, node, new HashSet<object>(ReferenceComparer.Instance));
            foreach (GraphNode orphan in token.Orphans)
                DisconnectNodeUsers(orphan, node, new HashSet<object>(ReferenceComparer.Instance));
        }
        foreach (List<GraphNode> pool in OrphanPools())
            foreach (GraphNode orphan in pool)
                DisconnectNodeUsers(orphan, node, new HashSet<object>(ReferenceComparer.Instance));
        RemoveFromOrphanPools(node);

        foreach (GraphNode child in children)
            if (!ReferenceEquals(child, node)) ReturnUnreferenced(child, orphanPool);
    }

    private HGSessionCommandResult Apply(Action mutation)
    {
        if (!TryClone(Document, out TDocument snapshot)) return HGSessionCommandResult.Rejected;
        bool dirty = IsDirty;
        try
        {
            mutation();
            Document.MarkDirty();
            undo.Add(snapshot);
            redo.Clear();
            IsDirty = true;
            Generation++;
            return HGSessionCommandResult.Changed;
        }
        catch (Exception exception)
        {
            Document = snapshot;
            IsDirty = dirty;
            Generation++;
            LastDiagnostic = Failure("mutation", DocumentId, exception.Message);
            return HGSessionCommandResult.Rejected;
        }
    }

    private bool TryClone(TDocument source, out TDocument copy)
    {
        copy = null;
        try
        {
            return binding.TryClone(source, out IGraphDocument cloned) && (copy = cloned as TDocument) != null;
        }
        catch (Exception exception)
        {
            LastDiagnostic = Failure("clone", DocumentId, exception.Message);
            return false;
        }
    }

    private void ReturnUnreferenced(GraphNode node, List<GraphNode> preferredPool = null)
    {
        if (node == null || IsReferenced(node) || IsReferencedByOrphans(node)) return;
        (preferredPool ?? Document.Orphans).Add(node);
    }

    private bool IsReferenced(GraphNode node)
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        foreach (object root in Document.Roots)
            if (ReferencesNode(root, node, visited)) return true;
        foreach (GraphToken token in DocumentTokens())
            if (ReferencesNode(token, node, visited)) return true;
        return false;
    }

    private bool IsReferencedByOrphans(GraphNode node)
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        foreach (List<GraphNode> pool in OrphanPools())
            foreach (GraphNode orphan in pool)
                if (ReferenceEquals(orphan, node) || ReferencesNode(orphan, node, visited)) return true;
        return false;
    }

    private bool IsDocumentOwned(GraphNode node)
        => node != null && (IsReferenced(node) || IsReferencedByOrphans(node));

    private IEnumerable<List<GraphNode>> OrphanPools()
    {
        yield return Document.Orphans;
        foreach (GraphToken token in DocumentTokens())
            yield return token.Orphans;
    }

    private IEnumerable<GraphToken> DocumentTokens()
        => (Document as ITokenOwner)?.Tokens ?? (IEnumerable<GraphToken>)Array.Empty<GraphToken>();

    private void RemoveFromOrphanPools(GraphNode node)
    {
        foreach (List<GraphNode> pool in OrphanPools()) pool.Remove(node);
    }

    private List<GraphNode> FindOrphanPool(object value)
    {
        foreach (GraphToken token in DocumentTokens())
            if (ReferencesObject(token, value, new HashSet<object>(ReferenceComparer.Instance))) return token.Orphans;
        return Document.Orphans;
    }

    private static void CollectDirectChildren(object value, HashSet<GraphNode> children, HashSet<object> visited)
    {
        if (value == null || !visited.Add(value)) return;
        if (value is GraphNode node)
        {
            children.Add(node);
            return;
        }
        if (value is GraphSlotBase slot)
        {
            if (slot.Node != null) children.Add(slot.Node);
            return;
        }
        if (value is UnityEngine.Object || value is string) return;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;
        if (value is IEnumerable list)
        {
            foreach (object item in list) CollectDirectChildren(item, children, visited);
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized)
                    CollectDirectChildren(field.GetValue(value), children, visited);
    }

    private static void DisconnectNodeUsers(object value, GraphNode node, HashSet<object> visited)
    {
        if (value == null || !visited.Add(value)) return;
        if (value is IGraphNodeOwner owner) owner.RemoveChild(node);
        if (value is GraphSlotBase slot)
        {
            if (ReferenceEquals(slot.Node, node)) slot.SetNode(null);
            else DisconnectNodeUsers(slot.Node, node, visited);
            return;
        }
        if (value is UnityEngine.Object || value is string) return;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;
        if (value is IEnumerable list)
        {
            foreach (object item in list) DisconnectNodeUsers(item, node, visited);
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized)
                    DisconnectNodeUsers(field.GetValue(value), node, visited);
    }

    private static bool ReferencesNode(object value, GraphNode expected, HashSet<object> visited)
    {
        if (value == null) return false;
        if (ReferenceEquals(value, expected)) return true;
        if (value is UnityEngine.Object || value is string) return false;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return false;
        if (!visited.Add(value)) return false;

        if (value is GraphSlotBase slot)
            return ReferenceEquals(slot.Node, expected) || ReferencesNode(slot.Node, expected, visited);

        if (value is IEnumerable list)
        {
            foreach (object item in list)
                if (ReferencesNode(item, expected, visited)) return true;
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized && ReferencesNode(field.GetValue(value), expected, visited)) return true;
        return false;
    }

    private static bool ReferencesObject(object value, object expected, HashSet<object> visited)
    {
        if (value == null) return false;
        if (ReferenceEquals(value, expected)) return true;
        if (value is UnityEngine.Object || value is string) return false;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return false;
        if (!visited.Add(value)) return false;

        if (value is IEnumerable list)
        {
            foreach (object item in list)
                if (ReferencesObject(item, expected, visited)) return true;
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(flags))
                if (!field.IsStatic && !field.IsNotSerialized && ReferencesObject(field.GetValue(value), expected, visited)) return true;
        return false;
    }

    private bool Owns(object value)
    {
        if (value == null) return false;
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        foreach (object root in Document.Roots)
            if (ReferencesObject(root, value, visited)) return true;
        foreach (GraphToken token in DocumentTokens())
            if (ReferencesObject(token, value, visited)) return true;
        foreach (var pool in OrphanPools())
            if (ReferencesObject(pool, value, visited)) return true;
        return false;
    }

    public IReadOnlyList<GraphDiagnostic> CollectDiagnostics() => CollectDiagnostics(Document).AsReadOnly();

    private List<GraphDiagnostic> CollectDiagnostics(IGraphDocument document)
    {
        var diagnostics = new List<GraphDiagnostic>();
        context.CollectDiagnostics(Owner, document, diagnostics);
        return diagnostics;
    }

    private HGSessionCommandResult Guard(Func<HGSessionCommandResult> command,
        HGSessionCommandResult failure = HGSessionCommandResult.Rejected)
    {
        LastDiagnostic = null;
        try
        {
            var result = command();
            if (LastDiagnostic == null && result != HGSessionCommandResult.Changed && result != HGSessionCommandResult.NoChange)
                LastDiagnostic = Failure("command", DocumentId, result.ToString());
            return result;
        }
        catch (Exception exception)
        {
            LastDiagnostic = Failure("command", DocumentId, exception.Message);
            return failure;
        }
    }

    private static GraphDiagnostic Failure(string operation, string documentId, string message)
        => new GraphDiagnostic("graphkit.session." + operation + "-failed", GraphDiagnosticSeverity.Error, message,
            new GraphDiagnosticLocation(documentId: documentId));
}

/// <summary>Generation-scoped commands using the actual window transaction and rebuild pipeline.</summary>
public sealed class HGWindowSession
{
    private readonly HaruGraphWindow window;
    private readonly HGModel model;
    internal HGWindowSession(HaruGraphWindow window, HGModel model) { this.window = window; this.model = model; }
    public HGWindowSnapshot Query() => window != null ? window.QueryDocument(model) : null;
    public IReadOnlyList<GraphDiagnostic> Validate()
        => window != null ? window.ValidateDocument(model) : new[]
        {
            new GraphDiagnostic("graphkit.session.closed", GraphDiagnosticSeverity.Error, "The window has been closed."),
        };
    public HGSessionCommandResult Connect(int generation, HGPortKey first, HGPortKey second)
        => window != null ? window.ConnectDocument(model, generation, first, second) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult Disconnect(int generation, HGPortKey input)
        => window != null ? window.DisconnectDocument(model, generation, input) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult EditValue(int generation, string nodeId, string fieldPath, object value)
        => window != null ? window.EditDocumentValue(model, generation, nodeId, fieldPath, value) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult Undo() => window != null ? window.DocumentHistory(model, false) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult Redo() => window != null ? window.DocumentHistory(model, true) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult Commit() => window != null ? window.CommitDocument(model) : HGSessionCommandResult.Rejected;
    public HGSessionCommandResult Cancel() => window != null ? window.CancelDocument(model) : HGSessionCommandResult.Rejected;
}

public readonly struct HGWindowLink
{
    public HGPortKey Input { get; }
    public HGPortKey Output { get; }
    internal HGWindowLink(HGPortKey input, HGPortKey output) { Input = input; Output = output; }
}

public sealed class HGWindowSnapshot
{
    public int Generation { get; }
    public bool IsDirty { get; }
    public IReadOnlyList<HGPortDescriptor> Ports { get; }
    public IReadOnlyList<HGNodeViewInfo> Nodes { get; }
    public IReadOnlyList<HGWindowLink> Links { get; }
    internal HGWindowSnapshot(int generation, List<HGPortDescriptor> ports, List<HGNodeViewInfo> nodes,
        List<HGWindowLink> links, bool dirty)
    { Generation = generation; Ports = ports.AsReadOnly(); Nodes = nodes.AsReadOnly(); Links = links.AsReadOnly(); IsDirty = dirty; }
}

/// <summary>Explicit carrier content; no arbitrary mutation callback can take over the transaction.</summary>
public sealed class HGCarrierSource
{
    private readonly NodeKind kind;
    private readonly GraphNodeContent content;
    internal GraphNodeContent Content => content;
    private readonly ScriptableObject asset;
    internal GraphToken Token { get; }
    internal bool IsValid => kind switch
    {
        NodeKind.Inline or NodeKind.Catalog => content != null,
        NodeKind.Asset => asset is IGraphAsset,
        NodeKind.Token => Token != null,
        _ => false,
    };

    private HGCarrierSource(NodeKind kind, GraphNodeContent content = null, ScriptableObject asset = null, GraphToken token = null)
    { this.kind = kind; this.content = content; this.asset = asset; Token = token; }

    public static HGCarrierSource Body(GraphNodeContent body) => new(NodeKind.Inline, body);
    public static HGCarrierSource Catalog(GraphNodeContent catalog) => new(NodeKind.Catalog, catalog);
    public static HGCarrierSource Asset(ScriptableObject asset) => new(NodeKind.Asset, asset: asset);
    public static HGCarrierSource NamedToken(GraphToken token) => new(NodeKind.Token, token: token);

    internal bool Accepts(GraphSlotBase slot) => kind switch
    {
        NodeKind.Inline => slot.AcceptsBody(content),
        NodeKind.Catalog => slot is CatalogSlotBase catalog && catalog.AcceptsCatalogObject(content),
        NodeKind.Asset => slot.AcceptsAsset(asset),
        NodeKind.Token => slot.AcceptsToken(Token),
        _ => false,
    };

    internal bool Matches(GraphNode carrier) => carrier.Kind == kind && (kind switch
    {
        NodeKind.Inline => ReferenceEquals(carrier.BodyObject, content),
        NodeKind.Catalog => ReferenceEquals(carrier.CatalogObject, content),
        NodeKind.Asset => carrier.AssetObject == asset,
        NodeKind.Token => ReferenceEquals(carrier.Token, Token),
        _ => false,
    });

    internal void Apply(GraphNode carrier, IEnumerable<object> shared)
    {
        if (kind == NodeKind.Token) { carrier.SetToken(Token); return; }
        if (kind == NodeKind.Asset)
        {
            var parameters = AssetGraphSchema.Read(asset, out var duplicates);
            if (duplicates.Count > 0) throw new InvalidOperationException("Asset has duplicate parameter identities.");
            carrier.Bindings.RemoveAll(binding => binding?.Slot == null || !parameters.Exists(parameter =>
                parameter.Name == binding.Name && parameter.Slot.FamilyType == binding.Slot.FamilyType));
            carrier.SetAsset(asset);
            return;
        }
        var copy = GraphDeepCopy.Copy(content, shared);
        if (copy == null || ReferenceEquals(copy, content)) throw new InvalidOperationException("Source clone failed.");
        HGModel.ResetNodeIds(copy, shared);
        if (kind == NodeKind.Inline) carrier.SetBody(copy);
        else carrier.SetCatalog(copy);
    }
}
}
