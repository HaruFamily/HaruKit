namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// 非泛型 base：給 Editor walker 不必反射就能取 _target 與候選池。
public abstract class FormulaAssetBase : ScriptableObject, IActionSystemAssetGraph
{
    /// <summary>本資產的候選節點清單。僅視覺化編輯器使用。</summary>
    public abstract List<GraphNode> Orphans { get; }
    public abstract object ContentObject { get; }

#if UNITY_EDITOR
    internal abstract object EditorGetTargetObject();
#endif
}

public abstract class FormulaAsset<T, TPack> : FormulaAssetBase
{
    [SerializeReference]
    private FormulaBase<T, TPack> _target;

    // 候選節點池：本資產編輯區專用，不參與求值與驗證。資產是獨立交易，候選不可寫進 Owner。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    public async UniTask<T> Evaluate(TPack pack, TokenTable<TPack> caller, IReadOnlyList<NamedFormulaSlot> bindings = null)
    {
        if (_target == null) return default;
        var tokens = TokenTable<TPack>.CreateAssetScope(this, bindings, caller);
        return await _target.Evaluate(pack, tokens);
    }

    public override List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public override object ContentObject => _target;

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
