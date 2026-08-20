namespace HaruFamily.Framework.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

// 非泛型 base：Verify 與編輯器不必帶泛型參數就能取節點、結果型別與型別相容判定。
public abstract class FormulaSlotBase
{
    /// <summary>目前接的節點。null＝常數模式，直接用預設值。</summary>
    public abstract GraphNode Node { get; }

    /// <summary>接上或斷開節點。斷開傳 null 即回常數模式。</summary>
    public abstract void SetNode(GraphNode node);

    /// <summary>求值結果型別（TResult）。</summary>
    public abstract Type ResultType { get; }

    /// <summary>公式求值封包型別（TPack）。資產綁定驗證用。</summary>
    public abstract Type PackType { get; }

    /// <summary>不分型別存取預設值，供編輯器輸入框讀寫。</summary>
    public abstract object DefaultObject { get; set; }

    /// <summary>這個欄位能不能接這個內嵌內容。</summary>
    public abstract bool AcceptsBody(ActionSystemNode body);

    /// <summary>這個欄位能不能接這個資產。</summary>
    public abstract bool AcceptsAsset(ScriptableObject asset);

    /// <summary>這個欄位能不能接這個具名變數。結果型別與封包型別都要一致。</summary>
    public abstract bool AcceptsEndpoint(GraphEndpoint endpoint);
}

[Serializable]
public abstract class FormulaSlot<TResult, TAsset, TFormula, TPack> : FormulaSlotBase, IFormulaSlot<TResult, TPack>
    where TAsset : FormulaAsset<TResult, TPack>
    where TFormula : FormulaBase<TResult, TPack>
{
    [SerializeField]
    protected TResult _default = default;

    // 唯一來源：節點決定這個欄位取值方式，不再有 UseType 與三個來源欄位互相打架的可能。
    [SerializeReference]
    private GraphNode _node;

    // 型別不符每個 Slot 只吼一次，避免逐次求值洗版。
    [NonSerialized] private bool _loggedMismatch;

    protected FormulaSlot() { }

    /// <summary>帶初始常數值：常數模式即此值，接了來源時是解析失敗的保底值。</summary>
    protected FormulaSlot(TResult defaultValue)
    {
        _default = defaultValue;
    }

    public override GraphNode Node => _node;

    public override void SetNode(GraphNode node) => _node = node;

    public override Type ResultType => typeof(TResult);
    public override Type PackType => typeof(TPack);

    public override object DefaultObject
    {
        get => _default;
        set
        {
            if (value is TResult typed) { _default = typed; return; }
            if (value == null && !typeof(TResult).IsValueType) { _default = default; return; }
            Debug.LogWarning($"[ActionSystem] 預設值型別不符（欄位 {typeof(TResult).Name}，傳入 {value?.GetType().Name ?? "null"}），忽略。");
        }
    }

    public override bool AcceptsBody(ActionSystemNode body) => body is TFormula;

    public override bool AcceptsAsset(ScriptableObject asset) => asset is TAsset;

    public override bool AcceptsEndpoint(GraphEndpoint endpoint) => endpoint?.Slot is IFormulaSlot<TResult, TPack>;

    /// <summary>常數模式的值，也是所有來源解析失敗時的保底值。</summary>
    public TResult Default { get => _default; set => _default = value; }

    public async UniTask<TResult> Evaluate(TPack pack, TokenTable<TPack> tokens)
    {
        // 空槽回保底值。企劃可以關掉一段公式而不必拆線。
        if (_node == null) return _default;

        // 停用與空槽走同一條路：都回保底值。
        if (_node.Disabled) return _default;

        switch (_node.Kind)
        {
            case NodeKind.Inline:
            {
                var formula = _node.GetBody<TFormula>();
                if (formula == null) return Mismatch("公式");
                return await formula.Evaluate(pack, tokens);
            }
            case NodeKind.Asset:
            {
                var asset = _node.GetAsset<TAsset>();
                if (asset == null) return Mismatch("資產");
                return await asset.Evaluate(pack, tokens, _node.Bindings);
            }
            case NodeKind.Token:
            {
                // 求值一律經過 TokenTable：呼叫端的參數覆蓋與循環偵測都在那裡，
                // 直接呼叫端點的 Slot 會繞過兩者。
                var endpoint = _node.Endpoint;
                if (endpoint == null || string.IsNullOrEmpty(endpoint.Name)) return _default;
                if (tokens == null || !tokens.Has<TResult>(endpoint.Name)) return _default;
                return await tokens.Resolve<TResult>(endpoint.Name, pack);
            }
            default:
                return _default;   // Empty：編輯中的空節點，存檔驗證會擋，runtime 走保底值續跑。
        }
    }

    private TResult Mismatch(string what)
    {
        if (!_loggedMismatch)
        {
            _loggedMismatch = true;
            Debug.LogWarning($"[ActionSystem] {typeof(TResult).Name} 欄位接的{what}為空或型別不符，改用預設值。");
        }
        return _default;
    }
}

}
