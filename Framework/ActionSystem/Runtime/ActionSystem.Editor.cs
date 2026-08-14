namespace PinPlugin.ActionSystem
{
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class ActionSystem<TTiming, TPack, TTokenEntryPack>
where TTiming : Enum
where TTokenEntryPack : TokenEntryPack<TPack>, new()
{
    [NonSerialized] private List<string> _errors = new();
    [NonSerialized] private List<string> _warnings = new();

    private void Err(string msg) => _errors.Add(msg);
    private void Warn(string msg) => _warnings.Add(msg);

    /// <summary>開啟視覺化編輯器並聚焦到目前選取的 Owner。</summary>
    // Owner 從 Selection 取：這個按鈕本來就只在 Inspector 上按得到。
    private void OpenVisualEditor()
    {
        if (ActionSystemEditorHooks.OpenGraphWindow == null)
        {
            Debug.LogError("[ActionSystem] 視覺化編輯器尚未載入（Editor assembly 未編譯完成？）。");
            return;
        }

        var owner = Selection.activeObject as ScriptableObject;
        if (owner == null)
        {
            Debug.LogError("[ActionSystem] 請從 Owner 資產的 Inspector 按此按鈕。");
            return;
        }

        ActionSystemEditorHooks.OpenGraphWindow(owner);
    }

    public void Verify()
    {
        // DeepCopy 與 Unity 反序列化不會保留 NonSerialized 驗證緩衝。
        _errors ??= new List<string>();
        _warnings ??= new List<string>();
        TokenEntry.AssignTokenKeys();

        _errors.Clear();
        _warnings.Clear();

        // 0. 空 Key / 1. 重複 Key / 2~4. 循環 + missing-key — 6 kind 由 concrete pack dispatch（型別配對它才知道）
        TokenEntry.ForEachKind(new VerifyKindVisitor(this));
        ReportDuplicateTimings();
        ReportEmptyRootActions();

        // 5. 節點內容：空節點、內容為 null、型別與欄位不相容
        ValidateTokenSlotSources();
        ValidateActionSlotSources();
        ReportAssetCycles();

        bool ok = _errors.Count == 0;
        _validated = ok;
        _hasLoggedValidationFailure = false;

        EmitSummary(ok);
    }

    private const string COLOR_OK      = "#5BE584";
    private const string COLOR_FAIL    = "#FF6B6B";
    private const string COLOR_WARN    = "#FFC857";
    private const string COLOR_NAME    = "#7FD0FF";
    private const string COLOR_DIVIDER = "#888888";
    private const string COLOR_TAG     = "#B084EB";

    private void EmitSummary(bool ok)
    {
        string name = Selection.activeObject != null ? Selection.activeObject.name : "?";
        string verdict = ok
            ? $"<b><color={COLOR_OK}>驗證成功</color></b>"
            : $"<b><color={COLOR_FAIL}>驗證失敗</color></b>";
        string errCount  = $"<color={COLOR_FAIL}>{_errors.Count}</color>";
        string warnCount = $"<color={COLOR_WARN}>{_warnings.Count}</color>";
        string divider   = $"<color={COLOR_DIVIDER}>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>";

        var sb = new StringBuilder();
        sb.AppendLine(divider);
        sb.AppendLine($"<color={COLOR_TAG}>[Verify]</color><color={COLOR_NAME}>[{name}]</color> {verdict} — 錯誤 {errCount} / 警告 {warnCount}");
        if (_errors.Count > 0)
        {
            sb.AppendLine($"<b><color={COLOR_FAIL}>【錯誤】</color></b>");
            for (int i = 0; i < _errors.Count; i++)
                sb.AppendLine($"  <color={COLOR_FAIL}>✗ [{i + 1}]</color> {_errors[i]}");
        }
        if (_warnings.Count > 0)
        {
            sb.AppendLine($"<b><color={COLOR_WARN}>【警告】</color></b>");
            for (int i = 0; i < _warnings.Count; i++)
                sb.AppendLine($"  <color={COLOR_WARN}>⚠ [{i + 1}]</color> {_warnings[i]}");
        }
        sb.Append(divider);
        string body = sb.ToString();
        if (ok) Debug.Log(body);
        else    Debug.LogError(body);
    }

    private void WarnEmptyTokenKeys(string typeName, List<string> keys)
    {
        if (keys == null) return;
        for (int i = 0; i < keys.Count; i++)
            if (string.IsNullOrEmpty(keys[i]))
                Warn($"{typeName}Tokens 第 {i} 筆 Key 為空（不會被任何引用解析到，建議移除）");
    }

    private void ReportDuplicateKeys(string typeName, List<string> keys)
    {
        if (keys == null) return;
        var seen = new HashSet<string>();
        var dup = new HashSet<string>();
        foreach (var k in keys)
        {
            if (string.IsNullOrEmpty(k)) continue;
            if (!seen.Add(k)) dup.Add(k);
        }
        foreach (var k in dup)
            Err($"{typeName}Tokens 重複 Key：'{k}'");
    }

    private void ReportDuplicateTimings()
    {
        if (ActionGroups == null) return;
        var seen = new HashSet<TTiming>();
        var dup = new HashSet<TTiming>();
        foreach (var g in ActionGroups)
        {
            if (g == null) continue;
            if (!seen.Add(g.Timing)) dup.Add(g.Timing);
        }
        foreach (var t in dup)
            Err($"ActionGroups 重複 Timing：'{t}'");
    }

    private void ReportEmptyRootActions()
    {
        if (ActionGroups == null) return;
        foreach (var group in ActionGroups)
        {
            if (group?.Actions == null) continue;
            for (int i = 0; i < group.Actions.Count; i++)
            {
                var slot = group.Actions[i];
                if (slot == null) continue;
                if (slot.Node == null || slot.Node.Kind == NodeKind.Empty)
                    Err($"{group.Timing} 第 {i + 1} 個動作尚未指定 Action 類型");
            }
        }
    }

    private void ReportAssetCycles()
    {
        var completed = new HashSet<UnityEngine.Object>();
        if (ActionGroups != null)
        {
            foreach (var group in ActionGroups)
            {
                if (group?.Actions == null) continue;
                for (int i = 0; i < group.Actions.Count; i++)
                    ValidateAssetCycles(group.Actions[i], $"{group.Timing} 第 {i + 1} 個動作", completed);
            }
        }
        TokenEntry.ForEachKind(new AssetCycleKindVisitor(this, completed));
    }

    private void ValidateAssetCycles(object root, string where, HashSet<UnityEngine.Object> completed)
    {
        if (root == null) return;
        var stack = new HashSet<UnityEngine.Object>();
        var path = new List<UnityEngine.Object>();
        foreach (var asset in DirectAssetReferences(root))
        {
            string cycle = FindAssetCycle(asset, stack, path, completed);
            if (cycle == null) continue;
            Err($"{where} Asset 循環引用：{cycle}");
            return;
        }
    }

    private string FindAssetCycle(UnityEngine.Object asset, HashSet<UnityEngine.Object> stack,
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

    private List<UnityEngine.Object> DirectAssetReferences(object root)
    {
        var result = new List<UnityEngine.Object>();
        CollectDirectAssetReferences(root, new HashSet<object>(), result);
        return result;
    }

    private void CollectDirectAssetReferences(object node, HashSet<object> visited, List<UnityEngine.Object> result)
    {
        if (node == null || !visited.Add(node)) return;

        // 欄位只往目前接的節點下沉；候選池（_orphans）不執行，不算引用。
        if (node is ActionSlot<TPack> actionSlot)
        {
            CollectDirectAssetReferences(actionSlot.Node, visited, result);
            return;
        }
        if (node is FormulaSlotBase formulaSlot)
        {
            CollectDirectAssetReferences(formulaSlot.Node, visited, result);
            return;
        }
        if (node is GraphNode graphNode)
        {
            if (graphNode.Kind == NodeKind.Asset && graphNode.AssetObject != null) result.Add(graphNode.AssetObject);
            else if (graphNode.Kind == NodeKind.Inline) CollectDirectAssetReferences(graphNode.BodyObject, visited, result);
            return;
        }
        if (node is UnityEngine.Object) return;

        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is string) return;
        string ns = type.Namespace;
        if (ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine."))) return;

        if (node is System.Collections.IList list)
        {
            foreach (var item in list) CollectDirectAssetReferences(item, visited, result);
            return;
        }

        foreach (var field in GetAllInstanceFields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            CollectDirectAssetReferences(field.GetValue(node), visited, result);
        }
    }

    private static object AssetContent(UnityEngine.Object asset)
    {
        if (asset is ActionAssetBase<TPack> actionAsset) return actionAsset.EditorGetAction();
        if (asset is FormulaAssetBase formulaAsset) return formulaAsset.EditorGetTargetObject();
        return null;
    }

    // ===== Generic token-kind walker（取代原 6 型 × 4 方法 ≈ 700 行重複） =====

    private void VerifyTokenKind<TResult, TEntry>(
        string typeName, List<TEntry> entries,
        Func<TEntry, string> getKey, Func<TEntry, FormulaSlotBase> getSlot)
        where TEntry : class
    {
        var lookup = MakeLookup<TResult, TEntry>(entries, getKey, getSlot);
        DetectCycles<TResult, TEntry>(typeName, entries, getKey, getSlot, lookup);
        ValidateTokenMissing<TResult, TEntry>(typeName, entries, getSlot, lookup);
        ValidateActionMissing<TResult>(typeName, lookup);
    }

    private static Func<string, IFormulaSlot<TResult, TPack>> MakeLookup<TResult, TEntry>(
        List<TEntry> entries, Func<TEntry, string> getKey, Func<TEntry, FormulaSlotBase> getSlot)
        where TEntry : class
    {
        return key =>
        {
            if (entries == null) return null;
            foreach (var e in entries)
                if (e != null && getKey(e) == key)
                    return getSlot(e) as IFormulaSlot<TResult, TPack>;
            return null;
        };
    }

    private void DetectCycles<TResult, TEntry>(
        string typeName, List<TEntry> entries,
        Func<TEntry, string> getKey, Func<TEntry, FormulaSlotBase> getSlot,
        Func<string, IFormulaSlot<TResult, TPack>> lookup)
        where TEntry : class
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null) continue;
            var key = getKey(e);
            var slot = getSlot(e);
            if (string.IsNullOrEmpty(key) || slot == null) continue;
            if (slot.IsSelfReferencing)
            {
                Err($"{typeName} token '{key}' 直接自我參照（接的變數節點就是自己）");
                continue;
            }
            var path = new List<string> { key };
            if (FindCycle<TResult>(slot, key, new HashSet<string> { key }, path, new HashSet<object>(), lookup))
                Err($"{typeName} token 迴圈：{string.Join(" → ", path)}");
        }
    }

    private bool FindCycle<TResult>(
        object node, string rootKey,
        HashSet<string> stack, List<string> path, HashSet<object> visited,
        Func<string, IFormulaSlot<TResult, TPack>> lookup)
    {
        if (node == null || !visited.Add(node)) return false;

        if (node is IFormulaSlot<TResult, TPack> && node is FormulaSlotBase fsb)
        {
            var key = fsb.DebugTokenKey;
            if (!string.IsNullOrEmpty(key))
            {
                path.Add(key);
                if (key == rootKey || stack.Contains(key)) return true;
                var next = lookup(key);
                if (next != null)
                {
                    stack.Add(key);
                    if (FindCycle<TResult>(next, rootKey, stack, path, visited, lookup)) return true;
                    stack.Remove(key);
                }
                path.RemoveAt(path.Count - 1);
            }
        }

        // 欄位只往目前接的節點下沉；候選池不參與驗證。
        if (node is ActionSlot<TPack> actionSlot)
            return FindCycle<TResult>(actionSlot.Node, rootKey, stack, path, visited, lookup);
        if (node is FormulaSlotBase slotBase)
            return FindCycle<TResult>(slotBase.Node, rootKey, stack, path, visited, lookup);

        foreach (var f in GetAllInstanceFields(node.GetType()))
        {
            var val = f.GetValue(node);
            if (val == null) continue;
            var t = val.GetType();
            if (t.IsPrimitive || val is string || t.IsEnum) continue;

            if (val is FormulaAsset<TResult, TPack> asset)
            {
                var inner = asset.EditorGetTargetObject();
                if (FindCycle<TResult>(inner, rootKey, stack, path, visited, lookup)) return true;
                continue;
            }

            if (val is UnityEngine.Object) continue;

            if (val is System.Collections.IList list)
            {
                foreach (var item in list)
                    if (FindCycle<TResult>(item, rootKey, stack, path, visited, lookup)) return true;
                continue;
            }

            if (FindCycle<TResult>(val, rootKey, stack, path, visited, lookup)) return true;
        }
        return false;
    }

    private void ValidateTokenMissing<TResult, TEntry>(
        string typeName, List<TEntry> entries,
        Func<TEntry, FormulaSlotBase> getSlot,
        Func<string, IFormulaSlot<TResult, TPack>> lookup)
        where TEntry : class
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null) continue;
            var slot = getSlot(e);
            if (slot == null) continue;
            ValidateMissing<TResult>(typeName, slot, new HashSet<object>(), lookup);
        }
    }

    private void ValidateActionMissing<TResult>(
        string typeName, Func<string, IFormulaSlot<TResult, TPack>> lookup)
    {
        if (ActionGroups == null) return;
        var visited = new HashSet<object>();
        foreach (var g in ActionGroups)
        {
            if (g?.Actions == null) continue;
            foreach (var slot in g.Actions)
                if (slot != null) ValidateMissing<TResult>(typeName, slot, visited, lookup);
        }
    }

    private void ValidateMissing<TResult>(
        string typeName, object node, HashSet<object> visited,
        Func<string, IFormulaSlot<TResult, TPack>> lookup)
    {
        if (node == null || !visited.Add(node)) return;

        if (node is IFormulaSlot<TResult, TPack> && node is FormulaSlotBase fsb)
        {
            var key = fsb.DebugTokenKey;
            if (!string.IsNullOrEmpty(key) && !fsb.IsSelfReferencing && lookup(key) == null)
                Err($"{typeName} token 引用不存在的 Key：'{key}'");
        }

        // 欄位只往目前接的節點下沉；候選池不參與驗證。
        if (node is ActionSlot<TPack> actionSlotNode)
        {
            ValidateMissing<TResult>(typeName, actionSlotNode.Node, visited, lookup);
            return;
        }
        if (node is FormulaSlotBase slotNode)
        {
            ValidateMissing<TResult>(typeName, slotNode.Node, visited, lookup);
            return;
        }

        foreach (var f in GetAllInstanceFields(node.GetType()))
        {
            var val = f.GetValue(node);
            if (val == null) continue;
            var t = val.GetType();
            if (t.IsPrimitive || val is string || t.IsEnum) continue;

            if (val is FormulaAsset<TResult, TPack> asset)
            {
                var inner = asset.EditorGetTargetObject();
                ValidateMissing<TResult>(typeName, inner, visited, lookup);
                continue;
            }

            if (val is UnityEngine.Object) continue;

            if (val is System.Collections.IList list)
            {
                foreach (var item in list)
                    ValidateMissing<TResult>(typeName, item, visited, lookup);
                continue;
            }

            ValidateMissing<TResult>(typeName, val, visited, lookup);
        }
    }

    // ===== 節點內容檢查（空節點 / 內容為 null / 型別與欄位不相容）=====

    private void ValidateTokenSlotSources()
    {
        var visited = new HashSet<object>();
        TokenEntry.ForEachKind(new SlotSourceKindVisitor(this, visited));
    }

    private void WalkTokenSlotSources<TEntry>(List<TEntry> entries, Func<TEntry, object> getSlot, HashSet<object> visited)
        where TEntry : class
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            var slot = getSlot(e);
            if (slot != null) ValidateSlotSources(slot, visited);
        }
    }

    private void ValidateActionSlotSources()
    {
        if (ActionGroups == null) return;
        var visited = new HashSet<object>();
        foreach (var g in ActionGroups)
        {
            if (g?.Actions == null) continue;
            foreach (var slot in g.Actions)
                if (slot != null) ValidateSlotSources(slot, visited);
        }
    }

    private void ValidateSlotSources(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) return;

        // 欄位只往目前接的節點下沉；候選池不執行、不驗證。
        if (node is ActionSlot<TPack> a)
        {
            CheckNode(a.Node, "動作欄位", a.AcceptsBody, a.AcceptsAsset, allowToken: false);
            ValidateSlotSources(a.Node, visited);
            return;
        }
        if (node is FormulaSlotBase fsb)
        {
            CheckNode(fsb.Node, fsb.GetType().Name, fsb.AcceptsBody, fsb.AcceptsAsset, allowToken: true);
            ValidateSlotSources(fsb.Node, visited);
            return;
        }
        if (node is GraphNode graphNode)
        {
            if (graphNode.Kind == NodeKind.Inline) ValidateSlotSources(graphNode.BodyObject, visited);
            else if (graphNode.Kind == NodeKind.Asset) ValidateSlotSources(graphNode.AssetObject, visited);
            return;
        }

        // 下沉樹：穿透 ActionAsset / FormulaAssetBase；其他 UnityEngine.Object 視 leaf。
        foreach (var f in GetAllInstanceFields(node.GetType()))
        {
            var val = f.GetValue(node);
            if (val == null) continue;
            var vt = val.GetType();
            if (vt.IsPrimitive || val is string || vt.IsEnum) continue;

            if (val is ActionAssetBase<TPack> aa)
            {
                if (!visited.Add(aa)) continue;
                var inner = aa.EditorGetAction();
                if (inner != null) ValidateSlotSources(inner, visited);
                continue;
            }

            if (val is FormulaAssetBase fa)
            {
                if (!visited.Add(fa)) continue;
                var inner = fa.EditorGetTargetObject();
                if (inner != null) ValidateSlotSources(inner, visited);
                continue;
            }

            if (val is UnityEngine.Object) continue;

            if (val is System.Collections.IList list)
            {
                foreach (var item in list) ValidateSlotSources(item, visited);
                continue;
            }

            ValidateSlotSources(val, visited);
        }
    }

    // 節點是唯一來源，所以只需檢查「這個節點的內容有沒有、對不對型別」一件事。
    private void CheckNode(GraphNode node, string where,
        Func<ActionSystemNode, bool> acceptsBody, Func<ScriptableObject, bool> acceptsAsset, bool allowToken)
    {
        if (node == null) return;   // 動作＝空槽、公式＝常數，都是合法狀態

        switch (node.Kind)
        {
            case NodeKind.Empty:
                Err($"{where} 有一個尚未指定內容的節點");
                return;

            case NodeKind.Inline:
                if (node.BodyObject == null) { Err($"{where} 的節點設為內嵌內容，但內容是空的"); return; }
                if (!acceptsBody(node.BodyObject))
                    Err($"{where} 接的內容型別不相容：{node.BodyObject.GetType().Name}");
                return;

            case NodeKind.Asset:
                if (node.AssetObject == null) { Err($"{where} 的節點設為資產，但沒有指定資產"); return; }
                if (!acceptsAsset(node.AssetObject))
                    Err($"{where} 接的資產型別不相容：{node.AssetObject.GetType().Name}");
                return;

            case NodeKind.Token:
                if (!allowToken) { Err($"{where} 不能接變數"); return; }
                if (string.IsNullOrEmpty(node.TokenKey)) Err($"{where} 的節點設為變數，但沒有指定變數");
                return;
        }
    }

    // 衍生型別 GetFields 不會回 base class 的 private 欄位 — 手動沿繼承鏈往上抓 DeclaredOnly。
    private static IEnumerable<System.Reflection.FieldInfo> GetAllInstanceFields(Type type)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                                                   | System.Reflection.BindingFlags.Public
                                                   | System.Reflection.BindingFlags.NonPublic
                                                   | System.Reflection.BindingFlags.DeclaredOnly;
        while (type != null && type != typeof(object))
        {
            foreach (var f in type.GetFields(flags)) yield return f;
            type = type.BaseType;
        }
    }

    // ===== Verify visitors（concrete pack 透過 ForEachKind 把 (TResult, TEntry) 配對交回這裡）=====

    private sealed class VerifyKindVisitor : ITokenKindVisitor<TPack>
    {
        private readonly ActionSystem<TTiming, TPack, TTokenEntryPack> _s;
        public VerifyKindVisitor(ActionSystem<TTiming, TPack, TTokenEntryPack> s) => _s = s;

        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries)
            where TEntry : class, ITokenEntry
        {
            var keys = entries?.ConvertAll(e => e?.Key);
            _s.WarnEmptyTokenKeys(typeName, keys);
            _s.ReportDuplicateKeys(typeName, keys);
            _s.VerifyTokenKind<TResult, TEntry>(typeName, entries, e => e.Key, e => e.Slot);
        }
    }

    private sealed class SlotSourceKindVisitor : ITokenKindVisitor<TPack>
    {
        private readonly ActionSystem<TTiming, TPack, TTokenEntryPack> _s;
        private readonly HashSet<object> _visited;
        public SlotSourceKindVisitor(ActionSystem<TTiming, TPack, TTokenEntryPack> s, HashSet<object> visited)
        {
            _s = s;
            _visited = visited;
        }

        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries)
            where TEntry : class, ITokenEntry
        {
            _s.WalkTokenSlotSources(entries, e => e?.Slot, _visited);
        }
    }

    private sealed class AssetCycleKindVisitor : ITokenKindVisitor<TPack>
    {
        private readonly ActionSystem<TTiming, TPack, TTokenEntryPack> system;
        private readonly HashSet<UnityEngine.Object> completed;

        public AssetCycleKindVisitor(ActionSystem<TTiming, TPack, TTokenEntryPack> system,
            HashSet<UnityEngine.Object> completed)
        {
            this.system = system;
            this.completed = completed;
        }

        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries)
            where TEntry : class, ITokenEntry
        {
            if (entries == null) return;
            foreach (var entry in entries)
                if (entry?.Slot != null)
                    system.ValidateAssetCycles(entry.Slot, $"{typeName} token '{entry.Key}'", completed);
        }
    }
}
#endif

}
