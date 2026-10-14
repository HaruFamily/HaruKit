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

    [HGOptionsSource("GraphKit.Tests.Options.Labels")]
    private static IEnumerable<HGOptionElement<int>> ADescriptions => new[]
    {
        new HGOptionElement<int>(1, "簡單", "適合初次遊玩"),
        new HGOptionElement<int>(2, "簡單", "另一個同名選項"),
        new HGOptionElement<int>(3, " "),
    };

    [HGOptionsSource("GraphKit.Tests.Options.Labels")]
    private static IEnumerable<int> BPlain => new[] { 1, 4 };

    [HGOptionsSource("GraphKit.Tests.Options.InvalidElement")]
    private static IEnumerable<HGOptionElement<string>> InvalidElement => new[]
    {
        new HGOptionElement<string>("valid", "有效"),
        new HGOptionElement<string>(null, "沒有值"),
    };

    [Test]
    public void DescribedIntegersMergeWithPlainValuesWithoutUsingLabelsAsIdentity()
    {
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Labels", typeof(int), out var entries, out var error), Is.True, error);
        CollectionAssert.AreEqual(new object[] { 1, 2, 3, 4 }, entries.ConvertAll(entry => entry.Value));
        CollectionAssert.AreEqual(new[] { "簡單", "簡單", "3", "4" }, entries.ConvertAll(entry => entry.Label));
        Assert.That(entries[0].Tooltip, Is.EqualTo("適合初次遊玩"));
        Assert.That(entries[1].Tooltip, Is.EqualTo("另一個同名選項"));
        Assert.That(entries[3].Tooltip, Is.Empty);
    }

    [Test]
    public void DescribedSourcesRejectWrongFieldTypesAndNullUnderlyingValues()
    {
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Labels", typeof(string), out var entries, out _), Is.False);
        Assert.That(entries, Is.Empty);
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.InvalidElement", typeof(string), out entries, out var error), Is.False);
        Assert.That(entries, Is.Empty);
        Assert.That(error, Does.Contain("InvalidElement").And.Contain("null"));
    }

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
