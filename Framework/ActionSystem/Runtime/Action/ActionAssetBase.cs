namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract class ActionAssetBase<TPack> : ScriptableObject
{
    [SerializeReference]
    private ActionBase<TPack> _action;
    public async UniTask Execute(TPack pack, TokenCache<TPack> tokens) => await _action.Execute(pack, tokens);

#if UNITY_EDITOR
    public void SetTarget(ActionBase<TPack> action) => _action = action;
    internal ActionBase<TPack> EditorGetAction() => _action;

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
