using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>找出整張圖用得到的 Property 定義。</summary>
    // 執行前備份、失敗回復與結果快照都必須走這一條。ProtoProperty 的定義住 graph.Properties，
    // 但 LocalProperty 是節點私有的，只讀那份清單會讓節點私有定義既回復不了也顯示不出來。
    public static class GraphPropertyWalk
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// 圖層 ProtoProperty 清單加上節點私有的 LocalProperty，依引用去重。
        /// 同一顆被多個讀取端與寫入端指著只會出現一次。
        /// </summary>
        // 不依停用狀態過濾：停用只影響這一次執行寫不寫得進去，不改變「這顆定義屬於這張圖」。
        public static List<GraphProperty> Collect(Graph graph)
        {
            var result = new List<GraphProperty>();
            if (graph == null) return result;

            var seen = new HashSet<object>(ReferenceComparer.Instance);
            var visited = new HashSet<object>(ReferenceComparer.Instance);

            foreach (GraphProperty property in graph.Properties) Take(property, result, seen);
            foreach (ActionSlot slot in graph.Actions) Walk(slot, result, seen, visited);
            foreach (GraphToken endpoint in graph.Tokens) Walk(endpoint?.Slot, result, seen, visited);
            return result;
        }

        private static void Take(GraphProperty property, List<GraphProperty> result, HashSet<object> seen)
        {
            if (property != null && seen.Add(property)) result.Add(property);
        }

        // 與 GraphVerifier.WalkSlots 同一種走訪，但收的是 Property 定義而不是診斷：
        // 欄位指著載體，載體才知道自己是不是 Property。
        private static void Walk(object owner, List<GraphProperty> result, HashSet<object> seen, HashSet<object> visited)
        {
            if (owner == null || owner is Object || owner is string) return;

            Type type = owner.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;
            if (!visited.Add(owner)) return;

            if (owner is GraphSlotBase slot)
            {
                Walk(slot.Node, result, seen, visited);
                return;
            }

            if (owner is GraphNode node)
            {
                switch (node.Kind)
                {
                    case NodeKind.Property:
                        Take(node.Property, result, seen);
                        return;
                    case NodeKind.Token:
                        Walk(node.Token?.Slot, result, seen, visited);
                        return;
                    default:
                        Walk(node.BodyObject, result, seen, visited);
                        return;
                }
            }

            if (owner is IEnumerable collection)
            {
                foreach (object item in collection) Walk(item, result, seen, visited);
                return;
            }

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(Fields))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    if (!field.IsPublic
                        && field.GetCustomAttribute<SerializeField>() == null
                        && field.GetCustomAttribute<SerializeReference>() == null) continue;

                    Walk(field.GetValue(owner), result, seen, visited);
                }
        }
    }
}
