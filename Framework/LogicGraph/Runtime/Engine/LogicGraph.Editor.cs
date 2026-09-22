namespace HaruFamily.Framework.LogicGraph
{
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>Editor-only validation surface for a LogicGraph working copy.</summary>
public interface ILogicGraphEditorDiagnostics
{
    IReadOnlyList<GraphDiagnostic> CollectDiagnostics(IExternalTokenKeys external = null, IGraphDomainDiagnostics domain = null);
}

public partial class LogicGraph<TTiming, TPack>
    : ILogicGraphEditorDiagnostics
where TTiming : Enum
{
    [NonSerialized] private List<GraphDiagnostic> _diagnostics = new();

    // 節點內容檢查跑兩趟：第一趟只走啟用路徑，第二趟才穿透停用節點。見 ValidateSlotSources。
    [NonSerialized] private bool _walkDisabled;
    [NonSerialized] private HashSet<GraphNode> _checkedNodes = new();

    /// <summary>Current verification output for Editor integrations; rebuilt by every Verify call.</summary>
    public IReadOnlyList<GraphDiagnostic> Diagnostics
        => _diagnostics ?? (IReadOnlyList<GraphDiagnostic>)Array.Empty<GraphDiagnostic>();

    private void Err(string code, string message, GraphDiagnosticLocation location = default)
        => _diagnostics.Add(new GraphDiagnostic("logicgraph." + code, GraphDiagnosticSeverity.Error, message, location));

    private void Warn(string code, string message, GraphDiagnosticLocation location = default)
        => _diagnostics.Add(new GraphDiagnostic("logicgraph." + code, GraphDiagnosticSeverity.Warning, message, location));

    /// <summary>第二趟才走到的節點代表所有指著它的路徑都被停用，runtime 不會求值，殘缺降成警告。</summary>
    private void Issue(string code, string message, GraphDiagnosticLocation location)
    {
        if (_walkDisabled) Warn(code, message, location);
        else Err(code, message, location);
    }

    /// <summary>Runs generic LogicGraph validation without emitting a Console summary.</summary>
    public IReadOnlyList<GraphDiagnostic> CollectDiagnostics(IExternalTokenKeys external = null, IGraphDomainDiagnostics domain = null)
        => RunValidation(external, domain ?? external as IGraphDomainDiagnostics, updateValidationState: false);

    IReadOnlyList<GraphDiagnostic> IGraphDocumentValidation.CollectDiagnostics(UnityEngine.Object owner)
        => RunOwnerValidation(owner, false);

    void IGraphDocumentValidation.Verify(UnityEngine.Object owner)
    {
        RunOwnerValidation(owner, true);
        EmitSummary(owner != null ? owner.name : "LogicGraph");
    }

    private IReadOnlyList<GraphDiagnostic> RunOwnerValidation(UnityEngine.Object owner, bool updateValidationState)
        => RunValidation(owner as IExternalTokenKeys, owner as IGraphDomainDiagnostics, updateValidationState, ReadUsage(owner));

    private static LogicGraphUsage<TTiming, TPack> ReadUsage(UnityEngine.Object owner)
    {
        var usage = new LogicGraphUsage<TTiming, TPack>();
        try
        {
            if (owner is ILogicGraphUsage<TTiming, TPack> configured) configured.ConfigureGraph(usage);
            else if (owner is ILogicGraphTimingOwner legacy && legacy.AllowedTimings != null)
            {
                var allowed = new List<TTiming>();
                foreach (var timing in legacy.AllowedTimings)
                    if (timing is TTiming typed) allowed.Add(typed);
                usage.AllowTimings(allowed);
            }
        }
        catch (Exception exception) { usage.RejectConfiguration(exception.Message); }
        return usage;
    }

    private IReadOnlyList<GraphDiagnostic> RunValidation(IExternalTokenKeys external, IGraphDomainDiagnostics domain,
        bool updateValidationState, LogicGraphUsage<TTiming, TPack> usage = null)
    {
        // DeepCopy 與 Unity 反序列化不會保留 NonSerialized 驗證緩衝。
        _diagnostics ??= new List<GraphDiagnostic>();

        _diagnostics.Clear();
        _walkDisabled = false;

        ReportDuplicateTokenNames();
        ValidatePropertyDefinitions(Properties, new GraphDiagnosticLocation(fieldPath: "Properties"));
        ReportExternalTokenKeys(external);
        ReportDuplicateTimings();
        ReportEmptyRootActions();
        ReportCarrierCycles();

        // 節點內容：空節點、內容為 null、型別與欄位不相容。
        // 跑兩趟：先只走啟用路徑（殘缺＝錯誤），再補走停用子樹（殘缺＝警告）。
        // 停用節點 runtime 直接回保底值、子樹不求值，但共用載體只要還有一條啟用路徑指著它就仍是錯誤，
        // 所以順序不能顛倒，也不能只走一趟。
        _checkedNodes ??= new HashSet<GraphNode>();
        _checkedNodes.Clear();

        _walkDisabled = false;
        ValidateTokens();
        ValidateActionSlotSources();

        _walkDisabled = true;
        ValidateTokens();
        ValidateActionSlotSources();
        _walkDisabled = false;

        ReportAssetCycles();

        if (domain != null)
        {
            // Provider 只取得文件與本次的結果容器，不可覆寫 Core 已收集的問題。
            var domainDiagnostics = new List<GraphDiagnostic>();
            try { domain.CollectDiagnostics(this, domainDiagnostics); }
            catch (Exception exception)
            {
                Err("domain.validation-failed", "領域驗證失敗：" + exception.Message);
            }
            foreach (var diagnostic in domainDiagnostics)
                if (diagnostic != null) _diagnostics.Add(diagnostic);
        }

        usage?.Collect(this, _diagnostics);

        if (updateValidationState)
        {
            _validated = !HasErrors();
            _hasLoggedValidationFailure = false;
        }

        return Diagnostics;
    }

    /// <summary>external：Owner 自己，宣告它會從圖外用字串 key 求值哪些Token（見 IExternalTokenKeys）。null＝沒有圖外引用。</summary>
    public void Verify(IExternalTokenKeys external = null, IGraphDomainDiagnostics domain = null)
    {
        RunValidation(external, domain ?? external as IGraphDomainDiagnostics, updateValidationState: true);
        var owner = external as UnityEngine.Object ?? domain as UnityEngine.Object;
        EmitSummary(owner != null ? owner.name : "LogicGraph");
    }

    private const string COLOR_OK      = "#5BE584";
    private const string COLOR_FAIL    = "#FF6B6B";
    private const string COLOR_WARN    = "#FFC857";
    private const string COLOR_NAME    = "#7FD0FF";
    private const string COLOR_DIVIDER = "#888888";
    private const string COLOR_TAG     = "#B084EB";

    private bool HasErrors()
    {
        foreach (var diagnostic in _diagnostics)
            if (diagnostic.Severity == GraphDiagnosticSeverity.Error) return true;
        return false;
    }

    private void EmitSummary(string name)
    {
        int errors = 0, warnings = 0;
        foreach (var diagnostic in _diagnostics)
        {
            if (diagnostic.Severity == GraphDiagnosticSeverity.Error) errors++;
            else if (diagnostic.Severity == GraphDiagnosticSeverity.Warning) warnings++;
        }
        bool ok = errors == 0;
        string verdict = ok
            ? $"<b><color={COLOR_OK}>驗證成功</color></b>"
            : $"<b><color={COLOR_FAIL}>驗證失敗</color></b>";
        string errCount  = $"<color={COLOR_FAIL}>{errors}</color>";
        string warnCount = $"<color={COLOR_WARN}>{warnings}</color>";
        string divider   = $"<color={COLOR_DIVIDER}>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>";

        var sb = new StringBuilder();
        sb.AppendLine(divider);
        sb.AppendLine($"<color={COLOR_TAG}>[Verify]</color><color={COLOR_NAME}>[{name}]</color> {verdict} — 錯誤 {errCount} / 警告 {warnCount}");
        foreach (var diagnostic in _diagnostics)
        {
            string color = diagnostic.Severity == GraphDiagnosticSeverity.Error ? COLOR_FAIL : COLOR_WARN;
            sb.AppendLine($"  <color={color}>[{diagnostic.Code}]</color> {diagnostic.Location.FieldPath} {diagnostic.Message}");
        }
        sb.Append(divider);
        string body = sb.ToString();
        if (ok) Debug.Log(body);
        else    Debug.LogError(body);
    }

    private static GraphDiagnosticLocation At(GraphDiagnosticLocation parent, string path, GraphNode node = null)
        => new GraphDiagnosticLocation(focusId: parent.FocusId, nodeId: node?.Id ?? parent.NodeId,
            tokenId: parent.TokenId, fieldPath: path);

    private static GraphDiagnosticLocation AtToken(GraphToken token, int index)
        => new GraphDiagnosticLocation(tokenId: token?.Id, fieldPath: $"Tokens[{index}]");

    private static GraphDiagnosticLocation AtAction(int groupIndex, int actionIndex, ActionSlot<TPack> slot)
        => new GraphDiagnosticLocation(nodeId: slot?.Node?.Id,
            fieldPath: $"ActionGroups[{groupIndex}].Actions[{actionIndex}]");

    /// <summary>Token唯一性是「族＋名稱」：撞號時外部只查得到其中一個，另一個等於默默失效。</summary>
    private void ReportDuplicateTokenNames()
    {
        var seen = new HashSet<(Type, string)>();
        var reported = new HashSet<string>();
        for (int i = 0; i < Tokens.Count; i++)
        {
            var endpoint = Tokens[i];
            var location = AtToken(endpoint, i);
            if (endpoint == null) { Err("token.missing", "Token 清單裡有空項目", location); continue; }
            if (endpoint.Slot == null) { Err("token.slot-missing", $"Token '{endpoint.Name ?? "(未命名)"}' 沒有指定結果型別", location); continue; }
            if (string.IsNullOrEmpty(endpoint.Name)) { Err("token.name-missing", $"有一個 {endpoint.ResultType?.Name} Token 沒有名稱", location); continue; }

            if (!seen.Add((endpoint.Slot.FamilyType, endpoint.Name)) && reported.Add(endpoint.Name))
                Err("token.duplicate", $"Token 名稱重複：'{endpoint.Name}'（同族內必須唯一）", location);
        }
    }

    private void ValidatePropertyDefinitions(IEnumerable<GraphProperty> properties, GraphDiagnosticLocation location)
    {
        var names = new HashSet<string>();
        int index = 0;
        foreach (var property in properties ?? Array.Empty<GraphProperty>())
        {
            var at = At(location, location.FieldPath + $"[{index++}]");
            if (property == null) { Issue("property.missing", "變數庫有空項目", at); continue; }
            if (!property.Proto) continue;
            if (string.IsNullOrWhiteSpace(property.Name))
                Issue("property.name-missing", "ProtoProperty 沒有名稱", at);
            else if (!names.Add(property.Name))
                Issue("property.duplicate", $"ProtoProperty 名稱重複：'{property.Name}'", at);
            if (property.Slot == null)
                Issue("property.slot-missing", $"ProtoProperty '{property.Name}' 沒有指定型別", at);
        }
    }

    /// <summary>
    /// Owner 指名了不存在的Token。這種引用在圖上沒有任何連線，runtime 只是 Has 回 false 然後靜默跳過，
    /// 所以打錯一個字的結果是「功能整個不會發生」，什麼訊息都沒有。
    /// 比名稱不比族：圖外引用只給得出名字，族由呼叫端自己探。
    /// </summary>
    private void ReportExternalTokenKeys(IExternalTokenKeys external)
    {
        var declared = external?.ExternalTokenKeys;
        if (declared == null) return;

        var names = new HashSet<string>();
        foreach (var endpoint in Tokens)
            if (!string.IsNullOrEmpty(endpoint?.Name)) names.Add(endpoint.Name);

        var reported = new HashSet<string>();
        foreach (var key in declared)
        {
            if (string.IsNullOrWhiteSpace(key) || names.Contains(key)) continue;
            if (reported.Add(key))
                Err("external-token.missing", $"圖外引用了不存在的 Token '{key}'（建一個同名 Token，或修正引用端的名稱；查不到的名字會被靜默跳過）",
                    new GraphDiagnosticLocation(fieldPath: nameof(IExternalTokenKeys.ExternalTokenKeys)));
        }
    }

    private void ReportDuplicateTimings()
    {
        if (ActionGroups == null) return;
        var seen = new HashSet<TTiming>();
        var dup = new HashSet<TTiming>();
        for (int i = 0; i < ActionGroups.Count; i++)
        {
            var g = ActionGroups[i];
            if (g == null) continue;
            if (!seen.Add(g.Timing) && dup.Add(g.Timing))
                Err("timing.duplicate", $"ActionGroups 重複 Timing：'{g.Timing}'",
                    new GraphDiagnosticLocation(fieldPath: $"ActionGroups[{i}].Timing"));
        }
    }

    private void ReportEmptyRootActions()
    {
        if (ActionGroups == null) return;
        for (int groupIndex = 0; groupIndex < ActionGroups.Count; groupIndex++)
        {
            var group = ActionGroups[groupIndex];
            if (group?.Actions == null) continue;
            for (int i = 0; i < group.Actions.Count; i++)
            {
                var slot = group.Actions[i];
                if (slot == null) continue;
                if (slot.Node != null && slot.Node.Kind != NodeKind.Empty) continue;

                // 停用的動作不執行，空著也跑得動，降成警告方便測試；與 ValidateSlotSources 的第二趟同一個理由。
                if (slot.Disabled || (slot.Node != null && slot.Node.Disabled))
                    Warn("action.missing", $"{group.Timing} 第 {i + 1} 個動作尚未指定 Action 類型（已停用）", AtAction(groupIndex, i, slot));
                else
                    Err("action.missing", $"{group.Timing} 第 {i + 1} 個動作尚未指定 Action 類型", AtAction(groupIndex, i, slot));
            }
        }
    }

    private void ReportAssetCycles()
    {
        var completed = new HashSet<UnityEngine.Object>();
        if (ActionGroups != null)
        {
            for (int groupIndex = 0; groupIndex < ActionGroups.Count; groupIndex++)
            {
                var group = ActionGroups[groupIndex];
                if (group?.Actions == null) continue;
                for (int i = 0; i < group.Actions.Count; i++)
                    ValidateAssetCycles(group.Actions[i], $"{group.Timing} 第 {i + 1} 個動作", completed, AtAction(groupIndex, i, group.Actions[i]));
            }
        }
        for (int i = 0; i < Tokens.Count; i++)
        {
            var endpoint = Tokens[i];
            if (endpoint?.Slot != null) ValidateAssetCycles(endpoint.Slot, $"Token '{endpoint.Name}'", completed, AtToken(endpoint, i));
        }
    }

    private void ValidateAssetCycles(object root, string where, HashSet<UnityEngine.Object> completed, GraphDiagnosticLocation location)
    {
        if (root == null) return;
        var stack = new HashSet<UnityEngine.Object>();
        var path = new List<UnityEngine.Object>();
        foreach (var asset in DirectAssetReferences(root))
        {
            string cycle = FindAssetCycle(asset, stack, path, completed);
            if (cycle == null) continue;
            Err("asset.cycle", $"{where} Asset 循環引用：{cycle}", location);
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

    private List<UnityEngine.Object> DirectAssetReferences(object root)
    {
        var result = new List<UnityEngine.Object>();
        CollectDirectAssetReferences(root, new HashSet<object>(ReferenceComparer.Instance), result);
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
    /// 端點是這張圖的對外介面，從它的取值欄位開始整棵子樹都是正式資料，跟動作樹一樣驗。
    /// 沒接來源的端點是具名常數，合法，不必檢查內容。
    /// </summary>
    private void ValidateTokens()
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        for (int i = 0; i < Tokens.Count; i++)
        {
            var endpoint = Tokens[i];
            if (endpoint?.Slot == null) continue;   // 空項目與缺 Slot 由 ReportDuplicateTokenNames 報
            var location = AtToken(endpoint, i);
            ValidateSlotSources(endpoint.Slot, visited, At(location, location.FieldPath + ".Slot"));
        }
    }

    private void ValidateActionSlotSources()
    {
        if (ActionGroups == null) return;
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        for (int groupIndex = 0; groupIndex < ActionGroups.Count; groupIndex++)
        {
            var g = ActionGroups[groupIndex];
            if (g?.Actions == null) continue;
            for (int i = 0; i < g.Actions.Count; i++)
                if (g.Actions[i] != null) ValidateSlotSources(g.Actions[i], visited, AtAction(groupIndex, i, g.Actions[i]));
        }
    }

    private void ValidateSlotSources(object node, HashSet<object> visited, GraphDiagnosticLocation location)
    {
        if (node == null || !visited.Add(node)) return;

        if (node is IGraphAsset assetGraph)
        {
            if (assetGraph.Root?.Disabled == true && !_walkDisabled) return;
            if (assetGraph.Root?.Kind == NodeKind.Property && _checkedNodes.Add(assetGraph.Root))
                Issue("asset.property-root", "資產根內容必須是公式或動作，不能直接引用 Property", location);
            if (!_walkDisabled && assetGraph is IPropertyOwner properties)
                ValidatePropertyDefinitions(properties.Properties, At(location, location.FieldPath + ".Asset.Properties"));
            ValidateSlotSources(assetGraph.Root, visited, At(location, location.FieldPath + ".Asset.Root"));
            // 資產的端點就是它的參數介面，跟內容一樣是正式資料；候選池不驗。
            if (assetGraph.Tokens != null)
                for (int i = 0; i < assetGraph.Tokens.Count; i++)
                    if (assetGraph.Tokens[i]?.Slot != null)
                        ValidateSlotSources(assetGraph.Tokens[i].Slot, visited, At(location, location.FieldPath + $".Asset.Tokens[{i}].Slot"));
            return;
        }

        // 欄位只往目前接的節點下沉；候選節點不執行、不驗證。
        if (node is ActionSlot<TPack> a)
        {
            // 停用的動作欄位不執行，整棵子樹留到第二趟走，殘缺降成警告。
            if (a.Disabled && !_walkDisabled) return;
            CheckNode(a.Node, "動作欄位", a.AcceptsBody, a.AcceptsAsset, a.AcceptsToken, a.AcceptsProperty, location);
            ValidateSlotSources(a.Node, visited, location);
            return;
        }
        if (node is GraphSlotBase fsb)
        {
            CheckNode(fsb.Node, fsb.GetType().Name, fsb.AcceptsBody, fsb.AcceptsAsset, fsb.AcceptsToken, fsb.AcceptsProperty, location);
            ValidateSlotSources(fsb.Node, visited, location);
            return;
        }
        if (node is GraphNode graphNode)
        {
            // 停用節點回保底值，子樹不求值，同樣留到第二趟。
            if (!_walkDisabled && (graphNode.Disabled
                || graphNode.AssetObject is IGraphAsset referencedAsset && referencedAsset.Root?.Disabled == true)) return;
            location = At(location, location.FieldPath, graphNode);
            for (int i = 0; i < graphNode.Bindings.Count; i++)
            {
                var binding = graphNode.Bindings[i];
                if (binding?.Slot == null) continue;
                if (!binding.OverrideEnabled && !_walkDisabled) continue;
                ValidateSlotSources(binding.Slot, visited, At(location, location.FieldPath + $".Bindings[{i}].Slot"));
            }
            if (graphNode.Kind == NodeKind.Inline) ValidateSlotSources(graphNode.BodyObject, visited, location);
            else if (graphNode.Kind == NodeKind.Asset)
            {
                if (!_walkDisabled) ValidateAssetBindings(graphNode, location);
                ValidateSlotSources(graphNode.AssetObject, visited, location);
            }
            return;
        }

        if (node is UnityEngine.Object) return;
        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is string) return;
        if (node is System.Collections.IList list)
        {
            for (int i = 0; i < list.Count; i++)
                ValidateSlotSources(list[i], visited, At(location, location.FieldPath + $"[{i}]"));
            return;
        }

        // 下沉序列化內容；資產分支共用上方的根載體與停用判定。
        foreach (var f in InstanceFields(node.GetType()))
        {
            if (f.IsStatic || f.IsNotSerialized) continue;
            var val = f.GetValue(node);
            ValidateSlotSources(val, visited, At(location, location.FieldPath + "." + f.Name));
        }
    }

    private void ReportCarrierCycles()
    {
        var completed = new HashSet<GraphNode>();
        if (ActionGroups != null)
        {
            for (int groupIndex = 0; groupIndex < ActionGroups.Count; groupIndex++)
            {
                var group = ActionGroups[groupIndex];
                if (group?.Actions == null) continue;
                for (int i = 0; i < group.Actions.Count; i++)
                {
                    var action = group.Actions[i];
                    if (!HasCarrierCycle(action, new HashSet<GraphNode>(), completed,
                        new HashSet<object>(ReferenceComparer.Instance))) continue;
                    Err("node.cycle", $"{group.Timing} 的動作圖有節點連線循環", AtAction(groupIndex, i, action));
                    return;
                }
            }
        }
        for (int i = 0; i < Tokens.Count; i++)
        {
            var endpoint = Tokens[i];
            if (endpoint?.Slot == null) continue;
            if (!HasCarrierCycle(endpoint.Slot, new HashSet<GraphNode>(), completed,
                new HashSet<object>(ReferenceComparer.Instance))) continue;
            Err("node.cycle", $"Token '{endpoint.Name}' 的節點圖有連線循環", AtToken(endpoint, i));
            return;
        }
    }

    private bool HasCarrierCycle(object value, HashSet<GraphNode> stack,
        HashSet<GraphNode> completed, HashSet<object> visitedObjects)
    {
        if (value == null) return false;
        if (value is PropertySlotBase || value is GraphPropertyInputSlot) return false;
        if (value is ActionSlot<TPack> actionSlot) return HasCarrierCycle(actionSlot.Node, stack, completed, visitedObjects);
        if (value is FormulaSlotBase formulaSlot) return HasCarrierCycle(formulaSlot.Node, stack, completed, visitedObjects);
        if (value is GraphNode carrier)
        {
            if (stack.Contains(carrier)) return true;
            if (completed.Contains(carrier)) return false;
            stack.Add(carrier);
            // Token節點要下沉到端點的取值欄位，否則「A Token引用 B、B 又引用 A」這種跨端點的環抓不到。
            bool cycle = carrier.Kind switch
            {
                NodeKind.Inline => HasCarrierCycle(carrier.BodyObject, stack, completed, visitedObjects),
                NodeKind.Token => HasCarrierCycle(carrier.Token?.Slot, stack, completed, visitedObjects),
                _ => false,
            };
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

    private void ValidateAssetBindings(GraphNode carrier, GraphDiagnosticLocation location)
    {
        if (carrier?.AssetObject == null) return;
        var parameters = AssetGraphSchema.Read(carrier.AssetObject, out var duplicates);
        foreach (var duplicate in duplicates)
            Err("asset.parameter-duplicate", $"資產 '{carrier.AssetObject.name}' 的參數標註名稱重複：'{duplicate}'", location);

        // 綁定與參數的配對鍵是（族, 名稱），和 TokenTable 的覆蓋表一致：同名不同族的參數是兩個參數。
        var byKey = new HashSet<(Type, string)>();
        var parameterNames = new HashSet<string>();
        foreach (var parameter in parameters)
        {
            byKey.Add((parameter.Slot.FamilyType, parameter.Name));
            parameterNames.Add(parameter.Name);
        }

        var bindingKeys = new HashSet<(Type, string)>();
        for (int i = 0; i < carrier.Bindings.Count; i++)
        {
            var binding = carrier.Bindings[i];
            var bindingLocation = At(location, location.FieldPath + $".Bindings[{i}]");
            if (binding == null) { Err("asset.binding-missing", $"資產 '{carrier.AssetObject.name}' 有空的參數綁定", bindingLocation); continue; }
            if (binding.Slot == null)
            {
                Err("asset.binding-slot-missing", $"資產 '{carrier.AssetObject.name}' 的參數 '{binding.Name}' 沒有 Slot", bindingLocation);
                continue;
            }
            var bindingKey = (binding.Slot.FamilyType, binding.Name);
            if (!bindingKeys.Add(bindingKey)) Err("asset.binding-duplicate", $"資產 '{carrier.AssetObject.name}' 的參數綁定重複：'{binding.Name}'", bindingLocation);
            if (byKey.Contains(bindingKey)) continue;
            Err(parameterNames.Contains(binding.Name) ? "asset.binding-incompatible" : "asset.binding-unknown", parameterNames.Contains(binding.Name)
                ? $"資產 '{carrier.AssetObject.name}' 的參數 '{binding.Name}' 型別不相容"
                : $"資產 '{carrier.AssetObject.name}' 已沒有參數 '{binding.Name}'，請移除舊綁定", bindingLocation);
        }
    }

    // 節點是唯一來源，所以只需檢查「這個節點的內容有沒有、對不對型別」一件事。
    private void CheckNode(GraphNode node, string where,
        Func<GraphNodeContent, bool> acceptsBody, Func<ScriptableObject, bool> acceptsAsset,
        Func<GraphToken, bool> acceptsToken, Func<GraphProperty, bool> acceptsProperty, GraphDiagnosticLocation location)
    {
        if (node == null) return;   // 動作＝空槽、公式＝常數，都是合法狀態

        if (node.Disabled && !_walkDisabled) return;   // 停用節點留到第二趟

        // 節點自身的殘缺跟誰指著它無關，全圖報一次就夠；型別相容則是逐欄位判定，同一趟內每個欄位都要判。
        bool first = _checkedNodes.Add(node);
        if (_walkDisabled && !first) return;   // 第二趟只補報第一趟走不到的節點，避免同一則訊息重出
        location = At(location, location.FieldPath, node);

        switch (node.Kind)
        {
            case NodeKind.Empty:
                if (first) Issue("node.empty", $"{where} 有一個尚未指定內容的節點", location);
                return;

            case NodeKind.Inline:
                if (node.BodyObject == null) { if (first) Issue("node.body-missing", $"{where} 的節點設為內嵌內容，但內容是空的", location); return; }
                if (!acceptsBody(node.BodyObject))
                    Issue("node.body-incompatible", $"{where} 接的內容型別不相容：{node.BodyObject.GetType().Name}", location);
                return;

            case NodeKind.Asset:
                if (node.AssetObject == null) { if (first) Issue("node.asset-missing", $"{where} 的節點設為資產，但沒有指定資產", location); return; }
                if (!acceptsAsset(node.AssetObject))
                    Issue("node.asset-incompatible", $"{where} 接的資產型別不相容：{node.AssetObject.GetType().Name}", location);
                return;

            case NodeKind.Token:
                // 端點被刪掉時參照直接變 null，看得見；不會像字串 key 一樣留著一個查不到的名字。
                if (node.Token == null) { if (first) Issue("node.token-missing", $"{where} 的節點設為 Token，但沒有指定 Token", location); return; }
                if (string.IsNullOrEmpty(node.Token.Name)) { if (first) Issue("node.token-name-missing", $"{where} 接的 Token 沒有名稱", location); return; }
                if (!acceptsToken(node.Token))
                    Issue("node.token-incompatible", $"{where} 接的 Token '{node.Token.Name}' 型別不相容：{node.Token.ResultType?.Name ?? "未指定"}", location);
                return;

            case NodeKind.Property:
                if (node.Property == null) { if (first) Issue("node.property-missing", $"{where} 沒有指定 Property", location); return; }
                if (node.Property.Slot == null) { if (first) Issue("node.property-slot-missing", $"{where} 的 Property 尚未定型", location); return; }
                if (!acceptsProperty(node.Property))
                    Issue("node.property-incompatible", $"{where} 接的 Property 型別不相容", location);
                return;
        }
    }
}
#endif

}
