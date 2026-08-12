namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// 非泛型 base：給 Editor walker 不必反射就能取 _target。
public abstract class FormulaAssetBase : ScriptableObject
{
#if UNITY_EDITOR
    internal abstract object EditorGetTargetObject();
#endif
}

public abstract class FormulaAsset<T, TPack> : FormulaAssetBase
{
    [SerializeReference]
    private FormulaBase<T, TPack> _target;
    public async UniTask<T> Evaluate(TPack pack, TokenCache<TPack> tokens) => _target != null ? await _target.Evaluate(pack, tokens) : default;

#if UNITY_EDITOR
    public void SetTarget(FormulaBase<T, TPack> target) => _target = target;
    internal FormulaBase<T, TPack> EditorGetTarget() => _target;
    internal override object EditorGetTargetObject() => _target;

    [SerializeField, HideInInspector]
    private List<ScriptableObject> _subscribers = new();

    public void RegisterSubscriber(ScriptableObject owner)
    {
        if (owner == null) return;
        if (!(owner is IActionSystemOwner)) return;
        if (_subscribers == null) _subscribers = new List<ScriptableObject>();
        if (_subscribers.Contains(owner)) return;
        _subscribers.Add(owner);
        EditorUtility.SetDirty(this);
    }

    public void UnregisterSubscriber(ScriptableObject owner)
    {
        if (owner == null || _subscribers == null) return;
        if (_subscribers.Remove(owner))
            EditorUtility.SetDirty(this);
    }

    public void ClearSubscribers()
    {
        if (_subscribers == null) return;
        _subscribers.Clear();
        EditorUtility.SetDirty(this);
    }

#endif
}

}
