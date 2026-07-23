namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
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
#if UNITY_EDITOR
    [OnValueChanged("NotifySubscribers", IncludeChildren = true)]
#endif
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

    private void NotifySubscribers()
    {
        if (_subscribers == null) return;
        _subscribers.RemoveAll(s => s == null);
        foreach (var so in _subscribers)
        {
            if (so is IActionSystemOwner owner)
            {
                owner.MarkActionSystemDirty();
                EditorUtility.SetDirty(so);
            }
        }
    }

    private bool _isSoleSelected => Selection.activeObject == this;

    [ShowInInspector, ShowIf("_isSoleSelected")]
    [LabelText("已註冊的 Owner")]
    [ListDrawerSettings(IsReadOnly = true, ShowFoldout = false, DraggableItems = false)]
    private List<OwnerRow> OwnerRows
    {
        get => OwnerRow.Build(_subscribers);
        set { /* no-op：保留 setter 讓 Odin 不把整段視為 read-only，子元素 [Button] 才能 enable */ }
    }

    [ShowIf("_isSoleSelected")]
    [Button("全部驗證", ButtonSizes.Medium)]
    private void VerifyAllOwners() => OwnerRow.VerifyAll(_subscribers);
#endif
}

}
