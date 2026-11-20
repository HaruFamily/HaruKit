using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;

namespace HaruKitEvi.Fixtures.Logic
{
    /// <summary>測試用時機。標籤同時測 enum 成員的 [HGLabel]。</summary>
    public enum EviTiming
    {
        [HGLabel("開始")] OnStart,
        [HGLabel("每次觸發")] OnTick,
        [HGLabel("結束")] OnEnd,
    }

    /// <summary>測試用執行上下文：呼叫端給的輸入，加上一份可檢查的輸出紀錄。</summary>
    public sealed class EviPack
    {
        public int Seed;
        public string Name = "Evi";
        public readonly List<string> Log = new List<string>();
    }

    // ---- Int 族 ----
    public abstract class EviIntFormula : FormulaBase<int, EviPack> { }

    [Serializable]
    public sealed class EviIntToken : FormulaToken<int, EviIntSlot, EviPack> { }

    [Serializable]
    [HGKind("Int", "Evi")]
    public sealed class EviIntSlot : FormulaSlot<int, EviIntFormula, EviIntToken, EviIntAsset, EviPack>
    {
        public EviIntSlot() { }
        public EviIntSlot(int value) : base(value) { }
    }

    // ---- Bool 族 ----
    public abstract class EviBoolFormula : FormulaBase<bool, EviPack> { }

    [Serializable]
    public sealed class EviBoolToken : FormulaToken<bool, EviBoolSlot, EviPack> { }

    [Serializable]
    [HGKind("Bool", "Evi")]
    public sealed class EviBoolSlot : FormulaSlot<bool, EviBoolFormula, EviBoolToken, EviBoolAsset, EviPack>
    {
        public EviBoolSlot() { }
        public EviBoolSlot(bool value) : base(value) { }
    }

    // ---- String 族 ----
    public abstract class EviStringFormula : FormulaBase<string, EviPack> { }

    [Serializable]
    public sealed class EviStringToken : FormulaToken<string, EviStringSlot, EviPack> { }

    [Serializable]
    [HGKind("Text", "Evi")]
    public sealed class EviStringSlot : FormulaSlot<string, EviStringFormula, EviStringToken, EviStringAsset, EviPack>
    {
        public EviStringSlot() { }
        public EviStringSlot(string value) : base(value) { }
    }
}
