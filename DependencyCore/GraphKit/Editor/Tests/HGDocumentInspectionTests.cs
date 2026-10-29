namespace HaruFamily.DependencyCore.GraphKit.Editor.Tests
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class HGDocumentInspectionTests
{
    [Test]
    public void CapturePreservesSharedCyclesLibrariesHiddenFieldsAndDoesNotInvokeDocumentGetters()
    {
        var document = new InspectionDocument();
        var action = new InspectionAction();
        var node = new GraphNode(action);
        action.Input.SetNode(node);
        document.Nodes.Add(node);
        document.Nodes.Add(node);
        document.Nodes.Add(new GraphNode(new InspectionFormula()));
        document.Tokens.Add(new GraphToken("Token", null));
        document.Properties.Add(new GraphProperty("Property", null));
        var tokenNode = new GraphNode();
        tokenNode.SetToken(document.Tokens[0]);
        var propertyNode = new GraphNode();
        propertyNode.SetProperty(document.Properties[0]);
        document.Nodes.Add(tokenNode);
        document.Nodes.Add(propertyNode);

        var snapshot = HGDocumentInspection.Capture(document, "test");

        Assert.That(snapshot.Complete, Is.True, string.Join("\n", snapshot.Diagnostics));
        Assert.That(snapshot.Records.Count(record => record.Kind == "node"), Is.EqualTo(4));
        Assert.That(snapshot.Records.Count(record => record.Kind == "action"), Is.EqualTo(1));
        Assert.That(snapshot.Records.Single(record => record.Kind == "action").Label, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.Records.Count(record => record.Kind == "formula"), Is.EqualTo(1));
        Assert.That(snapshot.Records.Count(record => record.Kind == "token"), Is.EqualTo(1));
        Assert.That(snapshot.Records.Count(record => record.Kind == "property"), Is.EqualTo(1));
        var assetSnapshot = HGDocumentInspection.Capture(new InspectionAsset { RootData = node }, "test");
        Assert.That(assetSnapshot.Records[0].Kind, Is.EqualTo("asset"));
        Assert.That(assetSnapshot.Records.Count(record => record.Kind == "action"), Is.EqualTo(1));
        var root = snapshot.Records.Single(record => record.Key == snapshot.Root);
        var listKey = root.Fields.Single(field => field.Name.EndsWith("/Nodes", StringComparison.Ordinal)).Value;
        var list = snapshot.Records.Single(record => record.Key == listKey);
        Assert.That(list.Fields[0].Value, Is.EqualTo(list.Fields[1].Value));
        Assert.That(node.Id, Is.Null.Or.Empty);
        Assert.That(root.Fields.Any(field => field.Name.EndsWith("/hidden", StringComparison.Ordinal)), Is.True);
        Assert.That(root.Fields.Any(field => field.Name.EndsWith("/RuntimeOnly", StringComparison.Ordinal)), Is.False);
        string captured = JsonUtility.ToJson(snapshot);
        node.Note = "later change";
        document.Nodes.Clear();
        Assert.That(JsonUtility.ToJson(snapshot), Is.EqualTo(captured));
    }

    [Test]
    public void CaptureReportsLimitsAndPreservesNullVersusEmptyLists()
    {
        var document = new InspectionDocument();
        var snapshot = HGDocumentInspection.Capture(document, "test");
        var root = snapshot.Records.Single(record => record.Key == snapshot.Root);
        Assert.That(root.Fields.Single(field => field.Name.EndsWith("/NullList", StringComparison.Ordinal)).Kind, Is.EqualTo("null"));
        Assert.That(root.Fields.Single(field => field.Name.EndsWith("/Nodes", StringComparison.Ordinal)).Kind, Is.EqualTo("reference"));
        var limited = HGDocumentInspection.Capture(document, "test", 1);
        Assert.That(limited.Complete, Is.False);
        Assert.That(limited.Diagnostics, Is.Not.Empty);
        Assert.That(limited.Records.Count, Is.EqualTo(1));
    }

    [Serializable]
    private sealed class InspectionDocument : IGraphDocument
    {
        public List<GraphNode> Nodes = new();
        public List<GraphToken> Tokens = new();
        public List<GraphProperty> Properties = new();
        public List<int> NullList;
        [SerializeField, HideInInspector] private string hidden = "included";
        [NonSerialized] public string RuntimeOnly = "excluded";
        public List<GraphNode> Orphans => throw new InvalidOperationException("Do not invoke lazy getters.");
        public IList Roots => throw new InvalidOperationException();
        public bool IsValidated => throw new InvalidOperationException();
        public Type PackType => throw new InvalidOperationException();
        public Type ItemSlotType => throw new InvalidOperationException();
        public string RootChip => throw new InvalidOperationException();
        public string RootNoun => throw new InvalidOperationException();
        public string WindowTitle => throw new InvalidOperationException();
        public HGCapabilities Capabilities => throw new InvalidOperationException();
        public void InvalidateValidation() => throw new InvalidOperationException();
        public void Verify() => throw new InvalidOperationException();
        public object DeepCopy() => throw new InvalidOperationException();
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => throw new InvalidOperationException();
        public object KeyOf(object root) => throw new InvalidOperationException();
        public string TitleOf(object root) => throw new InvalidOperationException();
        public IList ItemsOf(object root) => throw new InvalidOperationException();
        public object AddRoot(object key) => throw new InvalidOperationException();
    }

    [Serializable]
    private sealed class InspectionAsset : IGraphAsset
    {
        public GraphNode RootData;
        public object ContentObject => throw new InvalidOperationException();
        public GraphNode Root => throw new InvalidOperationException();
        public Type ResultType => throw new InvalidOperationException();
        public List<GraphNode> Orphans => throw new InvalidOperationException();
        public List<GraphToken> Tokens => throw new InvalidOperationException();
        public Vector2 Pos { get => throw new InvalidOperationException(); set => throw new InvalidOperationException(); }
        public bool HasPos => throw new InvalidOperationException();
        public void ClearPos() => throw new InvalidOperationException();
    }

    [Serializable]
    private sealed class InspectionAction : ActionNodeShape
    {
        public InspectionSlot Input = new();
    }

    [Serializable]
    private sealed class InspectionFormula : FormulaNodeShape<int, NullPack> { }

    [Serializable]
    private sealed class InspectionSlot : GraphSlotBase
    {
        [SerializeReference] private GraphNode node;
        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
    }
}
}
