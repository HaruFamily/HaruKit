namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The presentation role of a Tool-owned descriptor field.</summary>
public enum HGFieldRole
{
    Value,
    Slot,
    Group,
    List,
}

/// <summary>One typed field exposed by a Tool-owned node descriptor.</summary>
public sealed class HGFieldDescriptor
{
    public string Id { get; }
    public Type ValueType { get; }
    public HGFieldRole Role { get; }
    public string Label { get; }
    public string Description { get; }
    public bool HideLabel { get; }
    public int LabelWidthUnits { get; }
    public float LabelWidthRatio { get; }
    public bool ForceEnumButtons { get; }
    public bool ReadOnly { get; }
    public Func<object, object> Read { get; }
    public Action<object, object> Write { get; }
    public Func<object, bool> IsVisible { get; }
    private Func<object, object> CreateMissing { get; }

    private HGFieldDescriptor(string id, Type valueType, HGFieldRole role, string label, string description, bool hideLabel,
        int labelWidthUnits, float labelWidthRatio, bool forceEnumButtons, bool readOnly, Func<object, object> read,
        Action<object, object> write, Func<object, bool> isVisible, Func<object, object> createMissing = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Field id is required.", nameof(id));
        Id = id;
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Role = role;
        Label = label ?? id;
        Description = description;
        if (labelWidthUnits < 0) throw new ArgumentOutOfRangeException(nameof(labelWidthUnits));
        if (labelWidthRatio < 0f) throw new ArgumentOutOfRangeException(nameof(labelWidthRatio));
        HideLabel = hideLabel;
        LabelWidthUnits = labelWidthUnits;
        LabelWidthRatio = labelWidthRatio;
        ForceEnumButtons = forceEnumButtons;
        ReadOnly = readOnly;
        Read = read ?? throw new ArgumentNullException(nameof(read));
        Write = write;
        IsVisible = isVisible;
        CreateMissing = createMissing;
        if (role == HGFieldRole.Value && !readOnly && write == null) throw new ArgumentNullException(nameof(write));
    }

    /// <summary>Creates a descriptor while keeping target and value casts inside the Tool adapter.</summary>
    public static HGFieldDescriptor Create<TTarget, TValue>(string id, Func<TTarget, TValue> read,
        Action<TTarget, TValue> write = null, string label = null, string description = null, bool hideLabel = false,
        int labelWidthUnits = 0, float labelWidthRatio = 0f, bool forceEnumButtons = false,
        Func<TTarget, bool> isVisible = null)
        where TTarget : class
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        Action<object, object> typedWrite = null;
        if (write != null) typedWrite = (target, value) => write((TTarget)target, (TValue)value);
        return new HGFieldDescriptor(id, typeof(TValue), HGFieldRole.Value, label, description, hideLabel, labelWidthUnits,
            labelWidthRatio, forceEnumButtons, write == null, target => read((TTarget)target), typedWrite,
            isVisible == null ? null : target => isVisible((TTarget)target));
    }

    /// <summary>Creates a slot descriptor. Linking edits the referenced slot, never replaces the field instance.</summary>
    public static HGFieldDescriptor CreateSlot<TTarget, TSlot>(string id, Func<TTarget, TSlot> read,
        string label = null, string description = null, bool hideLabel = false, int labelWidthUnits = 0,
        float labelWidthRatio = 0f, Func<TTarget, bool> isVisible = null)
        where TTarget : class
        where TSlot : GraphSlotBase
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        return new HGFieldDescriptor(id, typeof(TSlot), HGFieldRole.Slot, label, description, hideLabel, labelWidthUnits,
            labelWidthRatio, false, false, target => read((TTarget)target), null,
            isVisible == null ? null : target => isVisible((TTarget)target));
    }

    /// <summary>Creates a slot descriptor that can normalize a missing slot in the current working copy.</summary>
    public static HGFieldDescriptor CreateSlotWithFactory<TTarget, TSlot>(string id, Func<TTarget, TSlot> read,
        Action<TTarget, TSlot> write, Func<TTarget, TSlot> create, string label = null, string description = null,
        bool hideLabel = false, int labelWidthUnits = 0, float labelWidthRatio = 0f, Func<TTarget, bool> isVisible = null)
        where TTarget : class
        where TSlot : GraphSlotBase
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        if (write == null) throw new ArgumentNullException(nameof(write));
        if (create == null) throw new ArgumentNullException(nameof(create));
        return new HGFieldDescriptor(id, typeof(TSlot), HGFieldRole.Slot, label, description, hideLabel, labelWidthUnits,
            labelWidthRatio, false, false, target => read((TTarget)target),
            (target, value) => write((TTarget)target, (TSlot)value),
            isVisible == null ? null : target => isVisible((TTarget)target), target => create((TTarget)target));
    }

    /// <summary>Creates an explicitly expandable group; GraphKit does not infer this role from the value type.</summary>
    public static HGFieldDescriptor CreateGroup<TTarget, TGroup>(string id, Func<TTarget, TGroup> read,
        string label = null, string description = null, bool hideLabel = false, Func<TTarget, bool> isVisible = null)
        where TTarget : class
        where TGroup : class
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        return new HGFieldDescriptor(id, typeof(TGroup), HGFieldRole.Group, label, description, hideLabel, 0, 0f, false,
            true, target => read((TTarget)target), null, isVisible == null ? null : target => isVisible((TTarget)target));
    }

    /// <summary>Creates an IList-backed descriptor with an explicit element type.</summary>
    public static HGFieldDescriptor CreateList<TTarget, TElement>(string id, Func<TTarget, System.Collections.IList> read,
        string label = null, string description = null, bool hideLabel = false, Func<TTarget, bool> isVisible = null)
        where TTarget : class
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        return new HGFieldDescriptor(id, typeof(TElement), HGFieldRole.List, label, description, hideLabel, 0, 0f, false,
            false, target => read((TTarget)target), null, isVisible == null ? null : target => isVisible((TTarget)target));
    }

    /// <summary>Creates a list descriptor that can normalize a missing IList in the current working copy.</summary>
    public static HGFieldDescriptor CreateListWithFactory<TTarget, TList, TElement>(string id, Func<TTarget, TList> read,
        Action<TTarget, TList> write, Func<TTarget, TList> create, string label = null, string description = null,
        bool hideLabel = false, Func<TTarget, bool> isVisible = null)
        where TTarget : class
        where TList : class, System.Collections.IList
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        if (write == null) throw new ArgumentNullException(nameof(write));
        if (create == null) throw new ArgumentNullException(nameof(create));
        return new HGFieldDescriptor(id, typeof(TElement), HGFieldRole.List, label, description, hideLabel, 0, 0f, false,
            false, target => read((TTarget)target), (target, value) => write((TTarget)target, (TList)value),
            isVisible == null ? null : target => isVisible((TTarget)target), target => create((TTarget)target));
    }

    /// <summary>Evaluates a Tool-owned visibility predicate without leaking predicate failures into the build loop.</summary>
    public bool TryGetVisibility(object target, out bool visible, out Exception exception)
    {
        try
        {
            visible = IsVisible?.Invoke(target) ?? true;
            exception = null;
            return true;
        }
        catch (Exception caught)
        {
            visible = true;
            exception = caught;
            return false;
        }
    }

    /// <summary>Writes a drawer result only when it matches the descriptor contract.</summary>
    public bool TryWrite(object target, object value, out Exception exception)
    {
        exception = null;
        if (ReadOnly || Write == null || target == null || !AcceptsValue(value)) return false;
        try
        {
            Write(target, value);
            return true;
        }
        catch (Exception caught)
        {
            exception = caught;
            return false;
        }
    }

    /// <summary>Creates and assigns a missing value only when this descriptor explicitly owns that normalization.</summary>
    public bool TryCreateMissing(object target, out object value, out Exception exception)
    {
        exception = null;
        try
        {
            value = null;
            if (CreateMissing == null || Write == null) return false;
            value = CreateMissing(target);
            if (value == null) throw new InvalidOperationException("The field factory returned null.");
            Write(target, value);
            return true;
        }
        catch (Exception caught)
        {
            value = null;
            exception = caught;
            return false;
        }
    }

    private bool AcceptsValue(object value)
    {
        if (value == null)
            return !ValueType.IsValueType || Nullable.GetUnderlyingType(ValueType) != null;
        return (Nullable.GetUnderlyingType(ValueType) ?? ValueType).IsInstanceOfType(value);
    }
}

