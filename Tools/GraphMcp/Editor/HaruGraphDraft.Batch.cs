namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class HaruGraphDraft
{
    private static bool BatchAction(string action) => authoringActions.Contains(action)
        || new[] { "endpoints", "check_connection", "connect", "disconnect", "value_fields", "set_value", "check" }.Contains(action);

    // References address a previous named step's data, never arbitrary JSONPath expressions or external objects.
    private static JToken ResolveBatchValue(JToken token, Dictionary<string, JToken> results, int depth = 0)
    {
        if (depth > 32) throw new ArgumentException("Batch input nesting exceeds 32 levels.");
        if (token is JObject obj)
        {
            if (obj.TryGetValue("$ref", out var reference))
            {
                if (obj.Count != 1 || reference.Type != JTokenType.String) throw new ArgumentException("A $ref object must contain only a string $ref.");
                string[] parts = ((string)reference).Split('.');
                if (parts.Length < 2 || !results.TryGetValue(parts[0], out var value)) throw new ArgumentException("$ref must start with a previous step id and a data path.");
                foreach (string part in parts.Skip(1))
                {
                    if (value is JObject fields) value = fields[part];
                    else if (value is JArray array && int.TryParse(part, out int index) && index >= 0 && index < array.Count) value = array[index];
                    else value = null;
                    if (value == null) throw new ArgumentException("$ref path does not exist: " + (string)reference);
                }
                return value.DeepClone();
            }
            return new JObject(obj.Properties().Select(property => new JProperty(property.Name, ResolveBatchValue(property.Value, results, depth + 1))));
        }
        if (token is JArray list) return new JArray(list.Select(item => ResolveBatchValue(item, results, depth + 1)));
        return token.DeepClone();
    }

    private static object ApplyBatch(string draftId, Draft draft, ToolParams input)
    {
        if (input.GetRaw("commands") is not JArray commands || commands.Count < 1 || commands.Count > 32)
            return Error("invalid_batch", "commands must contain 1..32 objects.");
        if (commands.ToString(Formatting.None).Length > 262144) return Error("invalid_batch", "Batch JSON exceeds 262144 characters.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in commands)
        {
            if (step is not JObject args || args["action"]?.Type != JTokenType.String || !BatchAction((string)args["action"]))
                return Error("invalid_batch", "Only graph editing and generation-local queries are allowed; no open, create-owner, preview, validate, save, release, history controls or nested batches.");
            if ((string)args["action"] == "check" && CheckInputError(args["checks"]) is string checkError)
                return Error("invalid_batch", checkError);
            if (args.Properties().Any(property => new[] { "draftid", "generation" }.Contains(property.Name.Replace("_", "").ToLowerInvariant())))
                return Error("invalid_batch", "Step draft_id and generation are supplied by the batch; omit them.");
            if (args["id"] != null && (args["id"].Type != JTokenType.String || string.IsNullOrEmpty((string)args["id"])
                || ((string)args["id"]).Length > 64 || ((string)args["id"]).Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') || !ids.Add((string)args["id"])))
                return Error("invalid_batch", "Optional step ids must be unique 1..64 letter/digit/underscore names.");
        }
        var history = draft.History.ToArray();
        int dropped = draft.HistoryDropped;
        var dependencies = draft.ParameterAssets.ToArray();
        var results = new Dictionary<string, JToken>(StringComparer.Ordinal);
        var resultGenerations = new Dictionary<string, int>(StringComparer.Ordinal);
        int initialGeneration = draft.Session.Generation;
        var steps = new List<object>();
        int failedIndex = -1;
        object failure = null;
        int resultSize = 0;
        draft.PreviewToken = null;
        draft.SaveAsToken = null;
        var outcome = draft.Session.RunAtomic(() =>
        {
            for (int i = 0; i < commands.Count; i++)
            {
                failedIndex = i;
                var raw = (JObject)commands[i];
                string id = (string)raw["id"];
                var keyArguments = raw.Properties().AsEnumerable();
                if ((string)raw["action"] == "check")
                    keyArguments = keyArguments.Concat(((JArray)raw["checks"]).OfType<JObject>()
                        .SelectMany(check => ((JObject)check["query"]).Properties()));
                foreach (var property in keyArguments)
                {
                    string key = property.Name.Replace("_", "").ToLowerInvariant();
                    if (!new[] { "slotkey", "sourcekey", "listkey", "rootkey", "rootindex", "tabindex" }.Contains(key)) continue;
                    bool current = property.Value is JObject reference && reference["$ref"]?.Type == JTokenType.String
                        ? resultGenerations.TryGetValue(((string)reference["$ref"]).Split('.')[0], out int sourceGeneration)
                            && sourceGeneration == draft.Session.Generation
                        : initialGeneration == draft.Session.Generation;
                    if (!current)
                    {
                        failure = new { code = "stale_step_key", message = "Query generation-local keys again after each mutation and reference that query's result." };
                        return false;
                    }
                }
                JObject args;
                try { args = (JObject)ResolveBatchValue(raw, results); }
                catch (ArgumentException exception) { failure = new { code = "invalid_reference", message = exception.Message }; return false; }
                args.Remove("id");
                args["draft_id"] = draftId;
                args["generation"] = draft.Session.Generation;
                var response = JObject.FromObject(HandleCommand(args));
                if (response["success"]?.Value<bool>() != true) { failure = response; return false; }
                resultSize += response.ToString(Formatting.None).Length;
                if (resultSize > 1048576) { failure = new { code = "result_limit", message = "Batch results exceed 1048576 characters; split the batch or reduce query pages." }; return false; }
                var data = response["data"];
                if (id != null)
                {
                    results.Add(id, data?.DeepClone() ?? JValue.CreateNull());
                    resultGenerations.Add(id, draft.Session.Generation);
                }
                steps.Add(new { index = i, id, action = (string)args["action"], data });
            }
            failedIndex = -1;
            return true;
        });
        draft.History.Clear(); draft.History.AddRange(history); draft.HistoryDropped = dropped;
        if (outcome != HGSessionCommandResult.Changed && outcome != HGSessionCommandResult.NoChange)
        {
            draft.ParameterAssets.Clear();
            foreach (var pair in dependencies) draft.ParameterAssets.Add(pair.Key, pair.Value);
            return new ErrorResponse("harugraph.draft.batch_rejected", new
            {
                rolled_back = true, failed_index = failedIndex, completed_steps = steps.Count, failure,
                generation = draft.Session.Generation, working_copy_dirty = draft.Session.IsDirty,
                diagnostic = draft.Session.LastDiagnostic,
                hint = "No batch edits retained. Resolve all generation-local keys again. Preview tokens were invalidated; failed-batch result ids must not be reused.",
            });
        }
        return CommandResult(draft, outcome, "apply_batch", new
        {
            steps = steps.ToArray(), atomic_working_copy = true,
            hint = "One Undo entry for the whole batch. Step endpoint keys are local to each step generation; resolve them again after later mutations. Preview and persistence remain separate.",
        });
    }
}
}
