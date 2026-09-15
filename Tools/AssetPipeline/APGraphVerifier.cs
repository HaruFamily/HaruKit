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
    // 具名Token沒有先後，步驟清單才有。
    public static class APGraphVerifier
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>驗證整張圖，回傳錯誤訊息清單。空清單＝通過。</summary>
        public static List<string> Collect(APGraph graph)
        {
            var errors = new List<string>();
            if (graph == null) return errors;

            CheckTokens(graph, errors);
            CheckSteps(graph, errors);
            return errors;
        }

        /// <summary>
        /// 整張圖讀到的 prototype key → 讀它的步驟。給資產頁檢查「這個 key 有沒有群組、群組是不是空的」。
        /// </summary>
        public static Dictionary<string, List<string>> CollectPrototypeKeyUsages(APGraph graph)
        {
            var usages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (graph == null) return usages;

            // 這裡只要 key，錯誤由 Collect 負責回報，所以丟一個不看的清單進去。
            var ignored = new List<string>();
            List<APActionSlot> steps = graph.Steps;

            for (int i = 0; i < steps.Count; i++)
            {
                string path = $"步驟[{i}] {steps[i]?.DisplayName}";
                var reads = new StepReads();
                CheckActionSlot(steps[i], path, ignored, reads);

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
        /// 依步驟順序檢查每一步：內容完不完整，以及讀到的目錄有沒有產出者排在前面。
        /// </summary>
        // prototype key 存不存在是「資產群組」層面的事，需要 AssetPipeline 上的群組清單，
        // 所以留在 ValidatePipelinePrototypeSources；這裡只看圖自己答得出來的東西。
        private static void CheckSteps(APGraph graph, List<string> errors)
        {
            // 目錄比參照，不比名稱：名稱只是顯示用，同名的兩顆仍然是兩顆。
            var producedCatalogs = new HashSet<AssetCatalog>();

            List<APActionSlot> steps = graph.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                APActionSlot slot = steps[i];
                string path = $"步驟[{i}]";

                var reads = new StepReads();
                CheckActionSlot(slot, path, errors, reads);

                // 動態來源的目錄是前面的步驟跑完才有內容的，所以只比對「到目前為止已產出」的集合。
                // 原型來源隨時都有值，不受步驟順序影響。
                foreach (AssetCatalog catalog in reads.CatalogReads)
                {
                    if (!catalog.AcceptsWrite) continue;
                    if (!producedCatalogs.Contains(catalog))
                        errors.Add($"{path} 讀取動態目錄，但寫入它的步驟不在前面。");
                }

                // 產出登記放在檢查之後：同一步讀自己的產出，仍然是「還沒跑完就讀」。
                foreach (AssetCatalog catalog in reads.CatalogOutputs)
                    producedCatalogs.Add(catalog);
            }
        }

        private static void CheckTokens(APGraph graph, List<string> errors)
        {
            // 唯一性是「族＋名稱」：同名不同族是兩個Token，不算重複。
            var seen = new HashSet<(Type, string)>();

            for (int i = 0; i < graph.Tokens.Count; i++)
            {
                GraphToken endpoint = graph.Tokens[i];
                if (endpoint == null) { errors.Add($"Token[{i}] 是空的。"); continue; }

                string path = $"Token[{endpoint.Name ?? i.ToString()}]";
                if (string.IsNullOrWhiteSpace(endpoint.Name)) errors.Add($"Token[{i}] 沒有名字。");
                if (endpoint.Slot == null) { errors.Add($"{path} 沒有指定型別。"); continue; }

                if (!string.IsNullOrWhiteSpace(endpoint.Name) && !seen.Add((endpoint.Slot.Kind, endpoint.Name)))
                    errors.Add($"{path} 與另一個同型別的Token重名。");

                CheckFormulaSlot(endpoint.Slot, path, errors, new StepReads(), new HashSet<object>(ReferenceComparer.Instance));
            }
        }

        private static void CheckActionSlot(APActionSlot slot, string path, List<string> errors, StepReads reads)
        {
            if (slot == null) { errors.Add($"{path} 是空的。"); return; }
            if (slot.Disabled) return;   // 停用的步驟不執行，殘缺不擋。

            GraphNode node = slot.Node;
            if (node == null) { errors.Add($"{path} 沒有接任何步驟內容。"); return; }
            if (node.Disabled) return;

            if (node.Kind != NodeKind.Inline)
            {
                errors.Add($"{path} 的節點不是步驟內容（管線步驟只能接內嵌節點）。");
                return;
            }

            GraphNodeContent body = node.BodyObject;
            if (body == null) { errors.Add($"{path} 的節點是空的。"); return; }
            if (!slot.AcceptsBody(body)) { errors.Add($"{path} 接的 {body.GetType().Name} 不是管線步驟。"); return; }

            CollectKeys(body, reads);
            WalkSlots(body, path, errors, reads, new HashSet<object>(ReferenceComparer.Instance));
        }

        private static void CheckFormulaSlot(FormulaSlotBase slot, string path, List<string> errors, StepReads reads, HashSet<object> visiting)
        {
            GraphNode node = slot?.Node;
            if (node == null) return;   // 常數模式，合法。
            if (node.Disabled) return;  // 停用的子樹不求值。

            switch (node.Kind)
            {
                case NodeKind.Empty:
                    errors.Add($"{path} 的節點還沒選內容。");
                    return;

                case NodeKind.Asset:
                    errors.Add($"{path} 接了共用資產，但 AssetPipeline 不支援資產節點。");
                    return;

                // 包不求值，只有宣告 AcceptsPack 的欄位（目前只有產出格）指得到它。
                case NodeKind.Pack:
                {
                    if (!slot.AcceptsPack) { errors.Add($"{path} 收不下包：這一格不是產出格。"); return; }
                    if (node.PackObject is not AssetCatalog catalog)
                    {
                        errors.Add($"{path} 接的包不是目錄。");
                        return;
                    }

                    catalog.SyncCells();
                    if (slot.IsOutput && !catalog.AcceptsWrite)
                        errors.Add($"{path} 是產出格，但接到的目錄設為原型來源，寫不進去。");

                    CheckCatalog(catalog, path, errors);

                    List<AssetCatalog> bucket = slot.IsOutput ? reads.CatalogOutputs : reads.CatalogReads;
                    if (!bucket.Contains(catalog)) bucket.Add(catalog);
                    return;
                }

                case NodeKind.Token:
                {
                    GraphToken endpoint = node.Token;
                    if (endpoint == null) { errors.Add($"{path} 指向的Token已不存在。"); return; }
                    if (!slot.AcceptsToken(endpoint)) { errors.Add($"{path} 指向的Token [{endpoint.Name}] 型別不相容。"); return; }

                    // 跨端點的環要在這裡抓：下沉到端點自己的取值欄位繼續走。
                    if (!visiting.Add(endpoint))
                    {
                        errors.Add($"{path} 形成Token循環（經過 [{endpoint.Name}]）。");
                        return;
                    }

                    try { CheckFormulaSlot(endpoint.Slot, $"{path}→[{endpoint.Name}]", errors, reads, visiting); }
                    finally { visiting.Remove(endpoint); }
                    return;
                }

                case NodeKind.Inline:
                {
                    GraphNodeContent body = node.BodyObject;
                    if (body == null) { errors.Add($"{path} 的節點是空的。"); return; }
                    if (!slot.AcceptsBody(body)) { errors.Add($"{path} 接的 {body.GetType().Name} 型別不相容。"); return; }

                    // 接到某一格＝讀它母目錄的內容。目錄本身接不到一般欄位上，所以讀取一律從這裡登記。
                    if (body is CatalogCell cell)
                    {
                        if (cell.Owner == null) errors.Add($"{path} 的目錄格沒有母目錄。");
                        else if (!reads.CatalogReads.Contains(cell.Owner))
                        {
                            reads.CatalogReads.Add(cell.Owner);
                            CheckCatalog(cell.Owner, path, errors);
                        }
                    }

                    if (!visiting.Add(node))
                    {
                        errors.Add($"{path} 形成節點循環。");
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
        private static void CheckCatalog(AssetCatalog catalog, string path, List<string> errors)
        {
            if (catalog.source != CatalogSource.Prototype) return;
            if (string.IsNullOrWhiteSpace(catalog.prototypeCatalogId))
                errors.Add($"{path} 的目錄設為原型來源，但沒有指定目錄。");
            else if (AssetPipeline.current != null
                     && AssetPipeline.current.FindCatalogById(catalog.prototypeCatalogId.Trim()) == null)
                errors.Add($"{path} 指到的目錄已不存在。");
        }

        private static void CollectKeys(object body, StepReads reads)
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
        private static void WalkSlots(object owner, string path, List<string> errors, StepReads reads, HashSet<object> visiting)
        {
            if (owner == null || owner is Object || owner is string) return;

            Type type = owner.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;

            if (owner is FormulaSlotBase slot)
            {
                CheckFormulaSlot(slot, path, errors, reads, visiting);
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

        /// <summary>一個步驟碰到的東西：讀到的 prototype key、讀到的目錄，以及它自己寫入的目錄。</summary>
        private sealed class StepReads
        {
            public readonly List<string> Prototype = new List<string>();
            public readonly List<AssetCatalog> CatalogReads = new List<AssetCatalog>();
            public readonly List<AssetCatalog> CatalogOutputs = new List<AssetCatalog>();
        }
    }
}
