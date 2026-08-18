namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;

public enum AGFocusKind
{
    None,
    /// <summary>
    /// 單獨一個動作。右欄動作清單移除後已經沒有路徑會設定它；
    /// 保留是因為 AGValidator 仍用它當「這則問題屬於哪個動作」的標籤（見 AGReport.CountFor）。
    /// </summary>
    Action,
    /// <summary>下鑽進一個共用資產的內部。</summary>
    Asset,

    /// <summary>
    /// 全部時機共用的那張畫布：每個 ActionTimingGroup 是一顆節點，可自由擺位，
    /// 跨時機的共用來源因此拉得到線。切時機不再是換畫布。
    /// </summary>
    // 排在最後而不是接在 Action 後面：其他 Kind 的數值不動，既有比較與紀錄不受影響。
    Timing,

    /// <summary>下鑽進一個具名變數的內部。端點是頭端，它的取值欄位就是這張畫布唯一的來源接點。</summary>
    Variable,
}

/// <summary>中欄目前在編輯什麼。切焦點就是換一份節點圖。</summary>
public class AGFocus
{
    public AGFocusKind Kind = AGFocusKind.None;

    // Action 焦點。畫布不再切到單一動作，但 AGValidator 仍用它當「這則問題屬於哪個動作」的標籤。
    public Enum Timing;
    public IList ActionList;
    public int ActionIndex = -1;
    public object ActionSlot;

    // Timing 焦點：ActionSystem 工作副本本身（＝AGModel.Data）。
    // 群組清單每次都從它現讀，新增／刪除時機不必回頭修焦點。
    public object Data;

    // 資產焦點：HostSlot 是合成出來的槽，內容＝資產內容的工作副本
    public UnityEngine.Object AssetObject;
    public object AssetHostSlot;
    public List<GraphNode> AssetOrphans;

    // 資產的變數工作副本。與 AssetOrphans 同一次 DeepCopy 出來，兩邊指向同一批端點物件。
    public List<GraphEndpoint> AssetEndpoints;

    /// <summary>
    /// 目前在編輯的變數端點。Owner 的變數走 <see cref="AGFocusKind.Variable"/>；
    /// 資產的變數仍留在 Asset 焦點裡（只是換一顆頭端），資產的存檔交易因此完全不受影響。
    /// </summary>
    public GraphEndpoint Endpoint;

    /// <summary>資產焦點的候選工作副本。存檔才覆寫資產，取消直接丟棄。變數子焦點的候選在端點自己身上。</summary>
    public List<GraphNode> Orphans => Kind == AGFocusKind.Asset && Endpoint == null ? AssetOrphans : null;

    /// <summary>
    /// 這個焦點畫成 HEAD 的東西。多數焦點只有一個 Slot 頭端；Timing 焦點則是**每個
    /// ActionTimingGroup 各一顆**——它們不是 Slot，建圖時走「一般物件節點」那條路。
    /// 群組清單現讀不快取：新增或刪除時機不需要重建焦點。
    /// </summary>
    public List<object> Roots
    {
        get
        {
            var roots = new List<object>();
            if (Kind == AGFocusKind.Timing)
            {
                if (AGReflect.Get(Data, "ActionGroups") is IList groups)
                    foreach (var g in groups)
                        if (g != null) roots.Add(g);
                return roots;
            }

            // 端點的取值欄位就是這張畫布的頭端；沒接來源時它自己是常數，畫面上仍是同一顆 HEAD。
            object single = Kind switch
            {
                AGFocusKind.Action => ActionSlot,
                AGFocusKind.Asset => Endpoint != null ? Endpoint.Slot : AssetHostSlot,
                AGFocusKind.Variable => Endpoint?.Slot,
                _ => null,
            };
            if (single != null) roots.Add(single);
            return roots;
        }
    }

    public string Title
    {
        get
        {
            switch (Kind)
            {
                case AGFocusKind.Action:
                    return ActionHeadTitle(ActionSlot);
                case AGFocusKind.Timing:
                    return "全部時機";
                case AGFocusKind.Asset:
                    if (Endpoint != null) return $"資產 {AssetObject?.name} ／ 變數 {Endpoint.Name ?? "（未命名）"}";
                    return AssetObject != null ? $"資產 {AssetObject.name}" : "資產";
                case AGFocusKind.Variable:
                    return Endpoint != null ? $"變數 {Endpoint.Name ?? "（未命名）"}" : "變數";
                default:
                    return "尚未選擇編輯對象";
            }
        }
    }

