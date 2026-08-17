namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
#endif


[Serializable]
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ActionSystem")]
public partial class ActionSystem<TTiming, TPack>
where TTiming : Enum
{
    [SerializeField]
    public bool AutoVerifyOnPlay = true;

    [SerializeReference]
    public List<ActionTimingGroup<TTiming, TPack>> ActionGroups = new();

    [SerializeField, HideInInspector]
    private bool _validated;

    // 座標住在各自的頭端（ActionTimingGroup / ActionSlot）與 GraphNode 上，這裡不再有旁路版面表。
    // 候選節點則掛在這裡：所有時機畫在同一張畫布，那張畫布的頭端就是整套 ActionSystem。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    /// <summary>時機畫布的候選節點清單。未標註者只供編輯；被標註者是正式對外端點。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    [NonSerialized] private bool _hasLoggedValidationFailure;

    // 名稱 → 被標註的載體。整張圖走一次才建得出來，所以建一次就留著；
    // runtime 的這份是 DeepCopy 出來的實體副本，結構不會再變。
    [NonSerialized] private Dictionary<string, GraphNode> _tokenNodes;

    // 建表時撞名的名字，Verify 讀它報錯。runtime 不看——先到先得已經給出確定結果。
    [NonSerialized] private List<string> _duplicateTokenNames;

    public bool IsValidated => _validated;

    public void MarkDirty()
    {
        _validated = false;
        _hasLoggedValidationFailure = false;
        _tokenNodes = null;
    }

    /// <summary>執行期標記已驗證。僅供程式建立且已自行驗證的空圖或資料；編輯器內容一律走 Verify()，勿用此繞過驗證閘。</summary>
    public void MarkValidated() => _validated = true;

    /// <summary>深層複製整套動作集（含 ActionGroups / Orphans / _validated 的 SerializeReference 多型樹）。Owner 建構期抄給實體用，免共用 SO 被 runtime 改動污染。</summary>
    public ActionSystem<TTiming, TPack> DeepCopy()
    {
        var copy = ActionSystemDeepCopy.Copy(this);
        if (copy == null)
        {
            Debug.LogError("[ActionSystem] DeepCopy 失敗，回傳空動作集。");
            return new ActionSystem<TTiming, TPack>();
        }
        return copy;
    }

    public bool HasContent()
    {
        return (ActionGroups?.Count ?? 0) > 0
            || (_orphans?.Count ?? 0) > 0;
    }

    public TokenTable<TPack> CreateTokenTable()
    {
        if (!_validated)
        {
            if (!_hasLoggedValidationFailure)
            {
                Debug.LogError("[ActionSystem] 尚未通過驗證或資料已變動，請按「驗證」按鈕重新驗證後才能執行。");
                _hasLoggedValidationFailure = true;
            }
            return new TokenTable<TPack>();
        }

        var table = new TokenTable<TPack>();
        foreach (var pair in TokenNodes()) table.Register(pair.Key, pair.Value);
        return table;
    }

    /// <summary>
    /// 名稱 → 被標註的載體。第一次呼叫時走一次全圖，之後沿用。
    /// 走訪範圍含候選池：對外端點通常沒有連入線，本來就住在候選池裡（見 PLAN 的「正式資料邊界」）。
    /// 不下沉到資產內部——資產的標註是它自己的參數，不是這張圖的端點。
    /// </summary>
    public IReadOnlyDictionary<string, GraphNode> TokenNodes()
    {
        if (_tokenNodes != null) return _tokenNodes;

        _tokenNodes = new Dictionary<string, GraphNode>();
        _duplicateTokenNames = null;
        var visited = new HashSet<object>();

        if (ActionGroups != null)
        {
            foreach (var group in ActionGroups)
            {
                if (group?.Actions == null) continue;
                foreach (var slot in group.Actions) CollectTokenNodes(slot, visited);
            }
        }
        foreach (var node in Orphans) CollectTokenNodes(node, visited);

        return _tokenNodes;
    }

    private void CollectTokenNodes(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) return;

        if (node is ActionSlot<TPack> actionSlot)
        {
            CollectTokenNodes(actionSlot.Node, visited);
            // 合併時機畫布前的候選仍序列化在個別 ActionSlot 上，必須維持可求值。
            foreach (var orphan in actionSlot.Orphans) CollectTokenNodes(orphan, visited);
            return;
        }
        if (node is FormulaSlotBase formulaSlot) { CollectTokenNodes(formulaSlot.Node, visited); return; }

        if (node is GraphNode carrier)
        {
            if (carrier.IsToken)
            {
                // 先到先得；重複的留給 Verify 報，runtime 仍然有一個確定的解析結果。
                if (!_tokenNodes.ContainsKey(carrier.TokenName)) _tokenNodes[carrier.TokenName] = carrier;
                else (_duplicateTokenNames ??= new List<string>()).Add(carrier.TokenName);
            }
            // 資產是另一張圖，它的標註是它自己的參數；這裡不下沉。
            if (carrier.Kind == NodeKind.Inline) CollectTokenNodes(carrier.BodyObject, visited);
            foreach (var binding in carrier.Bindings)
                if (binding?.Slot != null) CollectTokenNodes(binding.Slot, visited);
            return;
        }

        if (node is UnityEngine.Object) return;

        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is string) return;
        string ns = type.Namespace;
        if (ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine."))) return;

        if (node is System.Collections.IList list)
        {
            foreach (var item in list) CollectTokenNodes(item, visited);
            return;
        }

        foreach (var field in InstanceFields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            CollectTokenNodes(field.GetValue(node), visited);
        }
    }

    // 衍生型別 GetFields 不會回 base class 的 private 欄位 — 手動沿繼承鏈往上抓 DeclaredOnly。
    private static IEnumerable<System.Reflection.FieldInfo> InstanceFields(Type type)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                                                   | System.Reflection.BindingFlags.Public
                                                   | System.Reflection.BindingFlags.NonPublic
                                                   | System.Reflection.BindingFlags.DeclaredOnly;
        while (type != null && type != typeof(object))
        {
            foreach (var f in type.GetFields(flags)) yield return f;
            type = type.BaseType;
        }
    }

    public List<ActionSlot<TPack>> GetActions(TTiming timing)
    {
        if (ActionGroups == null) return new List<ActionSlot<TPack>>();
        foreach (var g in ActionGroups)
        {
            if (g == null) continue;
            if (EqualityComparer<TTiming>.Default.Equals(g.Timing, timing))
                return g.Actions ?? new List<ActionSlot<TPack>>();
        }
        return new List<ActionSlot<TPack>>();
    }

    public async UniTask TriggerAction(TTiming timing, TPack pack)
    {
        if (!_validated)
        {
            if (!_hasLoggedValidationFailure)
            {
                Debug.LogError("[ActionSystem] 尚未通過驗證或資料已變動，TriggerAction 已跳過。");
                _hasLoggedValidationFailure = true;
            }
            return;
        }
        var tokens = CreateTokenTable();
        var actions = GetActions(timing);
        foreach (var a in actions)
            if (a != null) await a.Execute(pack, tokens);
    }
}

[Serializable]
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ActionTimingGroup")]
public class ActionTimingGroup<TTiming, TPack> where TTiming : Enum
{
    public TTiming Timing;

    [SerializeReference]
    public List<ActionSlot<TPack>> Actions = new();

    // 群組在節點圖上畫成一顆節點（Header＝時機名、本體＝動作清單），所以它自己要記得座標。
    // 和 ActionSlot 同一套形狀：_hasPos 為 false 代表沒手動擺過，交給自動排版。
    [SerializeField, HideInInspector]
    private Vector2 _pos;

    [SerializeField, HideInInspector]
    private bool _hasPos;

    public Vector2 Pos
    {
        get => _pos;
        set { _pos = value; _hasPos = true; }
    }

    public bool HasPos => _hasPos;

    public void ClearPos() { _hasPos = false; _pos = Vector2.zero; }
}

}
