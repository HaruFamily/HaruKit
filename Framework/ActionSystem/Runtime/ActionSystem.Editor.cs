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
    // Owner 沿用專案既有慣例從 Selection 取（同 ActionSlot.FindOwnerSO）：這個按鈕本來就只在 Inspector 上按得到。
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

        // 5. Null slot：UseType / _formula / _asset / _tokenKey 一致性
        ValidateTokenNullSlots();
        ValidateActionNullSlots();
        ReportOrphanNodes();

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

    // 未連接節點只是「留在編輯區沒接線」，不阻擋存檔；但要講清楚它不會被執行。
    private void ReportOrphanNodes()
    {
        int count = _editorOrphans?.Count ?? 0;
        if (count > 0)
            Warn($"編輯區有 {count} 個未連接節點（不會被執行，可在視覺化編輯器接線或刪除）");
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
                Err($"{typeName} token '{key}' 直接自我參照（Slot.UseType=Token 且 _tokenKey={key}）");
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

    // ===== Null-slot 一致性（UseType vs _formula / _asset / _tokenKey）=====

    private void ValidateTokenNullSlots()
    {
        var visited = new HashSet<object>();
        TokenEntry.ForEachKind(new NullSlotKindVisitor(this, visited));
    }

    private void WalkTokenNullSlots<TEntry>(List<TEntry> entries, Func<TEntry, object> getSlot, HashSet<object> visited)
        where TEntry : class
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            var slot = getSlot(e);
            if (slot != null) ValidateNullSlots(slot, visited);
        }
    }

    private void ValidateActionNullSlots()
    {
        if (ActionGroups == null) return;
        var visited = new HashSet<object>();
        foreach (var g in ActionGroups)
        {
            if (g?.Actions == null) continue;
            foreach (var slot in g.Actions)
                if (slot != null) ValidateNullSlots(slot, visited);
        }
    }

    private void ValidateNullSlots(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) return;

        if (node is ActionSlot<TPack> a)
        {
            int ut = a.EditorUseTypeRaw; // Empty=0, Formula=1, Asset=2
            if (ut != 1 && a.EditorHasFormula)
                Warn($"ActionSlot UseType={UseTypeName_Action(ut)} 但仍殘留 _formula 設定（不會被使用，建議清除或切回 公式）");
            if (ut != 2 && a.EditorHasAsset)
                Warn($"ActionSlot UseType={UseTypeName_Action(ut)} 但仍殘留 _asset 設定（不會被使用，建議清除或切回 資產）");
            if (ut == 1 && !a.EditorHasFormula)
                Err("ActionSlot UseType=公式 但 _formula 為 null");
            if (ut == 2 && !a.EditorHasAsset)
                Err("ActionSlot UseType=資產 但 _asset 為 null");
        }
        else if (node is FormulaSlotBase fsb)
        {
            int ut = fsb.EditorUseTypeRaw; // Default=0, Formula=1, Asset=2, Token=3
            var name = node.GetType().Name;
            if (ut != 1 && fsb.EditorHasFormula)
                Warn($"{name} UseType={UseTypeName_Formula(ut)} 但仍殘留 _formula 設定（不會被使用，建議清除或切回 公式）");
            if (ut != 2 && fsb.EditorHasAsset)
                Warn($"{name} UseType={UseTypeName_Formula(ut)} 但仍殘留 _asset 設定（不會被使用，建議清除或切回 資產）");
            if (ut != 3 && fsb.EditorHasTokenKey)
                Warn($"{name} UseType={UseTypeName_Formula(ut)} 但仍殘留 _tokenKey 設定（不會被使用，建議清除或切回 變數）");
            if (ut == 1 && !fsb.EditorHasFormula) Err($"{name} UseType=公式 但 _formula 為 null");
            if (ut == 2 && !fsb.EditorHasAsset)   Err($"{name} UseType=資產 但 _asset 為 null");
            if (ut == 3 && !fsb.EditorHasTokenKey) Err($"{name} UseType=變數 但 _tokenKey 為空");
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
                if (inner != null) ValidateNullSlots(inner, visited);
                continue;
            }

            if (val is FormulaAssetBase fa)
            {
                if (!visited.Add(fa)) continue;
                var inner = fa.EditorGetTargetObject();
                if (inner != null) ValidateNullSlots(inner, visited);
                continue;
            }

            if (val is UnityEngine.Object) continue;

            if (val is System.Collections.IList list)
            {
                foreach (var item in list) ValidateNullSlots(item, visited);
                continue;
            }

            ValidateNullSlots(val, visited);
        }
    }

    private static string UseTypeName_Action(int ut) => ut switch
    {
        0 => "空",
        1 => "公式",
        2 => "資產",
        _ => ut.ToString(),
    };

    private static string UseTypeName_Formula(int ut) => ut switch
    {
        0 => "常數",
        1 => "公式",
        2 => "資產",
        3 => "變數",
        _ => ut.ToString(),
    };

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

    private sealed class NullSlotKindVisitor : ITokenKindVisitor<TPack>
    {
        private readonly ActionSystem<TTiming, TPack, TTokenEntryPack> _s;
        private readonly HashSet<object> _visited;
        public NullSlotKindVisitor(ActionSystem<TTiming, TPack, TTokenEntryPack> s, HashSet<object> visited)
        {
            _s = s;
            _visited = visited;
        }

        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries)
            where TEntry : class, ITokenEntry
        {
            _s.WalkTokenNullSlots(entries, e => e?.Slot, _visited);
        }
    }
}
#endif

}
