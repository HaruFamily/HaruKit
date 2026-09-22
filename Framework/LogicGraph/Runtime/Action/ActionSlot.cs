namespace HaruFamily.Framework.LogicGraph
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

/// <summary>
/// 動作欄位，同時是節點圖的頭端（發出點）：自己是一個固定節點，只有一個「來源」接點。
/// 具體 Action、Action 資產都是接在它右邊的 <see cref="GraphNode"/>，換來源不動頭端。
/// </summary>
[Serializable]
public class ActionSlot<TPack> : ActionSlotBase
{
    // 停用用「反向旗標」：既有資產沒有這個欄位，反序列化後 false = 啟用，不會整批被關掉。
    [SerializeField]
    private bool _disabled;

    [SerializeField]
    private string _label;

    [SerializeField, HideInInspector]
    private string _id;

    [SerializeField, HideInInspector]
    private Vector2 _pos;

    // false 代表使用者沒有手動擺過位置，交給自動排版。
    [SerializeField, HideInInspector]
    private bool _hasPos;

    [SerializeReference]
    private GraphNode _node;

    // 候選節點池：本頭端專用，不執行、不參與驗證，可反覆接回來源做 A/B 測試。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    [NonSerialized] private bool _loggedMismatch;

    public ActionSlot() { }

    /// <summary>程式建構（測試與程式組圖路徑）：直接包一個具體 Action。</summary>
    public ActionSlot(ActionBase<TPack> action)
    {
        _node = new GraphNode(action);
    }

    /// <summary>停用中的動作不執行，但設定完整保留（企劃暫時關掉某段效果用）。</summary>
    public override bool Disabled { get => _disabled; set => _disabled = value; }

    /// <summary>同名動作的區分標籤，只影響顯示。</summary>
    public override string Label { get => _label; set => _label = value; }

    /// <summary>頭端節點的穩定識別碼，焦點與座標都靠它。</summary>
    public override string Id => _id;

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public override string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    /// <summary>複製動作後必須換新識別碼，否則兩個頭端共用同一筆座標與焦點。</summary>
    public override void ResetId() => _id = null;

    public override Vector2 Pos
    {
        get => _pos;
        set { _pos = value; _hasPos = true; }
    }

    public override bool HasPos => _hasPos;

    public override void ClearPos() { _hasPos = false; _pos = Vector2.zero; }

    /// <summary>目前接的來源節點。null＝空槽。</summary>
    public override GraphNode Node => _node;

    public override void SetNode(GraphNode node) => _node = node;

    /// <summary>本頭端的候選節點池。僅視覺化編輯器使用。</summary>
    public override List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    public override Type PackType => typeof(TPack);
    public override Type BodyBaseType => typeof(ActionBase<TPack>);
    public override Type AssetBaseType => typeof(ActionAssetBase<TPack>);

    public override bool AcceptsBody(GraphNodeContent body) => body is ActionBase<TPack>;

    public override bool AcceptsAsset(ScriptableObject asset) => asset is ActionAssetBase<TPack>;

    /// <summary>動作欄位不能接具名Token：Token是公式端點，求值不執行副作用。</summary>
    public override bool AcceptsToken(GraphToken endpoint) => false;

    /// <summary>透過此入口執行子動作，沿用收到的 pack 與 tokens，以保留停用、資產作用域與執行觀察處理。</summary>
    /// <remarks>節點本體的例外與取消會向呼叫端傳遞；非同步工作應沿用 tokens.CancellationToken。</remarks>
    public async UniTask Execute(TPack pack, TokenTable<TPack> tokens)
    {
        if (_disabled) return;

        // 停用與空槽走同一條路：都不執行。企劃可以關掉一段動作而不必拆線。
        if (_node == null || _node.Disabled) return;

        using var visit = tokens?.EnterNode(_node);
        try
        {
            if (visit != null) await visit.WaitAsync();
            await ExecuteCore(pack, tokens);
            visit?.Complete();
        }
        catch (OperationCanceledException) { visit?.Cancel(); throw; }
        catch (Exception exception) { visit?.Fail(exception); throw; }
    }

    private async UniTask ExecuteCore(TPack pack, TokenTable<TPack> tokens)
    {
        switch (_node.Kind)
        {
            case NodeKind.Inline:
            {
                var action = _node.GetBody<ActionBase<TPack>>();
                if (action == null) { Mismatch("動作"); return; }
                await action.Execute(pack, tokens);
                return;
            }
            case NodeKind.Asset:
            {
                var asset = _node.GetAsset<ActionAssetBase<TPack>>();
                if (asset == null) { Mismatch("動作資產"); return; }
                await asset.Execute(pack, tokens, _node, _node.Bindings);
                return;
            }
            default:
                return;   // Empty：編輯中的空節點，存檔驗證會擋，runtime 跳過續跑。
        }
    }

    private void Mismatch(string what)
    {
        if (_loggedMismatch) return;
        _loggedMismatch = true;
        Debug.LogWarning($"[LogicGraph] 動作欄位接的{what}為空或型別不符，已跳過。");
    }
}

}
