namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>一則驗證訊息：在哪裡、是什麼問題、怎麼處理。</summary>
public class HGIssue
{
    public GraphDiagnostic Diagnostic { get; }
    public string Code => Diagnostic.Code;
    public bool IsError => Diagnostic.Severity == GraphDiagnosticSeverity.Error;
    public string Message => Diagnostic.Message;
    public string Where;
    public string Fix => Diagnostic.Fix;
    public GraphDiagnosticLocation Location => Diagnostic.Location;

    public HGFocus Focus;         // 點擊要跳到的焦點
    public GraphSlotBase Slot;    // 出問題的參數欄位（可空）
    public object Node;           // 出問題的節點（可空）：GraphNode 或 GraphToken

    public HGIssue(GraphDiagnostic diagnostic, string where, HGFocus focus, GraphSlotBase slot, object node)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        Where = where;
        Focus = focus;
        Slot = slot;
        Node = node;
    }

    public string Line => $"{Where}：{Message}　→ {Fix}";
}

/// <summary>驗證結果彙總。</summary>
public class HGReport
{
    public List<HGIssue> Issues = new();
    public DateTime Time = DateTime.Now;

    public int ErrorCount
    {
        get { int n = 0; foreach (var i in Issues) if (i.IsError) n++; return n; }
    }
    public int WarningCount => Issues.Count - ErrorCount;
    public bool CanSave => ErrorCount == 0;

    /// <summary>某個焦點下的問題數（右欄動作清單與時機下拉要顯示）。</summary>
    public void CountFor(HGFocus focus, out int errors, out int warnings)
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

    /// <summary>Replaces rebuild-scoped view diagnostics without duplicating them across repaints.</summary>
    public void ReplaceGraphViewDiagnostics(IEnumerable<GraphDiagnostic> diagnostics)
    {
        Issues.RemoveAll(issue => issue.Code.StartsWith("graphkit.metadata.", StringComparison.Ordinal)
            || issue.Code.StartsWith("graphkit.port-resolution.", StringComparison.Ordinal)
            || issue.Code.StartsWith("graphkit.port.", StringComparison.Ordinal)
            || issue.Code == "graphkit.build.failed" || issue.Code == "graphkit.provider.ports-failed");
        if (diagnostics == null) return;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic == null) continue;
            string where = string.IsNullOrEmpty(diagnostic.Location.FieldPath) ? "Metadata" : diagnostic.Location.FieldPath;
            Issues.Add(new HGIssue(diagnostic, where, null, null, null));
        }
    }

    /// <summary>Replaces Tool-owned diagnostics after a structural validation pass without coupling Core to Tool rules.</summary>
    public void ReplaceExtensionDiagnostics(IEnumerable<GraphDiagnostic> diagnostics)
    {
        Issues.RemoveAll(issue => !issue.Code.StartsWith("graphkit.", StringComparison.Ordinal));
        if (diagnostics == null) return;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic == null) continue;
            string where = string.IsNullOrEmpty(diagnostic.Location.FieldPath) ? "Domain" : diagnostic.Location.FieldPath;
            Issues.Add(new HGIssue(diagnostic, where, null, null, null));
        }
    }
}

