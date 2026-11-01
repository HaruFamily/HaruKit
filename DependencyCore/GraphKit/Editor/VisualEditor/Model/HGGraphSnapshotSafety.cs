namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared detached-graph and value safety checks for background queries and native commands.</summary>
internal static class HGGraphSnapshotSafety
{
    internal static bool CanEditValueType(Type type)
        => HGValueField.CanDraw(type) && type != typeof(AnimationCurve) && type != typeof(Gradient);
    internal static void RequireDetachedGraph(object source, object copy)
    {
        var original = new HashSet<object>(ReferenceComparer.Instance);
        var cloned = new HashSet<object>(ReferenceComparer.Instance);
        foreach (var slot in HGModel.WalkSlots(source, original))
            if (original.Count > 20000) throw new InvalidOperationException("來源圖超過隔離檢查上限。");
        foreach (var slot in HGModel.WalkSlots(copy, cloned))
            if (cloned.Count > 20000) throw new InvalidOperationException("預覽圖超過隔離檢查上限。");
        if (original.Count > 20000 || cloned.Count > 20000) throw new InvalidOperationException("圖超過隔離檢查上限。");
        foreach (var value in original)
            if ((value is GraphSlotBase || value is GraphNode || value is GraphNodeContent || value is GraphToken || value is GraphProperty)
                && cloned.Contains(value)) throw new InvalidOperationException("預覽副本仍共用來源圖物件，已停止建圖。");
    }

}
}
