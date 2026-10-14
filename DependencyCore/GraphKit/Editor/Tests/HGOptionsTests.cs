namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System.Collections.Generic;
using NUnit.Framework;

public sealed class HGOptionsTests
{
    [HGOptionsSource("GraphKit.Tests.Options.Merge", Group = "First")]
    private static readonly List<string> first = new() { "core:damage", "core:heal" };

    [HGOptionsSource("GraphKit.Tests.Options.Merge", Group = "Second")]
    private static IEnumerable<string> Second => new[] { "core:damage", "mod:fire" };

    [HGOptionsSource("GraphKit.Tests.Options.Numbers")]
    private static IEnumerable<float> Numbers() => new[] { 0.5f, 1.5f };

    [HGOptionsSource("GraphKit.Tests.Options.Throw")]
    private static IEnumerable<int> Broken()
    {
        yield return 1;
        throw new System.InvalidOperationException("iterator failed");
    }

    [HGOptionsSource("GraphKit.Tests.Options.Conflict")]
    private static IEnumerable<int> Integers => new[] { 1 };

    [HGOptionsSource("GraphKit.Tests.Options.Conflict")]
    private static IEnumerable<string> Strings => new[] { "one" };

    [Test]
    public void SourcesMergeByValueAndAreEnumeratedAgainForEachSnapshot()
    {
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Merge", typeof(string), out var entries, out var error), Is.True, error);
        CollectionAssert.AreEquivalent(new[] { "core:damage", "core:heal", "mod:fire" }, entries.ConvertAll(entry => entry.Value));
        first.Add("mod:ice");
        try
        {
            Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Merge", typeof(string), out entries, out error), Is.True, error);
            Assert.That(entries.Exists(entry => Equals(entry.Value, "mod:ice")), Is.True);
        }
        finally { first.Remove("mod:ice"); }
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Numbers", typeof(float), out entries, out error), Is.True, error);
        CollectionAssert.AreEqual(new object[] { 0.5f, 1.5f }, entries.ConvertAll(entry => entry.Value));
    }

    [Test]
    public void InvalidSourcesNeverExposePartialOrIncompatibleChoices()
    {
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Throw", typeof(int), out var entries, out var error), Is.False);
        Assert.That(entries, Is.Empty);
        Assert.That(error, Does.Contain("Broken").And.Contain("iterator failed"));
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Conflict", typeof(object), out entries, out error), Is.False);
        Assert.That(entries, Is.Empty);
        Assert.That(error, Does.Contain("型別衝突"));
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Numbers", typeof(int), out entries, out error), Is.False);
        Assert.That(entries, Is.Empty);
    }
}
}
