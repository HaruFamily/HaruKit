namespace HaruFamily.Framework.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>一則驗證訊息：在哪裡、是什麼問題、怎麼處理。</summary>
public class AGIssue
{
    public bool IsError;
    public string Message;
    public string Where;
    public string Fix;

    public AGFocus Focus;      // 點擊要跳到的焦點
    public object Slot;        // 出問題的參數欄位（可空）
    public object Node;        // 出問題的節點（可空）

    public string Line => $"{Where}：{Message}　→ {Fix}";
}

/// <summary>驗證結果彙總。</summary>
public class AGReport
{
    public List<AGIssue> Issues = new();
    public DateTime Time = DateTime.Now;

    public int ErrorCount
    {
        get { int n = 0; foreach (var i in Issues) if (i.IsError) n++; return n; }
    }
    public int WarningCount => Issues.Count - ErrorCount;
    public bool CanSave => ErrorCount == 0;

    /// <summary>某個焦點下的問題數（右欄動作清單與時機下拉要顯示）。</summary>
    public void CountFor(AGFocus focus, out int errors, out int warnings)
    {
        errors = 0; warnings = 0;
        foreach (var i in Issues)
        {
            if (i.Focus == null || !i.Focus.SameAs(focus)) continue;
            if (i.IsError) errors++; else warnings++;
        }
    }

    public bool HasIssue(object slotOrNode, out bool isError)
    {
        isError = false;
        if (slotOrNode == null) return false;      // null 會跟「沒有定位資訊」的訊息誤配
        bool found = false;
        foreach (var i in Issues)
        {
            if (!ReferenceEquals(i.Slot, slotOrNode) && !ReferenceEquals(i.Node, slotOrNode)) continue;
            found = true;
            if (i.IsError) { isError = true; return true; }
        }
        return found;
    }
}

/// <summary>
/// 依需求書 §10 產生結構化驗證訊息。跟 Core 的 Verify 規則同源，但額外帶「跳轉位置」，Console 才能一鍵定位。
/// </summary>
public static class AGValidator
{
    /// <summary>
    /// 圖的內容規則只有一套，編輯時與存檔時跑的是同一份（含 Token 循環與 Asset 參照循環）。
    /// includeMissingTypes 另外處理：它檢查的是 Owner 資產本身，編輯工作副本不會改變它，只在綁定與存檔時才有意義。
    /// </summary>
    public static AGReport Run(AGModel model, bool includeMissingTypes = false)
    {
        if (probeDepth == 0) assetHealth.Clear();
        var report = new AGReport();
        if (model?.Data == null) return report;

        var tokens = AGModel.ReadTokens(model.OwnerEndpoints);
        var checkedAssets = new HashSet<UnityEngine.Object>();

        // 1. 變數本身：名稱空白、名稱重複（同族內唯一）
        //    「宣告後沒有欄位引用」不是問題：變數的用途就是被圖外面用，沒有連入線是正常狀態。
        var seen = new HashSet<(Type, string)>();
        foreach (var t in tokens)
        {
            var focus = VariableFocus(t);
            if (!seen.Add((t.Kind, t.Key)))
                Err(report, focus, $"變數 {t.Key}", "名稱重複",
                    "改成同族內唯一的名稱；撞號時外部只查得到其中一個。", null, t.Endpoint);

            ValidateToken(report, model, focus, t);
        }

        // 2. 每個動作、每個 Token 的節點樹
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            for (int i = 0; i < g.Actions.Count; i++)
            {
                var slot = g.Actions[i];
                if (slot == null) continue;
                var focus = new AGFocus
                {
                    Kind = AGFocusKind.Action,
                    Timing = g.Timing,
                    ActionList = g.Actions,
                    ActionIndex = i,
                    ActionSlot = slot,
                };
                bool disabled = AGReflect.GetDisabled(slot) || (AGReflect.GetNode(slot)?.Disabled ?? false);
                if (AGReflect.UseType(slot) == 0)
                    Issue(report, disabled, focus, $"{g.Timing} 第 {i + 1} 個動作", "尚未指定 Action 類型",
                        "在空 Action Node 的下拉選單選擇一個 Action。", slot, null);
                WalkTree(report, model, focus, slot, $"{g.Timing} 第 {i + 1} 個動作", disabled);
                ValidateAssetCycles(report, focus, slot, null, $"{g.Timing} 第 {i + 1} 個動作", checkedAssets);
            }
        }

