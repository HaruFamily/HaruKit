namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

public sealed class HGAgentSetupTests
{
    [Test]
    public void CoreAssembliesDoNotReferenceOptionalMcpOrJson()
    {
        foreach (var assembly in new[] { typeof(GraphNode).Assembly, typeof(HGAgentSetupWindow).Assembly })
            Assert.That(assembly.GetReferencedAssemblies().Any(reference => reference.Name.Contains("MCPForUnity")
                || reference.Name.Contains("GraphMcp") || reference.Name == "Newtonsoft.Json"), Is.False);
    }

    [Test]
    public void ExportIsRepeatableAndPreservesCustomSkill()
    {
        string project = Path.Combine(Path.GetTempPath(), "GraphKitSkill_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        try
        {
            string target = HGAgentSetupWindow.ExportSkill(project, ".agents/skills");
            string original = File.ReadAllText(target);
            Assert.That(original, Does.Contain("name: harugraph-mcp"));
            Assert.That(HGAgentSetupWindow.ExportSkill(project, ".agents/skills"), Is.EqualTo(target));
            File.WriteAllText(target, "custom skill");
            Assert.Throws<IOException>(() => HGAgentSetupWindow.ExportSkill(project, ".agents/skills"));
            Assert.That(File.ReadAllText(target), Is.EqualTo("custom skill"));
            Assert.Throws<ArgumentException>(() => HGAgentSetupWindow.ExportSkill(project, "../escape"));
            string other = HGAgentSetupWindow.ExportSkill(project, ".claude/skills");
            Assert.That(File.ReadAllText(other), Is.EqualTo(original));
        }
        finally { Directory.Delete(project, true); }
    }
}
}
