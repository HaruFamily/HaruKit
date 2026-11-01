namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class HaruGraphWindow
{
    private bool CurrentFlowCommand(HGModel expected, int generation)
        => focus.Kind == HGFocusKind.Root && QueryDocument(expected) != null && generation == graphGeneration;

    private HGListItemSource EditableFlowList(string sectionPath)
    {
        IList list = HGFlowAddress.Section(model.Data, activeContext, sectionPath);
        foreach (var node in graph.Nodes)
            foreach (var row in HGGraph.AllRows(node.Rows))
                if (row.Items is HGListItemSource items && ReferenceEquals(items.List, list))
                {
                    if (row.Locked || node.InLockedSubtree || row.Descriptor?.ReadOnly == true || row.Field?.IsInitOnly == true || !items.CanEditStructure)
                        throw new ArgumentException("此分支為唯讀。");
                    return items;
                }
        throw new ArgumentException("此分支未提供可編輯欄位。");
    }

    private HGSessionCommandResult ApplyGraphMutation(Action mutation)
    {
        if (!TryMutateContent(() =>
        {
            bool tracking = model.TrackChanges;
            try { model.TrackChanges = false; mutation(); }
            finally { model.TrackChanges = tracking; }
            Invalidate();
        }, out var error))
        {
            ShowNotification(new GUIContent("圖編輯失敗：" + error));
            Repaint();
            return HGSessionCommandResult.Rejected;
        }
        Repaint();
        return HGSessionCommandResult.Changed;
    }

    internal HGSessionCommandResult InsertFlowStep(HGModel expected, int generation, string path, int index, Type type)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            var items = EditableFlowList(path);
            if (index < 0 || index > items.Count || type == null || type.IsAbstract || type.ContainsGenericParameters
                || !typeof(GraphNodeContent).IsAssignableFrom(type)) return HGSessionCommandResult.Rejected;
            if (!items.TryCreateElement(out var item) || item is not ActionSlotBase slot
                || HGReflect.CreateInstance(type) is not GraphNodeContent body || !slot.AcceptsBody(body)) return HGSessionCommandResult.Rejected;
            return ApplyGraphMutation(() =>
            {
                PreserveVisibleNodePositions();
                if (!items.Insert(index, slot)) throw new ArgumentException("無法新增步驟。");
                var carrier = new GraphNode(body); carrier.EnsureId(); slot.SetNode(carrier);
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

    internal HGSessionCommandResult ChangeFlowStep(HGModel expected, int generation, string path, int move)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            var step = HGFlowAddress.Step(model.Data, activeContext, path);
            var items = EditableFlowList(path.Substring(0, path.LastIndexOf('/')));
            if (move != 0 && move != -1 && move != 1) return HGSessionCommandResult.Rejected;
            if (move != 0 && (step.Index + move < 0 || step.Index + move >= items.Count)) return HGSessionCommandResult.NoChange;
            return ApplyGraphMutation(() =>
            {
                PreserveVisibleNodePositions();
                if (move != 0) { if (!items.Move(step.Index, step.Index + move)) throw new ArgumentException("無法移動步驟。"); }
                else
                {
                    var sources = CaptureRemovedItemSources(step.Slot);
                    if (!items.RemoveAt(step.Index)) throw new ArgumentException("無法移除步驟。");
                    PreserveRemovedItemSources(sources);
                }
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

    private HGRow EditableFlowRow(string nodeId, string fieldPath)
    {
        var node = NodeOfId(nodeId);
        var row = RowOf(nodeId, fieldPath);
        if (node == null || node.InLockedSubtree || row == null || row.Locked || row.Descriptor?.ReadOnly == true
            || row.Field?.IsInitOnly == true) throw new ArgumentException("欄位已變更或為唯讀。");
        if (!HGFlowAddress.Contains(model.Data, row.InputSlot ?? row.Target))
            throw new ArgumentException("欄位不屬於目前文件的可編輯資料。");
        return row;
    }

    private static Type FlowValueType(HGRow row)
        => row.InputSlot is FormulaSlotBase formula ? formula.DefaultEditType
            : row.InputSlot != null ? null : row.Descriptor?.ValueType ?? row.Field?.FieldType;

    private static bool PlainFlowValue(HGRow row)
        => HGGraphSnapshotSafety.CanEditValueType(FlowValueType(row)) && (row.Field == null
            || !Attribute.IsDefined(row.Field, typeof(HGOptionsAttribute)) && !Attribute.IsDefined(row.Field, typeof(HGSpinnerAttribute)));

    internal HGSessionCommandResult SetFlowValue(HGModel expected, int generation, string nodeId, string fieldPath, object value)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            var row = EditableFlowRow(nodeId, fieldPath);
            Type type = FlowValueType(row);
            if (!PlainFlowValue(row) || (value == null ? type.IsValueType : !type.IsInstanceOfType(value))) return HGSessionCommandResult.Rejected;
            if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)) || value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
                return HGSessionCommandResult.Rejected;
            if (row.Field != null && (value is int || value is float))
            {
                double number = Convert.ToDouble(value);
                var range = Attribute.GetCustomAttribute(row.Field, typeof(RangeAttribute)) as RangeAttribute;
                var min = Attribute.GetCustomAttribute(row.Field, typeof(MinAttribute)) as MinAttribute;
                if (range != null && (number < range.min || number > range.max) || min != null && number < min.min) return HGSessionCommandResult.Rejected;
            }
            object before = row.InputSlot is FormulaSlotBase input ? input.DefaultObject
                : row.Descriptor != null ? row.Descriptor.Read(row.Target) : row.Field.GetValue(row.Target);
            if (Equals(before, value)) return HGSessionCommandResult.NoChange;
            return ApplyGraphMutation(() =>
            {
                if (row.InputSlot is FormulaSlotBase formula) formula.DefaultObject = value;
                else if (row.Descriptor != null)
                { if (!row.Descriptor.TryWrite(row.Target, value, out var error)) throw error ?? new ArgumentException("欄位拒絕此值。"); }
                else row.Field.SetValue(row.Target, value);
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

    internal HGSessionCommandResult SetFlowSource(HGModel expected, int generation, string nodeId, string fieldPath,
        string stepPath, HGCarrierSource source)
    {
        if (!CurrentFlowCommand(expected, generation)) return HGSessionCommandResult.StaleGeneration;
        try
        {
            GraphSlotBase slot;
            if (stepPath != null)
            {
                var step = HGFlowAddress.Step(model.Data, activeContext, stepPath);
                EditableFlowList(stepPath.Substring(0, stepPath.LastIndexOf('/')));
                slot = step.Slot;
            }
            else slot = EditableFlowRow(nodeId, fieldPath).InputSlot;
            if (slot == null) return HGSessionCommandResult.Rejected;
            if (source == null) return slot.Node == null ? HGSessionCommandResult.NoChange
                : ApplyGraphMutation(() => AttachSource(slot, null));
            if (!source.IsValid || !source.Accepts(slot)
                || source.Token != null && !model.OwnerTokens.Contains(source.Token)
                || source.Property != null && CurrentProperties()?.Contains(source.Property) != true
                || source.Content != null && WouldCreateCycle(slot, source.Content)
                || source.Token != null && WouldCreateCycle(slot, source.Token.Slot)) return HGSessionCommandResult.Rejected;
            if (slot.Node != null && source.Matches(slot.Node)) return HGSessionCommandResult.NoChange;
            return ApplyGraphMutation(() =>
            {
                PreserveVisibleNodePositions();
                var carrier = new GraphNode();
                var shared = new List<object>(); shared.AddRange(model.OwnerTokens); shared.AddRange(VisibleProperties());
                if (source.AssetObject != null && slot.Node?.Kind == NodeKind.Asset)
                {
                    foreach (var current in model.AllSlots()) if (current.Node != null) shared.Add(current.Node);
                    var bindings = GraphDeepCopy.Copy(slot.Node.Bindings, shared);
                    if (bindings == null) throw new InvalidOperationException("無法保留資產參數。");
                    carrier.Bindings.AddRange(bindings);
                }
                source.Apply(carrier, shared);
                AttachSource(slot, carrier);
                if (carrier.Kind == NodeKind.Asset) model.EnsureAssetBindings(carrier);
            });
        }
        catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); return HGSessionCommandResult.Rejected; }
    }

}
}
