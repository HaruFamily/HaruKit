namespace PinPlugin.ActionSystem
{
using System;
using UnityEngine;

/// <summary>
/// Action 與 Formula 的非泛型共同 base。只帶視覺化編輯器需要的節點識別碼，不含任何執行語意。
/// </summary>
// 非泛型才能用一個 [SerializeReference] 清單同時收 Action 與各型別 Formula（未連接節點）。
[Serializable]
public abstract class ActionSystemNode
{
    [SerializeField, HideInInspector]
    private string editorNodeId;

    /// <summary>節點在視覺化編輯器內的穩定識別碼，用來記憶座標。空字串代表尚未指派。</summary>
    public string EditorNodeId => editorNodeId;

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public string EnsureEditorNodeId()
    {
        if (string.IsNullOrEmpty(editorNodeId)) editorNodeId = Guid.NewGuid().ToString("N");
        return editorNodeId;
    }

    /// <summary>複製節點後必須換新識別碼，否則兩個節點會共用同一筆座標記錄。</summary>
    public void ResetEditorNodeId() => editorNodeId = null;

#if UNITY_EDITOR
    /// <summary>深層複製整棵節點（含巢狀 Slot 的 SerializeReference 圖）。呼叫端負責重設所有識別碼。</summary>
    public ActionSystemNode EditorClone()
    {
        var copy = ActionSystemDeepCopy.Copy(this);
        if (copy == null) Debug.LogError($"[ActionSystem] 複製節點 {GetType().Name} 失敗。");
        return copy;
    }
#endif
}

/// <summary>視覺化編輯器記住的節點座標。key 為 <see cref="ActionSystemNode.EditorNodeId"/>。</summary>
[Serializable]
public class ActionNodeLayout
{
    public string NodeId;
    public Vector2 Position;

    /// <summary>false 代表使用者沒有手動擺過位置，交給自動排版。</summary>
    public bool HasPosition;

    /// <summary>這個節點屬於哪個焦點（動作或 Token）。未連接節點靠它決定要顯示在哪個編輯區。</summary>
    public string FocusId;
}

}
