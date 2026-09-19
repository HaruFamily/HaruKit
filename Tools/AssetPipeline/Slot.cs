using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 具名Token求值的遞迴防線。
    /// </summary>
    // 必須是非泛型的：泛型類別的 static 欄位是「每個封閉型別各一份」，
    // 放進 FormulaSlot<,> 的話 int 那份跟 float 那份互看不見，跨型別的環就抓不到。
    internal static class TokenGuard
    {
        private static readonly HashSet<GraphToken> InFlight = new HashSet<GraphToken>();

        public static bool TryEnter(GraphToken endpoint) => InFlight.Add(endpoint);

        public static void Exit(GraphToken endpoint) => InFlight.Remove(endpoint);
    }

    /// <summary>
    /// 管線的公式欄位：常數、內嵌公式或具名Token三選一，由載體節點決定。
    /// </summary>
    // 對應舊的 FormulaAssetBase：@default → _default，data / assetData 三態 → GraphNode.Kind，
    // formula 欄位 → 載體節點。舊的 AssetSource 模式改成「接一個讀 AssetPipelineSource 的葉節點公式」。
    // 公式家族固定唯一結果與 NullPack；Catalog 結果由 Cell 以相同 TResult 接入。
    [Serializable]
    public abstract class FormulaSlot<TResult, TFormula> : FormulaSlotBase
        where TFormula : FormulaBase<TResult, NullPack>
    {
        [SerializeField]
        protected TResult _default = default;

        [SerializeReference]
        private GraphNode _node;

        [NonSerialized] private bool _loggedMismatch;

        protected FormulaSlot() { }

        protected FormulaSlot(TResult defaultValue)
        {
            _default = defaultValue;
        }

        public override GraphNode Node => _node;

        public override void SetNode(GraphNode node) => _node = node;

        public override Type ResultType => typeof(TResult);

        public override Type PackType => typeof(NullPack);

        public override Type BodyBaseType => typeof(TFormula);

        /// <summary>AssetPipeline 沒有共用資產節點，這一格永遠接不到資產。</summary>
        public override Type AssetBaseType => null;

        public override object DefaultObject
        {
            get => _default;
            set
            {
                if (value is TResult typed) { _default = typed; return; }
                if (value == null && !typeof(TResult).IsValueType) { _default = default; return; }
                Debug.LogWarning($"[AssetPipeline] 預設值型別不符（欄位 {typeof(TResult).Name}，傳入 {value?.GetType().Name ?? "null"}），忽略。");
            }
        }

        public override bool AcceptsBody(GraphNodeContent body)
            => body is CatalogCell cell ? cell.ResultType == typeof(TResult) : body is TFormula;

        public override bool AcceptsAsset(ScriptableObject asset) => false;

        public override bool AcceptsToken(GraphToken endpoint) => endpoint?.Slot?.FamilyType == FamilyType;

        /// <summary>常數模式的值，也是所有來源解析失敗時的保底值。</summary>
        public TResult Default { get => _default; set => _default = value; }

        /// <summary>
        /// 沒接來源、或來源解析失敗時取什麼。預設就是 <see cref="Default"/>。
        /// </summary>
        // 給「常數欄位本身還要再解析一次」的族覆寫（資料夾族的固定值可能是物件也可能是路徑字串）。
        // 不可在這裡面回頭呼叫 Evaluate()，那會變成無窮遞迴。
        protected virtual TResult Fallback() => _default;

        /// <summary>求值。空槽、停用、型別不符一律回保底值，不丟例外。</summary>
        public TResult Evaluate()
        {
            // 空槽與停用走同一條路：都回保底值，企劃可以關掉一段而不必拆線。
            if (_node == null || _node.Disabled) return Fallback();

            switch (_node.Kind)
            {
                case NodeKind.Inline:
                {
                    if (_node.BodyObject is CatalogCell cell)
                    {
                        try
                        {
                            object value = cell.EvaluateObject();
                            if (value == null && default(TResult) is null) return default;
                            return value is TResult typed ? typed : Mismatch("目錄格");
                        }
                        catch (Exception e)
                        {
                            AssetPipeline.ReportFormulaWarning($"目錄格求值失敗：{e.Message}，改用預設值。");
                            return Fallback();
                        }
                    }
                    var formula = _node.GetBody<TFormula>();
                    if (formula == null) return Mismatch("公式");
                    try
                    {
                        return formula.Evaluate(default(NullPack));
                    }
                    catch (Exception e)
                    {
                        AssetPipeline.ReportFormulaWarning($"{formula.GetType().Name} 求值失敗：{e.Message}，改用預設值。");
                        return Fallback();
                    }
                }
                case NodeKind.Token:
                {
                    var endpoint = _node.Token;
                    if (endpoint?.Slot is not FormulaSlot<TResult, TFormula> slot) return Fallback();

                    // 編輯期 Verify 會擋掉環，這條是執行期最後一道防線：遞迴當場回保底值而不是炸堆疊。
                    if (!TokenGuard.TryEnter(endpoint))
                    {
                        AssetPipeline.ReportFormulaWarning($"Token [{endpoint.Name}] 遞迴求值，改用預設值。");
                        return Fallback();
                    }

                    try
                    {
                        return slot.Evaluate();
                    }
                    finally
                    {
                        TokenGuard.Exit(endpoint);
                    }
                }
                default:
                    return Fallback();   // Empty：編輯中的空節點，Verify 會擋，求值走保底值續跑。
            }
        }

        private TResult Mismatch(string what)
        {
            if (!_loggedMismatch)
            {
                _loggedMismatch = true;
                AssetPipeline.ReportFormulaWarning($"{typeof(TResult).Name} 欄位接的{what}為空或型別不符，改用預設值。");
            }
            return Fallback();
        }
    }

    /// <summary>
    /// 管線動作欄位，同時是節點圖的頭端：自己是一顆固定節點，只有一個「來源」接點。
    /// </summary>
    [Serializable]
    public class ActionSlot : ActionSlotBase
    {
        // 反向旗標：既有資料沒有這個欄位時反序列化為 false ＝ 啟用。
        [SerializeField]
        private bool _disabled;

        [SerializeField]
        private string _label;

        [SerializeField, HideInInspector]
        private string _id;

        [SerializeField, HideInInspector]
        private Vector2 _pos;

        [SerializeField, HideInInspector]
        private bool _hasPos;

        [SerializeReference]
        private GraphNode _node;

        [SerializeReference, HideInInspector]
        private List<GraphNode> _orphans = new List<GraphNode>();

        [NonSerialized] private bool _loggedMismatch;

        public ActionSlot() { }

        public ActionSlot(ActionBase action)
        {
            _node = new GraphNode(action);
        }

        public override bool Disabled { get => _disabled; set => _disabled = value; }

        public override string Label { get => _label; set => _label = value; }

        public override string Id => _id;

        public override string EnsureId()
        {
            if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
            return _id;
        }

        public override void ResetId() => _id = null;

        public override Vector2 Pos
        {
            get => _pos;
            set { _pos = value; _hasPos = true; }
        }

        public override bool HasPos => _hasPos;

        public override void ClearPos() { _hasPos = false; _pos = Vector2.zero; }

        public override GraphNode Node => _node;

        public override void SetNode(GraphNode node) => _node = node;

        public override List<GraphNode> Orphans
        {
            get { _orphans ??= new List<GraphNode>(); return _orphans; }
        }

        public override Type PackType => typeof(NullPack);

        public override Type BodyBaseType => typeof(ActionBase);

        public override Type AssetBaseType => null;

        public override bool AcceptsBody(GraphNodeContent body) => body is ActionBase;

        public override bool AcceptsAsset(ScriptableObject asset) => false;

        /// <summary>動作欄位不能接具名Token：Token是公式端點，求值不執行副作用。</summary>
        public override bool AcceptsToken(GraphToken endpoint) => false;

        /// <summary>
        /// 執行這個動作。停用、空槽、型別不符一律跳過。
        /// </summary>
        /// <returns>真的執行了才回 true；跳過回 false，呼叫端才不會把跳過算成成功。</returns>
        internal bool Execute(PipelineActionContext context)
        {
            if (_disabled) return false;
            if (_node == null || _node.Disabled) return false;
            if (_node.Kind != NodeKind.Inline) return false;

            var action = _node.GetBody<ActionBase>();
            if (action == null)
            {
                if (!_loggedMismatch)
                {
                    _loggedMismatch = true;
                    AssetPipeline.ReportFormulaWarning("動作欄位接的內容為空或型別不符，已跳過。");
                }
                return false;
            }

            action.Execute(context);
            return true;
        }

        /// <summary>這個動作在報告裡的顯示名：有標籤用標籤，否則用節點內容的型別名。</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_label)) return _label;
                return _node?.BodyObject?.GetType().Name ?? "(空動作)";
            }
        }
    }
}
