namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[McpForUnityTool("haru_graph_execute", Description = "Discover explicitly registered consumer executors for a graph draft, read their input/result contracts, and run on a detached snapshot. No built-in gameplay or pipeline execution. Provider owns external-asset/world isolation and cleanup. Timeout is cooperative; arbitrary trusted Editor code cannot be preempted. Does not save or change draft generation.")]
public static class HaruGraphExecute
{
    public sealed class Parameters
    {
        [ToolParameter("providers, describe, run")] public string action { get; set; }
        [ToolParameter("Draft from haru_graph_draft.")] public string draft_id { get; set; }
        [ToolParameter("Current draft generation.")] public int generation { get; set; }
        [ToolParameter("Exact registered provider ID.", Required = false)] public string provider_id { get; set; }
        [ToolParameter("Provider-defined input, according to describe.", Required = false)] public JObject input { get; set; }
        [ToolParameter("Cooperative timeout, 1..30000 milliseconds; default 5000.", Required = false)] public int? timeout_ms { get; set; }
    }
    public static object HandleCommand(JObject parameters)
    {
        try
        {
            if (parameters == null) return Error("invalid_request", "A request is required.");
            var p = new ToolParams(parameters);
            string action = p.Get("action");
            if (!new[] { "providers", "describe", "run" }.Contains(action)) return Error("unknown_action", "Use providers, describe or run.");
            return HaruGraphDraft.WithDraftSnapshot(parameters, snapshot =>
            {
                if (action == "providers")
                    return Bounded(new { providers = HaruGraphExtensions.Executors.Values.OrderBy(e => e.Id, StringComparer.Ordinal)
                        .Where(e => e.Supports(snapshot.Document)).Select(e => new { id = e.Id, description = e.Description }).ToArray(), generation = snapshot.Generation });
                if (!HaruGraphExtensions.Executors.TryGetValue(p.Get("provider_id") ?? "", out var executor) || !executor.Supports(snapshot.Document))
                    return Error("provider_unavailable", "Discover a supported registered provider first.");
                if (action == "describe") return Bounded(new { provider_id = executor.Id, contract = executor.Describe(snapshot.Document) });
                int timeout = p.GetRaw("timeout_ms")?.ToObject<int>() ?? 5000;
                if (timeout < 1 || timeout > 30000 || p.GetRaw("input") is not JObject input || input.ToString(Formatting.None).Length > 262144)
                    return Error("invalid_input", "Provide an input object <=262144 characters and timeout 1..30000 ms.");
                using var cancellation = new CancellationTokenSource(timeout);
                var result = executor.Run(snapshot, (JObject)input.DeepClone(), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (result == null) return Error("invalid_result", "Provider returned no result.");
                return Bounded(new { provider_id = executor.Id, generation = snapshot.Generation, result });
            });
        }
        catch (OperationCanceledException) { return Error("cancelled", "Provider execution was cancelled; provider owns cleanup."); }
        catch (Exception exception) { return Error("provider_failed", exception.Message); }
    }
    private static object Bounded(object data)
        => JObject.FromObject(data).ToString(Formatting.None).Length > 1048576
            ? Error("result_limit", "Provider response exceeds 1 MiB.") : new SuccessResponse("Consumer integration result; inspect the provider's result contract.", data);
    private static object Error(string code, string message) => new ErrorResponse("harugraph.execute." + code, new { message });
}
}
