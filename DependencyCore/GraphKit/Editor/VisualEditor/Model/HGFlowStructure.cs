namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared structure plans for window and background sessions; never reads screen coordinates.</summary>
internal static class HGFlowStructure
{
    internal static (HGListItemSource From, HGListItemSource To, List<ActionSlotBase> Slots, int Gap) ResolveTransfer(
        HGListItemSource from, int index, int count, HGListItemSource to, int gap)
    {
        if (count < 1 || count > 4096 || index < 0 || index > from.Count - count || gap < 0 || gap > to.Count)
            throw new ArgumentException("移動範圍或插入位置已變更。");
        var slots = new List<ActionSlotBase>();
        for (int i = 0; i < count; i++)
        {
            if (from.Get(index + i) is not ActionSlotBase slot || !to.ElementType.IsInstanceOfType(slot))
                throw new ArgumentException("目的分支不接受原步驟型別。");
            if (HGFlowAddress.Contains(slot, to.List)) throw new ArgumentException("不能移入自己或自己的子流程。");
            slots.Add(slot);
        }
        int destination = ReferenceEquals(from.List, to.List) && gap > index ? Math.Max(index, gap - count) : gap;
        return (from, to, slots, destination);
    }

    internal static (ActionSlotBase Slot, GraphNode Node, HGListItemSource Children) CreateWrapper(
        HGEditorExtensionContext context, HGListItemSource parent, Type containerType, string childSectionKey, IReadOnlyList<ActionSlotBase> selected)
    {
        if (containerType == null || containerType.IsAbstract || containerType.ContainsGenericParameters
            || !typeof(GraphNodeContent).IsAssignableFrom(containerType)
            || !parent.TryCreateElement(out var item) || item is not ActionSlotBase wrapper
            || HGReflect.CreateInstance(containerType) is not GraphNodeContent body || !wrapper.AcceptsBody(body))
            throw new ArgumentException("此分支不接受所選容器。");
        var description = HGFlowAddress.Describe(body, context);
        if (description == null || description.Kind != GraphFlowKind.Conditional && description.Kind != GraphFlowKind.Loop
            || description.Sections.Count == 0 || description.Sections.Any(section => section?.Actions == null || section.Actions.Count != 0))
            throw new ArgumentException("包裝容器必須宣告條件或迴圈，且所有子步驟初始為空。");
        bool validShape = description.Kind == GraphFlowKind.Conditional
            ? description.Inputs.Count == 1 && description.Sections.Count == 2
                && description.Sections[0].Role == GraphFlowSectionRole.WhenTrue && description.Sections[1].Role == GraphFlowSectionRole.WhenFalse
            : description.Inputs.Count > 0 && description.Sections.Count == 1 && description.Sections[0].Role == GraphFlowSectionRole.Body;
        if (!validShape || description.Inputs.Any(input => input?.Slot == null || !HGFlowAddress.Contains(body, input.Slot))
            || description.Inputs.Select(input => input.Key).Distinct(StringComparer.Ordinal).Count() != description.Inputs.Count
            || description.Sections.Any(section => section.Condition != null)
            || description.Sections.Select(section => section.Key).Distinct(StringComparer.Ordinal).Count() != description.Sections.Count)
            throw new ArgumentException("容器的流程宣告或條件輸入無效。");
        var matches = description.Sections.Where(section => section.Key == childSectionKey).ToArray();
        if (matches.Length != 1 || matches[0].Actions is not System.Collections.IList children
            || HGDocumentSession<IGraphDocument>.SlotListElementType(children) is not Type element
            || !HGFlowAddress.Contains(body, children) || HGFlowAddress.Contains(body, parent.List))
            throw new ArgumentException("容器未提供合法且獨立的子步驟清單。");
        if (description.Kind == GraphFlowKind.Loop && matches[0].Role != GraphFlowSectionRole.Body
            || description.Kind == GraphFlowKind.Conditional && matches[0].Role != GraphFlowSectionRole.WhenTrue
                && matches[0].Role != GraphFlowSectionRole.WhenFalse)
            throw new ArgumentException("包裝目標不是條件分支或迴圈本體。");
        foreach (var slot in selected)
            if (!element.IsInstanceOfType(slot)) throw new ArgumentException("容器子清單不接受原步驟型別。");
        var node = new GraphNode(body);
        node.EnsureId();
        wrapper.SetNode(node);
        return (wrapper, node, new HGListItemSource(children, element));
    }

    internal static HGListItemSource Editable(IGraphDocument document, HGEditorExtensionContext context, string path)
    {
        var live = HGFlowAddress.Section(document, context, path);
        var copy = GraphDeepCopy.Copy(document);
        HGGraphSnapshotSafety.RequireDetachedGraph(document, copy);
        var list = HGFlowAddress.Section(copy, context, path);
        var model = HGModel.CreateDetachedView(null, copy, context.Profile?.RootAdapter);
        var roots = model.ReadRootGroups().Where(root => root?.Root != null).Select(root => root.Root).ToArray();
        var graph = HGGraph.Build(model, roots, null, "flow-check", "流程", metadata: context.Metadata);
        if (graph.Diagnostics.Any(issue => issue.Severity == GraphDiagnosticSeverity.Error))
            throw new ArgumentException("流程欄位建圖失敗，不能確認可寫範圍。");
        foreach (var node in graph.Nodes)
            foreach (var row in HGGraph.AllRows(node.Rows))
                if (row.Items is HGListItemSource items && ReferenceEquals(items.List, list))
                {
                    if (row.Locked || node.InLockedSubtree || row.Descriptor?.ReadOnly == true || row.Field?.IsInitOnly == true || !items.CanEditStructure)
                        throw new ArgumentException("此分支為唯讀。");
                    return new HGListItemSource(live, HGDocumentSession<IGraphDocument>.SlotListElementType(live));
                }
        throw new ArgumentException("此分支未提供可編輯欄位。");
    }
}
}
