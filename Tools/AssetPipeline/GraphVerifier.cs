using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>節點圖的走訪與驗證。</summary>
    // 時序是 AssetPipeline 獨有的概念，所以住在這裡而不是 GraphKit 的 HGValidator：
        // 具名Token沒有先後，動作清單才有。
    public static class GraphVerifier
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>驗證整張圖，回傳具穩定 code 與位置的領域診斷。空清單＝通過。</summary>
        public static List<GraphDiagnostic> CollectDiagnostics(Graph graph)
        {
            var diagnostics = new List<GraphDiagnostic>();
            if (graph == null) return diagnostics;
            var errors = new DiagnosticCollector(diagnostics);

            CheckTokens(graph, errors);
            CheckProperties(graph, errors);
            CheckActions(graph, errors);
            return diagnostics;
        }

        /// <summary>Legacy text projection for callers that do not present structured diagnostics yet.</summary>
        public static List<string> Collect(Graph graph)
        {
            List<GraphDiagnostic> diagnostics = CollectDiagnostics(graph);
            var errors = new List<string>(diagnostics.Count);
            foreach (GraphDiagnostic diagnostic in diagnostics) errors.Add(diagnostic.Message);
            return errors;
        }

        /// <summary>
        /// 依動作順序檢查每一項：內容完不完整，以及讀到的目錄有沒有產出者排在前面。
        /// </summary>
        // 先寫後讀不檢查：Property 取代目錄之後，「還沒寫入」是合法狀態（讀到 default(T)），
        // 不是錯誤，所以沒有可以靜態阻擋的時序。
        private static void CheckActions(Graph graph, DiagnosticCollector errors)
        {
            List<ActionSlot> actions = graph.Actions;
            for (int i = 0; i < actions.Count; i++)
                CheckActionSlot(actions[i], $"動作[{i}]", errors, new ActionReads());
        }

        private static void CheckTokens(Graph graph, DiagnosticCollector errors)
        {
            // 唯一性是「族＋名稱」：同名不同族是兩個Token，不算重複。
            var seen = new HashSet<(Type, string)>();

            for (int i = 0; i < graph.Tokens.Count; i++)
            {
                GraphToken endpoint = graph.Tokens[i];
                if (endpoint == null) { errors.Add("assetpipeline.token.missing", $"Token[{i}]", "是空的。"); continue; }

                string path = $"Token[{endpoint.Name ?? i.ToString()}]";
                if (string.IsNullOrWhiteSpace(endpoint.Name)) errors.Add("assetpipeline.token.name-missing", $"Token[{i}]", "沒有名字。");
                if (endpoint.Slot == null) { errors.Add("assetpipeline.token.slot-missing", path, "沒有指定型別。"); continue; }

                if (!string.IsNullOrWhiteSpace(endpoint.Name) && !seen.Add((endpoint.Slot.FamilyType, endpoint.Name)))
                    errors.Add("assetpipeline.token.duplicate", path, "與另一個同型別的Token重名。");

            CheckSlot(endpoint.Slot, path, errors, new ActionReads(), new HashSet<object>(ReferenceComparer.Instance));
            }
        }

        /// <summary>Property 定義自己對不對。讀寫接線的相容性在 <see cref="CheckSlot"/>。</summary>
        // 未接時 Slot 提供常數初始值；接上時它是 Property 節點的型別化輸入來源。
        private static void CheckProperties(Graph graph, DiagnosticCollector errors)
        {
            // ProtoProperty 的 Key 是圖內全域名稱；不同族也不可同名。
            var seen = new HashSet<string>();

            for (int i = 0; i < graph.Properties.Count; i++)
            {
                GraphProperty property = graph.Properties[i];
                if (property == null) { errors.Add("assetpipeline.property.missing", $"Property[{i}]", "是空的。"); continue; }

                string path = $"Property[{property.Name ?? i.ToString()}]";
                if (!property.Proto) continue; // LocalProperty 不屬於圖層清單；保留舊資料但不把它當 Proto 定義驗證。
                if (string.IsNullOrWhiteSpace(property.Name))
                    errors.Add("assetpipeline.property.name-missing", $"Property[{i}]", "ProtoProperty 沒有名字。");
                if (property.Slot == null) { errors.Add("assetpipeline.property.slot-missing", path, "沒有指定型別。"); continue; }

                if (!string.IsNullOrWhiteSpace(property.Name) && !seen.Add(property.Name))
                    errors.Add("assetpipeline.property.duplicate", path, "與另一個 ProtoProperty 重名。");

                // 接了來源代表這顆該是Token而不是 Property：Property 的值由動作寫入，不由公式算出來。
                if (property.Slot.Node != null)
                    CheckSlot(property.Slot, path + ".input", errors, new ActionReads(), new HashSet<object>(ReferenceComparer.Instance));
            }
        }

        private static void CheckActionSlot(ActionSlotBase slot, string path, DiagnosticCollector errors, ActionReads reads,
            HashSet<object> visiting = null)
        {
            if (slot == null) { errors.Add("assetpipeline.action.missing", path, "是空的。"); return; }
            if (slot.Disabled) return;   // 停用的動作不執行，殘缺不擋。
            if (slot is not ActionSlot) { errors.Add("assetpipeline.action.slot-incompatible", path, "不是管線動作欄位。"); return; }

            GraphNode node = slot.Node;
            if (node == null) { errors.Add("assetpipeline.action.node-missing", path, "沒有接任何動作內容。"); return; }
            if (node.Disabled) return;

            if (node.Kind != NodeKind.Inline)
            {
                errors.Add("assetpipeline.action.node-not-inline", path, "的節點不是動作內容（管線動作只能接內嵌節點）。");
                return;
            }

            GraphNodeContent body = node.BodyObject;
            if (body == null) { errors.Add("assetpipeline.action.node-empty", path, "的節點是空的。"); return; }
            if (!slot.AcceptsBody(body)) { errors.Add("assetpipeline.action.body-incompatible", path, $"接的 {body.GetType().Name} 不是管線動作。"); return; }

            visiting ??= new HashSet<object>(ReferenceComparer.Instance);
            if (!visiting.Add(node)) { errors.Add("assetpipeline.node.cycle", path, "形成節點循環。"); return; }
            try
            {
                var ownReads = new ActionReads();
                var children = (body as ISequentialActionContainer)?.SequentialActions;
                if (children != null)
                    foreach (ActionSlotBase child in children)
                        if (child != null) ownReads.SequentialChildren.Add(child);

                // 只收容器自身的輸入，宣告過的子 Slot 由下面的循序走訪處理。
                WalkSlots(body, path, errors, ownReads, visiting);

                if (children != null)
                    for (int i = 0; i < children.Count; i++)
                        CheckActionSlot(children[i], $"{path}.SequentialActions[{i}]", errors, reads, visiting);
            }
            finally { visiting.Remove(node); }
        }

        // 公式欄位與目錄欄位共用這一條：兩者都是「指著一顆節點」，差別只在收得下哪些種類的節點。
        private static void CheckSlot(GraphSlotBase slot, string path, DiagnosticCollector errors, ActionReads reads, HashSet<object> visiting)
        {
            if (slot is ActionSlotBase action)
            {
                CheckActionSlot(action, path, errors, reads, visiting: visiting);
                return;
            }
            GraphNode node = slot?.Node;
            if (node == null) return;   // 常數模式，合法。
            if (node.Disabled) return;  // 停用的子樹不求值。

            switch (node.Kind)
            {
                case NodeKind.Empty:
                    errors.Add("assetpipeline.slot.node-empty", path, "的節點還沒選內容。");
                    return;

                case NodeKind.Asset:
                    errors.Add("assetpipeline.slot.asset-unsupported", path, "接了共用資產，但 AssetPipeline 不支援資產節點。");
                    return;

                // 讀取端（一般公式欄位）與寫入端（PropertySlotBase）共用這一條：兩者的差別是欄位型別，不是節點種類。
                case NodeKind.Property:
                {
                    GraphProperty property = node.Property;
                    if (property == null) { errors.Add("assetpipeline.property.target-missing", path, "指向的 Property 已不存在。"); return; }
                    if (!slot.AcceptsProperty(property))
                    {
                        errors.Add("assetpipeline.property.target-incompatible", path, $"指向的 Property [{property.Name}] 型別不相容。");
                        return;
                    }

                    // 到此為止，不往下走也不登記讀寫集合：Property 沒有取值子樹，
                    // 而寫入目標不是求值依賴——「讀 Property → 算 → 寫回同一顆」不該被判成循環。
                    return;
                }

                case NodeKind.Token:
                {
                    GraphToken endpoint = node.Token;
                    if (endpoint == null) { errors.Add("assetpipeline.token.target-missing", path, "指向的Token已不存在。"); return; }
                    if (!slot.AcceptsToken(endpoint)) { errors.Add("assetpipeline.token.target-incompatible", path, $"指向的Token [{endpoint.Name}] 型別不相容。"); return; }

                    // 跨端點的環要在這裡抓：下沉到端點自己的取值欄位繼續走。
                    if (!visiting.Add(endpoint))
                    {
                        errors.Add("assetpipeline.token.cycle", path, $"形成Token循環（經過 [{endpoint.Name}]）。");
                        return;
                    }

                    try { CheckSlot(endpoint.Slot, $"{path}→[{endpoint.Name}]", errors, reads, visiting); }
                    finally { visiting.Remove(endpoint); }
                    return;
                }

                case NodeKind.Inline:
                {
                    GraphNodeContent body = node.BodyObject;
                    if (body == null) { errors.Add("assetpipeline.slot.node-empty", path, "的節點是空的。"); return; }
                    if (!slot.AcceptsBody(body)) { errors.Add("assetpipeline.slot.body-incompatible", path, $"接的 {body.GetType().Name} 型別不相容。"); return; }

                    if (!visiting.Add(node))
                    {
                        errors.Add("assetpipeline.node.cycle", path, "形成節點循環。");
                        return;
                    }

                    try
                    {
                        WalkSlots(body, path, errors, reads, visiting);
                    }
                    finally { visiting.Remove(node); }
                    return;
                }
            }
        }


        /// <summary>反射走訪一個節點內容的所有欄位，找出巢狀的公式欄位並繼續往下檢查。</summary>
        // visited 一律用 ReferenceComparer：裸 HashSet<object> 對 struct 走值相等，
        // 兩個內容相同的 struct 第二個底下的子樹會整段被無聲跳過。
        private static void WalkSlots(object owner, string path, DiagnosticCollector errors, ActionReads reads, HashSet<object> visiting)
        {
            if (owner == null || owner is Object || owner is string) return;

            Type type = owner.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;

            if (owner is GraphSlotBase slot)
            {
                if (reads.SequentialChildren.Contains(slot)) return;
                CheckSlot(slot, path, errors, reads, visiting);
                return;
            }

            if (owner is IEnumerable collection)
            {
                int index = 0;
                foreach (object item in collection)
                {
                    WalkSlots(item, $"{path}[{index}]", errors, reads, visiting);
                    index++;
                }
                return;
            }

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(Fields))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    if (!field.IsPublic
                        && field.GetCustomAttribute<SerializeField>() == null
                        && field.GetCustomAttribute<SerializeReference>() == null) continue;

                    WalkSlots(field.GetValue(owner), $"{path}.{field.Name}", errors, reads, visiting);
                }
        }

        /// <summary>一個動作宣告的循序子動作。</summary>
        private sealed class ActionReads
        {
            public readonly HashSet<object> SequentialChildren = new HashSet<object>(ReferenceComparer.Instance);
        }

        private sealed class DiagnosticCollector
        {
            private readonly List<GraphDiagnostic> diagnostics;

            public DiagnosticCollector(List<GraphDiagnostic> diagnostics = null)
            {
                this.diagnostics = diagnostics ?? new List<GraphDiagnostic>();
            }

            public void Add(string code, string path, string detail)
            {
                diagnostics.Add(new GraphDiagnostic(code, GraphDiagnosticSeverity.Error, $"{path} {detail}",
                    new GraphDiagnosticLocation(fieldPath: path)));
            }
        }
    }
}
