using System;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 動作的產出端：指著一顆 Property 節點，執行時替換它的目前值。
    /// </summary>
    // 形狀對應 FormulaSlot<TResult, TFormula>：TResult 是寫進去的值，TSlot 是族身分。
    // 族用讀取端的 Slot 型別而不是 TResult，讀寫兩邊才會落在同一個相容判定上。
    //
    // 只有替換語意：不追加、不合併、不去重、不複製。清單的 Add 這類操作由處理該集合的
    // Action／Formula 表達，不從這一格長出第二種寫入模式。
    [Serializable]
    public abstract class SetPropertySlot<TResult, TSlot> : PropertySlotBase
        where TSlot : FormulaSlotBase
    {
        [SerializeReference]
        private GraphNode node;

        public override GraphNode Node => node;

        public override void SetNode(GraphNode value) => node = value;

        public override Type FamilyType => typeof(TSlot);

        /// <summary>接到的 Property；沒接、停用或族不符時回 null。</summary>
        // 族不符在拉線與落點就擋掉了，這裡是執行期的最後一道：寧可什麼都不寫，
        // 也不要把值塞進型別不對的儲存位置讓讀取端在別的地方才炸開。
        public GraphProperty Target
        {
            get
            {
                GraphNode carrier = node;
                if (carrier == null || carrier.Disabled || carrier.Kind != NodeKind.Property) return null;

                GraphProperty property = carrier.Property;
                return AcceptsProperty(property) ? property : null;
            }
        }

        /// <summary>替換目前值。沒接 Property 就什麼都不做。</summary>
        // 派發收在這裡，對稱 FormulaSlot.Evaluate／ActionSlot.Execute：
        // 呼叫端不必自己取 Target、判 null。
        /// <returns>實際寫入為 true；沒接、停用或族不符為 false。</returns>
        public bool Write(TResult value)
        {
            GraphProperty property = Target;
            if (property == null) return false;

            property.SetValue(value);
            return true;
        }
    }

    /// <summary>寫入 <see cref="ObjectListSlot"/> 族 Property 的產出端。</summary>
    [HGKind("Property")]
    [Serializable]
    public class ObjectListPropertySlot : SetPropertySlot<List<Object>, ObjectListSlot>
    {
    }
}
