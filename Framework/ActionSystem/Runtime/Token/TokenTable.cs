namespace HaruFamily.Framework.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次求值期間的具名變數表：（族, 名稱）→ 端點的取值欄位。族＝<see cref="FormulaSlotBase.Kind"/>。
///
/// 名稱唯一性含族，所以同名不同族可以並存（String 的 'X' 與 Key 的 'X' 是兩個變數）；
/// 查詢一律帶族，取不到就當作沒有這個值。
/// 求值不做記憶化：同一個名字被引用兩次就算兩次，非純函式（如 Random）每次都是新的結果。
/// </summary>
public class TokenTable<TPack>
{
    private readonly Dictionary<(Type, string), FormulaSlotBase> _slots = new();
    private readonly HashSet<(Type, string)> _inFlight = new();
    // 覆蓋表也以（族, 名稱）為鍵：資產參數同名不同族可以並存，只用名稱當鍵會讓後到的那個靜默被丟掉。
    private readonly Dictionary<(Type, string), NamedFormulaSlot> _overrides = new();
    private readonly HashSet<(Type, string)> _overrideInFlight = new();
    private TokenTable<TPack> _caller;

    // 沒有登記任何端點、也不會被寫入，所以共用一份就夠。
    private static readonly TokenTable<TPack> EmptyCaller = new();

    internal static TokenTable<TPack> CreateAssetScope(ScriptableObject asset,
        IReadOnlyList<NamedFormulaSlot> bindings, TokenTable<TPack> caller)
    {
        var table = new TokenTable<TPack> { _caller = caller };
        foreach (var parameter in AssetGraphSchema.ReadCached(asset))
            table.Register(parameter.Name, parameter.Slot);
        if (bindings != null)
        {
            foreach (var binding in bindings)
            {
                if (binding == null || !binding.OverrideEnabled || binding.Slot == null || string.IsNullOrEmpty(binding.Name)) continue;
                var key = (binding.Slot.Kind, binding.Name);
                if (!table._overrides.ContainsKey(key)) table._overrides[key] = binding;
            }
        }
        return table;
    }

    /// <summary>登記一個具名端點。同名同族後到者不覆蓋——重複由 Verify 擋，runtime 取先到的那個。</summary>
    public void Register(GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        Register(endpoint.Name, endpoint.Slot);
    }

    private void Register(string name, FormulaSlotBase slot)
    {
        if (string.IsNullOrEmpty(name) || slot == null) return;
        var key = (slot.Kind, name);
        if (!_slots.ContainsKey(key)) _slots[key] = slot;
    }

    /// <summary>這個名稱在這一族下求得出值嗎。呼叫端的覆蓋優先，其次才是本圖登記的端點。</summary>
    public bool Has(Type kind, string key)
        => HasOverride(kind, key) || (kind != null && !string.IsNullOrEmpty(key) && _slots.ContainsKey((kind, key)));

    internal bool HasOverride(Type kind, string key)
        => kind != null && !string.IsNullOrEmpty(key) && _overrides.ContainsKey((kind, key));

    internal async UniTask<T> ResolveOverride<T>(Type kind, string key, TPack pack)
    {
        if (!HasOverride(kind, key)) return default;
        if (_overrides[(kind, key)].Slot is not IFormulaSlot<T, TPack> typed) return default;
        var cycleKey = (kind, key);
        if (!_overrideInFlight.Add(cycleKey))
        {
            Debug.LogWarning($"[TokenTable] 資產參數 '{key}' 發生循環覆蓋");
            return default;
        }
        try
        {
            // 綁定住在呼叫端的圖，所以用呼叫端的表求值。沒有呼叫端表（頂層傳了 null）時用空表：
            // 綁定自己的子樹照算，只有它裡面的具名查詢查無值，不會整條回 default(T)。
            return await typed.Evaluate(pack, _caller ?? EmptyCaller);
        }
        finally
        {
            _overrideInFlight.Remove(cycleKey);
        }
    }

    public bool IsResolving(Type kind, string key)
        => kind != null && !string.IsNullOrEmpty(key) && _inFlight.Contains((kind, key));

    public async UniTask<T> Resolve<T>(Type kind, string key, TPack pack)
    {
        // 字串查詢與欄位取值必須給同一個答案：資產參數被呼叫端覆蓋時，兩條路徑都要拿到覆蓋值，
        // 否則資產內部用名字查自己的參數會靜默拿到內部端點的值。
        if (HasOverride(kind, key)) return await ResolveOverride<T>(kind, key, pack);

        if (kind == null || string.IsNullOrEmpty(key)) return default;
        var ck = (kind, key);
        if (!_slots.TryGetValue(ck, out var slot) || slot is not IFormulaSlot<T, TPack> typed) return default;

        if (_inFlight.Contains(ck))
        {
            Debug.LogWarning($"[TokenTable] {kind.Name} 變數 '{key}' 發生循環參照");
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
