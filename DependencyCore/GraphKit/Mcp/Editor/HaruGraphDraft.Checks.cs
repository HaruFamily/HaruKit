namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class HaruGraphDraft
{
    private static readonly string[] checkQueries = { "metadata_fields", "structured_types", "structured_fields", "catalog_fields", "field_options", "group_tabs", "nodes", "endpoints", "check_connection", "value_fields", "slot_value",
        "library", "slot_lists", "slot_items", "roots", "root_options", "group_scopes", "groups", "group_members", "validate", "slot_asset_reference", "slot_asset_choices" };

    private static string CheckInputError(JToken token)
    {
        if (token is not JArray checks || checks.Count < 1 || checks.Count > 32)
            return "checks must contain 1..32 objects.";
        if (checks.ToString(Formatting.None).Length > 65536) return "checks exceed 65536 characters.";
        foreach (var item in checks)
        {
            if (item is not JObject check || check.Properties().Any(p => !new[] { "name", "query", "path", "op", "expected" }.Contains(p.Name)))
                return "Each check accepts only name, query, path, op and expected.";
            if (check["name"] != null && (check["name"].Type != JTokenType.String || ((string)check["name"]).Length > 128))
                return "Optional check names must be strings of at most 128 characters.";
            if (check["query"] is not JObject query || query["action"]?.Type != JTokenType.String || !checkQueries.Contains((string)query["action"]))
                return "query must use an allowed read-only action; nested checks and mutations are forbidden.";
            if (query.Properties().Any(p => new[] { "draftid", "generation", "value" }.Contains(p.Name.Replace("_", "").ToLowerInvariant())))
                return "Query draft_id/generation are supplied by check; value is forbidden, including null.";
            if (check["path"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)check["path"])
                || ((string)check["path"]).Length > 512 || ((string)check["path"]).Split('.').Any(string.IsNullOrEmpty))
                return "path must be a nonempty dot-separated data path of at most 512 characters.";
            string op = (string)check["op"];
            if (!new[] { "equals", "exists", "count" }.Contains(op) || check["expected"] == null)
                return "op must be equals, exists or count, with an explicit expected value (null is allowed for equals).";
            if (op == "exists" && check["expected"].Type != JTokenType.Boolean) return "exists expects a boolean.";
            if (op == "count" && (check["expected"].Type != JTokenType.Integer || !int.TryParse(check["expected"].ToString(), out int count) || count < 0))
                return "count expects a nonnegative int32.";
        }
        return null;
    }

    private static JToken CheckPath(JToken value, string path)
    {
        foreach (string part in path.Split('.'))
        {
            if (value is JObject fields) value = fields[part];
            else if (value is JArray array && int.TryParse(part, out int index) && index >= 0 && index < array.Count) value = array[index];
            else return null;
            if (value == null) return null;
        }
        return value;
    }

    private static object CheckDraft(string draftId, Draft draft, ToolParams input)
    {
        var token = input.GetRaw("checks");
        string invalid = CheckInputError(token);
        if (invalid != null) return Error("invalid_checks", invalid);
        var entries = new List<object>();
        int failed = 0;
        foreach (JObject check in (JArray)token)
        {
            var query = (JObject)check["query"].DeepClone();
            query["draft_id"] = draftId;
            query["generation"] = draft.Session.Generation;
            var response = JObject.FromObject(HandleCommand(query));
            bool querySucceeded = response["success"]?.Value<bool>() == true;
            bool bounded = response.ToString(Formatting.None).Length <= 1048576;
            var actual = querySucceeded && bounded ? CheckPath(response["data"], (string)check["path"]) : null;
            bool exists = actual != null;
            string op = (string)check["op"];
            var expected = check["expected"];
            // JSON null exists; a missing path never satisfies equals(null) or count(0).
            JToken compared = op == "exists" ? new JValue(exists)
                : op == "count" ? actual is JArray array ? new JValue(array.Count) : null : actual;
            bool passed = querySucceeded && bounded && compared != null && JToken.DeepEquals(compared, expected);
            if (!passed) failed++;
            bool truncated = actual != null && actual.ToString(Formatting.None).Length > 4096;
            entries.Add(new
            {
                index = entries.Count, name = (string)check["name"], action = (string)query["action"], path = (string)check["path"], op,
                passed, exists, expected, actual = truncated ? null : actual, actual_truncated = truncated,
                actual_count = actual is JArray items ? (int?)items.Count : null,
                query_error = !querySucceeded ? (string)response["code"] ?? "query_failed" : !bounded ? "query_result_limit" : null,
            });
        }
        var report = new
        {
            generation = draft.Session.Generation, scope = "graph_query_expectations", passed = failed == 0,
            total = entries.Count, failed, checks = entries.ToArray(),
            hint = "Checks inspect only the requested query page. Use total/next_offset to assert full-list coverage. No gameplay execution or validation flags. Within apply_batch any failed check rolls back the batch.",
        };
        return failed == 0 ? new SuccessResponse("All graph expectations passed.", report)
            : new ErrorResponse("harugraph.draft.checks_failed", report);
    }
}
}
