namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
#endif


[Serializable, HideReferenceObjectPicker]
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ActionSystem")]
public partial class ActionSystem<TTiming, TPack, TTokenEntryPack>
where TTiming : Enum
where TTokenEntryPack : TokenEntryPack<TPack>, new()
{
    [SerializeField]
    [TabGroup("ActionSystem_Setting", "設定")]
    [LabelText("進入 Play 時自動驗證")]
    [ToggleLeft]
    public bool AutoVerifyOnPlay = true;

    [SerializeReference, ShowInInspector]
    [TabGroup("ActionSystem", "動作集")]
    [LabelText("動作集")]
    [ListDrawerSettings(NumberOfItemsPerPage = 5)]
    [OnValueChanged("MarkDirty", IncludeChildren = true)]
    public List<ActionTimingGroup<TTiming, TPack>> ActionGroups = new();


    [SerializeReference]
    [TabGroup("ActionSystem", "變數")]
    [HideLabel, InlineProperty]
    [OnValueChanged("MarkDirty", IncludeChildren = true)]
    public TTokenEntryPack TokenEntry = new();

    [SerializeField, HideInInspector]
    private bool _validated;

    [NonSerialized] private bool _hasLoggedValidationFailure;

    public bool IsValidated => _validated;

    private string ValidationButtonLabel => _validated
        ? "✓ 已驗證（重新驗證）"
        : "✗ 未驗證 — 按此驗證";
    private Color ValidationButtonColor => _validated
        ? new Color(0.6f, 1f, 0.6f)
        : new Color(1f, 0.7f, 0.7f);

    public void MarkDirty()
    {
        _validated = false;
        _hasLoggedValidationFailure = false;
    }

    /// <summary>執行期標記已驗證。僅供 mod 匯入路徑：空動作集 / 匯出時已過驗證的圖（_validated 隨 Odin JSON 序列化攜帶）。編輯器內容一律走 Verify()，勿用此繞過驗證閘。</summary>
    public void MarkValidated() => _validated = true;

    /// <summary>深層複製整套動作集（含 ActionGroups / TokenEntry / _validated 的 SerializeReference 多型樹）。Owner 建構期抄給實體用，免共用 SO 被 runtime 改動污染。</summary>
    // 用 Odin 序列化複製：唯一能完整還原 [SerializeReference] 多型樹的途徑（同 ConvertToFormula 的 CreateCopy）
    public ActionSystem<TTiming, TPack, TTokenEntryPack> DeepCopy()
    {
        var copy = Sirenix.Serialization.SerializationUtility.CreateCopy(this) as ActionSystem<TTiming, TPack, TTokenEntryPack>;
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

[Serializable, HideReferenceObjectPicker]
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ActionTimingGroup")]
public class ActionTimingGroup<TTiming, TPack> where TTiming : Enum
{
    [Space]
    [HideLabel]
    [EnumToggleButtons]
    public TTiming Timing;

    [SerializeReference, ShowInInspector]
    [LabelText("動作列表")]
    [TypeSelectorSettings(ShowCategories = true)]
    [ListDrawerSettings(NumberOfItemsPerPage = 5)]
    public List<ActionSlot<TPack>> Actions = new();
}

}
