namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>Authoring commands share the session rollback and history, and the editor's library rules.</summary>
public sealed partial class HGDocumentSession<TDocument> where TDocument : class, IGraphDocument
{
    private HGModel AuthoringModel() => HGModel.CreateDetachedView(Owner, Document, context.Profile?.RootAdapter);
    private string RootGroupScope => Document is HGAssetDocument asset ? asset.Scope : new HGFocus { Kind = HGFocusKind.Root }.Id;
    private string TokenGroupScope(GraphToken token) => (Document is HGAssetDocument ? RootGroupScope + "/" : "") + "var:" + token.Id;

    public IReadOnlyDictionary<string, string> GroupScopes()
    {
        var scopes = new Dictionary<string, string> { [RootGroupScope] = "Root" };
        foreach (var token in DocumentTokens())
            if (token != null && !string.IsNullOrEmpty(token.Id)) scopes.Add(TokenGroupScope(token), token.Name ?? token.Id);
        return scopes;
    }

    public HGSessionCommandResult CreateGroup(string scope, string name, out string groupId)
    {
        string created = null;
        var result = Guard(() =>
        {
            if (Document is not IGraphViewStateOwner owner || !GroupScopes().ContainsKey(scope) || string.IsNullOrWhiteSpace(name))
                return HGSessionCommandResult.Rejected;
            return Apply(() =>
            {
                var group = new GraphNodeGroup(scope, "", new UnityEngine.Rect(0, 0, 320, 220), 0);
                group.AddTab(name.Trim());
                owner.ViewState.AddNodeGroup(group);
                created = group.Id;
            }, content: false);
        });
        groupId = result == HGSessionCommandResult.Changed ? created : null;
        return result;
    }

    public HGSessionCommandResult EditGroup(GraphNodeGroup group, string name, bool? collapsed, bool delete) => Guard(() =>
    {
        if (Document is not IGraphViewStateOwner owner || group == null || !owner.ViewState.NodeGroups.Contains(group))
            return HGSessionCommandResult.Rejected;
        if (name != null && string.IsNullOrWhiteSpace(name)) return HGSessionCommandResult.Rejected;
        return Apply(() =>
        {
            if (delete) { owner.ViewState.RemoveNodeGroup(group); return; }
            if (name != null)
            {
                if (group.Tabs.Count == 0) group.AddTab(name.Trim());
                else group.SetTabName(0, name.Trim());
            }
            if (collapsed.HasValue) group.SetCollapsed(collapsed.Value);
        }, content: false);
    });

    public HGSessionCommandResult SetGroupMember(GraphNodeGroup group, GraphNode node, bool member) => Guard(() =>
    {
        if (Document is not IGraphViewStateOwner owner || group == null || !owner.ViewState.NodeGroups.Contains(group)
            || !IsDocumentOwned(node) || string.IsNullOrEmpty(node.Id)) return HGSessionCommandResult.Rejected;
        var roots = new List<object>();
        if (group.Scope == RootGroupScope)
        {
            foreach (var root in Document.Roots) roots.Add(root);
            roots.AddRange(Document.Orphans);
        }
        else
        {
            var token = DocumentTokens().SingleOrDefault(item => item != null && TokenGroupScope(item) == group.Scope);
            if (token == null) return HGSessionCommandResult.Rejected;
            roots.Add(token.Slot);
            roots.AddRange(token.Orphans);
        }
        bool visible = roots.Any(root => ReferenceEquals(root, node)
            || HGModel.WalkSlots(root, new HashSet<object>(ReferenceComparer.Instance)).Any(slot => ReferenceEquals(slot.Node, node)));
        if (member && !visible) return HGSessionCommandResult.Rejected;
        return Apply(() =>
        {
            if (member)
                foreach (var other in owner.ViewState.NodeGroups)
                    if (other != null && other.Scope == group.Scope && !ReferenceEquals(other, group)) other.SetMember(node.Id, false);
            group.SetMember(node.Id, member);
        }, content: false);
    });

    public HGSessionCommandResult SetSlotDefault(FormulaSlotBase slot, object value) => Guard(() =>
    {
        if (slot == null || slot is UnionSlotBase || !InspectObjects().Any(item => ReferenceEquals(item, slot))
            || Document is IPropertyOwner properties && properties.Properties.Any(item => ReferenceEquals(item?.Slot, slot)))
            return HGSessionCommandResult.Rejected;
        Type type = slot.DefaultEditType;
        if (type == null || (value == null ? type.IsValueType : !type.IsInstanceOfType(value))) return HGSessionCommandResult.Rejected;
        if (Equals(slot.DefaultObject, value)) return HGSessionCommandResult.NoChange;
        return Apply(() => slot.DefaultObject = GraphDeepCopy.Copy(value));
    });

