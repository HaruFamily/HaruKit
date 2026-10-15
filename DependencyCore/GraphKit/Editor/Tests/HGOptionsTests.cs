namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System.Collections.Generic;
using NUnit.Framework;

public sealed class HGOptionsTests
{
    [HGCatalog("GraphKit.Tests.Options.Merge", Group = "First")]
    private static readonly List<string> first = new() { "core:damage", "core:heal" };

    [HGCatalog("GraphKit.Tests.Options.Merge", Group = "Second")]
    private static IEnumerable<string> Second => new[] { "core:damage", "mod:fire" };

    [HGCatalog("GraphKit.Tests.Options.Numbers")]
    private static IEnumerable<float> Numbers() => new[] { 0.5f, 1.5f };

    [HGCatalog("GraphKit.Tests.Options.Throw")]
    private static IEnumerable<int> Broken()
    {
        yield return 1;
        throw new System.InvalidOperationException("iterator failed");
    }

    [HGCatalog("GraphKit.Tests.Options.Conflict")]
    private static IEnumerable<int> Integers => new[] { 1 };

    [HGCatalog("GraphKit.Tests.Options.Conflict")]
    private static IEnumerable<string> Strings => new[] { "one" };

    [HGCatalog("GraphKit.Tests.Options.Labels")]
    private static IEnumerable<HGCatalogEntry<int>> ADescriptions => new[]
    {
        new HGCatalogEntry<int>(1, "簡單", "適合初次遊玩"),
        new HGCatalogEntry<int>(2, "簡單", "另一個同名選項"),
        new HGCatalogEntry<int>(3, " "),
    };

    [HGCatalog("GraphKit.Tests.Options.Labels")]
    private static IEnumerable<int> BPlain => new[] { 1, 4 };

    [HGCatalog("GraphKit.Tests.Options.InvalidElement")]
    private static IEnumerable<HGCatalogEntry<string>> InvalidElement => new[]
    {
        new HGCatalogEntry<string>("valid", "有效"),
        new HGCatalogEntry<string>(null, "沒有值"),
    };

    [Test]
    public void SpinnerCountEditsPreserveUnknownValuesAndLeaveSourceUntouched()
    {
        var original = new List<int> { -1, 42, 1, 1, -1 };
        var values = HGSpinnerField.Copy(original);
        HGSpinnerField.SetCount(values, 1, 3);
        HGSpinnerField.SetCount(values, -1, 1);
        var result = HGSpinnerField.Materialize(typeof(List<int>), values);
        CollectionAssert.AreEqual(new[] { -1, 42, 1, 1, 1 }, result);
        CollectionAssert.AreEqual(new[] { -1, 42, 1, 1, -1 }, original);
        HGSpinnerField.SetCount(values, 42, 0);
        Assert.That(HGSpinnerField.Count(values, 42), Is.Zero);
    }

    [Test]
    public void SpinnerArraysStoreValuesRatherThanDisplayLabels()
    {
        Assert.That(HGOptionsCatalog.TryGet("GraphKit.Tests.Options.Labels", typeof(int), out var entries, out var error), Is.True, error);
        var values = HGSpinnerField.Copy(new[] { 3 });
        HGSpinnerField.SetCount(values, entries[0].Value, 2);
        HGSpinnerField.SetCount(values, entries[1].Value, 1);
        CollectionAssert.AreEqual(new[] { 3, 1, 1, 2 }, HGSpinnerField.Materialize(typeof(int[]), values));
        Assert.That(HGSpinnerField.TryElementType(typeof(IEnumerable<int>), out _), Is.False);
    }

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
