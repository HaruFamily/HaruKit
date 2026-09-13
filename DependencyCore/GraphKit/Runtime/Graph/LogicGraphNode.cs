namespace HaruFamily.Framework.LogicGraph
{
using System;
using UnityEngine;

/// <summary>
/// Action 與 Formula 的非泛型共同 base。節點身分與座標住在 <see cref="GraphNode"/>，這裡只留內容本體。
/// </summary>
// 非泛型才能讓一個 GraphNode 用單一 [SerializeReference] 欄位同時收 Action 與各型別 Formula。
[Serializable]
public abstract class LogicGraphNode
{
#if UNITY_EDITOR
    /// <summary>深層複製整棵節點內容（含巢狀 Slot 的 SerializeReference 圖）。呼叫端負責重設載體識別碼。</summary>
    public LogicGraphNode EditorClone()
    {
        var copy = LogicGraphDeepCopy.Copy(this);
        if (copy == null) Debug.LogError($"[GraphKit] 複製節點 {GetType().Name} 失敗。");
        return copy;
    }
#endif
}

/// <summary>動作節點的形狀基底：有副作用、沒有結果型別。執行方法在子類。</summary>
// 形狀與執行分層：編輯器要的是「這顆是動作、屬於哪個 pack」這種型別關係，不是執行語意。
// 宣告在這一層，編輯器就不必為了問型別關係去引用帶非同步相依的執行層。零欄位，不影響序列化。
[Serializable]
public abstract class ActionNodeBase<TPack> : LogicGraphNode { }

/// <summary>公式節點的形狀基底：帶結果型別。求值方法在子類。</summary>
[Serializable]
public abstract class FormulaNodeBase<TResult, TPack> : LogicGraphNode { }

}
