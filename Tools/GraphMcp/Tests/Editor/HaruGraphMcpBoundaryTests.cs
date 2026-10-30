namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public sealed class HaruGraphMcpBoundaryTests
{
    [Test]
    public void IntegrationAndTestsReferenceOnlyGenericAssemblies()
    {
        foreach (var assembly in new[] { typeof(HaruGraphDraft).Assembly, GetType().Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(value => value.Name);
            foreach (string name in references)
                Assert.That(name.StartsWith("System") || name.StartsWith("Unity") || name == "mscorlib"
                    || name == "netstandard" || name == "Newtonsoft.Json" || name == "nunit.framework"
                    || name == "MCPForUnity.Editor" || name == "HaruFamily.Tools.GraphMcp.Editor"
                    || name == "HaruFamily.DependencyCore.GraphKit" || name == "HaruFamily.DependencyCore.GraphKit.Editor",
                    Is.True, assembly.GetName().Name + " unexpectedly references " + name);
        }
        Assert.That(typeof(HaruGraphDraft.Parameters).GetProperty("cases"), Is.Null);
    }

    [TestCase("formula_test_capabilities")]
    [TestCase("action_test_capabilities")]
    [TestCase("world_test_capabilities")]
    [TestCase("run_formula_tests")]
    [TestCase("run_action_tests")]
    [TestCase("run_world_tests")]
    public void ConsumerExecutionIsNotDispatchedByGenericTool(string action)
    {
        Error(HaruGraphDraft.HandleCommand(new JObject { ["action"] = action }), "unknown_action");
    }

    [Test]
    public void MissingSnapshotRequestDoesNotInvokeConsumer()
    {
        Error(HaruGraphDraft.WithDraftSnapshot(null, _ => throw new Exception("Must not run")), "invalid_request");
        Error(HaruGraphDraft.WithDraftSnapshot(new JObject(), null), "invalid_request");
    }

    private static void Error(object response, string code)
    {
        var json = JObject.FromObject(response);
        Assert.That((bool)json["success"], Is.False, json.ToString());
        Assert.That((string)json["code"], Is.EqualTo("harugraph.draft." + code));
    }
}
}
