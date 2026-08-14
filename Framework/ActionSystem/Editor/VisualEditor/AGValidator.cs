namespace PinPlugin.ActionSystem.Editor
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
        var report = new AGReport();
        if (model?.Data == null) return report;

        var tokens = model.ReadTokens();
        var usedTokens = new HashSet<string>();
        var checkedAssets = new HashSet<UnityEngine.Object>();

        // 1. Token 本身：空 Key、重複 Key、循環
        var seen = new HashSet<string>();
        foreach (var t in tokens)
        {
            var focus = new AGFocus { Kind = AGFocusKind.Token, Token = t };
            if (string.IsNullOrWhiteSpace(t.Key))
            {
                Warn(report, focus, "變數清單", "Token 名稱為空", "補上名稱，否則沒有任何欄位引用得到它。", t.Slot, null);
                continue;
            }
            if (!seen.Add(TokenId(t)))
                Err(report, focus, $"變數 {t.Key}", "名稱重複", "改成唯一名稱，重複的 Key 會互相覆蓋。", t.Slot, null);

            var path = new List<string>();
            if (DetectCycle(t, tokens, new HashSet<string>(), path))
                Err(report, focus, $"變數 {t.Key}", $"循環引用：{string.Join(" → ", path)}", "把其中一段改成常數或另一個變數，切斷環路。", t.Slot, null);
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
                if (AGReflect.UseType(slot) == 0)
                    Err(report, focus, $"{g.Timing} 第 {i + 1} 個動作", "尚未指定 Action 類型",
                        "在空 Action Node 的下拉選單選擇一個 Action。", slot, null);
                WalkTree(report, model, focus, slot, tokens, usedTokens, $"{g.Timing} 第 {i + 1} 個動作");
                ValidateAssetCycles(report, focus, slot, null, $"{g.Timing} 第 {i + 1} 個動作", checkedAssets);
            }
        }

        foreach (var t in tokens)
        {
            if (t.Slot == null) continue;
            var focus = new AGFocus { Kind = AGFocusKind.Token, Token = t };
            WalkTree(report, model, focus, t.Slot, tokens, usedTokens, $"變數 {t.Key}");
            ValidateAssetCycles(report, focus, t.Slot, null, $"變數 {t.Key}", checkedAssets);
        }

        // 3. 未使用的 Token
        foreach (var t in tokens)
        {
            if (string.IsNullOrWhiteSpace(t.Key)) continue;
            if (usedTokens.Contains(TokenId(t))) continue;
            var focus = new AGFocus { Kind = AGFocusKind.Token, Token = t };
            Warn(report, focus, $"變數 {t.Key}", "宣告後沒有任何欄位引用", "確認是否還需要，或把它接到某個參數欄位。", t.Slot, null);
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

    /// <summary>只驗一棵子樹（資產焦點用）。變數以 Owner 的變數清單為準——資產目前就是以名稱對應呼叫端變數。</summary>
    public static AGReport RunSubtree(AGModel model, AGFocus focus, object rootSlot, string where)
    {
        var report = new AGReport();
        if (model?.Data == null || rootSlot == null) return report;

        var tokens = model.ReadTokens();
        var used = new HashSet<string>();
        WalkTree(report, model, focus, rootSlot, tokens, used, where);
        ValidateAssetCycles(report, focus, rootSlot, focus?.AssetObject, where, new HashSet<UnityEngine.Object>());
        return report;
    }

    // ===== 節點樹走訪 =====

    private static void WalkTree(AGReport report, AGModel model, AGFocus focus, object slot,
        List<AGToken> tokens, HashSet<string> usedTokens, string where)
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        WalkSlot(report, model, focus, slot, tokens, usedTokens, where, visited);
    }

    private static void WalkSlot(AGReport report, AGModel model, AGFocus focus, object slot,
        List<AGToken> tokens, HashSet<string> usedTokens, string where, HashSet<object> visited)
    {
        if (slot == null || !visited.Add(slot)) return;

        bool isAction = AGReflect.IsActionSlotType(slot.GetType());
        int useType = AGReflect.UseType(slot);

        if (useType == 1)
        {
            var formula = AGReflect.GetFormula(slot);
            if (formula == null)
                Err(report, focus, where, "欄位設為公式，但內容是空的", "選一個公式，或把模式改回常數。", slot, null);
            else
                WalkNode(report, model, focus, formula, tokens, usedTokens, where, visited);
        }
        else if (useType == 2)
        {
            if (AGReflect.GetAsset(slot) == null)
                Err(report, focus, where, "欄位設為資產，但沒有指定資產", "指定一個資產，或把模式改回常數。", slot, null);
        }
        else if (!isAction && useType == 3)
        {
            string key = AGReflect.GetTokenKey(slot);
            if (string.IsNullOrEmpty(key))
            {
                Err(report, focus, where, "欄位設為變數，但沒有指定變數", "從左欄拖一個變數進來，或把模式改回常數。", slot, null);
            }
            else
            {
                var rt = AGReflect.ResultType(slot.GetType());
                bool found = false;
                foreach (var t in tokens)
                    if (t.Key == key && t.ResultType == rt) { found = true; break; }
                if (found) usedTokens.Add(TokenId(rt, key));
                else Err(report, focus, where, $"引用了不存在的變數 '{key}'", "新增同名同型別的變數，或改指向現有變數。", slot, null);
            }
        }
    }

    private static void WalkNode(AGReport report, AGModel model, AGFocus focus, object node,
        List<AGToken> tokens, HashSet<string> usedTokens, string where, HashSet<object> visited)
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
                WalkSlot(report, model, focus, val, tokens, usedTokens, $"{nodeWhere}.{AGReflect.FieldLabel(f)}", visited);
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
                        WalkSlot(report, model, focus, item, tokens, usedTokens, itemWhere, visited);
                    else if (!item.GetType().IsPrimitive && item is not string && item is not UnityEngine.Object)
                        WalkNode(report, model, focus, item, tokens, usedTokens, itemWhere, visited);
                }
                continue;
            }

            WalkNode(report, model, focus, val, tokens, usedTokens, nodeWhere, visited);
        }
    }

    // ===== 循環偵測 =====

    private static bool DetectCycle(AGToken start, List<AGToken> tokens, HashSet<string> stack, List<string> path)
    {
        path.Add(start.Key);
        if (!stack.Add(TokenId(start))) return true;

        bool cycle = false;
        foreach (var slot in SlotsOf(start.Slot))
        {
            if (AGReflect.UseType(slot) != 3) continue;
            string key = AGReflect.GetTokenKey(slot);
            if (string.IsNullOrEmpty(key)) continue;
            var rt = AGReflect.ResultType(slot.GetType());

            foreach (var t in tokens)
            {
                if (t.Key != key || t.ResultType != rt) continue;
                if (DetectCycle(t, tokens, stack, path)) { cycle = true; break; }
            }
            if (cycle) break;
        }

        stack.Remove(TokenId(start));
        if (!cycle) path.RemoveAt(path.Count - 1);
        return cycle;
    }

    private static IEnumerable<object> SlotsOf(object slot)
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var s in AGModel.WalkSlots(slot, visited)) yield return s;
    }

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
                result.Add(asset);
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

    private static object AssetContent(UnityEngine.Object asset)
        => AGReflect.Get(asset, "_action") ?? AGReflect.Get(asset, "_target");

    private static string TokenId(AGToken t) => TokenId(t.ResultType, t.Key);

    private static string TokenId(Type resultType, string key)
        => (resultType?.AssemblyQualifiedName ?? "?") + "|" + key;

    private static void Err(AGReport r, AGFocus focus, string where, string message, string fix, object slot, object node)
        => r.Issues.Add(new AGIssue { IsError = true, Focus = focus, Where = where, Message = message, Fix = fix, Slot = slot, Node = node });

    private static void Warn(AGReport r, AGFocus focus, string where, string message, string fix, object slot, object node)
        => r.Issues.Add(new AGIssue { IsError = false, Focus = focus, Where = where, Message = message, Fix = fix, Slot = slot, Node = node });
}

}
