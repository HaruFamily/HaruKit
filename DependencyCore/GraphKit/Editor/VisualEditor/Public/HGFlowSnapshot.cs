namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;

/// <summary>Optional session-scoped flow metadata. A returned description replaces the container's declaration.</summary>
public interface IHGFlowMetadataProvider
{
    bool TryDescribeFlow(object content, out GraphFlowDescription description);
}

/// <summary>Copied input identity; no live Slot or graph object is exposed.</summary>
public sealed class HGFlowInputView
{
    public string Key { get; }
    public string Label { get; }
    public string SlotType { get; }
    public string SourceNodeId { get; }
    public string UnlinkedSummary { get; }
    public bool Missing { get; }

    internal HGFlowInputView(string key, string label, GraphSlotBase slot)
    {
        Key = key;
        Label = label;
        Missing = slot == null;
        SlotType = slot?.GetType().AssemblyQualifiedName;
        SourceNodeId = slot?.Node?.Id;
        if (slot is FormulaSlotBase formula && formula.Node == null)
        {
            object value = formula.DefaultObject;
            UnlinkedSummary = value == null ? "空" : value is bool flag ? (flag ? "是" : "否")
                : value is string text ? text : value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal
                    ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : "未接線設定";
        }
    }
}

public sealed class HGFlowSectionView
{
    public string Path { get; }
    public string Label { get; }
    public GraphFlowSectionRole Role { get; }
    public HGFlowInputView Condition { get; }
    public IReadOnlyList<HGFlowStep> Steps { get; }

    internal HGFlowSectionView(string path, GraphFlowSection source, List<HGFlowStep> steps)
    {
        Path = path;
        Label = source.Label;
        Role = source.Role;
        Condition = source.Condition == null ? null : new HGFlowInputView("condition", "條件", source.Condition);
        Steps = steps.AsReadOnly();
    }
}

/// <summary>One use position, not a new carrier. Paths expire whenever the source structure changes.</summary>
public sealed class HGFlowStep
{
    public string Path { get; }
    public string NodeId { get; }
    public string Label { get; }
    public NodeKind SourceKind { get; }
    public bool HasCustomLabel { get; }
    public bool Disabled { get; }
    public bool Empty { get; }
    public bool Cycle { get; internal set; }
    public bool Unknown { get; internal set; }
    public int ReferenceCount { get; internal set; }
    public bool IsShared => ReferenceCount > 1;
    public GraphFlowKind? FlowKind { get; internal set; }
    public IReadOnlyList<HGFlowInputView> Inputs { get; internal set; } = Array.Empty<HGFlowInputView>();
    public IReadOnlyList<HGFlowSectionView> Sections { get; internal set; } = Array.Empty<HGFlowSectionView>();

    internal HGFlowStep(string path, ActionSlotBase slot, bool parentDisabled)
    {
        var node = slot?.Node;
        Path = path;
        NodeId = node?.Id;
        SourceKind = node?.Kind ?? NodeKind.Empty;
        HasCustomLabel = !string.IsNullOrEmpty(slot?.Label);
        Label = string.IsNullOrEmpty(slot?.Label) ? node?.BodyObject?.GetType().Name ?? SourceKind.ToString() : slot.Label;
        Disabled = parentDisabled || slot?.Disabled == true || node?.Disabled == true;
        Empty = node == null || node.Kind == NodeKind.Empty;
    }
}

public sealed class HGFlowRoot
{
    public string Path { get; }
    public string Label { get; }
    public GraphFlowKind? FlowKind { get; internal set; }
    public bool Unknown => !FlowKind.HasValue;
    public IReadOnlyList<HGFlowInputView> Inputs { get; internal set; } = Array.Empty<HGFlowInputView>();
    public IReadOnlyList<HGFlowSectionView> Sections { get; internal set; } = Array.Empty<HGFlowSectionView>();

    internal HGFlowRoot(string path, string label) { Path = path; Label = label; }
}

public sealed class HGFlowIssue
{
    public string Code { get; }
    public string Path { get; }
    public string Message { get; }
    internal HGFlowIssue(string code, string path, string message) { Code = code; Path = path; Message = message; }
}

