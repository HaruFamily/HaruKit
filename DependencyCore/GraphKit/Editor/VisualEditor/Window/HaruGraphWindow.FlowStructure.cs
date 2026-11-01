namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class HaruGraphWindow
{
    private (HGListItemSource From, HGListItemSource To, List<ActionSlotBase> Slots, int Gap) ResolveFlowTransfer(
        string sourceSection, int index, int count, string targetSection, int gap)
        => HGFlowStructure.ResolveTransfer(EditableFlowList(sourceSection), index, count, EditableFlowList(targetSection), gap);

    internal bool CanTransferFlowSteps(HGModel expected, int generation, string sourceSection, int index, int count, string targetSection, int gap)
    {
        // A hover query must not rebuild or normalize the live working document.
        if (!ReferenceEquals(expected, model) || graphDirty || generation != graphGeneration || focus.Kind != HGFocusKind.Root) return false;
        try { ResolveFlowTransfer(sourceSection, index, count, targetSection, gap); return true; }
        catch (Exception) { return false; }
    }

    internal HGSessionCommandResult TransferFlowSteps(HGModel expected, int generation, string sourceSection, int index, int count,
        string targetSection, int gap)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            var plan = ResolveFlowTransfer(sourceSection, index, count, targetSection, gap);
            if (ReferenceEquals(plan.From.List, plan.To.List) && plan.Gap == index) return HGSessionCommandResult.NoChange;
            return ApplyGraphMutation(() =>
            {
                PreserveVisibleNodePositions();
                for (int i = 0; i < count; i++)
                    if (!plan.From.RemoveAt(index)) throw new InvalidOperationException("無法移出原步驟。");
                for (int i = 0; i < count; i++)
                    if (!plan.To.Insert(plan.Gap + i, plan.Slots[i])) throw new InvalidOperationException("無法插入目的分支。");
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

    internal HGSessionCommandResult WrapFlowSteps(HGModel expected, int generation, string sourceSection, int index, int count,
        Type containerType, string childSectionKey)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            var parent = EditableFlowList(sourceSection);
            if (count < 1 || count > 4096 || index < 0 || index > parent.Count - count) return HGSessionCommandResult.Rejected;
            var selected = new List<ActionSlotBase>();
            for (int i = 0; i < count; i++)
            {
                if (parent.Get(index + i) is not ActionSlotBase slot) return HGSessionCommandResult.Rejected;
                selected.Add(slot);
            }
            var plan = HGFlowStructure.CreateWrapper(activeContext, parent, containerType, childSectionKey, selected);
            return ApplyGraphMutation(() =>
            {
                PreserveVisibleNodePositions();
                for (int i = 0; i < count; i++)
                    if (!parent.RemoveAt(index)) throw new InvalidOperationException("無法移出待包裝步驟。");
                foreach (var slot in selected)
                    if (!plan.Children.Add(slot)) throw new InvalidOperationException("容器無法接收原步驟。");
                if (!parent.Insert(index, plan.Slot)) throw new InvalidOperationException("無法插入外層容器。");
                graphDirty = true;
                EnsureGraph();
                // Recheck the inserted container's actual metadata, including hidden or readonly child fields.
                EditableFlowList(sourceSection + "/" + index + "/" + Uri.EscapeDataString(childSectionKey));
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

}
}
