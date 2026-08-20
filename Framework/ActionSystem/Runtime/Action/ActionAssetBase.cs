namespace HaruFamily.Framework.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ActionAssetBase<TPack> : ScriptableObject, IActionSystemAssetGraph
{
    // 根內容的載體。存 GraphNode 而不是裸 Action，根節點才有地方放 Id／座標／備註／停用旗標，
    // 跟圖上其他節點同一套規則；只存裸內容時每次開畫布都要現包一顆，位置永遠留不住。
    [SerializeReference]
    private GraphNode _root;

    // 舊格式：只存裸內容。第一次讀 Root 時就地補上載體，存檔時清空。
    [SerializeReference, HideInInspector]
    private ActionBase<TPack> _action;

    // 候選節點池：本資產編輯區專用，不執行、不參與驗證。資產是獨立交易，候選不可寫進 Owner。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    // 本資產的具名變數，同時就是它對呼叫端的參數介面。
    [SerializeReference, HideInInspector]
    private List<GraphEndpoint> _endpoints = new();

    // 資產畫布 HEAD 的座標。HEAD 那個容器槽是編輯期現做的，沒有地方落腳，所以記在資產本體上，
    // 形狀與 ActionSlot／GraphEndpoint／ActionTimingGroup 一致（Pos／HasPos／ClearPos）。
    [SerializeField, HideInInspector]
    private Vector2 _headPos;

    [SerializeField, HideInInspector]
    private bool _hasHeadPos;

    public async UniTask Execute(TPack pack, TokenTable<TPack> caller, IReadOnlyList<NamedFormulaSlot> bindings = null)
    {
        var action = Root?.GetBody<ActionBase<TPack>>();
        if (action == null)
        {
            Debug.LogWarning($"[ActionSystem] 動作資產 '{name}' 沒有內容，已跳過。");
            return;
        }
        var tokens = TokenTable<TPack>.CreateAssetScope(this, bindings, caller);
        await action.Execute(pack, tokens);
    }

    /// <summary>本資產的候選節點清單。僅視覺化編輯器使用。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public List<GraphEndpoint> Endpoints
    {
        get { _endpoints ??= new List<GraphEndpoint>(); return _endpoints; }
    }

    /// <summary>根內容的載體。舊格式（裸 Action）在這裡就地補上載體，呼叫端只需要認 GraphNode。</summary>
    public GraphNode Root
    {
        get
        {
            if (_root == null && _action != null) _root = new GraphNode(_action);
            return _root;
        }
    }

    public object ContentObject => Root?.BodyObject;

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
    /// <summary>寫回根節點（視覺化編輯器存檔）。寫進新格式並清掉舊欄位，同一份內容不會有兩個來源。</summary>
    public void SetRoot(GraphNode root)
    {
        _root = root;
        _action = null;
    }

    public void SetTarget(ActionBase<TPack> action) => SetRoot(action == null ? null : new GraphNode(action));
    internal ActionBase<TPack> EditorGetAction() => Root?.GetBody<ActionBase<TPack>>();

    // 「誰引用我」不存在資產身上：那是衍生資料，存了就會過期。編輯器要用時從 AGReferenceIndex 現算。
#endif
}

}
