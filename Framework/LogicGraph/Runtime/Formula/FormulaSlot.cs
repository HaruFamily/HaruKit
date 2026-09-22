namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>可接常數、公式、資產或 Token 的求值欄位。具體 Slot 型別是公式族的身分，同結果型別仍可分成不同族。</summary>
/// <remarks>固定設定用一般序列化欄位；需要由圖提供數值時使用 Slot。新增族需搭配 Formula 與 FormulaAsset 型別。</remarks>
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
    public override Type BodyBaseType => typeof(TFormula);
    public override Type AssetBaseType => typeof(TAsset);

    public override object DefaultObject
    {
        get => _default;
        set
        {
            if (value is TResult typed) { _default = typed; return; }
            if (value == null && !typeof(TResult).IsValueType) { _default = default; return; }
            Debug.LogWarning($"[LogicGraph] 預設值型別不符（欄位 {typeof(TResult).Name}，傳入 {value?.GetType().Name ?? "null"}），忽略。");
        }
    }

    public override bool AcceptsBody(GraphNodeContent body) => body is TFormula;

    public override bool AcceptsAsset(ScriptableObject asset) => asset is TAsset;

    // 只認同族，不認同結果型別：string 同時有 String 與 Key 兩族，收下別族的Token等於從側門繞過那一族的規則。
    public override bool AcceptsToken(GraphToken endpoint) => endpoint?.Slot?.FamilyType == FamilyType;

    public override bool AcceptsProperty(GraphProperty property) => property?.FamilyType == FamilyType;

    /// <summary>常數模式的值，也是所有來源解析失敗時的保底值。</summary>
    public TResult Default { get => _default; set => _default = value; }

    /// <summary>透過此入口求子公式值，沿用收到的 pack 與 tokens，以保留停用、保底值、Token／資產作用域與執行觀察處理。</summary>
    /// <remarks>每次呼叫重新求值；節點本體的例外與取消會向呼叫端傳遞，不會轉成保底值。</remarks>
    public async UniTask<TResult> Evaluate(TPack pack, TokenTable<TPack> tokens)
    {
        // 空槽回保底值。企劃可以關掉一段公式而不必拆線。
        if (_node == null) return _default;

        // 停用與空槽走同一條路：都回保底值。
        if (_node.Disabled) return _default;

        using var visit = tokens?.EnterNode(_node);
        try
        {
            if (visit != null) await visit.WaitAsync();
            var result = await EvaluateCore(pack, tokens);
            visit?.Complete();
            return result;
        }
        catch (OperationCanceledException) { visit?.Cancel(); throw; }
        catch (Exception exception) { visit?.Fail(exception); throw; }
    }

    private async UniTask<TResult> EvaluateCore(TPack pack, TokenTable<TPack> tokens)
    {
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
                // 資產根停用與 inline 停用相同：採用呼叫欄位自己的保底值，不求值參數。
                if (asset.Root?.Disabled == true) return _default;
                return await asset.Evaluate(pack, tokens, _node, _node.Bindings);
            }
            case NodeKind.Token:
            {
                // 求值一律經過 TokenTable：呼叫端的參數覆蓋與循環偵測都在那裡，
                // 直接呼叫端點的 Slot 會繞過兩者。
                var endpoint = _node.Token;
                if (endpoint == null || string.IsNullOrEmpty(endpoint.Name)) return _default;
                if (tokens == null || !tokens.Has(FamilyType, endpoint.Name)) return _default;
                return await tokens.Resolve<TResult>(FamilyType, endpoint.Name, pack);
            }
            case NodeKind.Property:
            {
                GraphProperty property = _node.Property;
                if (property?.FamilyType != FamilyType || tokens?.Properties == null) return _default;
                return tokens.Properties.Read<TResult>(property);
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
            Debug.LogWarning($"[LogicGraph] {typeof(TResult).Name} 欄位接的{what}為空或型別不符，改用預設值。");
        }
        return _default;
    }
}

}
