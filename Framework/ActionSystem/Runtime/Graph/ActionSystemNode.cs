namespace HaruFamily.Framework.ActionSystem
{
using System;
using UnityEngine;

/// <summary>
/// Action 與 Formula 的非泛型共同 base。節點身分與座標住在 <see cref="GraphNode"/>，這裡只留內容本體。
/// </summary>
// 非泛型才能讓一個 GraphNode 用單一 [SerializeReference] 欄位同時收 Action 與各型別 Formula。
[Serializable]
public abstract class ActionSystemNode
{
#if UNITY_EDITOR
    /// <summary>深層複製整棵節點內容（含巢狀 Slot 的 SerializeReference 圖）。呼叫端負責重設載體識別碼。</summary>
    public ActionSystemNode EditorClone()
    {
        var copy = ActionSystemDeepCopy.Copy(this);
        if (copy == null) Debug.LogError($"[ActionSystem] 複製節點 {GetType().Name} 失敗。");
        return copy;
    }
#endif
}

}
