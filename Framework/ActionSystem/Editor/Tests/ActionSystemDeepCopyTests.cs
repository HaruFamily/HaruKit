namespace PinPlugin.ActionSystem.Editor.Tests
{
using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ActionSystemDeepCopyTests
{
    [Test]
    public void Copy_ClonesGraphAndPreservesReferences()
    {
        var asset = ScriptableObject.CreateInstance<CloneAsset>();
        var source = new CloneNode { Asset = asset };
        source.Children.Add(source);
        source.Shared = new CloneLeaf { Value = 7 };
        source.Other = source.Shared;

        var copy = ActionSystemDeepCopy.Copy(source);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy, Is.Not.SameAs(source));
        Assert.That(copy.Asset, Is.SameAs(asset));
        Assert.That(copy.Children[0], Is.SameAs(copy));
        Assert.That(copy.Shared, Is.Not.SameAs(source.Shared));
        Assert.That(copy.Other, Is.SameAs(copy.Shared));
        Assert.That(copy.Shared.Value, Is.EqualTo(7));

        UnityEngine.Object.DestroyImmediate(asset);
    }

    /// <summary>共用來源：兩個欄位指到同一個載體，複製後必須還是同一個，否則存檔會裂成兩個節點。</summary>
    [Test]
    public void Copy_KeepsSharedCarrierShared()
    {
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.Pos = new Vector2(120f, 40f);
        var holder = new CarrierHolder { A = carrier, B = carrier };

        var copy = ActionSystemDeepCopy.Copy(holder);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.A, Is.Not.SameAs(carrier));
        Assert.That(copy.B, Is.SameAs(copy.A));
        Assert.That(copy.A.Id, Is.EqualTo(carrier.Id));
        Assert.That(copy.A.Pos, Is.EqualTo(carrier.Pos));
    }

    /// <summary>載體內容繞回自己：複製不可無限遞迴，且環要保留。</summary>
    [Test]
    public void Copy_KeepsCarrierCycle()
    {
        var body = new CarrierNode();
        var carrier = new GraphNode(body);
        carrier.EnsureId();
        body.Child = carrier;

        var copy = ActionSystemDeepCopy.Copy(carrier);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy, Is.Not.SameAs(carrier));
        Assert.That(copy.Kind, Is.EqualTo(NodeKind.Inline));
        var copiedBody = copy.GetBody<CarrierNode>();
        Assert.That(copiedBody, Is.Not.Null);
        Assert.That(copiedBody, Is.Not.SameAs(body));
        Assert.That(copiedBody.Child, Is.SameAs(copy));
    }

    /// <summary>載體指到的資產是 Unity Object，複製後必須是同一個資產而不是新複本。</summary>
    [Test]
    public void Copy_KeepsCarrierAssetReference()
    {
        var asset = ScriptableObject.CreateInstance<CloneAsset>();
        var carrier = new GraphNode();
        carrier.SetAsset(asset);

        var copy = ActionSystemDeepCopy.Copy(carrier);

        Assert.That(copy.Kind, Is.EqualTo(NodeKind.Asset));
        Assert.That(copy.AssetObject, Is.SameAs(asset));

        UnityEngine.Object.DestroyImmediate(asset);
    }

    [Serializable]
    private sealed class CarrierHolder
    {
        public GraphNode A;
        public GraphNode B;
    }

    [Serializable]
    private sealed class CarrierNode : ActionSystemNode
    {
        public GraphNode Child;
    }

    [Serializable]
    private sealed class CloneNode
    {
        public CloneAsset Asset;
        public List<CloneNode> Children = new();
        public CloneLeaf Shared;
        public CloneLeaf Other;
    }

    [Serializable]
    private sealed class CloneLeaf
    {
        public int Value;
    }

    private sealed class CloneAsset : ScriptableObject { }
}
}