    /// <summary>HEAD 節點的名稱：直接用編輯對象自己的名字，與右欄／左欄清單的顯示規則一致。</summary>
    public string HeadTitle
    {
        get
        {
            switch (Kind)
            {
                case AGFocusKind.Action:
                    return ActionHeadTitle(ActionSlot);
                // 時機焦點有多顆 HEAD，名字由每個群組自己的 Timing 決定，不從焦點來。
                case AGFocusKind.Timing:
                    return "";
                case AGFocusKind.Asset:
                    if (Endpoint != null) return Endpoint.Name ?? "（未命名變數）";
                    return AssetObject != null ? AssetObject.name : "（未指定資產）";
                case AGFocusKind.Variable:
                    return Endpoint?.Name ?? "（未命名變數）";
                default:
                    return "";
            }
        }
    }

    /// <summary>穩定字串：HEAD、候選與獨立參照靠它認得所屬焦點。</summary>
    public string Id
    {
        get
        {
            switch (Kind)
            {
                case AGFocusKind.Action:
                    return "act:" + AGReflect.EnsureSlotEditorId(ActionSlot);
                // 只有一張時機畫布，所以焦點 id 是常數；每顆群組 HEAD 的 id 走 AGGraph.GroupHeadId。
                case AGFocusKind.Timing:
                    return "tim:*";
                case AGFocusKind.Asset:
                    string asset = AssetObject != null
                        ? "ast:" + UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(AssetObject))
                        : "ast:?";
                    return Endpoint != null ? asset + "/var:" + Endpoint.EnsureId() : asset;
                // 用端點的 Guid 而不是名字：改名不會換掉焦點 id，座標與 EditorPrefs 記憶都留著。
                case AGFocusKind.Variable:
                    return Endpoint != null ? "var:" + Endpoint.EnsureId() : "var:?";
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// 候選池掛在頭端上，切焦點時視窗用它指定 AGModel.OrphanHead。
    /// 時機畫布沒有單一頭端，候選就掛在整套 ActionSystem 上——那張畫布的主人本來就是它。
    /// </summary>
    public object Head => Kind switch
    {
        AGFocusKind.Action => ActionSlot,
        AGFocusKind.Timing => Data,
        // 端點自己就是頭端：Id、座標與候選池都在它身上，跟 ActionSlot 同一套。
        AGFocusKind.Asset => Endpoint != null ? Endpoint : (object)this,
        AGFocusKind.Variable => Endpoint,
        _ => null,
    };

    public bool SameAs(AGFocus other)
    {
        if (other == null || other.Kind != Kind) return false;
        switch (Kind)
        {
            case AGFocusKind.Action: return ReferenceEquals(ActionSlot, other.ActionSlot);
            // 時機畫布只有一張，同 Kind 就是同一個焦點。
            case AGFocusKind.Timing: return true;
            case AGFocusKind.Asset:
                return AssetObject == other.AssetObject && ReferenceEquals(Endpoint, other.Endpoint);
            case AGFocusKind.Variable: return ReferenceEquals(Endpoint, other.Endpoint);
            default: return true;
        }
    }

    /// <summary>一個動作頭端的名字：有標籤用標籤，否則用內容型別名。</summary>
    public static string ActionHeadTitle(object actionSlot)
    {
        string label = AGReflect.GetLabel(actionSlot);
        return string.IsNullOrEmpty(label) ? ActionName(actionSlot) : label;
    }

    public static string ActionName(object actionSlot)
    {
        if (actionSlot == null) return "（空動作）";
        int useType = AGReflect.UseType(actionSlot);
        if (useType == 1)
        {
            var f = AGReflect.GetFormula(actionSlot);
            return f != null ? AGReflect.TypeName(f.GetType()) : "（未指定動作）";
        }
        if (useType == 2)
        {
            var a = AGReflect.GetAsset(actionSlot);
            return a != null ? a.name : "（未指定資產）";
        }
        return "（未指定動作）";
    }
}

}