/// <summary>Complete Tool-owned metadata for one node type. It never merges with reflection metadata.</summary>
public sealed class HGNodeDescriptor
{
    public Type NodeType { get; }
    public IReadOnlyList<HGFieldDescriptor> Fields { get; }

    public HGNodeDescriptor(Type nodeType, IReadOnlyList<HGFieldDescriptor> fields)
    {
        NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        if (fields == null) throw new ArgumentNullException(nameof(fields));

        var ids = new HashSet<string>();
        var copy = new List<HGFieldDescriptor>(fields.Count);
        foreach (var field in fields)
        {
            if (field == null) throw new ArgumentException("Descriptor fields cannot contain null.", nameof(fields));
            if (!ids.Add(field.Id)) throw new ArgumentException($"Duplicate field id '{field.Id}'.", nameof(fields));
            copy.Add(field);
        }
        Fields = copy.AsReadOnly();
    }
}

/// <summary>Session-scoped metadata supplied explicitly by a Tool; absent descriptors fall back to GraphKit reflection.</summary>
public interface IHGEditorMetadataProvider
{
    bool TryGetNodeDescriptor(Type nodeType, out HGNodeDescriptor descriptor);
    bool TryGetValueDrawer(Type valueType, out IHGValueDrawer drawer);
}

/// <summary>Context supplied to a typed value drawer without exposing HGRow or the Editor model.</summary>
public readonly struct HGValueDrawerContext
{
    public HGFieldDescriptor Field { get; }
    public object Target { get; }
    public bool ReadOnly { get; }

    public HGValueDrawerContext(HGFieldDescriptor field, object target, bool readOnly)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Target = target;
        ReadOnly = readOnly || field.ReadOnly;
    }
}

/// <summary>Value-drawer output; the caller owns the subsequent write and transaction.</summary>
public readonly struct HGValueDrawerResult
{
    public bool Changed { get; }
    public object Value { get; }

    public HGValueDrawerResult(bool changed, object value)
    {
        Changed = changed;
        Value = value;
    }

    public static HGValueDrawerResult Unchanged(object value) => new(false, value);
}

/// <summary>Typed Tool drawing contract. Measure returns the field control height; Measure and Draw receive the same context and width.</summary>
public interface IHGValueDrawer
{
    float Measure(in HGValueDrawerContext context, float width);
    HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value);
}

/// <summary>Typed drawing adapter; runtime type checks stay at the editor boundary.</summary>
public abstract class HGValueDrawer<T> : IHGValueDrawer
{
    public abstract float Measure(in HGValueDrawerContext context, float width);
    public abstract HGValueDrawerResult DrawValue(Rect rect, in HGValueDrawerContext context, T value);

    public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value)
    {
        if (value is T typed) return DrawValue(rect, context, typed);
        if (value == null && default(T) is null) return DrawValue(rect, context, default);
        throw new ArgumentException("Drawer value does not match " + typeof(T).FullName);
    }
}
}