/// <summary>Bounded, read-only flow projection. Sharing counts cover projected action uses, not all document references.</summary>
public sealed class HGFlowSnapshot
{
    public IReadOnlyList<HGFlowRoot> Roots { get; }
    public IReadOnlyList<HGFlowIssue> Issues { get; }
    public bool Complete => Issues.Count == 0;

    private HGFlowSnapshot(List<HGFlowRoot> roots, List<HGFlowIssue> issues)
    { Roots = roots.AsReadOnly(); Issues = issues.AsReadOnly(); }

    public static HGFlowSnapshot Capture(IGraphDocument document, HGEditorExtensionContext context = null,
        int maxSteps = 4096, int maxDepth = 64)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (maxSteps < 1 || maxSteps > 65536 || maxDepth < 1 || maxDepth > 128)
            throw new ArgumentOutOfRangeException(nameof(maxSteps), "Flow limits require 1..65536 steps and 1..128 depth.");
        return new Reader(document, context ?? HGEditorExtensionContext.Default, maxSteps, maxDepth).Capture();
    }

    private sealed class Reader
    {
        private readonly IGraphDocument document;
        private readonly HGEditorExtensionContext context;
        private readonly int maxSteps;
        private readonly int maxDepth;
        private int stepCount;
        private int sectionCount;
        private int inputCount;
        private readonly List<HGFlowRoot> roots = new();
        private readonly List<HGFlowIssue> issues = new();
        private readonly HashSet<object> active = new(ReferenceComparer.Instance);
        private readonly Dictionary<object, List<HGFlowStep>> uses = new(ReferenceComparer.Instance);

        internal Reader(IGraphDocument document, HGEditorExtensionContext context, int maxSteps, int maxDepth)
        { this.document = document; this.context = context; this.maxSteps = maxSteps; this.maxDepth = maxDepth; }

        internal HGFlowSnapshot Capture()
        {
            try
            {
                var adapter = context.Profile?.RootAdapter ?? HGModel.HGDocumentRootAdapter.Instance;
                var sourceRoots = adapter.ReadRoots(document);
                if (sourceRoots == null) Issue("roots-missing", "", "Root adapter returned no collection.");
                else for (int i = 0; i < sourceRoots.Count; i++)
                {
                    string path = "root/" + i;
                    if (i >= maxSteps) { Issue("limit", path, "Root limit reached."); break; }
                    var source = sourceRoots[i];
                    var root = new HGFlowRoot(path, source == null ? null : document.TitleOf(source.Root));
                    roots.Add(root);
                    if (source?.Root == null) { Issue("missing-root", path, "Root adapter returned an empty root."); continue; }
                    var description = Describe(source?.Root, path);
                    if (description == null) continue;
                    root.FlowKind = description.Kind;
                    root.Inputs = Inputs(description);
                    root.Sections = Sections(description, path, false, 0);
                }
            }
            catch (Exception exception) { Issue("read-failed", "", exception.Message); }
            foreach (var list in uses.Values)
                foreach (var step in list) step.ReferenceCount = list.Count;
            return new HGFlowSnapshot(roots, issues);
        }

        private void Issue(string code, string path, string message) => issues.Add(new HGFlowIssue("graphkit.flow." + code, path, message));

        private GraphFlowDescription Describe(object content, string path)
        {
            try
            {
                GraphFlowDescription result;
                if (context.FlowMetadata != null && context.FlowMetadata.TryDescribeFlow(content, out result))
                    return Validate(result, path);
                if (content is IGraphFlowContainer flow) return Validate(flow.DescribeFlow(), path);
                if (content is ISequentialActionContainer sequence)
                    return Validate(GraphFlowDescription.Sequence(sequence.SequentialActions), path);
            }
            catch (Exception exception) { Issue("description-failed", path, exception.Message); }
            return null;
        }

        private GraphFlowDescription Validate(GraphFlowDescription description, string path)
        {
            if (description == null || !Enum.IsDefined(typeof(GraphFlowKind), description.Kind)
                || description.Sections.Count > maxSteps || description.Inputs.Count > maxSteps)
            { Issue("invalid-description", path, "Missing, invalid or oversized flow declaration."); return null; }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in description.Inputs)
                if (input == null || !keys.Add(input.Key) || input.Slot == null)
                { Issue("invalid-description", path, "Inputs require distinct keys and actual Slots."); return null; }
            keys.Clear();
            int count = description.Sections.Count;
            for (int i = 0; i < count; i++)
            {
                var section = description.Sections[i];
                if (section == null || !keys.Add(section.Key) || section.Actions == null)
                { Issue("invalid-description", path, "Sections require distinct keys and actual action lists."); return null; }
                bool valid = description.Kind switch
                {
                    GraphFlowKind.Sequence => count == 1 && section.Role == GraphFlowSectionRole.Body && section.Condition == null,
                    GraphFlowKind.Loop => count == 1 && section.Role == GraphFlowSectionRole.Body
                        && description.Inputs.Count > 0 && section.Condition == null,
                    GraphFlowKind.Conditional => count == 2 && description.Inputs.Count == 1 && section.Condition == null
                        && section.Role == (i == 0 ? GraphFlowSectionRole.WhenTrue : GraphFlowSectionRole.WhenFalse),
                    GraphFlowKind.FirstMatch => description.Inputs.Count == 0
                        && section.Role == (i == count - 1 ? GraphFlowSectionRole.Default : GraphFlowSectionRole.Case)
                        && (i == count - 1 ? section.Condition == null : section.Condition != null),
                    GraphFlowKind.RoundRobin => description.Inputs.Count == 0
                        && section.Role == GraphFlowSectionRole.Case && section.Condition == null,
                    _ => false,
                };
                if (!valid) { Issue("invalid-description", path, "Section roles do not match the declared control structure."); return null; }
            }
            if (count == 0) { Issue("invalid-description", path, "A control structure requires a section."); return null; }
            return description;
        }

        private IReadOnlyList<HGFlowInputView> Inputs(GraphFlowDescription description)
        {
            var inputs = new List<HGFlowInputView>();
            foreach (var input in description.Inputs)
            {
                if (inputCount >= maxSteps) { Issue("limit", "", "Input limit reached."); break; }
                inputCount++;
                inputs.Add(new HGFlowInputView(input.Key, input.Label, input.Slot));
            }
            return inputs.AsReadOnly();
        }

        private IReadOnlyList<HGFlowSectionView> Sections(GraphFlowDescription description, string path, bool disabled, int depth)
        {
            var sections = new List<HGFlowSectionView>();
            foreach (var section in description.Sections)
            {
                if (sectionCount >= maxSteps) { Issue("limit", path, "Section limit reached."); break; }
                sectionCount++;
                string sectionPath = path + "/" + Uri.EscapeDataString(section.Key);
                var steps = new List<HGFlowStep>();
                for (int i = 0; i < section.Actions.Count; i++)
                {
                    if (stepCount >= maxSteps) { Issue("limit", sectionPath, "Step limit reached."); break; }
                    var slot = section.Actions[i];
                    var step = new HGFlowStep(sectionPath + "/" + i, slot, disabled);
                    steps.Add(step);
                    stepCount++;
                    var node = slot?.Node;
                    if (slot == null) Issue("missing-slot", step.Path, "Action list contains a null Slot.");
                    if (node == null) continue;
                    if (!uses.TryGetValue(node, out var positions)) uses[node] = positions = new List<HGFlowStep>();
                    positions.Add(step);
                    if (step.Empty) continue;
                    if (!active.Add(node)) { step.Cycle = true; Issue("cycle", step.Path, "Recursive action use stopped."); continue; }
                    try
                    {
                        if (depth >= maxDepth) { Issue("limit", step.Path, "Flow depth limit reached."); continue; }
                        if (node.Kind != NodeKind.Inline) { step.Unknown = true; continue; }
                        if (node.BodyObject == null) { step.Unknown = true; Issue("missing-body", step.Path, "Inline carrier has no content."); continue; }
                        var nested = Describe(node.BodyObject, step.Path);
                        step.Unknown = nested == null;
                        if (nested == null) continue;
                        step.FlowKind = nested.Kind;
                        step.Inputs = Inputs(nested);
                        step.Sections = Sections(nested, step.Path, step.Disabled, depth + 1);
                    }
                    finally { active.Remove(node); }
                }
                sections.Add(new HGFlowSectionView(sectionPath, section, steps));
            }
            return sections.AsReadOnly();
        }
    }
}
}
