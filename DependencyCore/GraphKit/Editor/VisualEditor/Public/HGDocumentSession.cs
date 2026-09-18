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
    private readonly List<TDocument> undo = new List<TDocument>();
    private readonly List<TDocument> redo = new List<TDocument>();

    public Object Owner { get; }
    public string DocumentId => binding.DocumentId;
    public TDocument Document { get; private set; }
    public int Generation { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    private HGDocumentSession(Object owner, HGDocumentBinding<TDocument> binding, TDocument document)
    {
        Owner = owner;
        this.binding = binding;
        Document = document;
    }

    public static bool TryOpen(Object owner, HGDocumentBinding<TDocument> binding, out HGDocumentSession<TDocument> session)
    {
        session = null;
        if (owner == null || binding == null) return false;
        if (!binding.TryRead(owner, out IGraphDocument live) && !binding.TryCreate(out live)) return false;
        if (!binding.TryClone(live, out IGraphDocument copy) || copy is not TDocument document) return false;
        session = new HGDocumentSession<TDocument>(owner, binding, document);
        return true;
    }

    /// <summary>Creates a registry for this exact document and generation. Rebuild it after every successful mutation.</summary>
    public HGPortRegistry CreatePortRegistry() => new HGPortRegistry(Generation, this);

    public HGSessionCommandResult Connect(HGPortRegistry registry, HGPortKey first, HGPortKey second)
    {
        if (!TryGetConnection(registry, first, second, out HGPort input, out HGPort output, out var result)) return result;
        return ReplaceInputSource(input, output.Source);
    }

    /// <summary>Replaces one registered input with a document-owned source through the normal session transaction.</summary>
    public HGSessionCommandResult ReplaceSource(HGPortRegistry registry, HGPortKey inputKey, IHGPortSource source)
    {
        if (!IsCurrentRegistry(registry)) return HGSessionCommandResult.StaleGeneration;
        if (!registry.TryGet(inputKey, out HGPort input) || !input.IsInput || input.InputSlot == null)
            return HGSessionCommandResult.Rejected;
        return ReplaceInputSource(input, source);
    }

    public HGSessionCommandResult Disconnect(HGPortRegistry registry, HGPortKey key)
    {
        if (!IsCurrentRegistry(registry)) return HGSessionCommandResult.StaleGeneration;
        if (!registry.TryGet(key, out HGPort input) || !input.IsInput || input.InputSlot == null)
            return HGSessionCommandResult.Rejected;

        GraphNode previous = input.InputSlot.Node;
        if (previous == null) return HGSessionCommandResult.NoChange;
        if (!CaptureUndo()) return HGSessionCommandResult.Rejected;
        input.InputSlot.SetNode(null);
        ReturnUnreferenced(previous);
        Changed();
        return HGSessionCommandResult.Changed;
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
    {
        if (!TryClone(Document, out TDocument toStore)) return HGSessionCommandResult.WriteFailed;
        toStore.MarkDirty();
        toStore.Verify();
        if (!toStore.IsValidated) return HGSessionCommandResult.ValidationFailed;
        if (!binding.TryWrite(Owner, toStore)) return HGSessionCommandResult.WriteFailed;
        EditorUtility.SetDirty(Owner);
        if (Owner is Component component && component.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        AssetDatabase.SaveAssets();
        Document = toStore;
        undo.Clear();
        redo.Clear();
        IsDirty = false;
        Generation++;
        return HGSessionCommandResult.Changed;
    }

    public HGSessionCommandResult Cancel()
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
        if (HGPortConnection.Check(a, b, Generation) != HGPortConnectionResult.Allowed)
        {
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
        if (HGPortConnection.CheckInputSource(input, source, Generation) != HGPortConnectionResult.Allowed)
            return HGSessionCommandResult.Rejected;

        GraphNode next = source.OutputNode;
        if (!IsDocumentOwned(next)) return HGSessionCommandResult.Rejected;
        if (ReferenceEquals(input.InputSlot.Node, next)) return HGSessionCommandResult.NoChange;

        if (!CaptureUndo()) return HGSessionCommandResult.Rejected;
        GraphNode previous = input.InputSlot.Node;
        List<GraphNode> orphanPool = FindOrphanPool(input.InputSlot);
        input.InputSlot.SetNode(next);
        RemoveFromOrphanPools(next);
        ReturnUnreferenced(previous, orphanPool);
        Changed();
        return HGSessionCommandResult.Changed;
    }

    /// <summary>Deletes a document-owned carrier, disconnects its users, and returns direct child sources to the candidate pool.</summary>
    public HGSessionCommandResult DeleteNode(GraphNode node)
    {
        if (node == null || !IsDocumentOwned(node))
            return HGSessionCommandResult.Rejected;
        if (!CaptureUndo()) return HGSessionCommandResult.Rejected;
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
        Changed();
        return HGSessionCommandResult.Changed;
    }

    private bool CaptureUndo()
    {
        if (!TryClone(Document, out TDocument snapshot)) return false;
        undo.Add(snapshot);
        redo.Clear();
        return true;
    }

    private bool TryClone(TDocument source, out TDocument copy)
    {
        copy = null;
        return binding.TryClone(source, out IGraphDocument cloned) && (copy = cloned as TDocument) != null;
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

    private void Changed()
    {
        Document.MarkDirty();
        IsDirty = true;
        Generation++;
    }
}
}
