namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using NUnit.Framework;
using UnityEngine;

public class HGPortTests
{
    [Test]
    public void KeyUsesStableOwnerPathAndRole()
    {
        var first = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var rebuilt = new HGPortKey("node", "/steps[2]", HGPortRole.Input);
        var output = new HGPortKey("node", "/steps[2]", HGPortRole.Output);

        Assert.That(rebuilt, Is.EqualTo(first));
        Assert.That(output, Is.Not.EqualTo(first));
    }

    [Test]
    public void BuildContextRejectsPortFromOldGeneration()
    {
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 4);

        Assert.That(build.Add(AggregatePort(3)), Is.False);
        Assert.That(build.Add(AggregatePort(4)), Is.True);
        Assert.That(graph.Ports, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExplicitProviderAddsAdapterThroughBuildSeam()
    {
        var extension = new HGEditorExtensionContext(new TestProvider());
        var graph = new HGGraphView();
        var build = new HGPortBuildContext(graph, 9);

        extension.Provider.AddPorts(build);

        Assert.That(graph.Ports, Has.Count.EqualTo(1));
        Assert.That(graph.Ports[0].Key.OwnerId, Is.EqualTo("test-adapter"));
        Assert.That(graph.Ports[0].Binding, Is.InstanceOf<IHGAggregatePortBinding>());
    }

    [Test]
    public void ConnectionCoordinatorAppliesGenerationVisibilityLockAndPolicy()
    {
        const int generation = 5;
        var slot = new TestSlot();
        bool locked = false;
        var input = new HGPort(new HGPortKey("input", "/value", HGPortRole.Input),
            new HGInputPortBinding(slot),
            new HGDelegatePortPolicy(() => true, source => source.Accepts(slot)),
            new HGDelegatePortPresentation(slot, () => Vector2.zero, () => Rect.zero,
                () => true, () => locked), generation);
        var source = new HGDelegatePortSource(new GraphNode(), null, candidate => ReferenceEquals(candidate, slot));
        var output = new HGPort(new HGPortKey("output", "", HGPortRole.Output),
            new HGOutputPortBinding(source),
            new HGDelegatePortPolicy(() => true),
            new HGDelegatePortPresentation(new object(), () => Vector2.one, () => Rect.zero,
                () => true, () => false), generation);

        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.True);
        Assert.That(HGPortConnection.CanConnect(input, output, generation + 1), Is.False);
        locked = true;
        Assert.That(HGPortConnection.CanConnect(input, output, generation), Is.False);
    }

    private static HGPort AggregatePort(int generation, string owner = "aggregate")
        => new HGPort(new HGPortKey(owner, "/items", HGPortRole.Aggregate),
            HGAggregatePortBinding.Instance,
            new HGDelegatePortPolicy(() => false),
            new HGDelegatePortPresentation(new object(), () => Vector2.zero, () => Rect.zero,
                () => true, () => false), generation);

    private sealed class TestProvider : IHGEditorExtensionProvider
    {
        public bool Supports(Object owner, IGraphDocument document) => true;
        public void AddPorts(HGPortBuildContext context) => context.Add(AggregatePort(context.Generation, "test-adapter"));
    }

    private sealed class TestSlot : GraphSlotBase
    {
        private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }
}
}