/// <summary>
/// 依需求書 §10 產生結構化驗證訊息。跟 Core 的 Verify 規則同源，但額外帶「跳轉位置」，Console 才能一鍵定位。
/// </summary>
public static class HGValidator
{
    /// <summary>
    /// 圖的內容規則只有一套，編輯時與存檔時跑的是同一份（含 Token 循環與 Asset 參照循環）。
    /// includeMissingTypes 另外處理：它檢查的是 Owner 資產本身，編輯工作副本不會改變它，只在綁定與存檔時才有意義。
    /// </summary>
    public static HGReport Run(HGModel model, bool includeMissingTypes = false)
    {
        if (probeDepth == 0) assetHealth.Clear();
        var report = new HGReport();
        if (model?.Data == null) return report;

        var tokens = HGModel.ReadTokens(model.OwnerTokens);
        var checkedAssets = new HashSet<UnityEngine.Object>();

        // 1. Token本身：名稱空白、名稱重複（同族內唯一）
        //    「宣告後沒有欄位引用」不是問題：Token的用途就是被圖外面用，沒有連入線是正常狀態。
        var seen = new HashSet<(Type, string)>();
        foreach (var t in tokens)
        {
            var focus = TokenFocus(t);
            if (!seen.Add((t.FamilyType, t.Key)))
                Err(report, "graphkit.token.duplicate", focus, $"Token {t.Key}", "名稱重複",
                    "改成同族內唯一的名稱；撞號時外部只查得到其中一個。", null, t.Token);

            ValidateToken(report, model, focus, t);
        }

        // 2. 每個動作、每個 Token 的節點樹
        foreach (var g in model.ReadRootGroups())
        {
            if (g.Items == null) continue;
            for (int i = 0; i < g.Items.Count; i++)
            {
                var slot = g.Items[i] as GraphSlotBase;
                if (slot == null) continue;
                var focus = new HGFocus
                {
                    Kind = HGFocusKind.Action,
                    RootKey = g.RootKey,
                    ActionList = g.Items,
                    ActionIndex = i,
                    ActionSlot = slot,
                };
                bool disabled = HGReflect.GetDisabled(slot) || (HGReflect.GetNode(slot)?.Disabled ?? false);
                if (slot.Node == null)
                    Issue(report, "graphkit.root.action-missing", disabled, focus, $"{g.RootKey} 第 {i + 1} 個動作", "尚未指定 Action 類型",
                        "在空 Action Node 的下拉選單選擇一個 Action。", slot, null);
                WalkTree(report, model, focus, slot, $"{g.RootKey} 第 {i + 1} 個動作", disabled);
                ValidateAssetCycles(report, focus, slot, null, $"{g.RootKey} 第 {i + 1} 個動作", checkedAssets);
            }
        }

        // 從Token的取值欄位開始整棵子樹都是正式資料，要跟動作樹一樣驗。
        foreach (var t in tokens)
        {
            WalkTokenCarrier(report, model, TokenFocus(t), t, checkedAssets, null);
        }

        // 2.1 Owner 指名了不存在的標註。runtime 只是 Has 回 false 然後靜默跳過，
        //     所以打錯一個字的結果是「功能整個不會發生」，什麼訊息都沒有。
        foreach (var key in ExternalTokenKeys(model.Owner))
        {
            bool declared = false;
            foreach (var t in tokens)
                if (t.Key == key) { declared = true; break; }
            if (declared) continue;
            Err(report, "graphkit.external-token.missing", null, model.Owner != null ? model.Owner.name : "編輯對象",
                $"Inspector 指名了不存在的 Token '{key}'",
                "在左欄建一個同名 Token，或修正 Inspector 上的名稱；查不到的 key 會被靜默跳過。", null, null);
        }

        // 4. SerializeReference 型別遺失（類別被改名或刪掉）
        //    反射看不到殘骸，只有 Unity 的 managed reference API 知道。存檔會把殘骸永久抹掉，所以必須擋。
        //    對象是 Owner 本體：編輯工作副本不會改變它，所以只在綁定與存檔時檢查。
        if (includeMissingTypes && model.Owner != null
            && UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(model.Owner))
        {
            foreach (var missing in UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(model.Owner))
            {
                Err(report, "graphkit.serialize-reference.missing-type", null, "資產本體",
                    $"有節點的程式類別已不存在：{missing.namespaceName}.{missing.className}（{missing.assemblyName}）",
                    "把類別改回原名，或確認要放棄這段內容後手動清除；直接存檔會永久刪掉它。", null, null);
            }
        }

        return report;
    }

