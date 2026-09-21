using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 節點圖的走訪與驗證。把舊 PipelineGraphAnalyzer 的兩條檢查
    /// （prototype key 缺漏、動態目錄在寫入者之前被讀取）接到節點圖上。
    /// </summary>
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

            SyncCatalogs(graph);
            CheckTokens(graph, errors);
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

        /// <summary>正式驗證前先把每一格接回它的母目錄。</summary>
        // 格子的 Owner 不序列化，接回去是驗證與執行的前提，不是驗證結果的一部分，所以先走一整趟：
        // 一、驗證是依動作順序走的，讀取排在產出之前時，格子在被檢查的當下還沒有母目錄，
        //     報出來的會是「沒有母目錄」而不是真正的時序錯誤。
        // 二、只有產出格指得到目錄，原型目錄沒有任何動作寫得進去，它的節點只存在候選池裡，
        //     光走動作永遠碰不到它——那一整條路的格子會全部取不到內容。
        // 候選節點本身不參與驗證，所以這一趟的錯誤一律丟掉，真正的錯誤由後面兩段負責。
        private static void SyncCatalogs(Graph graph)
        {
            var ignored = new DiagnosticCollector();

            List<ActionSlot> actions = graph.Actions;
            for (int i = 0; i < actions.Count; i++)
                CheckActionSlot(actions[i], $"動作[{i}]", ignored, new ActionReads());

            foreach (GraphNode node in graph.Orphans)
            {
                if (node == null) continue;
                if (node.CatalogObject is AssetCatalogBase catalog) catalog.SyncCells();
                WalkSlots(node.BodyObject, "候選節點", ignored, new ActionReads(),
                    new HashSet<object>(ReferenceComparer.Instance));
            }
        }

        /// <summary>
        /// 整張圖讀到的 prototype key → 讀它的動作。給資產頁檢查「這個 key 有沒有群組、群組是不是空的」。
        /// </summary>
        public static Dictionary<string, List<string>> CollectPrototypeKeyUsages(Graph graph)
        {
            var usages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (graph == null) return usages;

            // 這裡只要 key，錯誤由 Collect 負責回報，所以丟一個不看的清單進去。
            var ignored = new DiagnosticCollector();
            List<ActionSlot> actions = graph.Actions;

            for (int i = 0; i < actions.Count; i++)
            {
                string path = $"動作[{i}] {actions[i]?.DisplayName}";
                var reads = new ActionReads();
                CheckActionSlot(actions[i], path, ignored, reads);

                foreach (string key in reads.Prototype)
                {
                    if (!usages.TryGetValue(key, out List<string> list))
                    {
                        list = new List<string>();
                        usages.Add(key, list);
                    }
                    if (!list.Contains(path)) list.Add(path);
                }
            }

            return usages;
        }

        /// <summary>
        /// 依動作順序檢查每一項：內容完不完整，以及讀到的目錄有沒有產出者排在前面。
        /// </summary>
        // prototype key 存不存在是「資產群組」層面的事，需要 AssetPipeline 上的群組清單，
        // 所以留在 ValidatePipelinePrototypeSources；這裡只看圖自己答得出來的東西。
        private static void CheckActions(Graph graph, DiagnosticCollector errors)
        {
            // 目錄比參照，不比名稱：名稱只是顯示用，同名的兩顆仍然是兩顆。
            var producedCatalogs = new HashSet<AssetCatalogBase>();

            List<ActionSlot> actions = graph.Actions;
            for (int i = 0; i < actions.Count; i++)
                CheckActionSlot(actions[i], $"動作[{i}]", errors, new ActionReads(), producedCatalogs);
        }

        private static void CheckCatalogOrder(ActionReads reads, string path, DiagnosticCollector errors,
            HashSet<AssetCatalogBase> producedCatalogs)
        {
            foreach (AssetCatalogBase catalog in reads.CatalogReads)
            {
                if (catalog is not DynamicAssetCatalog dynamic) continue;
                if (dynamic.initialization == DynamicAssetCatalog.InitializationMode.Retain) continue;
                if (!producedCatalogs.Contains(catalog))
                    errors.Add("assetpipeline.action.dynamic-catalog-read-before-write", path,
                        "讀取動態目錄，但寫入它的動作不在前面。");
            }
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

        private static void CheckActionSlot(ActionSlotBase slot, string path, DiagnosticCollector errors, ActionReads reads,
            HashSet<AssetCatalogBase> producedCatalogs = null, HashSet<object> visiting = null)
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

                // 只收容器自身的輸入／輸出，宣告過的子 Slot 由下面的循序走訪處理。
                CollectKeys(body, ownReads);
                WalkSlots(body, path, errors, ownReads, visiting);
                if (producedCatalogs != null) CheckCatalogOrder(ownReads, path, errors, producedCatalogs);
                AddKeys(ownReads.Prototype, reads.Prototype);

                if (children != null)
                    for (int i = 0; i < children.Count; i++)
                        CheckActionSlot(children[i], $"{path}.SequentialActions[{i}]", errors, reads,
                            producedCatalogs, visiting);

                // 自身輸出在子動作結束後才可用；不可提前登記來掩蓋讀取在前的錯誤。
                if (producedCatalogs != null)
                    foreach (AssetCatalogBase catalog in ownReads.CatalogOutputs)
                        producedCatalogs.Add(catalog);

                foreach (AssetCatalogBase catalog in ownReads.CatalogReads)
                    if (!reads.CatalogReads.Contains(catalog)) reads.CatalogReads.Add(catalog);
                foreach (AssetCatalogBase catalog in ownReads.CatalogOutputs)
                    if (!reads.CatalogOutputs.Contains(catalog)) reads.CatalogOutputs.Add(catalog);
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

                // 包不求值，只有目錄欄位（CatalogSlotBase，目前只有產出格）指得到它。
                case NodeKind.Catalog:
                {
                    if (slot is not CatalogSlotBase catalogSlot) { errors.Add("assetpipeline.slot.catalog-incompatible", path, "收不下包：這一格不是產出格。"); return; }
                    if (node.CatalogObject is not AssetCatalogBase catalog)
                    {
                        errors.Add("assetpipeline.slot.catalog-invalid", path, "接的包不是目錄。");
                        return;
                    }

                    catalog.SyncCells();
                    if (catalogSlot.WritesToCatalog && catalog is not DynamicAssetCatalog)
                        errors.Add("assetpipeline.catalog.output-not-dynamic", path, "是產出格，只接得上動態目錄。");

                    CheckCatalog(catalog, path, errors);

                    List<AssetCatalogBase> bucket = catalogSlot.WritesToCatalog ? reads.CatalogOutputs : reads.CatalogReads;
                    if (!bucket.Contains(catalog)) bucket.Add(catalog);
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

                    // 接到某一格＝讀它母目錄的內容。目錄本身接不到一般欄位上，所以讀取一律從這裡登記。
                    if (body is CatalogCell cell)
                    {
                        // Owner 的宣告型別是泛型基底，這裡要的是 AssetPipeline 這一種目錄。
                        var cellOwner = cell.Owner as AssetCatalogBase;
                        if (cellOwner == null) errors.Add("assetpipeline.catalog.owner-missing", path, "的目錄格沒有母目錄。");
                        else if (!reads.CatalogReads.Contains(cellOwner))
                        {
                            reads.CatalogReads.Add(cellOwner);
                            CheckCatalog(cellOwner, path, errors);
                        }
                    }

                    if (!visiting.Add(node))
                    {
                        errors.Add("assetpipeline.node.cycle", path, "形成節點循環。");
                        return;
                    }

                    try
                    {
                        CollectKeys(body, reads);
                        WalkSlots(body, path, errors, reads, visiting);
                    }
                    finally { visiting.Remove(node); }
                    return;
                }
            }
        }

        /// <summary>目錄自己的設定對不對。同一顆在同一步只報一次，由呼叫端以 reads 去重。</summary>
        private static void CheckCatalog(AssetCatalogBase catalog, string path, DiagnosticCollector errors)
        {
            // 動態目錄的內容來自動作，設定上沒有東西可錯；時序由 CheckActions 負責。
            if (catalog is not PrototypeAssetCatalog prototype) return;

            if (string.IsNullOrWhiteSpace(prototype.catalogId))
                errors.Add("assetpipeline.catalog.id-missing", path, "的原型目錄沒有指定目錄。");
            else if (AssetPipeline.current != null
                     && AssetPipeline.current.FindCatalogById(prototype.catalogId.Trim()) == null)
                errors.Add("assetpipeline.catalog.target-missing", path, "指到的目錄已不存在。");
        }

        private static void CollectKeys(object body, ActionReads reads)
        {
            if (body is IPrototypeKeyReader prototypeReader)
                AddKeys(prototypeReader.PrototypeInputKeys, reads.Prototype);
        }

        private static void AddKeys(IEnumerable<string> source, List<string> target)
        {
            if (source == null) return;
            foreach (string key in source)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                string trimmed = key.Trim();
                if (!target.Contains(trimmed)) target.Add(trimmed);
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

        /// <summary>一個動作碰到的東西：讀到的 prototype key、讀到的目錄，以及它自己寫入的目錄。</summary>
        private sealed class ActionReads
        {
            public readonly HashSet<object> SequentialChildren = new HashSet<object>(ReferenceComparer.Instance);
            public readonly List<string> Prototype = new List<string>();
            public readonly List<AssetCatalogBase> CatalogReads = new List<AssetCatalogBase>();
            public readonly List<AssetCatalogBase> CatalogOutputs = new List<AssetCatalogBase>();
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
