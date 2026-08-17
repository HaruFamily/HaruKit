namespace PinPlugin.ActionSystem
{
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class ActionSystem<TTiming, TPack>
where TTiming : Enum
{
    [NonSerialized] private List<string> _errors = new();
    [NonSerialized] private List<string> _warnings = new();

    // 節點內容檢查跑兩趟：第一趟只走啟用路徑，第二趟才穿透停用節點。見 ValidateSlotSources。
    [NonSerialized] private bool _walkDisabled;
    [NonSerialized] private HashSet<GraphNode> _checkedNodes = new();

    private void Err(string msg) => _errors.Add(msg);
    private void Warn(string msg) => _warnings.Add(msg);

    /// <summary>第二趟才走到的節點代表所有指著它的路徑都被停用，runtime 不會求值，殘缺降成警告。</summary>
    private void Issue(string msg)
    {
        if (_walkDisabled) Warn(msg);
        else Err(msg);
    }

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

        _errors.Clear();
        _warnings.Clear();

        // 標註表要照現在的資料重建一次，不能沿用上一輪的快取。
        _tokenNodes = null;
        var tokenNodes = TokenNodes();

        ReportDuplicateTokenNames();
        ReportDuplicateTimings();
        ReportEmptyRootActions();
        ReportCarrierCycles(tokenNodes);

        // 節點內容：空節點、內容為 null、型別與欄位不相容。
        // 跑兩趟：先只走啟用路徑（殘缺＝錯誤），再補走停用子樹（殘缺＝警告）。
        // 停用節點 runtime 直接回保底值、子樹不求值，但共用載體只要還有一條啟用路徑指著它就仍是錯誤，
        // 所以順序不能顛倒，也不能只走一趟。
        _checkedNodes ??= new HashSet<GraphNode>();
        _checkedNodes.Clear();

        _walkDisabled = false;
        ValidateTokenNodes(tokenNodes);
        ValidateActionSlotSources();

        _walkDisabled = true;
        ValidateTokenNodes(tokenNodes);
        ValidateActionSlotSources();
        _walkDisabled = false;

        ReportAssetCycles(tokenNodes);

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

    /// <summary>標註名稱在一張圖內全域唯一：撞名時外部只查得到其中一顆，另一顆等於默默失效。</summary>
    private void ReportDuplicateTokenNames()
    {
        if (_duplicateTokenNames == null) return;
        var reported = new HashSet<string>();
        foreach (var name in _duplicateTokenNames)
            if (reported.Add(name))
                Err($"標註名稱重複：'{name}'（同一張圖內必須唯一，不分結果型別）");
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
                if (slot.Node != null && slot.Node.Kind != NodeKind.Empty) continue;

                // 停用的動作不執行，空著也跑得動，降成警告方便測試；與 ValidateSlotSources 的第二趟同一個理由。
                if (slot.Disabled || (slot.Node != null && slot.Node.Disabled))
                    Warn($"{group.Timing} 第 {i + 1} 個動作尚未指定 Action 類型（已停用）");
                else
                    Err($"{group.Timing} 第 {i + 1} 個動作尚未指定 Action 類型");
            }
        }
    }

    private void ReportAssetCycles(IReadOnlyDictionary<string, GraphNode> tokenNodes)
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
        foreach (var pair in tokenNodes)
            ValidateAssetCycles(pair.Value, $"標註 '{pair.Key}'", completed);
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
        if (cycle == null && asset is ScriptableObject scriptable)
        {
            foreach (var parameter in AssetGraphSchema.Read(scriptable, out _))
            {
                foreach (var child in DirectAssetReferences(parameter.Node))
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

    private List<UnityEngine.Object> DirectAssetReferences(object root)
    {
        var result = new List<UnityEngine.Object>();
        CollectDirectAssetReferences(root, new HashSet<object>(), result);
        return result;
    }

    private void CollectDirectAssetReferences(object node, HashSet<object> visited, List<UnityEngine.Object> result)
    {
        if (node == null || !visited.Add(node)) return;

        // 欄位只往目前接的節點下沉；沒有標註的候選節點不執行，不算引用。
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
            foreach (var binding in graphNode.Bindings)
                if (binding?.Slot != null) CollectDirectAssetReferences(binding.Slot, visited, result);
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

        foreach (var field in InstanceFields(type))
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

    // ===== 節點內容檢查（空節點 / 內容為 null / 型別與欄位不相容）=====

    /// <summary>
    /// 被標註的節點是這張圖的對外端點，從它開始整棵子樹都是正式資料，所以要跟動作樹一樣驗。
    /// 它多半沒有連入線（住在候選池），沒有父欄位可以判定型別相容，所以只驗「內容有沒有」。
    /// </summary>
    private void ValidateTokenNodes(IReadOnlyDictionary<string, GraphNode> tokenNodes)
    {
        var visited = new HashSet<object>();
        foreach (var pair in tokenNodes)
        {
            var node = pair.Value;
            if (node == null) continue;
            if (node.Disabled && !_walkDisabled) continue;   // 停用端點留到第二趟

            bool first = _checkedNodes.Add(node);
            if (first || !_walkDisabled)
            {
                if (first && !IsFormulaToken(node))
                    Issue($"標註 '{pair.Key}' 必須掛在可求值的公式或公式資產節點上");

                switch (node.Kind)
                {
                    case NodeKind.Empty:
                        if (first) Issue($"標註 '{pair.Key}' 的節點尚未指定內容");
                        break;
                    case NodeKind.Inline:
                        if (node.BodyObject == null && first) Issue($"標註 '{pair.Key}' 的節點設為內嵌內容，但內容是空的");
                        break;
                    case NodeKind.Asset:
                        if (node.AssetObject == null && first) Issue($"標註 '{pair.Key}' 的節點設為資產，但沒有指定資產");
                        break;
                }
            }

            ValidateSlotSources(node, visited);
        }
    }

    private static bool IsFormulaToken(GraphNode node)
    {
        if (node == null) return false;
        if (node.Kind == NodeKind.Asset) return node.AssetObject is FormulaAssetBase;
        if (node.Kind != NodeKind.Inline || node.BodyObject == null) return false;

        for (var type = node.BodyObject.GetType(); type != null && type != typeof(object); type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FormulaBase<,>)) return true;
        return false;
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

        if (node is IActionSystemAssetGraph assetGraph)
        {
            ValidateSlotSources(assetGraph.ContentObject, visited);
            if (assetGraph.Orphans != null)
                foreach (var orphan in assetGraph.Orphans)
                    if (orphan?.IsToken == true) ValidateSlotSources(orphan, visited);
            return;
        }

        // 欄位只往目前接的節點下沉；沒有標註的候選節點不執行、不驗證。
        if (node is ActionSlot<TPack> a)
        {
            // 停用的動作欄位不執行，整棵子樹留到第二趟走，殘缺降成警告。
            if (a.Disabled && !_walkDisabled) return;
            CheckNode(a.Node, "動作欄位", a.AcceptsBody, a.AcceptsAsset);
            ValidateSlotSources(a.Node, visited);
            return;
        }
        if (node is FormulaSlotBase fsb)
        {
            CheckNode(fsb.Node, fsb.GetType().Name, fsb.AcceptsBody, fsb.AcceptsAsset);
            ValidateSlotSources(fsb.Node, visited);
            return;
        }
        if (node is GraphNode graphNode)
        {
            // 停用節點回保底值，子樹不求值，同樣留到第二趟。
            if (graphNode.Disabled && !_walkDisabled) return;
            foreach (var binding in graphNode.Bindings)
            {
                if (binding?.Slot == null) continue;
                if (!binding.OverrideEnabled && !_walkDisabled) continue;
                ValidateSlotSources(binding.Slot, visited);
            }
            if (graphNode.Kind == NodeKind.Inline) ValidateSlotSources(graphNode.BodyObject, visited);
            else if (graphNode.Kind == NodeKind.Asset)
            {
                if (!_walkDisabled) ValidateAssetBindings(graphNode);
                ValidateSlotSources(graphNode.AssetObject, visited);
            }
            return;
        }

        // 下沉樹：穿透 ActionAsset / FormulaAssetBase；其他 UnityEngine.Object 視 leaf。
        foreach (var f in InstanceFields(node.GetType()))
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

    private void ReportCarrierCycles(IReadOnlyDictionary<string, GraphNode> tokenNodes)
    {
        var completed = new HashSet<GraphNode>();
        if (ActionGroups != null)
        {
            foreach (var group in ActionGroups)
            {
                if (group?.Actions == null) continue;
                foreach (var action in group.Actions)
                {
                    if (!HasCarrierCycle(action, new HashSet<GraphNode>(), completed, new HashSet<object>())) continue;
                    Err($"{group.Timing} 的動作圖有節點連線循環");
                    return;
                }
            }
        }
        foreach (var pair in tokenNodes)
        {
            if (!HasCarrierCycle(pair.Value, new HashSet<GraphNode>(), completed, new HashSet<object>())) continue;
            Err($"標註 '{pair.Key}' 的節點圖有連線循環");
            return;
        }
    }

    private bool HasCarrierCycle(object value, HashSet<GraphNode> stack,
        HashSet<GraphNode> completed, HashSet<object> visitedObjects)
    {
        if (value == null) return false;
        if (value is ActionSlot<TPack> actionSlot) return HasCarrierCycle(actionSlot.Node, stack, completed, visitedObjects);
        if (value is FormulaSlotBase formulaSlot) return HasCarrierCycle(formulaSlot.Node, stack, completed, visitedObjects);
        if (value is GraphNode carrier)
        {
            if (stack.Contains(carrier)) return true;
            if (completed.Contains(carrier)) return false;
            stack.Add(carrier);
            bool cycle = carrier.Kind == NodeKind.Inline
                && HasCarrierCycle(carrier.BodyObject, stack, completed, visitedObjects);
            if (!cycle)
            {
                foreach (var binding in carrier.Bindings)
                {
                    if (binding?.Slot == null || !HasCarrierCycle(binding.Slot, stack, completed, visitedObjects)) continue;
                    cycle = true;
                    break;
                }
            }
            stack.Remove(carrier);
            completed.Add(carrier);
            return cycle;
        }
        if (value is UnityEngine.Object || !visitedObjects.Add(value)) return false;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string) return false;
        if (value is System.Collections.IList list)
        {
            foreach (var item in list)
                if (HasCarrierCycle(item, stack, completed, visitedObjects)) return true;
            return false;
        }
        foreach (var field in InstanceFields(type))
            if (!field.IsStatic && !field.IsNotSerialized
                && HasCarrierCycle(field.GetValue(value), stack, completed, visitedObjects)) return true;
        return false;
    }

    private void ValidateAssetBindings(GraphNode carrier)
    {
        if (carrier?.AssetObject == null) return;
        var parameters = AssetGraphSchema.Read(carrier.AssetObject, out var duplicates);
        foreach (var duplicate in duplicates)
            Err($"資產 '{carrier.AssetObject.name}' 的參數標註名稱重複：'{duplicate}'");

        var byName = new Dictionary<string, AssetParameterDefinition>();
        foreach (var parameter in parameters)
            if (!byName.ContainsKey(parameter.Name)) byName.Add(parameter.Name, parameter);

        var bindingNames = new HashSet<string>();
        foreach (var binding in carrier.Bindings)
        {
            if (binding == null) { Err($"資產 '{carrier.AssetObject.name}' 有空的參數綁定"); continue; }
            if (!bindingNames.Add(binding.Name)) Err($"資產 '{carrier.AssetObject.name}' 的參數綁定重複：'{binding.Name}'");
            if (!byName.TryGetValue(binding.Name, out var parameter))
            {
                Err($"資產 '{carrier.AssetObject.name}' 已沒有參數 '{binding.Name}'，請移除舊綁定");
                continue;
            }
            if (binding.Slot == null)
            {
                Err($"資產 '{carrier.AssetObject.name}' 的參數 '{binding.Name}' 沒有 Slot");
                continue;
            }
            if (binding.Slot.ResultType != parameter.ResultType || binding.Slot.PackType != parameter.PackType)
                Err($"資產 '{carrier.AssetObject.name}' 的參數 '{binding.Name}' 型別不相容");
        }
    }

    // 節點是唯一來源，所以只需檢查「這個節點的內容有沒有、對不對型別」一件事。
    private void CheckNode(GraphNode node, string where,
        Func<ActionSystemNode, bool> acceptsBody, Func<ScriptableObject, bool> acceptsAsset)
    {
        if (node == null) return;   // 動作＝空槽、公式＝常數，都是合法狀態

        if (node.Disabled && !_walkDisabled) return;   // 停用節點留到第二趟

        // 節點自身的殘缺跟誰指著它無關，全圖報一次就夠；型別相容則是逐欄位判定，同一趟內每個欄位都要判。
        bool first = _checkedNodes.Add(node);
        if (_walkDisabled && !first) return;   // 第二趟只補報第一趟走不到的節點，避免同一則訊息重出

        switch (node.Kind)
        {
            case NodeKind.Empty:
                if (first) Issue($"{where} 有一個尚未指定內容的節點");
                return;

            case NodeKind.Inline:
                if (node.BodyObject == null) { if (first) Issue($"{where} 的節點設為內嵌內容，但內容是空的"); return; }
                if (!acceptsBody(node.BodyObject))
                    Issue($"{where} 接的內容型別不相容：{node.BodyObject.GetType().Name}");
                return;

            case NodeKind.Asset:
                if (node.AssetObject == null) { if (first) Issue($"{where} 的節點設為資產，但沒有指定資產"); return; }
                if (!acceptsAsset(node.AssetObject))
                    Issue($"{where} 接的資產型別不相容：{node.AssetObject.GetType().Name}");
                return;
        }
    }
}
#endif

}