    public HGSessionCommandResult CreateNode(HGCarrierSource source, out string nodeId)
    {
        string created = null;
        var result = Guard(() =>
        {
            if (source == null || !source.IsValid
                || source.Token != null && !DocumentTokens().Contains(source.Token)
                || source.Property != null && (Document is not IPropertyOwner properties || !properties.Properties.Contains(source.Property)))
                return HGSessionCommandResult.Rejected;
            var shared = InspectObjects();
            return Apply(() =>
            {
                var node = new GraphNode();
                source.Apply(node, shared);
                created = node.EnsureId();
                Document.Orphans.Add(node);
            });
        });
        nodeId = result == HGSessionCommandResult.Changed ? created : null;
        return result;
    }

    public HGSessionCommandResult CreateDefinition(bool property, Type type, string name, out string definitionId)
    {
        string created = null;
        var result = Guard(() =>
        {
            if (type == null || type.IsAbstract || type.ContainsGenericParameters || string.IsNullOrWhiteSpace(name))
                return HGSessionCommandResult.Rejected;
            if (property ? Document is not IPropertyOwner : Document is not ITokenOwner)
                return HGSessionCommandResult.Rejected;
            return Apply(() =>
            {
                var model = AuthoringModel();
                if (property)
                {
                    var scope = ((IPropertyOwner)Document).Properties;
                    var item = model.CreateProperty(scope, type, out string error);
                    if (item == null || !model.RenameProperty(item, name, scope, out error))
                        throw new InvalidOperationException(error);
                    created = item.Id;
                }
                else
                {
                    var scope = ((ITokenOwner)Document).Tokens;
                    var item = model.CreateToken(scope, type, out string error);
                    if (item == null || item.IsObsolete || !model.RenameToken(item, name, scope, out error))
                        throw new InvalidOperationException(error ?? "Token declaration is obsolete.");
                    created = item.Id;
                }
            });
        });
        definitionId = result == HGSessionCommandResult.Changed ? created : null;
        return result;
    }

    private bool OwnsDefinition(object definition)
        => definition is GraphToken token && DocumentTokens().Contains(token)
            || definition is GraphProperty property && Document is IPropertyOwner owner && owner.Properties.Contains(property);

    public HGSessionCommandResult RenameDefinition(object definition, string name) => Guard(() =>
    {
        if (!OwnsDefinition(definition) || string.IsNullOrWhiteSpace(name)) return HGSessionCommandResult.Rejected;
        string previous = definition is GraphToken token ? token.Name : ((GraphProperty)definition).Name;
        if (previous == name.Trim()) return HGSessionCommandResult.NoChange;
        return Apply(() =>
        {
            var model = AuthoringModel();
            string error;
            bool renamed = definition is GraphToken item
                ? model.RenameToken(item, name, ((ITokenOwner)Document).Tokens, out error)
                : model.RenameProperty((GraphProperty)definition, name, ((IPropertyOwner)Document).Properties, out error);
            if (!renamed) throw new InvalidOperationException(error);
        });
    });

    public HGSessionCommandResult DuplicateDefinition(object definition, out string definitionId)
    {
        string created = null;
        var result = Guard(() =>
        {
            if (!OwnsDefinition(definition)) return HGSessionCommandResult.Rejected;
            return Apply(() =>
            {
                var model = AuthoringModel();
                string error;
                created = definition is GraphToken token
                    ? model.DuplicateToken(token, ((ITokenOwner)Document).Tokens, out error)?.Id
                    : model.DuplicateProperty((GraphProperty)definition, ((IPropertyOwner)Document).Properties, out error)?.Id;
                if (created == null) throw new InvalidOperationException(error);
            });
        });
        definitionId = result == HGSessionCommandResult.Changed ? created : null;
        return result;
    }

    public HGSessionCommandResult DeleteDefinition(object definition) => Guard(() =>
    {
        if (!OwnsDefinition(definition)) return HGSessionCommandResult.Rejected;
        var carriers = InspectObjects().OfType<GraphNode>().ToArray();
        return Apply(() =>
        {
            var model = AuthoringModel();
            if (definition is GraphToken token) model.DeleteToken(token, ((ITokenOwner)Document).Tokens, carriers);
            else model.DeleteProperty((GraphProperty)definition, ((IPropertyOwner)Document).Properties, carriers);
        });
    });

