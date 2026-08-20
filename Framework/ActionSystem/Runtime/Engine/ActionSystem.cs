namespace HaruFamily.Framework.ActionSystem
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
    [SerializeReference]
    public List<ActionTimingGroup<TTiming, TPack>> ActionGroups = new();

    [SerializeField, HideInInspector]
    private bool _validated;

    // 座標住在各自的頭端（ActionTimingGroup / ActionSlot）與 GraphNode 上，這裡不再有旁路版面表。
    // 候選節點則掛在這裡：所有時機畫在同一張畫布，那張畫布的頭端就是整套 ActionSystem。
    [SerializeReference, HideInInspector]
    private List<GraphNode> _orphans = new();

    /// <summary>時機畫布的候選節點清單。只供編輯，不執行、不驗證。</summary>
    public List<GraphNode> Orphans
    {
        get { _orphans ??= new List<GraphNode>(); return _orphans; }
    }

    // 具名變數的頭端清單：每一筆有自己的畫布與候選池，是這張圖的對外端點。
    [SerializeReference, HideInInspector]
    private List<GraphEndpoint> _endpoints = new();

    /// <summary>本圖的具名變數。從端點開始的整棵子樹都是正式資料。</summary>
    public List<GraphEndpoint> Endpoints
    {
        get { _endpoints ??= new List<GraphEndpoint>(); return _endpoints; }
    }

    [NonSerialized] private bool _hasLoggedValidationFailure;

    public bool IsValidated => _validated;

    public void MarkDirty()
    {
        _validated = false;
        _hasLoggedValidationFailure = false;
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
            || (_orphans?.Count ?? 0) > 0
            || (_endpoints?.Count ?? 0) > 0;
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
        foreach (var endpoint in Endpoints) table.Register(endpoint);
        return table;
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
