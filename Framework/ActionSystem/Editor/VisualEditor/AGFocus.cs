namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;

public enum AGFocusKind
{
    None,
    /// <summary>右欄選到的一個動作。</summary>
    Action,
    /// <summary>左欄選到的一個 Token。</summary>
    Token,
    /// <summary>下鑽進一個共用資產的內部。</summary>
    Asset,
}

/// <summary>中欄目前在編輯什麼。切焦點就是換一份節點圖。</summary>
public class AGFocus
{
    public AGFocusKind Kind = AGFocusKind.None;

    // Action 焦點
    public Enum Timing;
    public IList ActionList;
    public int ActionIndex = -1;
    public object ActionSlot;

    // Token 焦點
    public AGToken Token;

    // 資產焦點：HostSlot 是合成出來的槽，內容＝資產內容的工作副本
    public UnityEngine.Object AssetObject;
    public object AssetHostSlot;

    public object RootSlot => Kind switch
    {
        AGFocusKind.Action => ActionSlot,
        AGFocusKind.Token => Token?.Slot,
        AGFocusKind.Asset => AssetHostSlot,
        _ => null,
    };

    public string Title
    {
        get
        {
            switch (Kind)
            {
                case AGFocusKind.Action:
                    string label = AGReflect.GetLabel(ActionSlot);
                    string name = ActionName(ActionSlot);
                    return string.IsNullOrEmpty(label) ? name : label;
                case AGFocusKind.Token:
                    return Token != null ? $"變數 {Token.Key}" : "變數";
                case AGFocusKind.Asset:
                    return AssetObject != null ? $"資產 {AssetObject.name}" : "資產";
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
                    string label = AGReflect.GetLabel(ActionSlot);
                    return string.IsNullOrEmpty(label) ? ActionName(ActionSlot) : label;
                case AGFocusKind.Token:
                    return Token != null && !string.IsNullOrEmpty(Token.Key) ? "@" + Token.Key : "（未命名變數）";
                case AGFocusKind.Asset:
                    return AssetObject != null ? AssetObject.name : "（未指定資產）";
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
                case AGFocusKind.Token:
                    return Token != null ? TokenFocusId(Token.ResultType, Token.Key) : "tok:?";
                case AGFocusKind.Asset:
                    return AssetObject != null
                        ? "ast:" + UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(AssetObject))
                        : "ast:?";
                default:
                    return "";
            }
        }
    }

    public static string TokenFocusId(Type resultType, string key)
        => $"tok:{resultType?.AssemblyQualifiedName ?? "?"}:{key ?? ""}";

    /// <summary>候選池掛在頭端上，切焦點時視窗用它指定 AGModel.OrphanHead。</summary>
    public object Head => Kind switch
    {
        AGFocusKind.Action => ActionSlot,
        AGFocusKind.Token => Token?.Entry,
        AGFocusKind.Asset => AssetObject,
        _ => null,
    };

    public bool SameAs(AGFocus other)
    {
        if (other == null || other.Kind != Kind) return false;
        switch (Kind)
        {
            case AGFocusKind.Action: return ReferenceEquals(ActionSlot, other.ActionSlot);
            case AGFocusKind.Token: return Token != null && other.Token != null
                && Token.Key == other.Token.Key && Token.ResultType == other.Token.ResultType;
            case AGFocusKind.Asset: return AssetObject == other.AssetObject;
            default: return true;
        }
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
