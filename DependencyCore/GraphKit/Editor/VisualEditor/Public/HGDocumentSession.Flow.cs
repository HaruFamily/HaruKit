namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;

public sealed partial class HGDocumentSession<TDocument> where TDocument : class, IGraphDocument
{
    /// <summary>Moves original action slots between declared writable sections, as one history entry.</summary>
    public HGSessionCommandResult TransferFlowSteps(int generation, string from, int index, int count, string to, int gap)
        => Guard(() =>
    {
        if (generation != Generation) return HGSessionCommandResult.StaleGeneration;
        var plan = HGFlowStructure.ResolveTransfer(HGFlowStructure.Editable(Document, context, from), index, count,
            HGFlowStructure.Editable(Document, context, to), gap);
        if (ReferenceEquals(plan.From.List, plan.To.List) && plan.Gap == index) return HGSessionCommandResult.NoChange;
        return Apply(() =>
        {
            for (int i = 0; i < count; i++)
                if (!plan.From.RemoveAt(index)) throw new ArgumentException("無法移出步驟。");
            for (int i = 0; i < count; i++)
                if (!plan.To.Insert(plan.Gap + i, plan.Slots[i])) throw new ArgumentException("無法插入步驟。");
        });
    });

    /// <summary>Adds only an outer conditional/loop; retains original slots and shared carriers.</summary>
    public HGSessionCommandResult WrapFlowSteps(int generation, string section, int index, int count, Type type, string childKey)
        => Guard(() =>
    {
        if (generation != Generation) return HGSessionCommandResult.StaleGeneration;
        var parent = HGFlowStructure.Editable(Document, context, section);
        if (count < 1 || count > 4096 || index < 0 || index > parent.Count - count) return HGSessionCommandResult.Rejected;
        var selected = new List<ActionSlotBase>();
        for (int i = 0; i < count; i++)
        {
            if (parent.Get(index + i) is not ActionSlotBase slot) return HGSessionCommandResult.Rejected;
            selected.Add(slot);
        }
        var plan = HGFlowStructure.CreateWrapper(context, parent, type, childKey, selected);
        return Apply(() =>
        {
            for (int i = 0; i < count; i++)
                if (!parent.RemoveAt(index)) throw new ArgumentException("無法移出步驟。");
            foreach (var slot in selected)
                if (!plan.Children.Add(slot)) throw new ArgumentException("容器無法接收步驟。");
            if (!parent.Insert(index, plan.Slot)) throw new ArgumentException("無法插入容器。");
            HGFlowStructure.Editable(Document, context, section + "/" + index + "/" + Uri.EscapeDataString(childKey));
        });
    });

    /// <summary>Creates one inline action in a declared section using the ordinary rollback/history.</summary>
    public HGSessionCommandResult InsertFlowStep(int generation, string section, int index, Type type) => Guard(() =>
    {
        if (generation != Generation) return HGSessionCommandResult.StaleGeneration;
        var list = HGFlowStructure.Editable(Document, context, section);
        if (index < 0 || index > list.Count || type == null || type.IsAbstract || type.ContainsGenericParameters
            || !typeof(GraphNodeContent).IsAssignableFrom(type) || !list.TryCreateElement(out var item)
            || item is not ActionSlotBase slot || HGReflect.CreateInstance(type) is not GraphNodeContent body || !slot.AcceptsBody(body))
            return HGSessionCommandResult.Rejected;
        return Apply(() =>
        {
            var node = new GraphNode(body); node.EnsureId(); slot.SetNode(node);
            if (!list.Insert(index, slot)) throw new ArgumentException("無法新增步驟。");
        });
    });

    public HGSessionCommandResult RemoveFlowStep(int generation, string path) => Guard(() =>
    {
        if (generation != Generation) return HGSessionCommandResult.StaleGeneration;
        var step = HGFlowAddress.Step(Document, context, path);
        HGFlowStructure.Editable(Document, context, path.Substring(0, path.LastIndexOf('/')));
        return RemoveSlot(step.List, step.Index);
    });

    /// <summary>Returns the declared section's action family without exposing its mutable list.</summary>
    public Type FlowActionBase(string section)
    {
        var list = HGFlowAddress.Section(Document, context, section);
        var type = SlotListElementType(list);
        return HGReflect.CreateInstance(type) is ActionSlotBase slot ? slot.BodyBaseType : null;
    }
}
}