    /// <summary>只驗一棵子樹（資產焦點用）。</summary>
    public static HGReport RunSubtree(HGModel model, HGFocus focus, GraphSlotBase rootSlot, string where)
    {
        if (probeDepth == 0) assetHealth.Clear();
        var report = new HGReport();
        if (model?.Data == null || rootSlot == null) return report;

        WalkTree(report, model, focus, rootSlot, where, false);
        ValidateAssetCycles(report, focus, rootSlot, focus?.AssetObject, where, new HashSet<UnityEngine.Object>());

        var rootCarrier = HGReflect.GetNode(rootSlot);
        if (rootCarrier?.Kind == NodeKind.Token)
            Err(report, "graphkit.asset.token-root", focus, where, "資產內容不能只是一個 Token 引用",
                "資產的內容要是公式或動作；要對外開參數請用左欄的 Token 清單。", rootSlot, rootCarrier);

        var tokens = HGModel.ReadTokens(focus?.AssetTokens);
        var seen = new HashSet<(Type, string)>();
        foreach (var token in tokens)
        {
            var tokenFocus = AssetTokenFocus(focus, token);
            if (!seen.Add((token.FamilyType, token.Key)))
                Err(report, "graphkit.token.duplicate", tokenFocus, $"Token {token.Key}", "名稱重複",
                    "改成這個資產內同族唯一的名稱。", null, token.Token);
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
    public static bool AssetHasError(HGModel model, Type hostSlotType, UnityEngine.Object asset)
    {
        if (model == null || asset == null || hostSlotType == null) return false;
        if (assetHealth.TryGetValue(asset, out bool cached)) return cached;
        // 先佔位：巢狀引用繞回自己時當成沒問題，循環本身由 ValidateAssetCycles 專門報。
        assetHealth[asset] = false;

        var root = HGReflect.AssetRoot(asset);
        if (root == null) return false;   // 空資產：資產畫布也不報，這裡跟著不報
        var host = HGReflect.CreateInstance(hostSlotType) as GraphSlotBase;
        if (host == null) return false;
        HGReflect.SetNode(host, root);

        var probeFocus = new HGFocus
        {
            Kind = HGFocusKind.Asset,
            AssetObject = asset,
            AssetHostSlot = host,
            AssetOrphans = HGReflect.Orphans(asset),
            AssetTokens = HGReflect.Tokens(asset),
        };

        probeDepth++;
        HGReport probe;
        try { probe = RunSubtree(model, probeFocus, host, asset.name); }
        finally { probeDepth--; }

        bool hasError = probe.ErrorCount > 0;
        assetHealth[asset] = hasError;
        return hasError;
    }

    /// <summary>問題要跳回那個Token自己的畫布。</summary>
    private static HGFocus TokenFocus(HGToken token)
        => new HGFocus { Kind = HGFocusKind.Token, Token = token?.Token };

    private static HGFocus AssetTokenFocus(HGFocus assetFocus, HGToken token)
        => new HGFocus
        {
            Kind = HGFocusKind.Asset,
            AssetObject = assetFocus?.AssetObject,
            AssetHostSlot = assetFocus?.AssetHostSlot,
            AssetOrphans = assetFocus?.AssetOrphans,
            AssetTokens = assetFocus?.AssetTokens,
            Token = token?.Token,
        };

    private static void ValidateToken(HGReport report, HGModel model, HGFocus focus, HGToken token)
    {
        if (token?.Token == null) return;
        if (token.Token.Slot == null)
        {
            Err(report, "graphkit.token.slot-missing", focus, $"Token {token.Key ?? "（未命名）"}", "沒有取值欄位",
                "刪掉這個 Token 重建；結果型別是建立時決定的。", null, token.Token);
            return;
        }
        if (string.IsNullOrEmpty(token.Key))
            Err(report, "graphkit.token.name-missing", focus, $"{HGReflect.ResultTypeName(token.ResultType)} Token", "沒有名稱",
                "取一個名字；外部是用名字查它的值。", null, token.Token);
    }

    private static void WalkTokenCarrier(HGReport report, HGModel model, HGFocus focus, HGToken token,
        HashSet<UnityEngine.Object> checkedAssets, UnityEngine.Object rootAsset)
    {
        if (token?.Token?.Slot == null) return;
        string where = $"Token {token.Key}";
        WalkTree(report, model, focus, token.Token.Slot, where, false);
        ValidateAssetCycles(report, focus, token.Token.Slot, rootAsset, where, checkedAssets);
    }

    // ===== 節點樹走訪 =====

    private static void WalkTree(HGReport report, HGModel model, HGFocus focus, GraphSlotBase slot,
        string where, bool disabled)
    {
        var visited = new HashSet<object>(HGRefComparer.Instance);
        WalkSlot(report, model, focus, slot, where, visited, disabled);
    }

    private static void WalkSlot(HGReport report, HGModel model, HGFocus focus, GraphSlotBase slot,
        string where, HashSet<object> visited, bool disabled)
    {
        if (slot == null || !visited.Add(slot)) return;

        var carrier = slot.Node;
        var contentKind = carrier?.Kind;

        // 停用往下傳染：載體停用後整棵子樹都不求值，殘缺一律降成警告。
        disabled = disabled || (HGReflect.GetNode(slot)?.Disabled ?? false);

        if (contentKind is NodeKind.Inline or NodeKind.Empty)
        {
            var formula = HGReflect.GetFormula(slot);
            if (formula == null)
                Issue(report, "graphkit.slot.formula-missing", disabled, focus, where, "欄位設為公式，但內容是空的", "選一個公式，或把模式改回常數。", slot, null);
            else
                WalkNode(report, model, focus, formula, where, visited, disabled);
        }
        else if (contentKind == NodeKind.Asset)
        {
            var asset = HGReflect.GetAsset(slot);
            if (asset == null)
                Issue(report, "graphkit.slot.asset-missing", disabled, focus, where, "欄位設為資產，但沒有指定資產", "指定一個資產，或把模式改回常數。", slot, null);
            ValidateAssetBindings(report, focus, carrier, where);

            // 資產內部殘缺在這張畫布上修不了，所以只報一條入口級錯誤讓人跳進去；不報的話會變成
            // 「視覺驗證全綠、存檔被 Core 擋住且沒有訊息」。細項在資產畫布自己的驗證裡。
            if (AssetHasError(model, slot.GetType(), asset))
                Issue(report, "graphkit.asset.invalid", disabled, focus, where, $"資產 '{asset.name}' 內部有錯誤",
                    "雙擊這顆節點進入資產畫布，依那裡的驗證訊息修正。", slot, carrier);
            if (carrier != null)
            {
                foreach (var binding in carrier.Bindings)
                    if (binding?.Slot != null)
                        WalkSlot(report, model, focus, binding.Slot, $"{where}.{binding.Name}", visited,
                            disabled || !binding.OverrideEnabled);
            }
        }
        else if (contentKind == NodeKind.Token)
        {
            // 端點被刪掉時參照會變 null，這裡看得到；不會像字串 key 一樣留著一個查不到的名字。
            var endpoint = HGReflect.GetToken(slot);
            if (endpoint == null)
                Issue(report, "graphkit.slot.token-missing", disabled, focus, where, "欄位設為 Token，但沒有指定 Token",
                    "選一個 Token，或把模式改回常數。", slot, HGReflect.GetNode(slot));
            else if (!HGReflect.AcceptsToken(slot, endpoint))
                Err(report, "graphkit.token.type-incompatible", focus, where, $"接的 Token '{endpoint.Name}' 型別不相容",
                    "改接同結果型別的 Token。", slot, HGReflect.GetNode(slot));
            else if (!InScope(model, focus, endpoint))
                Err(report, "graphkit.token.out-of-scope", focus, where, $"接的 Token '{endpoint.Name}' 不屬於這張圖",
                    "改接本圖 Token 清單裡的 Token；求值是用名字在本圖的 Token 表查的，跨圖引用永遠查不到，會靜默取預設值。",
                    slot, HGReflect.GetNode(slot));
        }
    }

    /// <summary>
    /// 這個端點在不在當前這張圖的Token清單裡。資產焦點看資產自己的清單，其餘看 Owner 的。
    /// </summary>
    // 求值時 Token 節點是拿「名字」去當前作用域的 TokenTable 查（資產作用域只登記資產自己的參數），
    // 所以引用到別張圖的端點物件不會報錯、也不會求出值，只會回預設值——這是唯一擋得住的地方。
    private static bool InScope(HGModel model, HGFocus focus, GraphToken endpoint)
    {
        var scope = focus != null && focus.Kind == HGFocusKind.Asset ? focus.AssetTokens : model?.OwnerTokens;
        if (scope == null) return true;   // 讀不到清單就不判，寧可不報也不要誤報
        foreach (var other in scope)
            if (ReferenceEquals(other, endpoint)) return true;
        return false;
    }

    private static void ValidateAssetBindings(HGReport report, HGFocus focus, GraphNode carrier, string where)
    {
        if (carrier?.AssetObject == null) return;
        var parameters = AssetGraphSchema.Read(carrier.AssetObject, out var duplicates);
        foreach (var duplicate in duplicates)
            Err(report, "graphkit.asset-binding.parameter-duplicate", focus, where, $"資產參數標註名稱重複：'{duplicate}'", "進入資產並改成唯一名稱。", null, carrier);

        // 綁定與參數的配對鍵是（族, 名稱），和 TokenTable 的覆蓋表一致：同名不同族的參數是兩個參數。
        var byKey = new HashSet<(Type, string)>();
        var parameterNames = new HashSet<string>();
        foreach (var parameter in parameters)
        {
            byKey.Add((parameter.Slot.FamilyType, parameter.Name));
            parameterNames.Add(parameter.Name);
        }
        var seen = new HashSet<(Type, string)>();
        foreach (var binding in carrier.Bindings)
        {
            if (binding == null) { Err(report, "graphkit.asset-binding.null", focus, where, "有空的資產參數綁定", "移除空綁定。", null, carrier); continue; }
            if (binding.Slot == null)
            {
                Err(report, "graphkit.asset-binding.slot-missing", focus, where, $"資產參數 '{binding.Name}' 沒有取值欄位", "重新建立這筆綁定。", null, carrier);
                continue;
            }
            var key = (binding.Slot.FamilyType, binding.Name);
            if (!seen.Add(key))
                Err(report, "graphkit.asset-binding.duplicate", focus, where, $"資產參數綁定重複：'{binding.Name}'", "移除重複綁定。", binding.Slot, carrier);
            if (byKey.Contains(key)) continue;
            if (parameterNames.Contains(binding.Name))
                Err(report, "graphkit.asset-binding.type-incompatible", focus, where, $"資產參數 '{binding.Name}' 型別不相容", "重新建立這筆綁定。", binding.Slot, carrier);
            else
                Err(report, "graphkit.asset-binding.orphaned", focus, where, $"資產已沒有參數 '{binding.Name}'", "切換資產或移除這筆舊綁定。", binding.Slot, carrier);
        }
    }

    private static void WalkNode(HGReport report, HGModel model, HGFocus focus, object node,
        string where, HashSet<object> visited, bool disabled)
    {
        if (node == null || !visited.Add(node)) return;
        string nodeWhere = $"{where} → {HGReflect.TypeName(node.GetType())}";

        foreach (var f in HGReflect.Fields(node.GetType()))
        {
            if (f.IsNotSerialized || f.IsStatic) continue;
            var val = f.GetValue(node);
            if (val == null) continue;

            var t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || val is string || val is UnityEngine.Object) continue;

            if (val is GraphSlotBase fieldSlot)
            {
                WalkSlot(report, model, focus, fieldSlot, $"{nodeWhere}.{HGReflect.FieldLabel(f)}", visited, disabled);
                continue;
            }

            if (val is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    string itemWhere = $"{nodeWhere}.{HGReflect.FieldLabel(f)}[{i + 1}]";
                    if (item == null)
                    {
                        Warn(report, "graphkit.list.null-item", focus, itemWhere, "清單有空項目", "填入內容或移除這一列。", null, node);
                        continue;
                    }
                    if (item is GraphSlotBase itemSlot)
                        WalkSlot(report, model, focus, itemSlot, itemWhere, visited, disabled);
                    else if (!item.GetType().IsPrimitive && item is not string && item is not UnityEngine.Object)
                        WalkNode(report, model, focus, item, itemWhere, visited, disabled);
                }
                continue;
            }

            WalkNode(report, model, focus, val, nodeWhere, visited, disabled);
        }
    }

