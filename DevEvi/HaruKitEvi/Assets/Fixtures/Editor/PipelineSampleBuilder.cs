using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Tools.AssetPipeline;
using HaruKitEvi.PipelineSteps;

namespace HaruKitEvi.FixtureTools
{
    /// <summary>用程式組出 AssetPipeline 範例管線：回報、交易寫入、條件，以及一個預設停用的失敗步驟。</summary>
    internal static class PipelineSampleBuilder
    {
        public static void Fill(AssetPipeline pipeline, PipelineCounter counter)
        {
            pipeline.graph = new Graph();
            var group = new ActionGroup();

            // 1. 回報「計數器目前是 N」
            var counterText = new PipeIntToText();
            Link(counterText.value, new PipeCounterValue { counter = counter });

            var headline = new PipeJoinText();
            headline.parts.Add(new StringSlot("計數器目前是 "));
            var valuePart = new StringSlot();
            Link(valuePart, counterText);
            headline.parts.Add(valuePart);

            var report = new PipeReport();
            Link(report.message, headline);
            group.Actions.Add(Step(report));

            // 2. 經由交易把計數器 +1
            group.Actions.Add(Step(new PipeBumpCounter { counter = counter, amount = new IntSlot(1) }));

            // 3. 計數器大於 3 才回報
            var isGreater = new PipeIsGreater { b = new IntSlot(3) };
            Link(isGreater.a, new PipeCounterValue { counter = counter });

            var branch = new PipeIf();
            Link(branch.condition, isGreater);
            branch.whenTrue = Step(new PipeReport { message = new StringSlot("計數器已經大於 3") });
            branch.whenFalse = Step(new PipeSkip { reason = "計數器還沒大於 3。" });
            group.Actions.Add(Step(branch));

            // 4. 預設停用；啟用後整次執行會失敗，第 2 步的 +1 應該被回復。
            ActionSlot failure = Step(new PipeFailStep());
            failure.Label = "啟用我來測回復";
            failure.Disabled = true;
            group.Actions.Add(failure);

            pipeline.graph.Groups.Add(group);
            pipeline.graph.InvalidateValidation();
        }

        private static GraphNode Node(GraphNodeContent body)
        {
            var node = new GraphNode(body);
            node.EnsureId();
            return node;
        }

        private static ActionSlot Step(ActionBase action)
        {
            var slot = new ActionSlot();
            slot.SetNode(Node(action));
            slot.EnsureId();
            return slot;
        }

        private static void Link(GraphSlotBase slot, GraphNodeContent body) => slot.SetNode(Node(body));
    }
}
