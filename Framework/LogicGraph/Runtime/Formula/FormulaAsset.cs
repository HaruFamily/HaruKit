namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

// 非泛型 base：給 Editor walker 不必反射就能取根節點與候選池。
public abstract class FormulaAssetBase : ScriptableObject, IFormulaGraphAsset, IPropertyOwner, IGraphViewStateOwner
{
    /// <summary>本資產的候選節點清單。僅視覺化編輯器使用。</summary>
    public abstract List<GraphNode> Orphans { get; }
    public abstract object ContentObject { get; }
    public abstract List<GraphToken> Tokens { get; }
    public abstract List<GraphProperty> Properties { get; }

    /// <summary>根內容的載體。根節點的 Id／座標／備註都住在它身上，和圖上其他節點同一套。</summary>
    public abstract GraphNode Root { get; }

    /// <summary>公式資產的結果型別。</summary>
    public abstract Type ResultType { get; }
    public abstract FormulaSlotBase Input { get; }
    public abstract void SetInput(FormulaSlotBase input);
    public abstract string ConfigurationError { get; }

    // HEAD 座標屬於資產本體，與可替換的根輸入分開保存。
    [SerializeField, HideInInspector]
    private Vector2 _headPos;

    [SerializeField, HideInInspector]
    private bool _hasHeadPos;

    /// <summary>資產畫布 HEAD 的座標。編輯器用；設值即視為「使用者手動擺過」。</summary>
    public Vector2 Pos
    {
        get => _headPos;
        set { _headPos = value; _hasHeadPos = true; }
    }

    /// <summary>false 代表沒有手動擺過位置，交給自動排版。</summary>
    public bool HasPos => _hasHeadPos;

    public void ClearPos() { _hasHeadPos = false; _headPos = Vector2.zero; }

    // 資產畫布的收合版面。與 HEAD 座標同樣記在資產本體上，只給編輯器用。
    [SerializeField, HideInInspector]
    private GraphViewState _viewState = new();

    GraphViewState IGraphViewStateOwner.ViewState => _viewState ??= new GraphViewState();

#if UNITY_EDITOR
    internal abstract object EditorGetTargetObject();
#endif
}

public abstract class FormulaAssetBase<T, TPack> : FormulaAssetBase
{
    public sealed override Type ResultType => typeof(T);

    // 候選節點池：本資產編輯區專用，不參與求值與驗證。資產是獨立交易，候選不可寫進 Owner。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    // 本資產的具名Token，同時就是它對呼叫端的參數介面。
    [SerializeReference, HideInInspector]
    private List<GraphToken> _endpoints = new();

    [SerializeReference, HideInInspector]
    private List<GraphProperty> _properties = new();

    public UniTask<T> Evaluate(TPack pack, TokenTable<TPack> caller, IReadOnlyList<NamedFormulaSlot> bindings = null)
        => Evaluate(pack, caller, null, bindings);

    internal async UniTask<T> Evaluate(TPack pack, TokenTable<TPack> caller, GraphNode callerNode,
        IReadOnlyList<NamedFormulaSlot> bindings)
    {
        var root = Root;
        if (root?.Disabled == true) return default;
        if (ConfigurationError is string error) throw new InvalidOperationException(error);
        var input = (IFormulaSlot<T, TPack>)Input;
        var tokens = TokenTable<TPack>.CreateAssetScope(this, callerNode, bindings, caller);
        tokens.CancellationToken.ThrowIfCancellationRequested();
        var result = await input.Evaluate(pack, tokens);
        tokens.CancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public override List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public override List<GraphToken> Tokens
    {
        get { _endpoints ??= new List<GraphToken>(); return _endpoints; }
    }

    public override List<GraphProperty> Properties
    {
        get { _properties ??= new List<GraphProperty>(); return _properties; }
    }

    public override GraphNode Root => Input?.Node;
    public override object ContentObject => Input;

#if UNITY_EDITOR
    public void SetRoot(GraphNode root) => Input.SetNode(root);
    public void SetTarget(GraphNodeContent target) => SetRoot(target == null ? null : new GraphNode(target));
    internal override object EditorGetTargetObject() => Input;

    // 「誰引用我」不存在資產身上：那是衍生資料，存了就會過期。編輯器要用時從 HGReferenceIndex 現算。
#endif
}

/// <summary>公式資產的根輸入與結果；輸入可為一般 Slot 或 Union。</summary>
public abstract class FormulaAsset<TResult, TInput, TPack> : FormulaAssetBase<TResult, TPack>
    where TInput : FormulaSlotBase, IFormulaSlot<TResult, TPack>, new()
{
    [SerializeReference] private FormulaSlotBase input = new TInput();
    public sealed override FormulaSlotBase Input => input;
    public TInput RootInput => input as TInput;
    public sealed override string ConfigurationError => input is not TInput ? $"資產根輸入必須為 {typeof(TInput).Name}。" : null;

    public sealed override void SetInput(FormulaSlotBase value)
    {
        if (value is not TInput typed) throw new ArgumentException($"Asset input must be {typeof(TInput).Name}.", nameof(value));
        input = typed;
    }
}

}
