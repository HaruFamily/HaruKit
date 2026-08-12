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
