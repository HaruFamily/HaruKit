using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
using HaruKitEvi.Fixtures.Logic;

namespace HaruKitEvi.FixtureTools
{
    /// <summary>用程式組出 LogicGraph 範例圖，涵蓋巢狀公式、條件、迴圈與多個時機。</summary>
    internal static class LogicSampleBuilder
    {
        public static void Fill(EviGraphDefinition definition)
        {
            definition.ResetGraph();
            LogicGraph<EviTiming, EviPack> graph = definition.Graph;

            // 開始：Log("Seed + 2 = " + (Seed + 2))、依 Seed 分支、重複三次
            var sum = new EviAdd();
            Link(sum.A, new EviPackSeed());
            sum.B = new EviIntSlot(2);

            var sumText = new EviIntToText();
            Link(sumText.Value, sum);

            var headline = new EviConcat();
            headline.Parts.Add(new EviStringSlot("Seed + 2 = "));
            headline.Parts.Add(Link(new EviStringSlot(), sumText));

            var logHeadline = new EviLog();
            Link(logHeadline.Message, headline);

            var compare = new EviCompare { Op = EviCompareOp.Greater, B = new EviIntSlot(5) };
            Link(compare.A, new EviPackSeed());

            var branch = new EviBranch();
            Link(branch.Condition, compare);
            branch.WhenTrue.Add(Step(new EviLog { Message = new EviStringSlot("Seed 大於 5") }));
            branch.WhenFalse.Add(Step(new EviLog { Message = new EviStringSlot("Seed 不大於 5") }));

            var repeat = new EviRepeat { Count = new EviIntSlot(3) };
            repeat.Body.Add(Step(new EviLog { Message = new EviStringSlot("重複中") }));

            graph.ActionGroups.Add(new ActionTimingGroup<EviTiming, EviPack>
            {
                Timing = EviTiming.OnStart,
                Actions = new List<ActionSlot<EviPack>> { Step(logHeadline), Step(branch), Step(repeat) },
            });

            // 結束：Log("再見，" + Name)
            var farewell = new EviConcat();
            farewell.Parts.Add(new EviStringSlot("再見，"));
            farewell.Parts.Add(Link(new EviStringSlot(), new EviPackName()));

            var logFarewell = new EviLog();
            Link(logFarewell.Message, farewell);

            graph.ActionGroups.Add(new ActionTimingGroup<EviTiming, EviPack>
            {
                Timing = EviTiming.OnEnd,
                Actions = new List<ActionSlot<EviPack>> { Step(logFarewell) },
            });

            graph.InvalidateValidation();
        }

        private static GraphNode Node(GraphNodeContent body)
        {
            var node = new GraphNode(body);
            node.EnsureId();
            return node;
        }

        private static ActionSlot<EviPack> Step(ActionBase<EviPack> action)
        {
            var slot = new ActionSlot<EviPack>();
            slot.SetNode(Node(action));
            slot.EnsureId();
            return slot;
        }

        private static TSlot Link<TSlot>(TSlot slot, GraphNodeContent body) where TSlot : GraphSlotBase
        {
            slot.SetNode(Node(body));
            return slot;
        }
    }
}
