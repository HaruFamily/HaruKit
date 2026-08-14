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
public partial class ActionSystem<TTiming, TPack, TTokenEntryPack>
where TTiming : Enum
where TTokenEntryPack : TokenEntryPack<TPack>, new()
{
    [SerializeField]
    public bool AutoVerifyOnPlay = true;

    [SerializeReference]
    public List<ActionTimingGroup<TTiming, TPack>> ActionGroups = new();


    [SerializeReference]
    public TTokenEntryPack TokenEntry = new();

    [SerializeField, HideInInspector]
    private bool _validated;

    // 座標與候選節點都住在各自的頭端（ActionSlot / TokenEntryBase）與 GraphNode 上，這裡不再有旁路版面表。

    [NonSerialized] private bool _hasLoggedValidationFailure;

    public bool IsValidated => _validated;

    public void MarkDirty()
    {
        _validated = false;
        _hasLoggedValidationFailure = false;
    }

    /// <summary>執行期標記已驗證。僅供程式建立且已自行驗證的空圖或資料；編輯器內容一律走 Verify()，勿用此繞過驗證閘。</summary>
    public void MarkValidated() => _validated = true;

    /// <summary>深層複製整套動作集（含 ActionGroups / TokenEntry / _validated 的 SerializeReference 多型樹）。Owner 建構期抄給實體用，免共用 SO 被 runtime 改動污染。</summary>
    public ActionSystem<TTiming, TPack, TTokenEntryPack> DeepCopy()
    {
        var copy = ActionSystemDeepCopy.Copy(this);
        if (copy == null)
        {
            Debug.LogError("[ActionSystem] DeepCopy 失敗，回傳空動作集。");
            return new ActionSystem<TTiming, TPack, TTokenEntryPack>();
        }
        return copy;
    }

    public bool HasContent()
    {
        return (ActionGroups?.Count ?? 0) > 0
            || TokenEntry.HasContent();
    }

    public TokenCache<TPack> CreateTokenCache()
    {
        if (!_validated)
        {
            if (!_hasLoggedValidationFailure)
            {
                Debug.LogError("[ActionSystem] 尚未通過驗證或資料已變動，請按「驗證」按鈕重新驗證後才能執行。");
                _hasLoggedValidationFailure = true;
            }
            return new TokenCache<TPack>();
        }
        TokenEntry.AssignTokenKeys();
        var t = new TokenCache<TPack>();
        TokenEntry.BuildDict(t);
        return t;
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
        var tokens = CreateTokenCache();
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
}

}
