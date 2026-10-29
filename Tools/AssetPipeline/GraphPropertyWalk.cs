using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>執行前備份、回復及結果快照共用的圖層 Property 清單。</summary>
    public static class GraphPropertyWalk
    {
        public static List<GraphProperty> Collect(Graph graph)
        {
            var result = new List<GraphProperty>();
            var seen = new HashSet<object>(ReferenceComparer.Instance);
            if (graph != null)
                foreach (var property in graph.Properties)
                    if (property != null && seen.Add(property)) result.Add(property);
            return result;
        }
    }
}