        // 從變數的取值欄位開始整棵子樹都是正式資料，要跟動作樹一樣驗。
        foreach (var t in tokens)
        {
            WalkTokenCarrier(report, model, VariableFocus(t), t, checkedAssets, null);
        }

        // 2.1 Owner 指名了不存在的標註。runtime 只是 Has 回 false 然後靜默跳過，
        //     所以打錯一個字的結果是「功能整個不會發生」，什麼訊息都沒有。
        foreach (var key in ExternalTokenKeys(model.Owner))
        {
            bool declared = false;
            foreach (var t in tokens)
                if (t.Key == key) { declared = true; break; }
            if (declared) continue;
            Err(report, null, model.Owner != null ? model.Owner.name : "編輯對象",
                $"Inspector 指名了不存在的變數 '{key}'",
                "在左欄建一個同名變數，或修正 Inspector 上的名稱；查不到的 key 會被靜默跳過。", null, null);
        }

        // 4. SerializeReference 型別遺失（類別被改名或刪掉）
        //    反射看不到殘骸，只有 Unity 的 managed reference API 知道。存檔會把殘骸永久抹掉，所以必須擋。
        //    對象是 Owner 本體：編輯工作副本不會改變它，所以只在綁定與存檔時檢查。
        if (includeMissingTypes && model.Owner != null
            && UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(model.Owner))
        {
            foreach (var missing in UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(model.Owner))
            {
                Err(report, null, "資產本體",
                    $"有節點的程式類別已不存在：{missing.namespaceName}.{missing.className}（{missing.assemblyName}）",
                    "把類別改回原名，或確認要放棄這段內容後手動清除；直接存檔會永久刪掉它。", null, null);
            }
        }

