using System;
using System.Collections;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public sealed class PipelineValueSnapshot
    {
        public List<PipelineAssetRecord> Assets { get; } = new();
        public string Value { get; private set; }
        public bool Observed { get; private set; }

        public static PipelineValueSnapshot Unobserved() => new() { Value = "本次尚未求值" };
        public static PipelineValueSnapshot Capture(object value)
        {
            var result = new PipelineValueSnapshot { Observed = true };
            if (value is Object asset)
                result.Assets.Add(new PipelineAssetRecord(asset, null, PipelineItemStatus.Collected, ""));
            else if (value is IList list)
            {
                foreach (object item in list)
                    if (item is Object obj && obj != null)
                        result.Assets.Add(new PipelineAssetRecord(obj, null, PipelineItemStatus.Collected, ""));
                result.Value = $"{list.Count} 項";
            }
            else result.Value = value?.ToString() ?? "null";
            return result;
        }
    }

    public sealed class PipelineCatalogSnapshot
    {
        public string NodeId;
        public string Name;
        public string State;
        public PipelineValueSnapshot Contents;
        public List<(string NodeId, PipelineValueSnapshot Value)> Cells { get; } = new();

        internal static List<GraphNode> FindCatalogs(Graph graph)
        {
            var result = new List<GraphNode>();
            var seen = new HashSet<GraphNode>();
            var pending = new Queue<object>();
            var traversal = new HGModel();
            pending.Enqueue(graph);
            // 沿用只走序列化載體的遍歷，不能往 Unity 物件或 Cell 的非序列化 Owner 回頭走。
            while (pending.Count > 0)
            {
                foreach (GraphNode node in traversal.CarriersOf(new[] { pending.Dequeue() }, null))
                {
                    if (!seen.Add(node) || node.CatalogObject is not AssetCatalogBase catalog) continue;
                    result.Add(node);
                    pending.Enqueue(catalog.Cells);
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        internal static List<PipelineCatalogSnapshot> Capture(Graph graph,
            Dictionary<CatalogCell, PipelineValueSnapshot> observations, bool evaluateCells)
        {
            var result = new List<PipelineCatalogSnapshot>();
            foreach (GraphNode node in FindCatalogs(graph))
            {
                var catalog = (AssetCatalogBase)node.CatalogObject;
                catalog.SyncCells();
                var snapshot = new PipelineCatalogSnapshot
                {
                    NodeId = node.Id,
                    Name = HGReflect.TypeName(catalog.GetType()) + (string.IsNullOrWhiteSpace(node.Note) ? "" : $" · {node.Note}"),
                    State = catalog is DynamicAssetCatalog dynamic && !dynamic.IsInitialized ? "尚未初始化／執行" : "資料快照",
                    Contents = PipelineValueSnapshot.Capture(catalog.Read()),
                };
                foreach (GraphNode child in catalog.Cells)
                {
                    if (child?.BodyObject is not CatalogCell cell) continue;
                    PipelineValueSnapshot value;
                    if (evaluateCells)
                    {
                        try { value = PipelineValueSnapshot.Capture(cell.EvaluateObject()); }
                        catch (Exception exception) { value = PipelineValueSnapshot.Capture($"求值失敗：{exception.Message}"); }
                    }
                    else if (!observations.TryGetValue(cell, out value)) value = PipelineValueSnapshot.Unobserved();
                    snapshot.Cells.Add((child.Id, value));
                }
                result.Add(snapshot);
            }
            return result;
        }
    }
}
