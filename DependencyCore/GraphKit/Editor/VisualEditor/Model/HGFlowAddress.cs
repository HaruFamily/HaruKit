namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>Resolves generation-local use paths against actual declared action lists, never preview carrier IDs.</summary>
internal static class HGFlowAddress
{
    internal static IList Section(IGraphDocument document, HGEditorExtensionContext context, string path)
    {
        var parts = (path ?? "").Split('/');
        if (parts.Length < 3 || parts.Length > 131 || parts[0] != "root" || !int.TryParse(parts[1], out int index))
            throw new ArgumentException("流程位置無效，請重新讀取。");
        var roots = (context.Profile?.RootAdapter ?? HGModel.HGDocumentRootAdapter.Instance).ReadRoots(document);
        if (index < 0 || index >= roots.Count) throw new ArgumentException("流程入口已變更。");
        object content = roots[index].Root;
        for (int p = 2; p < parts.Length; p += 2)
        {
            var description = Describe(content, context);
            var sections = description?.Sections.Where(section => section != null && section.Key == Uri.UnescapeDataString(parts[p])).ToArray();
            if (sections == null || sections.Length != 1) throw new ArgumentException("流程分支已變更或未宣告。");
            if (sections[0].Actions is not IList list || HGDocumentSession<IGraphDocument>.SlotListElementType(list) == null)
                throw new ArgumentException("此分支不支援清單編輯。");
            if (p == parts.Length - 1)
            {
                if (!Contains(document, list)) throw new ArgumentException("流程描述沒有指向文件的實際清單。");
                return list;
            }
            if (p + 1 >= parts.Length || !int.TryParse(parts[p + 1], out int item) || item < 0 || item >= list.Count
                || list[item] is not ActionSlotBase slot || slot.Node?.Kind != NodeKind.Inline)
                throw new ArgumentException("子流程位置已變更。");
            content = slot.Node.BodyObject;
        }
        throw new ArgumentException("流程位置不是分支。");
    }

    internal static (IList List, int Index, ActionSlotBase Slot) Step(IGraphDocument document, HGEditorExtensionContext context, string path)
    {
        int split = path?.LastIndexOf('/') ?? -1;
        if (split < 0 || !int.TryParse(path.Substring(split + 1), out int index)) throw new ArgumentException("步驟位置無效。");
        var list = Section(document, context, path.Substring(0, split));
        if (index < 0 || index >= list.Count || list[index] is not ActionSlotBase slot) throw new ArgumentException("步驟已變更。");
        return (list, index, slot);
    }

    internal static GraphFlowDescription Describe(object content, HGEditorExtensionContext context)
    {
        if (context.FlowMetadata != null && context.FlowMetadata.TryDescribeFlow(content, out var declared)) return declared;
        if (content is IGraphFlowContainer flow) return flow.DescribeFlow();
        return content is ISequentialActionContainer sequence ? GraphFlowDescription.Sequence(sequence.SequentialActions) : null;
    }

    internal static bool Contains(object document, object target)
    {
        var pending = new Stack<object>();
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        pending.Push(document);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == null || current is string || current is Type || current is UnityEngine.Object || !visited.Add(current)) continue;
            if (visited.Count > 20000) throw new ArgumentException("流程清單歸屬檢查超過上限。");
            if (ReferenceEquals(current, target)) return true;
            if (current is IList list) { foreach (var item in list) pending.Push(item); continue; }
            if (current.GetType().IsPrimitive || current.GetType().IsEnum) continue;
            foreach (var field in HGReflect.Fields(current.GetType()))
                if (!field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized && (field.IsPublic
                    || Attribute.IsDefined(field, typeof(UnityEngine.SerializeField)) || Attribute.IsDefined(field, typeof(UnityEngine.SerializeReference))))
                    pending.Push(field.GetValue(current));
        }
        return false;
    }
}
}
