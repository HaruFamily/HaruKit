namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;

/// <summary>Declared control structure; GraphKit does not execute or evaluate it.</summary>
public enum GraphFlowKind { Sequence, Conditional, Loop, FirstMatch }

/// <summary>The execution role of a section, independent of its display label.</summary>
public enum GraphFlowSectionRole { Body, WhenTrue, WhenFalse, Case, Default }

/// <summary>Optional structural description. Reading it must not evaluate, initialize or modify graph data.</summary>
public interface IGraphFlowContainer
{
    GraphFlowDescription DescribeFlow();
}

/// <summary>A named input of a control structure; the Slot is borrowed, never evaluated by the reader.</summary>
public sealed class GraphFlowInput
{
    public string Key { get; }
    public string Label { get; }
    public GraphSlotBase Slot { get; }

    public GraphFlowInput(string key, string label, GraphSlotBase slot)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Input key is required.", nameof(key));
        Key = key;
        Label = label;
        Slot = slot;
    }
}

/// <summary>Actual serialized child Slots in execution order. Null actions represent incomplete data.</summary>
public sealed class GraphFlowSection
{
    public string Key { get; }
    public string Label { get; }
    public GraphFlowSectionRole Role { get; }
    public GraphSlotBase Condition { get; }
    public IReadOnlyList<ActionSlotBase> Actions { get; }

    public GraphFlowSection(string key, string label, GraphFlowSectionRole role,
        IReadOnlyList<ActionSlotBase> actions, GraphSlotBase condition = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Section key is required.", nameof(key));
        Key = key;
        Label = label;
        Role = role;
        Actions = actions;
        Condition = condition;
    }
}

/// <summary>Nonserialized declaration borrowing live Slots. Consumers must copy presentation data before retaining it.</summary>
public sealed class GraphFlowDescription
{
    public GraphFlowKind Kind { get; }
    public IReadOnlyList<GraphFlowInput> Inputs { get; }
    public IReadOnlyList<GraphFlowSection> Sections { get; }

    public GraphFlowDescription(GraphFlowKind kind, IReadOnlyList<GraphFlowSection> sections,
        IReadOnlyList<GraphFlowInput> inputs = null)
    {
        Kind = kind;
        Sections = sections == null ? throw new ArgumentNullException(nameof(sections))
            : new List<GraphFlowSection>(sections).AsReadOnly();
        Inputs = inputs == null ? Array.Empty<GraphFlowInput>() : new List<GraphFlowInput>(inputs).AsReadOnly();
    }

    public static GraphFlowDescription Sequence(IReadOnlyList<ActionSlotBase> actions)
        => new GraphFlowDescription(GraphFlowKind.Sequence, new[]
        {
            new GraphFlowSection("body", "動作", GraphFlowSectionRole.Body, actions),
        });
}
}
