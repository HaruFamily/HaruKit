namespace HaruFamily.Framework.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次求值期間的具名變數表：（結果型別, 名稱）→ 端點的取值欄位。
///
/// 名稱唯一性含結果型別，所以同名不同型可以並存；查詢一律帶 T，取不到就當作沒有這個值。
/// 求值不做記憶化：同一個名字被引用兩次就算兩次，非純函式（如 Random）每次都是新的結果。
/// </summary>
public class TokenTable<TPack>
{
    private readonly Dictionary<(Type, string), FormulaSlotBase> _slots = new();
    private readonly HashSet<(Type, string)> _inFlight = new();
    private readonly Dictionary<string, NamedFormulaSlot> _overrides = new();
    private readonly HashSet<(Type, string)> _overrideInFlight = new();
    private TokenTable<TPack> _caller;

    // 沒有登記任何端點、也不會被寫入，所以共用一份就夠。
    private static readonly TokenTable<TPack> EmptyCaller = new();

    internal static TokenTable<TPack> CreateAssetScope(ScriptableObject asset,
        IReadOnlyList<NamedFormulaSlot> bindings, TokenTable<TPack> caller)
    {
        var table = new TokenTable<TPack> { _caller = caller };
        foreach (var parameter in AssetGraphSchema.ReadCached(asset))
            table.Register(parameter.Name, parameter.ResultType, parameter.Slot);
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

    /// <summary>登記一個具名端點。同名同型後到者不覆蓋——重複由 Verify 擋，runtime 取先到的那個。</summary>
    public void Register(GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        Register(endpoint.Name, endpoint.ResultType, endpoint.Slot);
    }

    private void Register(string name, Type resultType, FormulaSlotBase slot)
    {
        if (string.IsNullOrEmpty(name) || resultType == null || slot == null) return;
        var key = (resultType, name);
        if (!_slots.ContainsKey(key)) _slots[key] = slot;
    }

    /// <summary>這個名稱在 T 型別下求得出值嗎。呼叫端的覆蓋優先，其次才是本圖登記的端點。</summary>
    public bool Has<T>(string key)
        => HasOverride<T>(key) || (!string.IsNullOrEmpty(key) && _slots.ContainsKey((typeof(T), key)));

    internal bool HasOverride<T>(string key)
        => !string.IsNullOrEmpty(key)
        && _overrides.TryGetValue(key, out var binding)
        && binding?.Slot is IFormulaSlot<T, TPack>;

    internal async UniTask<T> ResolveOverride<T>(string key, TPack pack)
    {
        if (!HasOverride<T>(key)) return default;
        var cycleKey = (typeof(T), key);
        if (!_overrideInFlight.Add(cycleKey))
        {
            Debug.LogWarning($"[TokenTable] 資產參數 '{key}' 發生循環覆蓋");
            return default;
        }
        try
        {
            // 綁定住在呼叫端的圖，所以用呼叫端的表求值。沒有呼叫端表（頂層傳了 null）時用空表：
            // 綁定自己的子樹照算，只有它裡面的具名查詢查無值，不會整條回 default(T)。
            return await ((IFormulaSlot<T, TPack>)_overrides[key].Slot).Evaluate(pack, _caller ?? EmptyCaller);
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
        // 字串查詢與欄位取值必須給同一個答案：資產參數被呼叫端覆蓋時，兩條路徑都要拿到覆蓋值，
        // 否則資產內部用名字查自己的參數會靜默拿到內部端點的值。
        if (HasOverride<T>(key)) return await ResolveOverride<T>(key, pack);

        if (string.IsNullOrEmpty(key)) return default;
        var ck = (typeof(T), key);
        if (!_slots.TryGetValue(ck, out var slot) || slot is not IFormulaSlot<T, TPack> typed) return default;

        if (_inFlight.Contains(ck))
        {
            Debug.LogWarning($"[TokenTable] {typeof(T).Name} 變數 '{key}' 發生循環參照");
            return default;
        }

        _inFlight.Add(ck);
        try
        {
            // 端點沒接來源時，這裡回的就是它自己的常數值。
            return await typed.Evaluate(pack, this);
        }
        finally
        {
            _inFlight.Remove(ck);
        }
    }
}

}
