namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static object Geometry(Draft draft, ToolParams input, bool preview)
    {
        var layout = draft.Session.CaptureLayout(input.Get("scope_id"), input.Get("group_id"), FlowInt(input, "tab_index"));
        bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        if (layout.Nodes.Any(node => !Finite(node.Rect.x) || !Finite(node.Rect.y) || !Finite(node.Rect.width) || !Finite(node.Rect.height)
            || !Finite(node.Rect.xMax) || !Finite(node.Rect.yMax))
            || layout.Edges.Any(edge => !Finite(edge.Input.x) || !Finite(edge.Input.y) || !Finite(edge.Output.x) || !Finite(edge.Output.y))
            || layout.Ports.Any(port => !Finite(port.Position.x) || !Finite(port.Position.y)))
            return Error("invalid_geometry", "Canvas geometry contains nonfinite coordinates.");
        var byId = layout.Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        bool ContainsNode(HGLayoutNode parent, HGLayoutNode child)
        {
            for (int depth = 0; depth <= HGGraph.MaxNestedDepth && child.ParentNodeId != null; depth++)
            {
                if (child.ParentNodeId == parent.Id) return true;
                if (!byId.TryGetValue(child.ParentNodeId, out var next)) break;
                child = next;
            }
            return false;
        }
        var overlaps = new List<object>();
        int overlapCount = 0;
        for (int i = 0; i < layout.Nodes.Count; i++)
            for (int j = i + 1; j < layout.Nodes.Count; j++)
                if (!ContainsNode(layout.Nodes[i], layout.Nodes[j]) && !ContainsNode(layout.Nodes[j], layout.Nodes[i])
                    && layout.Nodes[i].Rect.Overlaps(layout.Nodes[j].Rect))
                {
                    overlapCount++;
                    if (overlaps.Count < 64) overlaps.Add(new { first = layout.Nodes[i].Id, second = layout.Nodes[j].Id });
                }
        string svg = preview ? LayoutSvg(layout) : null;
        if (svg?.Length > 1048576) return Error("preview_limit", "SVG exceeds one million characters; choose a smaller scope.");
        return new SuccessResponse("Background measurement on a detached copy. No opening, focusing, resizing, visible tab switching, saving or window events. Preview is a schematic, not a Unity screenshot or visual acceptance.", new
        {
            generation = draft.Session.Generation, scope_id = input.Get("scope_id"), group_id = input.Get("group_id"), tab_index = FlowInt(input, "tab_index"),
            coordinate_space = "graph", preview_kind = preview ? "measured_schematic_svg" : null, svg,
            nodes = layout.Nodes.Select(node => new { node_id = node.Id, parent_node_id = node.ParentNodeId, preview_only_id = node.SyntheticId, title = node.Title,
                rect = WireValue(node.Rect), disabled = node.Disabled }).ToArray(),
            ports = layout.Ports.Select(port => new { node_id = port.NodeId, field_path = port.FieldPath,
                input = port.Input, position = WireValue(port.Position) }).ToArray(),
            edges = layout.Edges.Select(edge => new { input_node_id = edge.InputNodeId, output_node_id = edge.OutputNodeId,
                field_path = edge.FieldPath, input = WireValue(edge.Input), output = WireValue(edge.Output),
                straight_distance_estimate = Math.Sqrt(Math.Pow((double)edge.Input.x - edge.Output.x, 2) + Math.Pow((double)edge.Input.y - edge.Output.y, 2)) }).ToArray(),
            overlap_count = overlapCount, overlaps = overlaps.ToArray(), overlap_list_truncated = overlapCount > overlaps.Count,
            boundary_edges_omitted = layout.BoundaryEdges, diagnostics = layout.Diagnostics,
            wire_crossings_measured = false, text_clipping_measured = false,
        });
    }

    private static string LayoutSvg(HGLayoutSnapshot layout)
    {
        if (layout.Nodes.Any(node => Math.Abs(node.Rect.x) > 10000000 || Math.Abs(node.Rect.y) > 10000000
            || node.Rect.width > 10000000 || node.Rect.height > 10000000)
            || layout.Ports.Any(port => Math.Abs(port.Position.x) > 10000000 || Math.Abs(port.Position.y) > 10000000))
            throw new InvalidOperationException("SVG preview coordinates exceed ten million units; geometry remains available without SVG.");
        float minX = layout.Nodes.Count == 0 ? 0 : layout.Nodes.Min(node => node.Rect.x) - 24;
        float minY = layout.Nodes.Count == 0 ? 0 : layout.Nodes.Min(node => node.Rect.y) - 24;
        float width = layout.Nodes.Count == 0 ? 320 : Math.Max(100, layout.Nodes.Max(node => node.Rect.xMax) - minX + 24);
        float height = layout.Nodes.Count == 0 ? 180 : Math.Max(100, layout.Nodes.Max(node => node.Rect.yMax) - minY + 24);
        string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        string Escape(string value) => System.Security.SecurityElement.Escape(value ?? "");
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"").Append(N(minX)).Append(' ').Append(N(minY)).Append(' ').Append(N(width)).Append(' ').Append(N(height)).Append("\">");
        svg.Append("<rect x=\"").Append(N(minX)).Append("\" y=\"").Append(N(minY)).Append("\" width=\"").Append(N(width)).Append("\" height=\"").Append(N(height)).Append("\" fill=\"#222429\"/>");
        foreach (var edge in layout.Edges)
        {
            var a = edge.Input; var b = edge.Output;
            float tangent = Math.Max(40, Math.Abs(a.x - b.x) * 0.5f);
            svg.Append("<path fill=\"none\" stroke=\"#a8b7c4\" stroke-width=\"2\" d=\"M ").Append(N(a.x)).Append(' ').Append(N(a.y))
                .Append(" C ").Append(N(a.x + tangent)).Append(' ').Append(N(a.y)).Append(' ').Append(N(b.x - tangent)).Append(' ').Append(N(b.y)).Append(' ').Append(N(b.x)).Append(' ').Append(N(b.y)).Append("\"/>");
        }
        foreach (var node in layout.Nodes)
        {
            var r = node.Rect;
            string title = node.Title?.Length > 120 ? node.Title.Substring(0, 120) : node.Title;
            svg.Append("<g><title>").Append(Escape(node.Id)).Append("</title><rect x=\"").Append(N(r.x)).Append("\" y=\"").Append(N(r.y))
                .Append("\" width=\"").Append(N(r.width)).Append("\" height=\"").Append(N(r.height)).Append("\" fill=\"").Append(node.Disabled ? "#303238" : "#414a54")
                .Append("\" stroke=\"#b39a64\"/><text x=\"").Append(N(r.x + 8)).Append("\" y=\"").Append(N(r.y + 19))
                .Append("\" fill=\"#eeeeee\" font-size=\"12\" font-family=\"sans-serif\">").Append(Escape(title)).Append("</text></g>");
        }
        return svg.Append("</svg>").ToString();
    }
}
}