        return report;
    }

    /// <summary>只驗一棵子樹（資產焦點用）。</summary>
    public static AGReport RunSubtree(AGModel model, AGFocus focus, object rootSlot, string where)
    {
        if (probeDepth == 0) assetHealth.Clear();
        var report = new AGReport();
        if (model?.Data == null || rootSlot == null) return report;

        WalkTree(report, model, focus, rootSlot, where, false);
        ValidateAssetCycles(report, focus, rootSlot, focus?.AssetObject, where, new HashSet<UnityEngine.Object>());

        var rootCarrier = AGReflect.GetNode(rootSlot);
        if (rootCarrier?.Kind == NodeKind.Token)
            Err(report, focus, where, "資產內容不能只是一個變數引用",
                "資產的內容要是公式或動作；要對外開參數請用左欄的變數清單。", rootSlot, rootCarrier);

        var tokens = AGModel.ReadTokens(focus?.AssetEndpoints);
        var seen = new HashSet<(Type, string)>();
        foreach (var token in tokens)
        {
            var tokenFocus = AssetVariableFocus(focus, token);
            if (!seen.Add((token.Kind, token.Key)))
                Err(report, tokenFocus, $"變數 {token.Key}", "名稱重複",
                    "改成這個資產內同族唯一的名稱。", null, token.Endpoint);
            ValidateToken(report, model, tokenFocus, token);
            WalkTokenCarrier(report, model, tokenFocus, token, new HashSet<UnityEngine.Object>(), focus?.AssetObject);
        }
        return report;
    }

    // 一次驗證裡同一個資產只探一次：巢狀資產與多處引用都會問到同一顆。
    private static readonly Dictionary<UnityEngine.Object, bool> assetHealth = new();

    // >0 代表正在探測資產內部，此時不可清快取（清掉會讓循環引用的探測無限遞迴）。
    private static int probeDepth;

    /// <summary>
    /// 資產「存檔後的內容」自己還有沒有錯。規則與資產畫布完全相同（直接跑 <see cref="RunSubtree"/>），
    /// 但呼叫端只拿一個是非——細項留在資產畫布裡報，才不會在改不了它的畫布上列一堆跳不過去的訊息。
    /// </summary>
    /// hostSlotType：資產內容要塞進哪一種欄位才驗得動（＝引用它的那個欄位型別）。資產本身不記這件事。
    public static bool AssetHasError(AGModel model, Type hostSlotType, UnityEngine.Object asset)
    {
        if (model == null || asset == null || hostSlotType == null) return false;
        if (assetHealth.TryGetValue(asset, out bool cached)) return cached;
        // 先佔位：巢狀引用繞回自己時當成沒問題，循環本身由 ValidateAssetCycles 專門報。
        assetHealth[asset] = false;

        var root = AGReflect.AssetRoot(asset);
        if (root == null) return false;   // 空資產：資產畫布也不報，這裡跟著不報
        object host = AGReflect.CreateInstance(hostSlotType);
        if (host == null) return false;
        AGReflect.SetNode(host, root);

        var probeFocus = new AGFocus
        {
            Kind = AGFocusKind.Asset,
            AssetObject = asset,
            AssetHostSlot = host,
            AssetOrphans = AGReflect.Orphans(asset),
            AssetEndpoints = AGReflect.Endpoints(asset),
        };

        probeDepth++;
        AGReport probe;
        try { probe = RunSubtree(model, probeFocus, host, asset.name); }
        finally { probeDepth--; }

        bool hasError = probe.ErrorCount > 0;
        assetHealth[asset] = hasError;
        return hasError;
    }

    /// <summary>問題要跳回那個變數自己的畫布。</summary>
    private static AGFocus VariableFocus(AGToken token)
        => new AGFocus { Kind = AGFocusKind.Variable, Endpoint = token?.Endpoint };

    private static AGFocus AssetVariableFocus(AGFocus assetFocus, AGToken token)
        => new AGFocus
        {
            Kind = AGFocusKind.Asset,
            AssetObject = assetFocus?.AssetObject,
            AssetHostSlot = assetFocus?.AssetHostSlot,
            AssetOrphans = assetFocus?.AssetOrphans,
            AssetEndpoints = assetFocus?.AssetEndpoints,
            Endpoint = token?.Endpoint,
        };

    private static void ValidateToken(AGReport report, AGModel model, AGFocus focus, AGToken token)
    {
        if (token?.Endpoint == null) return;
        if (token.Endpoint.Slot == null)
        {
            Err(report, focus, $"變數 {token.Key ?? "（未命名）"}", "沒有取值欄位",
                "刪掉這個變數重建；結果型別是建立時決定的。", null, token.Endpoint);
            return;
        }
        if (string.IsNullOrEmpty(token.Key))
            Err(report, focus, $"{AGReflect.ResultTypeName(token.ResultType)} 變數", "沒有名稱",
                "取一個名字；外部是用名字查它的值。", null, token.Endpoint);
    }

    private static void WalkTokenCarrier(AGReport report, AGModel model, AGFocus focus, AGToken token,
        HashSet<UnityEngine.Object> checkedAssets, UnityEngine.Object rootAsset)
    {
        if (token?.Endpoint?.Slot == null) return;
        string where = $"變數 {token.Key}";
        WalkTree(report, model, focus, token.Endpoint.Slot, where, false);
        ValidateAssetCycles(report, focus, token.Endpoint.Slot, rootAsset, where, checkedAssets);
    }

    // ===== 節點樹走訪 =====

    private static void WalkTree(AGReport report, AGModel model, AGFocus focus, object slot,
        string where, bool disabled)
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        WalkSlot(report, model, focus, slot, where, visited, disabled);
    }

    private static void WalkSlot(AGReport report, AGModel model, AGFocus focus, object slot,
        string where, HashSet<object> visited, bool disabled)
    {
        if (slot == null || !visited.Add(slot)) return;

        int useType = AGReflect.UseType(slot);

        // 停用往下傳染：載體停用後整棵子樹都不求值，殘缺一律降成警告。
        disabled = disabled || (AGReflect.GetNode(slot)?.Disabled ?? false);

        if (useType == 1)
        {
            var formula = AGReflect.GetFormula(slot);
            if (formula == null)
                Issue(report, disabled, focus, where, "欄位設為公式，但內容是空的", "選一個公式，或把模式改回常數。", slot, null);
            else
                WalkNode(report, model, focus, formula, where, visited, disabled);
        }
        else if (useType == 2)
        {
            var asset = AGReflect.GetAsset(slot);
            if (asset == null)
                Issue(report, disabled, focus, where, "欄位設為資產，但沒有指定資產", "指定一個資產，或把模式改回常數。", slot, null);
            var carrier = AGReflect.GetNode(slot);
            ValidateAssetBindings(report, focus, carrier, where);

            // 資產內部殘缺在這張畫布上修不了，所以只報一條入口級錯誤讓人跳進去；不報的話會變成
            // 「視覺驗證全綠、存檔被 Core 擋住且沒有訊息」。細項在資產畫布自己的驗證裡。
            if (AssetHasError(model, slot.GetType(), asset))
                Issue(report, disabled, focus, where, $"資產 '{asset.name}' 內部有錯誤",
                    "雙擊這顆節點進入資產畫布，依那裡的驗證訊息修正。", slot, carrier);
            if (carrier != null)
            {
                foreach (var binding in carrier.Bindings)
                    if (binding?.Slot != null)
                        WalkSlot(report, model, focus, binding.Slot, $"{where}.{binding.Name}", visited,
                            disabled || !binding.OverrideEnabled);
            }
        }
        else if (useType == 3)
        {
            // 端點被刪掉時參照會變 null，這裡看得到；不會像字串 key 一樣留著一個查不到的名字。
            var endpoint = AGReflect.GetEndpoint(slot);
            if (endpoint == null)
                Issue(report, disabled, focus, where, "欄位設為變數，但沒有指定變數",
                    "選一個變數，或把模式改回常數。", slot, AGReflect.GetNode(slot));
            else if (!AGReflect.AcceptsEndpoint(slot, endpoint))
                Err(report, focus, where, $"接的變數 '{endpoint.Name}' 型別不相容",
                    "改接同結果型別的變數。", slot, AGReflect.GetNode(slot));
            else if (!InScope(model, focus, endpoint))
                Err(report, focus, where, $"接的變數 '{endpoint.Name}' 不屬於這張圖",
                    "改接本圖變數清單裡的變數；求值是用名字在本圖的變數表查的，跨圖引用永遠查不到，會靜默取預設值。",
                    slot, AGReflect.GetNode(slot));
        }
    }

    /// <summary>
    /// 這個端點在不在當前這張圖的變數清單裡。資產焦點看資產自己的清單，其餘看 Owner 的。
    /// </summary>
    // 求值時 Token 節點是拿「名字」去當前作用域的 TokenTable 查（資產作用域只登記資產自己的參數），
    // 所以引用到別張圖的端點物件不會報錯、也不會求出值，只會回預設值——這是唯一擋得住的地方。
    private static bool InScope(AGModel model, AGFocus focus, GraphEndpoint endpoint)
    {
        var scope = focus != null && focus.Kind == AGFocusKind.Asset ? focus.AssetEndpoints : model?.OwnerEndpoints;
        if (scope == null) return true;   // 讀不到清單就不判，寧可不報也不要誤報
        foreach (var other in scope)
            if (ReferenceEquals(other, endpoint)) return true;
        return false;
    }

    private static void ValidateAssetBindings(AGReport report, AGFocus focus, GraphNode carrier, string where)
    {
        if (carrier?.AssetObject == null) return;
        var parameters = AssetGraphSchema.Read(carrier.AssetObject, out var duplicates);
        foreach (var duplicate in duplicates)
            Err(report, focus, where, $"資產參數標註名稱重複：'{duplicate}'", "進入資產並改成唯一名稱。", null, carrier);

        // 綁定與參數的配對鍵是（族, 名稱），和 TokenTable 的覆蓋表一致：同名不同族的參數是兩個參數。
        var byKey = new HashSet<(Type, string)>();
        var parameterNames = new HashSet<string>();
        foreach (var parameter in parameters)
        {
            byKey.Add((parameter.Slot.Kind, parameter.Name));
            parameterNames.Add(parameter.Name);
        }
        var seen = new HashSet<(Type, string)>();
        foreach (var binding in carrier.Bindings)
        {
            if (binding == null) { Err(report, focus, where, "有空的資產參數綁定", "移除空綁定。", null, carrier); continue; }
            if (binding.Slot == null)
            {
                Err(report, focus, where, $"資產參數 '{binding.Name}' 沒有取值欄位", "重新建立這筆綁定。", null, carrier);
                continue;
            }
            var key = (binding.Slot.Kind, binding.Name);
            if (!seen.Add(key))
                Err(report, focus, where, $"資產參數綁定重複：'{binding.Name}'", "移除重複綁定。", binding.Slot, carrier);
            if (byKey.Contains(key)) continue;
            if (parameterNames.Contains(binding.Name))
                Err(report, focus, where, $"資產參數 '{binding.Name}' 型別不相容", "重新建立這筆綁定。", binding.Slot, carrier);
            else
                Err(report, focus, where, $"資產已沒有參數 '{binding.Name}'", "切換資產或移除這筆舊綁定。", binding.Slot, carrier);
        }
    }

    private static void WalkNode(AGReport report, AGModel model, AGFocus focus, object node,
        string where, HashSet<object> visited, bool disabled)
    {
        if (node == null || !visited.Add(node)) return;
        string nodeWhere = $"{where} → {AGReflect.TypeName(node.GetType())}";

        foreach (var f in AGReflect.Fields(node.GetType()))
        {
            if (f.IsNotSerialized || f.IsStatic) continue;
            var val = f.GetValue(node);
            if (val == null) continue;

            var t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || val is string || val is UnityEngine.Object) continue;

            if (AGReflect.IsSlotType(t))
            {
                WalkSlot(report, model, focus, val, $"{nodeWhere}.{AGReflect.FieldLabel(f)}", visited, disabled);
                continue;
            }

            if (val is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    string itemWhere = $"{nodeWhere}.{AGReflect.FieldLabel(f)}[{i + 1}]";
                    if (item == null)
                    {
                        Warn(report, focus, itemWhere, "清單有空項目", "填入內容或移除這一列。", null, node);
                        continue;
                    }
                    if (AGReflect.IsSlotType(item.GetType()))
                        WalkSlot(report, model, focus, item, itemWhere, visited, disabled);
                    else if (!item.GetType().IsPrimitive && item is not string && item is not UnityEngine.Object)
                        WalkNode(report, model, focus, item, itemWhere, visited, disabled);
                }
                continue;
            }

            WalkNode(report, model, focus, val, nodeWhere, visited, disabled);
        }
    }

    // Token 循環偵測已隨標註化移除：圖內引用一律是連線，環在拉線當下就被 CanConnectLink 擋掉。

    // ===== Asset 參照循環 =====

    private static void ValidateAssetCycles(AGReport report, AGFocus focus, object root,
        UnityEngine.Object rootAsset, string where, HashSet<UnityEngine.Object> completed)
    {
        var stack = new HashSet<UnityEngine.Object>();
        var path = new List<UnityEngine.Object>();
        if (rootAsset != null)
        {
            stack.Add(rootAsset);
            path.Add(rootAsset);
        }

        string cycle = null;
        foreach (var asset in DirectAssetReferences(root))
        {
            cycle = FindAssetCycle(asset, stack, path, completed);
            if (cycle != null) break;
        }
        if (rootAsset != null) completed.Add(rootAsset);
        if (cycle == null) return;

        Err(report, focus, where, $"Asset 循環引用：{cycle}",
            "替換其中一個 Asset，切斷遞迴引用。", root, null);
    }

    private static string FindAssetCycle(UnityEngine.Object asset, HashSet<UnityEngine.Object> stack,
        List<UnityEngine.Object> path, HashSet<UnityEngine.Object> completed)
    {
        if (asset == null || completed.Contains(asset)) return null;
        if (stack.Contains(asset))
        {
            int start = 0;
            while (start < path.Count && path[start] != asset) start++;
            var names = new List<string>();
            for (int i = start; i < path.Count; i++) names.Add(path[i] != null ? path[i].name : "?");
            names.Add(asset.name);
            return string.Join(" → ", names);
        }

        stack.Add(asset);
        path.Add(asset);
        string cycle = null;
        foreach (var child in DirectAssetReferences(AssetContent(asset)))
        {
            cycle = FindAssetCycle(child, stack, path, completed);
            if (cycle != null) break;
        }
        if (cycle == null && asset is UnityEngine.ScriptableObject scriptable)
        {
            foreach (var parameter in AssetGraphSchema.Read(scriptable, out _))
            {
                foreach (var child in DirectAssetReferences(parameter.Slot))
                {
                    cycle = FindAssetCycle(child, stack, path, completed);
                    if (cycle != null) break;
                }
                if (cycle != null) break;
            }
        }
        path.RemoveAt(path.Count - 1);
        stack.Remove(asset);
        completed.Add(asset);
        return cycle;
    }

    private static IEnumerable<UnityEngine.Object> DirectAssetReferences(object root)
    {
        var result = new List<UnityEngine.Object>();
        CollectDirectAssetReferences(root, new HashSet<object>(AGRefComparer.Instance), result);
        return result;
    }

    private static void CollectDirectAssetReferences(object node, HashSet<object> visited,
        List<UnityEngine.Object> result)
    {
        if (node == null || !visited.Add(node)) return;
        Type type = node.GetType();
        if (AGReflect.IsSlotType(type))
        {
            int useType = AGReflect.UseType(node);
            if (useType == 1)
                CollectDirectAssetReferences(AGReflect.GetFormula(node), visited, result);
            else if (useType == 2 && AGReflect.GetAsset(node) is UnityEngine.Object asset)
            {
                result.Add(asset);
                var carrier = AGReflect.GetNode(node);
                if (carrier != null)
                    foreach (var binding in carrier.Bindings)
                        if (binding?.Slot != null) CollectDirectAssetReferences(binding.Slot, visited, result);
            }
            return;
        }
        if (node is UnityEngine.Object) return;

        if (type.IsPrimitive || type.IsEnum || node is string) return;
        string ns = type.Namespace;
        if (ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine."))) return;

        if (node is IList rootList)
        {
            foreach (var item in rootList) CollectDirectAssetReferences(item, visited, result);
            return;
        }

        foreach (var field in AGReflect.Fields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            var value = field.GetValue(node);
            if (value == null) continue;
            if (value is IList list)
            {
                foreach (var item in list) CollectDirectAssetReferences(item, visited, result);
                continue;
            }
            CollectDirectAssetReferences(value, visited, result);
        }
    }

    private static object AssetContent(UnityEngine.Object asset) => AGReflect.AssetRoot(asset)?.BodyObject;

    /// <summary>
    /// Owner 宣告「我會從圖外用字串 key 求值」的那些變數名。編輯器不認得任何專案型別，
    /// 所以走 Core 的 `IExternalTokenKeys` 介面問，不是去讀 AffixDefinition 之類的欄位。
    /// </summary>
    private static HashSet<string> ExternalTokenKeys(UnityEngine.Object owner)
    {
        var keys = new HashSet<string>();
        if (owner is not IExternalTokenKeys declaring) return keys;

        var declared = declaring.ExternalTokenKeys;
        if (declared == null) return keys;
        foreach (var key in declared)
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
        return keys;
    }

    private static void Err(AGReport r, AGFocus focus, string where, string message, string fix, object slot, object node)
        => r.Issues.Add(new AGIssue { IsError = true, Focus = focus, Where = where, Message = message, Fix = fix, Slot = slot, Node = node });

    /// <summary>
    /// 停用路徑上的殘缺降成警告：那段 runtime 直接回保底值、不求值，擋存檔只會妨礙測試。
    /// 共用載體若同時被啟用路徑指著，那條路徑會另外走一遍並報成錯誤，所以不必在這裡取聯集。
    /// </summary>
    private static void Issue(AGReport r, bool disabled, AGFocus focus, string where, string message, string fix, object slot, object node)
    {
        if (disabled) Warn(r, focus, where, message, fix, slot, node);
        else Err(r, focus, where, message, fix, slot, node);
    }

    private static void Warn(AGReport r, AGFocus focus, string where, string message, string fix, object slot, object node)
        => r.Issues.Add(new AGIssue { IsError = false, Focus = focus, Where = where, Message = message, Fix = fix, Slot = slot, Node = node });
}

}
