using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    [Serializable]
    [HGNode("記錄訊息", "求出訊息，寫進 Pack.Log 與 Console", "基本")]
    public sealed class EviLog : ActionBase<EviPack>
    {
        [HGLabel("訊息")] public EviStringSlot Message = new EviStringSlot("Hello");

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            string text = await Message.Evaluate(pack, tokens);
            pack?.Log.Add(text);
            Debug.Log("[Evi] " + text);
        }
    }

    [Serializable]
    [HGNode("條件分支", "條件成立跑上面那串，否則跑下面那串", "流程")]
    public sealed class EviBranch : ActionBase<EviPack>, IGraphFlowContainer
    {
        [HGLabel("條件")] public EviBoolSlot Condition = new EviBoolSlot(true);
        [HGLabel("成立")] public List<ActionSlot<EviPack>> WhenTrue = new List<ActionSlot<EviPack>>();
        [HGLabel("不成立")] public List<ActionSlot<EviPack>> WhenFalse = new List<ActionSlot<EviPack>>();

        public GraphFlowDescription DescribeFlow() => new GraphFlowDescription(GraphFlowKind.Conditional,
            new[]
            {
                new GraphFlowSection("true", "成立", GraphFlowSectionRole.WhenTrue, WhenTrue),
                new GraphFlowSection("false", "不成立", GraphFlowSectionRole.WhenFalse, WhenFalse),
            },
            new[] { new GraphFlowInput("condition", "條件", Condition) });

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            bool condition = await Condition.Evaluate(pack, tokens);
            foreach (ActionSlot<EviPack> step in condition ? WhenTrue : WhenFalse)
                if (step != null) await step.Execute(pack, tokens);
        }
    }

    [Serializable]
    [HGNode("重複", "把內容重複執行指定次數（上限 1000）", "流程")]
    public sealed class EviRepeat : ActionBase<EviPack>, IGraphFlowContainer
    {
        private const int MaxCount = 1000;

        [HGLabel("次數")] public EviIntSlot Count = new EviIntSlot(2);
        [HGLabel("內容")] public List<ActionSlot<EviPack>> Body = new List<ActionSlot<EviPack>>();

        public GraphFlowDescription DescribeFlow() => new GraphFlowDescription(GraphFlowKind.Loop,
            new[] { new GraphFlowSection("body", "內容", GraphFlowSectionRole.Body, Body) },
            new[] { new GraphFlowInput("count", "次數", Count) });

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            int count = Mathf.Clamp(await Count.Evaluate(pack, tokens), 0, MaxCount);
            CancellationToken cancellation = tokens != null ? tokens.CancellationToken : CancellationToken.None;
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (ActionSlot<EviPack> step in Body)
                    if (step != null) await step.Execute(pack, tokens);
            }
        }
    }

    [Serializable]
    [HGNode("執行子動作", "只包一個動作欄位（測單值動作槽的內放與拖出）", "流程")]
    public sealed class EviRunChild : ActionBase<EviPack>
    {
        [HGLabel("子動作")] public ActionSlot<EviPack> Child = new ActionSlot<EviPack>();

        protected override UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
            => Child.Execute(pack, tokens);
    }

    [Serializable]
    [HGNode("等待", "等待指定秒數（真實時間）；用來測執行觀察、Hold 與取消", "基本")]
    public sealed class EviWait : ActionBase<EviPack>
    {
        [HGLabel("秒數")] public float Seconds = 0.5f;

        protected override UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            CancellationToken cancellation = tokens != null ? tokens.CancellationToken : CancellationToken.None;
            return UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, Seconds)), DelayType.Realtime,
                PlayerLoopTiming.Update, cancellation);
        }
    }

    [Serializable]
    [HGNode("寫入整數 Property", "把數值寫進選定的 Property，後面的節點可以讀到", "Property")]
    public sealed class EviStoreInt : ActionBase<EviPack>
    {
        [HGLabel("數值")] public EviIntSlot Value = new EviIntSlot(0);
        [HGLabel("輸出")] public PropertySlot<int, EviIntSlot> Output = new PropertySlot<int, EviIntSlot>();

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            int value = await Value.Evaluate(pack, tokens);
            if (!Output.Write(value, tokens)) Debug.LogWarning("[Evi] Property 寫入失敗：目標停用或族不符。");
        }
    }

    [Serializable]
    [HGNode("寫入文字 Property", "把文字寫進選定的 Property", "Property")]
    public sealed class EviStoreText : ActionBase<EviPack>
    {
        [HGLabel("文字")] public EviStringSlot Value = new EviStringSlot(string.Empty);
        [HGLabel("輸出")] public PropertySlot<string, EviStringSlot> Output = new PropertySlot<string, EviStringSlot>();

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            string value = await Value.Evaluate(pack, tokens);
            if (!Output.Write(value, tokens)) Debug.LogWarning("[Evi] Property 寫入失敗：目標停用或族不符。");
        }
    }

    [Serializable]
    [HGNode("故意失敗", "丟出例外，用來測執行失敗的顯示與中止", "基本")]
    public sealed class EviFail : ActionBase<EviPack>
    {
        [HGLabel("訊息")] public string Message = "EviFail：故意失敗";

        protected override UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
            => throw new InvalidOperationException(Message);
    }
}