    // Token 循環偵測已隨標註化移除：圖內引用一律是連線，環在拉線當下就被 Port policy 擋掉。

    // ===== Asset 參照循環 =====

    private static void ValidateAssetCycles(HGReport report, HGFocus focus, GraphSlotBase root,
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

        Err(report, "graphkit.asset.cycle", focus, where, $"Asset 循環引用：{cycle}",
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
        CollectDirectAssetReferences(root, new HashSet<object>(HGRefComparer.Instance), result);
        return result;
    }

    private static void CollectDirectAssetReferences(object node, HashSet<object> visited,
        List<UnityEngine.Object> result)
    {
        if (node == null || !visited.Add(node)) return;
        Type type = node.GetType();
        if (node is GraphSlotBase slot)
        {
            var contentKind = slot.Node?.Kind;
            if (contentKind is NodeKind.Inline or NodeKind.Empty)
                CollectDirectAssetReferences(HGReflect.GetFormula(slot), visited, result);
            else if (contentKind == NodeKind.Asset && HGReflect.GetAsset(slot) is UnityEngine.Object asset)
            {
                result.Add(asset);
                var carrier = slot.Node;
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

        foreach (var field in HGReflect.Fields(type))
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

    private static object AssetContent(UnityEngine.Object asset) => HGReflect.AssetRoot(asset)?.BodyObject;

    /// <summary>
    /// Owner 宣告「我會從圖外用字串 key 求值」的那些Token名。編輯器不認得任何專案型別，
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

    private static void Err(HGReport r, string code, HGFocus focus, string where, string message, string fix, GraphSlotBase slot, object node)
        => AddIssue(r, code, GraphDiagnosticSeverity.Error, focus, where, message, fix, slot, node);

    /// <summary>
    /// 停用路徑上的殘缺降成警告：那段 runtime 直接回保底值、不求值，擋存檔只會妨礙測試。
    /// 共用載體若同時被啟用路徑指著，那條路徑會另外走一遍並報成錯誤，所以不必在這裡取聯集。
    /// </summary>
    private static void Issue(HGReport r, string code, bool disabled, HGFocus focus, string where, string message, string fix, GraphSlotBase slot, object node)
    {
        if (disabled) Warn(r, code, focus, where, message, fix, slot, node);
        else Err(r, code, focus, where, message, fix, slot, node);
    }

    private static void Warn(HGReport r, string code, HGFocus focus, string where, string message, string fix, GraphSlotBase slot, object node)
        => AddIssue(r, code, GraphDiagnosticSeverity.Warning, focus, where, message, fix, slot, node);

    private static void AddIssue(HGReport report, string code, GraphDiagnosticSeverity severity, HGFocus focus, string where,
        string message, string fix, GraphSlotBase slot, object node)
    {
        string nodeId = node is GraphNode graphNode ? graphNode.Id : null;
        string tokenId = node is GraphToken graphToken ? graphToken.Id : null;
        var location = new GraphDiagnosticLocation(focusId: focus?.Id, nodeId: nodeId, tokenId: tokenId);
        report.Issues.Add(new HGIssue(new GraphDiagnostic(code, severity, message, location, fix), where, focus, slot, node));
    }
}

}
