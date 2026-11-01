namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

public static partial class HaruGraphDraft
{
    private static readonly string[] flowActions = { "flow", "flow_candidates", "flow_insert", "flow_remove", "flow_transfer", "flow_wrap" };

    private static int? FlowInt(ToolParams input, string name)
    {
        var token = input.GetRaw(name);
        if (token == null) return null;
        if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out int number))
            throw new ArgumentException(name + " requires an int32.");
        return number;
    }

    private static object BoundedFlowResponse(object response)
        => JObject.FromObject(response).ToString(Newtonsoft.Json.Formatting.None).Length > 1048576
            ? Error("result_limit", "Flow/geometry response exceeds one million characters; reduce page_size or choose a smaller scope.") : response;

    private static object FlowCommand(Draft draft, ToolParams input, string action, int offset, int pageSize)
    {
        var session = draft.Session;
        if (action == "flow")
        {
            var flow = session.CaptureFlow();
            var entries = new List<object>();
            void Sections(IReadOnlyList<HGFlowSectionView> sections, string parent)
            {
                foreach (var section in sections)
                {
                    entries.Add(new { kind = "section", path = section.Path, parent_path = parent, label = section.Label,
                        role = section.Role.ToString(), condition = section.Condition, count = section.Steps.Count });
                    foreach (var step in section.Steps)
                    {
                        entries.Add(new { kind = "step", path = step.Path, parent_path = section.Path, node_id = step.NodeId,
                            label = step.Label, source_kind = step.SourceKind.ToString(), flow_kind = step.FlowKind?.ToString(),
                            shared = step.IsShared, references = step.ReferenceCount, disabled = step.Disabled,
                            unknown = step.Unknown, cycle = step.Cycle, empty = step.Empty, inputs = step.Inputs });
                        Sections(step.Sections, step.Path);
                    }
                }
            }
            foreach (var root in flow.Roots) Sections(root.Sections, root.Path);
            return new SuccessResponse("Declared flow uses, not guessed coordinates. Paths expire after every mutation; shared node fields affect every use. External references and unknown containers are not expanded.", new
            {
                generation = session.Generation, complete = flow.Complete, issues = flow.Issues,
                roots = flow.Roots.Select(root => new { path = root.Path, label = root.Label, kind = root.FlowKind?.ToString() }).ToArray(),
                total = entries.Count, next_offset = (long)offset + pageSize < entries.Count ? (int?)(offset + pageSize) : null,
                entries = entries.Skip(offset).Take(pageSize).ToArray(),
            });
        }
        string section = input.Get("section_path");
        if (action == "flow_remove")
            return CommandResult(draft, session.RemoveFlowStep(session.Generation, input.Get("flow_path")), action);
        if (action == "flow_transfer")
        {
            int? index = FlowInt(input, "index"), count = FlowInt(input, "count"), gap = FlowInt(input, "destination_index");
            if (!index.HasValue || !count.HasValue || !gap.HasValue) return Error("range_required", "Provide index, count and destination_index (pre-removal gap).");
            return CommandResult(draft, session.TransferFlowSteps(session.Generation, section, index.Value, count.Value,
                input.Get("target_section_path"), gap.Value), action);
        }
        var types = AuthoringTypes(draft, session.FlowActionBase(section)).ToArray();
        if (action == "flow_candidates")
            return new SuccessResponse("Exact inline action types for the declared section. Containers remain consumer-declared; flow_wrap requires an empty Conditional or Loop with a valid child_section_key.", new
            {
                generation = session.Generation, section_path = section, total = types.Length,
                next_offset = (long)offset + pageSize < types.Length ? (int?)(offset + pageSize) : null,
                entries = types.Skip(offset).Take(pageSize).Select(type =>
                {
                    GraphFlowDescription description = null;
                    string descriptionError = null;
                    try
                    {
                        var body = HGReflect.CreateInstance(type);
                        if (draft.Context.FlowMetadata?.TryDescribeFlow(body, out description) != true)
                            description = (body as IGraphFlowContainer)?.DescribeFlow();
                    }
                    catch (Exception exception) { descriptionError = exception.Message; }
                    return new { type = type.AssemblyQualifiedName, label = HGReflect.TypeName(type), kind = description?.Kind.ToString(),
                        description_error = descriptionError,
                        child_sections = description?.Sections.Select(child => new { key = child.Key, label = child.Label, role = child.Role.ToString() }).ToArray() };
                }).ToArray(),
            });
        var selected = types.SingleOrDefault(type => type.AssemblyQualifiedName == input.Get("type"));
        int? at = FlowInt(input, "index");
        if (selected == null || !at.HasValue) return Error("flow_target_required", "Select an exact type from flow_candidates and provide index.");
        if (action == "flow_insert") return CommandResult(draft, session.InsertFlowStep(session.Generation, section, at.Value, selected), action);
        int? size = FlowInt(input, "count");
        if (!size.HasValue) return Error("range_required", "Provide count for flow_wrap.");
        return CommandResult(draft, session.WrapFlowSteps(session.Generation, section, at.Value, size.Value, selected,
            input.Get("child_section_key")), action);
    }
}
}
