using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;

namespace HaruKitEvi.Fixtures.Logic
{
    // ================= Int =================

    [Serializable]
    [HGNode("加法", "A + B", "數值")]
    public sealed class EviAdd : EviIntFormula
    {
        [HGLabel("A")] public EviIntSlot A = new EviIntSlot(1);
        [HGLabel("B")] public EviIntSlot B = new EviIntSlot(1);

        protected override async UniTask<int> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int a = await A.Evaluate(pack, tokens);
            int b = await B.Evaluate(pack, tokens);
            return a + b;
        }
    }

    [Serializable]
    [HGNode("乘法", "A × B", "數值")]
    public sealed class EviMultiply : EviIntFormula
    {
        [HGLabel("A")] public EviIntSlot A = new EviIntSlot(1);
        [HGLabel("B")] public EviIntSlot B = new EviIntSlot(2);

        protected override async UniTask<int> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int a = await A.Evaluate(pack, tokens);
            int b = await B.Evaluate(pack, tokens);
            return a * b;
        }
    }

    [Serializable]
    [HGNode("加總", "把清單裡每一項加起來（測公式 Slot 清單）", "數值")]
    public sealed class EviSum : EviIntFormula
    {
        [HGLabel("項目")] public List<EviIntSlot> Values = new List<EviIntSlot>();

        protected override async UniTask<int> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int total = 0;
            foreach (EviIntSlot value in Values)
                if (value != null) total += await value.Evaluate(pack, tokens);
            return total;
        }
    }

    [Serializable]
    [HGNode("Pack 的 Seed", "讀取呼叫端傳入的 Seed", "上下文")]
    public sealed class EviPackSeed : EviIntFormula
    {
        protected override UniTask<int> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
            => UniTask.FromResult(pack != null ? pack.Seed : 0);
    }

    [Serializable]
    [HGNode("隨機整數", "Min 到 Max（含）之間的隨機整數；每次求值都重抽", "數值")]
    public sealed class EviRandomRange : EviIntFormula
    {
        [HGLabel("Min")] public EviIntSlot Min = new EviIntSlot(1);
        [HGLabel("Max")] public EviIntSlot Max = new EviIntSlot(6);

        protected override async UniTask<int> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int min = await Min.Evaluate(pack, tokens);
            int max = await Max.Evaluate(pack, tokens);
            if (max < min) (min, max) = (max, min);
            return UnityEngine.Random.Range(min, max + 1);
        }
    }

    // ================= Bool =================

    public enum EviCompareOp
    {
        [HGLabel(">")] Greater,
        [HGLabel("=")] Equal,
        [HGLabel("<")] Less,
    }

    [Serializable]
    [HGNode("比較", "比較兩個整數", "邏輯")]
    public sealed class EviCompare : EviBoolFormula
    {
        [HGLabel("A")] public EviIntSlot A = new EviIntSlot(0);
        [HGLabel("運算"), HGEnum] public EviCompareOp Op = EviCompareOp.Greater;
        [HGLabel("B")] public EviIntSlot B = new EviIntSlot(0);

        protected override async UniTask<bool> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int a = await A.Evaluate(pack, tokens);
            int b = await B.Evaluate(pack, tokens);
            switch (Op)
            {
                case EviCompareOp.Greater: return a > b;
                case EviCompareOp.Less: return a < b;
                default: return a == b;
            }
        }
    }

    [Serializable]
    [HGNode("反相", "true 變 false，false 變 true", "邏輯")]
    public sealed class EviNot : EviBoolFormula
    {
        [HGLabel("輸入")] public EviBoolSlot Input = new EviBoolSlot(false);

        protected override async UniTask<bool> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
            => !await Input.Evaluate(pack, tokens);
    }

    [Serializable]
    [HGNode("全部成立", "清單裡每一項都是 true 才成立；空清單視為成立", "邏輯")]
    public sealed class EviAll : EviBoolFormula
    {
        [HGLabel("條件")] public List<EviBoolSlot> Conditions = new List<EviBoolSlot>();

        protected override async UniTask<bool> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            foreach (EviBoolSlot condition in Conditions)
                if (condition != null && !await condition.Evaluate(pack, tokens)) return false;
            return true;
        }
    }

    // ================= String =================

    [Serializable]
    [HGNode("串接文字", "把每一段依序接起來", "文字")]
    public sealed class EviConcat : EviStringFormula
    {
        [HGLabel("片段")] public List<EviStringSlot> Parts = new List<EviStringSlot>();

        protected override async UniTask<string> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            var builder = new StringBuilder();
            foreach (EviStringSlot part in Parts)
                if (part != null) builder.Append(await part.Evaluate(pack, tokens));
            return builder.ToString();
        }
    }

    [Serializable]
    [HGNode("整數轉文字", "把整數套進格式字串（{0} 代表數值）", "文字")]
    public sealed class EviIntToText : EviStringFormula
    {
        [HGLabel("數值")] public EviIntSlot Value = new EviIntSlot(0);
        [HGLabel("格式")] public string Format = "{0}";

        protected override async UniTask<string> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
        {
            int value = await Value.Evaluate(pack, tokens);
            return string.IsNullOrEmpty(Format) ? value.ToString() : string.Format(Format, value);
        }
    }

    [Serializable]
    [HGNode("Pack 的名稱", "讀取呼叫端傳入的 Name", "上下文")]
    public sealed class EviPackName : EviStringFormula
    {
        protected override UniTask<string> OnEvaluate(EviPack pack, TokenTable<EviPack> tokens)
            => UniTask.FromResult(pack != null ? pack.Name : string.Empty);
    }
}
