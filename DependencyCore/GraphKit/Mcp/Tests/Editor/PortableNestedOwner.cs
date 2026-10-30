namespace HaruFamily.Tools.GraphMcp.Editor.Tests
{
using System;
using UnityEngine;

public sealed class PortableNestedOwner : ScriptableObject
{
    public PortableNestedData Data = new();
}
[Serializable]
public sealed class PortableNestedData
{
    [SerializeReference] public PortableGraphDocument First = new();
    [SerializeReference] public PortableGraphDocument Second = new();
}
}
