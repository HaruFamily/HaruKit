using HaruFamily.DependencyCore.GraphKit;
using HaruKitEvi.Fixtures.Plain;

namespace HaruKitEvi.FixtureTools
{
    /// <summary>用程式組出純 GraphKit 範例圖：巢狀清單、流程容器與公式鏈。</summary>
    internal static class PlainSampleBuilder
    {
        public static void Fill(PlainGraphOwner owner)
        {
            owner.ResetDocument();
            PlainDocument document = owner.Document;
            PlainRoot root = document.Main;

            root.Actions.Add(Step(new PlainNote { Text = "第一步" }));

            var amount = new PlainAdd { A = new PlainValueSlot(1), B = new PlainValueSlot(2) };
            var step = new PlainStep();
            Link(step.Amount, amount);
            step.Child = Step(new PlainNote { Text = "子動作" });
            root.Actions.Add(Step(step));

            var loop = new PlainLoop { Count = new PlainValueSlot(3) };
            loop.Body.Add(Step(new PlainNote { Text = "迴圈內 A" }));
            loop.Body.Add(Step(new PlainNote { Text = "迴圈內 B" }));

            var condition = new PlainCondition();
            condition.WhenTrue.Add(Step(loop));
            condition.WhenFalse.Add(Step(new PlainNote { Text = "不成立時" }));

            var sequence = new PlainSequence();
            sequence.Actions.Add(Step(condition));
            sequence.Actions.Add(Step(new PlainNote { Text = "收尾" }));
            root.Actions.Add(Step(sequence));

            document.InvalidateValidation();
        }

        private static GraphNode Node(GraphNodeContent body)
        {
            var node = new GraphNode(body);
            node.EnsureId();
            return node;
        }

        private static PlainActionSlot Step(PlainAction action)
        {
            var slot = new PlainActionSlot();
            slot.SetNode(Node(action));
            slot.EnsureId();
            return slot;
        }

        private static void Link(GraphSlotBase slot, GraphNodeContent body) => slot.SetNode(Node(body));
    }
}
