namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

// 非泛型 base：給 Editor walker 不必反射就能取根節點與候選池。
public abstract class FormulaAssetBase : ScriptableObject, IActionSystemAssetGraph
{
    /// <summary>本資產的候選節點清單。僅視覺化編輯器使用。</summary>
    public abstract List<GraphNode> Orphans { get; }
    public abstract object ContentObject { get; }
    public abstract List<GraphEndpoint> Endpoints { get; }

    /// <summary>根內容的載體。根節點的 Id／座標／備註都住在它身上，和圖上其他節點同一套。</summary>
    public abstract GraphNode Root { get; }

    // 資產畫布 HEAD 的座標。HEAD 那個容器槽是編輯期現做的，沒有地方落腳，所以記在資產本體上，
    // 形狀與 ActionSlot／GraphEndpoint／ActionTimingGroup 一致（Pos／HasPos／ClearPos）。
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

#if UNITY_EDITOR
    internal abstract object EditorGetTargetObject();
#endif
}

public abstract class FormulaAsset<T, TPack> : FormulaAssetBase
{
    // 根內容的載體。存 GraphNode 而不是裸公式，根節點才有地方放 Id／座標／備註／停用旗標，
    // 跟圖上其他節點同一套規則；只存裸內容時每次開畫布都要現包一顆，位置永遠留不住。
    [SerializeReference]
    private GraphNode _root;

    // 舊格式：只存裸內容。第一次讀 Root 時就地補上載體，存檔時清空。
    [SerializeReference, HideInInspector]
    private FormulaBase<T, TPack> _target;

    // 候選節點池：本資產編輯區專用，不參與求值與驗證。資產是獨立交易，候選不可寫進 Owner。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    // 本資產的具名變數，同時就是它對呼叫端的參數介面。
    [SerializeReference, HideInInspector]
    private List<GraphEndpoint> _endpoints = new();

    public async UniTask<T> Evaluate(TPack pack, TokenTable<TPack> caller, IReadOnlyList<NamedFormulaSlot> bindings = null)
    {
        var target = Root?.GetBody<FormulaBase<T, TPack>>();
        if (target == null) return default;
        var tokens = TokenTable<TPack>.CreateAssetScope(this, bindings, caller);
        return await target.Evaluate(pack, tokens);
    }

    public override List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public override List<GraphEndpoint> Endpoints
    {
        get { _endpoints ??= new List<GraphEndpoint>(); return _endpoints; }
    }

    /// <summary>根內容的載體。舊格式（裸公式）在這裡就地補上載體，呼叫端只需要認 GraphNode。</summary>
    public override GraphNode Root
    {
        get
        {
            if (_root == null && _target != null) _root = new GraphNode(_target);
            return _root;
        }
    }

    public override object ContentObject => Root?.BodyObject;

#if UNITY_EDITOR
    /// <summary>寫回根節點（視覺化編輯器存檔）。寫進新格式並清掉舊欄位，同一份內容不會有兩個來源。</summary>
    public void SetRoot(GraphNode root)
    {
        _root = root;
        _target = null;
    }

    public void SetTarget(FormulaBase<T, TPack> target) => SetRoot(target == null ? null : new GraphNode(target));
    internal FormulaBase<T, TPack> EditorGetTarget() => Root?.GetBody<FormulaBase<T, TPack>>();
    internal override object EditorGetTargetObject() => Root?.BodyObject;

    // 「誰引用我」不存在資產身上：那是衍生資料，存了就會過期。編輯器要用時從 AGReferenceIndex 現算。
#endif
}

}
