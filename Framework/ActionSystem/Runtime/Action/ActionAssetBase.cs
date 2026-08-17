namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract class ActionAssetBase<TPack> : ScriptableObject, IActionSystemAssetGraph
{
    [SerializeReference]
    private ActionBase<TPack> _action;

    // 候選節點池：本資產編輯區專用，不執行、不參與驗證。資產是獨立交易，候選不可寫進 Owner。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    public async UniTask Execute(TPack pack, TokenTable<TPack> caller, IReadOnlyList<NamedFormulaSlot> bindings = null)
    {
        if (_action == null)
        {
            Debug.LogWarning($"[ActionSystem] 動作資產 '{name}' 沒有內容，已跳過。");
            return;
        }
        var tokens = TokenTable<TPack>.CreateAssetScope(this, bindings, caller);
        await _action.Execute(pack, tokens);
    }

    /// <summary>本資產的候選節點清單。僅視覺化編輯器使用。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public object ContentObject => _action;

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
