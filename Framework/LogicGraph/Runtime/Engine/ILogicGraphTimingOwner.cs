namespace HaruFamily.Framework.LogicGraph
{
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>Editor-only timing choices for an Owner whose document is a LogicGraph.</summary>
public interface ILogicGraphTimingOwner : IGraphOwner
{
#if UNITY_EDITOR
    IReadOnlyList<Enum> AllowedTimings { get; }
#endif
}
}
