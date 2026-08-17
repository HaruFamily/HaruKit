namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次求值期間的具名端點表：名稱 → 被標註的載體。
///
/// 名稱在一張圖內全域唯一（不分結果型別），所以這裡只有一張表；型別檢查留到 Has/Resolve
/// 那一刻用 GetBody/GetAsset 做，不合型別就當作沒有這個值。
/// </summary>
public class TokenTable<TPack>
{
    private readonly Dictionary<string, GraphNode> _nodes = new();
    private readonly HashSet<(Type, string)> _inFlight = new();
    private readonly Dictionary<string, NamedFormulaSlot> _overrides = new();
    private readonly HashSet<(Type, string)> _overrideInFlight = new();
    private TokenTable<TPack> _caller;

    internal static TokenTable<TPack> CreateAssetScope(ScriptableObject asset,
        IReadOnlyList<NamedFormulaSlot> bindings, TokenTable<TPack> caller)
    {
        var table = new TokenTable<TPack> { _caller = caller };
        foreach (var parameter in AssetGraphSchema.Read(asset, out _))
            table.Register(parameter.Name, parameter.Node);
        if (bindings != null)
        {
            foreach (var binding in bindings)
            {
                if (binding == null || !binding.OverrideEnabled || binding.Slot == null || string.IsNullOrEmpty(binding.Name)) continue;
                if (!table._overrides.ContainsKey(binding.Name)) table._overrides[binding.Name] = binding;
            }
        }
        return table;
    }

    /// <summary>登記一個標註端點。同名後到者不覆蓋——重複名稱由 Verify 擋，runtime 取先到的那個。</summary>
    public void Register(string name, GraphNode node)
    {
        if (string.IsNullOrEmpty(name) || node == null) return;
        if (!_nodes.ContainsKey(name)) _nodes[name] = node;
    }

    public bool Has<T>(string key) => TryGetSource<T>(key, out _);

    internal bool HasOverride<T>(string key)
        => !string.IsNullOrEmpty(key)
        && _overrides.TryGetValue(key, out var binding)
        && binding?.Slot is IFormulaSlot<T, TPack>;

    internal async UniTask<T> ResolveOverride<T>(string key, TPack pack)
    {
        if (!HasOverride<T>(key) || _caller == null) return default;
        var cycleKey = (typeof(T), key);
        if (!_overrideInFlight.Add(cycleKey))
        {
            Debug.LogWarning($"[TokenTable] 資產參數 '{key}' 發生循環覆蓋");
            return default;
        }
        try
        {
            return await ((IFormulaSlot<T, TPack>)_overrides[key].Slot).Evaluate(pack, _caller);
        }
        finally
        {
            _overrideInFlight.Remove(cycleKey);
        }
    }

    public bool IsResolving<T>(string key)
        => !string.IsNullOrEmpty(key) && _inFlight.Contains((typeof(T), key));

    public async UniTask<T> Resolve<T>(string key, TPack pack)
    {
        if (!TryGetSource<T>(key, out var node)) return default;

        var ck = (typeof(T), key);
        if (_inFlight.Contains(ck))
        {
            Debug.LogWarning($"[TokenTable] {typeof(T).Name} token '{key}' 發生循環參照");
            return default;
        }

        _inFlight.Add(ck);
        try
        {
            return await Evaluate<T>(node, pack);
        }
        finally
        {
            _inFlight.Remove(ck);
        }
    }

    /// <summary>這個名稱在 T 型別下求得出值嗎。沒登記、停用、空節點或型別不符都回 false。</summary>
    private bool TryGetSource<T>(string key, out GraphNode node)
    {
        node = null;
        if (string.IsNullOrEmpty(key)) return false;
        if (!_nodes.TryGetValue(key, out node) || node == null) return false;
        if (node.Disabled) return false;   // 停用端點＝沒有這個值，呼叫端走自己的保底值

        return node.Kind switch
        {
            NodeKind.Inline => node.GetBody<FormulaBase<T, TPack>>() != null,
            NodeKind.Asset => node.GetAsset<FormulaAsset<T, TPack>>() != null,
            _ => false,
        };
    }

    private async UniTask<T> Evaluate<T>(GraphNode node, TPack pack)
    {
        switch (node.Kind)
        {
            case NodeKind.Inline:
            {
                var formula = node.GetBody<FormulaBase<T, TPack>>();
                return formula != null ? await formula.Evaluate(pack, this) : default;
            }
            case NodeKind.Asset:
            {
                var asset = node.GetAsset<FormulaAsset<T, TPack>>();
                return asset != null ? await asset.Evaluate(pack, this, node.Bindings) : default;
            }
            default:
                return default;
        }
    }
}

}