    public HGSessionCommandResult ConfigureProperty(GraphProperty property, bool? retain, bool setInitial, object initial) => Guard(() =>
    {
        if (!OwnsDefinition(property) || property.Slot == null) return HGSessionCommandResult.Rejected;
        Type type = property.ResultType;
        if (setInitial && (type == null || (initial == null ? type.IsValueType : !type.IsInstanceOfType(initial))))
            return HGSessionCommandResult.Rejected;
        if ((!retain.HasValue || retain.Value == property.RetainValue) && (!setInitial || Equals(initial, property.InitialValue)))
            return HGSessionCommandResult.NoChange;
        return Apply(() =>
        {
            if (retain.HasValue) property.RetainValue = retain.Value;
            if (setInitial) property.SetInitialValue(GraphDeepCopy.Copy(initial));
        });
    });

    /// <summary>Only mutable, serialized Slot lists are writable; definition and orphan libraries use dedicated commands.</summary>
    public static Type SlotListElementType(IList items)
    {
        if (items == null || items.IsFixedSize || items.IsReadOnly) return null;
        Type type = items.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>)) return null;
        Type element = type.GetGenericArguments()[0];
        return typeof(GraphSlotBase).IsAssignableFrom(element) && !element.IsAbstract && !element.ContainsGenericParameters
            && element.GetConstructor(Type.EmptyTypes) != null ? element : null;
    }

    public HGSessionCommandResult InsertSlot(IList items, int index) => Guard(() =>
    {
        Type type = SlotListElementType(items);
        if (type == null || !InspectObjects().Any(value => ReferenceEquals(value, items)) || index < 0 || index > items.Count)
            return HGSessionCommandResult.Rejected;
        return Apply(() => items.Insert(index, Activator.CreateInstance(type)));
    });

    public HGSessionCommandResult RemoveSlot(IList items, int index) => Guard(() =>
    {
        if (SlotListElementType(items) == null || !InspectObjects().Any(value => ReferenceEquals(value, items))
            || index < 0 || index >= items.Count) return HGSessionCommandResult.Rejected;
        GraphNode source = (items[index] as GraphSlotBase)?.Node;
        var pool = FindOrphanPool(items);
        return Apply(() => { items.RemoveAt(index); ReturnUnreferenced(source, pool); });
    });

    public HGSessionCommandResult MoveSlot(IList items, int from, int to) => Guard(() =>
    {
        if (SlotListElementType(items) == null || !InspectObjects().Any(value => ReferenceEquals(value, items))
            || from < 0 || from >= items.Count || to < 0 || to >= items.Count) return HGSessionCommandResult.Rejected;
        if (from == to) return HGSessionCommandResult.NoChange;
        return Apply(() => { object item = items[from]; items.RemoveAt(from); items.Insert(to, item); });
    });

    public IReadOnlyList<object> AvailableRootKeys()
        => (context.Profile?.RootAdapter ?? HGModel.HGDocumentRootAdapter.Instance).RootKeys(Document, Owner);
    public IReadOnlyList<HGRootGroupView> ReadRootGroups() => AuthoringModel().ReadRootGroups();

    public HGSessionCommandResult AddRoot(object key) => Guard(() =>
    {
        if (!AvailableRootKeys().Contains(key)) return HGSessionCommandResult.Rejected;
        var model = AuthoringModel();
        if (model.HasRoot(key)) return HGSessionCommandResult.NoChange;
        return Apply(() => { if (model.AddRoot(key) == null) throw new InvalidOperationException("Root creation failed."); });
    });

    public HGSessionCommandResult RemoveRoot(object root) => Guard(() =>
    {
        var model = AuthoringModel();
        var group = model.ReadRootGroups().FirstOrDefault(item => ReferenceEquals(item.Root, root));
        if (group == null) return HGSessionCommandResult.Rejected;
        var children = new HashSet<GraphNode>();
        CollectDirectChildren(root, children, new HashSet<object>(ReferenceComparer.Instance));
        return Apply(() =>
        {
            model.RemoveRoot(group);
            if (model.ReadRootGroups().Any(item => ReferenceEquals(item.Root, root)))
                throw new InvalidOperationException("Root removal was rejected.");
            foreach (var node in children) ReturnUnreferenced(node);
        });
    });
}
}
