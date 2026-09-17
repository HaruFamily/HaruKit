namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;

public enum HGFocusKind
{
    None,
    /// <summary>
    /// 單獨一個動作。右欄動作清單移除後已經沒有路徑會設定它；
    /// 保留是因為 HGValidator 仍用它當「這則問題屬於哪個動作」的標籤（見 HGReport.CountFor）。
    /// </summary>
    Action,
    /// <summary>下鑽進一個共用資產的內部。</summary>
    Asset,

    /// <summary>全部 root 共用的那張畫布；每個 root 是一顆節點，可自由擺位與共用來源。</summary>
    // 排在最後而不是接在 Action 後面：其他 Kind 的數值不動，既有比較與紀錄不受影響。
    Root,

    [Obsolete("Use Root.")]
    Timing = Root,

    /// <summary>下鑽進一個具名Token的內部。端點是頭端，它的取值欄位就是這張畫布唯一的來源接點。</summary>
    Token,
}

/// <summary>中欄目前在編輯什麼。切焦點就是換一份節點圖。</summary>
public class HGFocus : IOrphanPool
{
    public HGFocusKind Kind = HGFocusKind.None;

    // Action 焦點。畫布不再切到單一動作，但 HGValidator 仍用它當「這則問題屬於哪個動作」的標籤。
    // 型別是 object：識別值是什麼由 IGraphDocument 的實作決定，這裡只做相等比較與顯示。
    private object rootKey;
    public object RootKey { get => rootKey; set => rootKey = value; }
    [Obsolete("Use RootKey.")]
    public object Timing { get => rootKey; set => rootKey = value; }
    public IList ActionList;
    public int ActionIndex = -1;
    public GraphSlotBase ActionSlot;

    // Root 焦點：文件工作副本本身（＝HGModel.Data）。
    // 群組清單每次都從它現讀，新增／刪除 root 不必回頭修焦點。
    public object Data;

    // 資產焦點：HostSlot 是合成出來的槽，內容＝資產內容的工作副本
    public UnityEngine.Object AssetObject;
    public GraphSlotBase AssetHostSlot;
    public List<GraphNode> AssetOrphans;

    // 資產的Token工作副本。與 AssetOrphans 同一次 DeepCopy 出來，兩邊指向同一批端點物件。
    public List<GraphToken> AssetTokens;

    /// <summary>
    /// 目前在編輯的Token端點。Owner 的Token走 <see cref="HGFocusKind.Token"/>；
    /// 資產的Token仍留在 Asset 焦點裡（只是換一顆頭端），資產的存檔交易因此完全不受影響。
    /// </summary>
    public GraphToken Token;

    /// <summary>資產焦點的候選工作副本。存檔才覆寫資產，取消直接丟棄。Token子焦點的候選在端點自己身上。</summary>
    public List<GraphNode> Orphans => Kind == HGFocusKind.Asset && Token == null ? AssetOrphans : null;

