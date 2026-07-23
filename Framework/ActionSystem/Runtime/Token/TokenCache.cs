namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TokenCache<TPack>
{
    private readonly Dictionary<Type, IDictionary> _slots = new();
    private readonly Dictionary<(Type, string), object> _resolved = new();
    private readonly HashSet<(Type, string)> _inFlight = new();

    public void Register<T>(Dictionary<string, IFormulaSlot<T, TPack>> src)
    {
        if (src == null) return;
        if (!_slots.TryGetValue(typeof(T), out var dict))
        {
            dict = new Dictionary<string, IFormulaSlot<T, TPack>>();
            _slots[typeof(T)] = dict;
        }
        var typed = (Dictionary<string, IFormulaSlot<T, TPack>>)dict;
        foreach (var kv in src) typed[kv.Key] = kv.Value;
    }

    public bool Has<T>(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!_slots.TryGetValue(typeof(T), out var dict)) return false;
        return ((Dictionary<string, IFormulaSlot<T, TPack>>)dict).ContainsKey(key);
    }

    public bool IsResolving<T>(string key)
        => !string.IsNullOrEmpty(key) && _inFlight.Contains((typeof(T), key));

    public async UniTask<T> Resolve<T>(string key, TPack pack)
    {
        if (string.IsNullOrEmpty(key)) return default;
        var ck = (typeof(T), key);
        if (_resolved.TryGetValue(ck, out var hit)) return hit is T t ? t : default;
        if (!_slots.TryGetValue(typeof(T), out var dict)) return default;
        var typed = (Dictionary<string, IFormulaSlot<T, TPack>>)dict;
        if (!typed.TryGetValue(key, out var slot) || slot == null) return default;
        if (_inFlight.Contains(ck))
        {
            Debug.LogWarning($"[TokenCache] {typeof(T).Name} token '{key}' 發生循環參照");
            return default;
        }
        _inFlight.Add(ck);
        var result = await slot.Evaluate(pack, this);
        _inFlight.Remove(ck);
        _resolved[ck] = result;
        return result;
    }
}

}