    /// <summary>
    /// 這個焦點畫成 HEAD 的東西。多數焦點只有一個 Slot 頭端；Root 焦點則是每個
    /// root 各一顆，它們不是 Slot，建圖時走一般物件節點那條路。
    /// 群組清單現讀不快取：新增或刪除 root 不需要重建焦點。
    /// </summary>
    public List<object> Roots
    {
        get
        {
            var roots = new List<object>();
            if (Kind == HGFocusKind.Root)
            {
                if ((Data as IGraphDocument)?.Roots is IList groups)
                    foreach (var g in groups)
                        if (g != null) roots.Add(g);
                return roots;
            }

            // 端點的取值欄位就是這張畫布的頭端；沒接來源時它自己是常數，畫面上仍是同一顆 HEAD。
            object single = Kind switch
            {
                HGFocusKind.Action => ActionSlot,
                HGFocusKind.Asset => Token != null ? Token.Slot : AssetHostSlot,
                HGFocusKind.Token => Token?.Slot,
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
                case HGFocusKind.Action:
                    return ActionHeadTitle(ActionSlot);
                case HGFocusKind.Root:
                    return $"全部{HGGraph.RootNoun(Data as IGraphDocument)}";
                case HGFocusKind.Asset:
                    if (Token != null) return $"資產 {AssetObject?.name} ／ Token {Token.Name ?? "（未命名）"}";
                    return AssetObject != null ? $"資產 {AssetObject.name}" : "資產";
                case HGFocusKind.Token:
                    return Token != null ? $"Token {Token.Name ?? "（未命名）"}" : "Token";
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
                case HGFocusKind.Action:
                    return ActionHeadTitle(ActionSlot);
                // Root 焦點有多顆 HEAD，名字由每個 root 自己的 key 決定，不從焦點來。
                case HGFocusKind.Root:
                    return "";
                case HGFocusKind.Asset:
                    if (Token != null) return Token.Name ?? "（未命名 Token）";
                    return AssetObject != null ? AssetObject.name : "（未指定資產）";
                case HGFocusKind.Token:
                    return Token?.Name ?? "（未命名 Token）";
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
                case HGFocusKind.Action:
                    return "act:" + HGReflect.EnsureSlotEditorId(ActionSlot);
                // 保留既有 focus id，避免既有 session 視圖狀態失去對應；每顆 root HEAD 的 id 走 HGGraph.GroupHeadId。
                case HGFocusKind.Root:
                    return "tim:*";
                case HGFocusKind.Asset:
                    string asset = AssetObject != null
                        ? "ast:" + UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(AssetObject))
                        : "ast:?";
                    return Token != null ? asset + "/var:" + Token.EnsureId() : asset;
                // 用端點的 Guid 而不是名字：改名不會換掉焦點 id，座標與 EditorPrefs 記憶都留著。
                case HGFocusKind.Token:
                    return Token != null ? "var:" + Token.EnsureId() : "var:?";
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// 候選池掛在頭端上，切焦點時視窗用它指定 HGModel.OrphanHead。
    /// Root 畫布沒有單一頭端，候選就掛在整份文件上——那張畫布的主人本來就是它。
    /// </summary>
    public object Head => Kind switch
    {
        HGFocusKind.Action => ActionSlot,
        HGFocusKind.Root => Data,
        // 端點自己就是頭端：Id、座標與候選池都在它身上，跟 ActionSlot 同一套。
        HGFocusKind.Asset => Token != null ? Token : (object)this,
        HGFocusKind.Token => Token,
        _ => null,
    };

    /// <summary>
    /// HEAD 的座標主人。Token畫布是端點自己；**資產本體畫布是資產 SO**——它的 HEAD 容器槽是每次進來
    /// 現做的，記在上面等於不記。其餘焦點回 null，由 HGGraph 退回用 root slot 當載體。
    /// </summary>
    public object HeadCarrier => Token != null
        ? Token
        : Kind == HGFocusKind.Asset ? AssetObject : null;

    public bool SameAs(HGFocus other)
    {
        if (other == null || other.Kind != Kind) return false;
        switch (Kind)
        {
            case HGFocusKind.Action: return ReferenceEquals(ActionSlot, other.ActionSlot);
            // Root 畫布只有一張，同 Kind 就是同一個焦點。
            case HGFocusKind.Root: return true;
            case HGFocusKind.Asset:
                return AssetObject == other.AssetObject && ReferenceEquals(Token, other.Token);
            case HGFocusKind.Token: return ReferenceEquals(Token, other.Token);
            default: return true;
        }
    }

    /// <summary>一個動作頭端的名字：有標籤用標籤，否則用內容型別名。</summary>
    public static string ActionHeadTitle(GraphSlotBase actionSlot)
    {
        string label = HGReflect.GetLabel(actionSlot);
        return string.IsNullOrEmpty(label) ? ActionName(actionSlot) : label;
    }

    public static string ActionName(GraphSlotBase actionSlot)
    {
        if (actionSlot == null) return "（空動作）";
        int useType = HGReflect.UseType(actionSlot);
        if (useType == 1)
        {
            var f = HGReflect.GetFormula(actionSlot);
            return f != null ? HGReflect.TypeName(f.GetType()) : "（未指定動作）";
        }
        if (useType == 2)
        {
            var a = HGReflect.GetAsset(actionSlot);
            return a != null ? a.name : "（未指定資產）";
        }
        return "（未指定動作）";
    }
}

}
