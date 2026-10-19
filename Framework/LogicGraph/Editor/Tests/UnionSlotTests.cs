using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
    public sealed class UnionSlotTests
    {
        public sealed class Pack { public int Calls; }
        public enum Timing { Run }
        [Serializable] public abstract class NumberFormula : FormulaBase<int, Pack> { }
        public sealed class NumberAsset : FormulaAsset<int, Pack> { }
        [Serializable] public class NumberSlot : FormulaSlot<int, NumberAsset, NumberFormula, Pack> { }
        [Serializable] public sealed class IdSlot : NumberSlot { }
        [Serializable] public abstract class TextFormula : FormulaBase<string, Pack> { }
        public sealed class TextAsset : FormulaAsset<string, Pack> { }
        [Serializable] public sealed class TextSlot : FormulaSlot<string, TextAsset, TextFormula, Pack> { }
        [Serializable] public abstract class RealFormula : FormulaBase<float, Pack> { }
        public sealed class RealAsset : FormulaAsset<float, Pack> { }
        [Serializable] public sealed class RealSlot : FormulaSlot<float, RealAsset, RealFormula, Pack> { }
        [Serializable] public abstract class BoolFormula : FormulaBase<bool, Pack> { }
        public sealed class BoolAsset : FormulaAsset<bool, Pack> { }
        [Serializable] public sealed class BoolSlot : FormulaSlot<bool, BoolAsset, BoolFormula, Pack> { }
        [Serializable] public abstract class LongFormula : FormulaBase<long, Pack> { }
        public sealed class LongAsset : FormulaAsset<long, Pack> { }
        [Serializable] public sealed class LongSlot : FormulaSlot<long, LongAsset, LongFormula, Pack> { }

        [Serializable, HGNode("Union test number")]
        public sealed class Number : NumberFormula
        {
            public int Value = 7;
            protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
            { pack.Calls++; return UniTask.FromResult(Value); }
        }

        [Serializable, HGNode("Union test text")]
        public sealed class Text : TextFormula
        {
            protected override UniTask<string> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
                => UniTask.FromResult("text");
        }

        [Serializable]
        public sealed class Two : UnionSlot<string, Pack, NumberSlot, TextSlot>
        {
            [HGSlotSetting] public string Prefix = "";

            protected override async UniTask<string> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
            {
                switch (SelectedSlot)
                {
                    case NumberSlot number: return Prefix + (await number.Evaluate(pack, tokens)).ToString();
                    case TextSlot text: return Prefix + await text.Evaluate(pack, tokens);
                    default: throw new InvalidOperationException();
                }
            }
        }

        [Serializable]
        public sealed class Five : UnionSlot<string, Pack, NumberSlot, TextSlot, RealSlot, BoolSlot, LongSlot>
        {
            protected override async UniTask<string> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
            {
                switch (SelectedSlot)
                {
                    case NumberSlot number: return (await number.Evaluate(pack, tokens)).ToString();
                    case TextSlot text: return await text.Evaluate(pack, tokens);
                    case RealSlot real: return (await real.Evaluate(pack, tokens)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case BoolSlot boolean: return (await boolean.Evaluate(pack, tokens)).ToString();
                    case LongSlot large: return (await large.Evaluate(pack, tokens)).ToString();
                    default: throw new InvalidOperationException();
                }
            }
        }

        [Serializable] public sealed class Holder { public Two A = new(); public Two B = new(); public Five C = new(); }

        [Serializable]
        public sealed class Ambiguous : UnionSlot<int, Pack, NumberSlot, IdSlot>
        {
            protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
                => ((NumberSlot)SelectedSlot).Evaluate(pack, tokens);
        }

        [Serializable, HGNode("Union test action")]
        public sealed class ReadUnion : ActionBase<Pack>
        {
            public Two Input = new();
            protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            { await Input.Evaluate(pack, tokens); }
        }
        public sealed class Owner : ScriptableObject { public LogicGraph<Timing, Pack> Graph = new(); }

        [Test]
        public async Task TwoBranches_PreserveSourceSemanticsAndFamilyIdentity()
        {
            var pack = new Pack();
            var graph = new LogicGraph<Timing, Pack>();
            var property = new GraphProperty("value", new NumberSlot { Default = 23 }, true);
            graph.Properties.Add(property);
            var tokens = graph.CreateTokenTable();
            var slot = new Two();
            slot.DefaultObject = "3";
            Assert.That(slot.DefaultEditType, Is.EqualTo(typeof(string)));
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("3"));
            var carrier = new GraphNode(new Number());
            slot.SetNode(carrier);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("7"));
            carrier.Disabled = true;
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("3"));
            Assert.That(pack.Calls, Is.EqualTo(1));

            var textToken = new GraphToken("same", new TextSlot { Default = "token" });
            var intToken = new GraphToken("same", new NumberSlot { Default = 11 });
            tokens.Register(textToken);
            tokens.Register(intToken);
            var tokenNode = new GraphNode();
            tokenNode.SetToken(textToken);
            slot.SetNode(tokenNode);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("token"));
            tokenNode.SetToken(intToken);
            slot.SetNode(tokenNode);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("11"));
            Assert.That(slot.AcceptsToken(new GraphToken("id", new IdSlot())), Is.False);

            var propertyNode = new GraphNode();
            propertyNode.SetProperty(property);
            slot.SetNode(propertyNode);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("23"));

            var asset = ScriptableObject.CreateInstance<NumberAsset>();
            try
            {
                asset.SetRoot(new GraphNode(new Number { Value = 31 }));
                var assetNode = new GraphNode();
                assetNode.SetAsset(asset);
                slot.SetNode(assetNode);
                Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("31"));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await slot.Evaluate(pack, tokens.CreateEvaluationScope(cancellation.Token)));
        }

        [Test]
        public async Task FiveBranches_ResultDefaultSurvivesSourceChangesCopyAndSerialization()
        {
            var holder = new Holder();
            holder.C.Default = "fallback";
            FormulaSlotBase[] branches = {
                new NumberSlot { Default = 17 }, new TextSlot { Default = "hello" },
                new RealSlot { Default = 0.5f }, new BoolSlot { Default = true },
                new LongSlot { Default = 9000000000L },
            };
            string[] expected = { "17", "hello", "0.5", "True", "9000000000" };
            for (int i = 0; i < branches.Length; i++)
            {
                var token = new GraphToken("source", branches[i]);
                var tokens = new TokenTable<Pack>();
                tokens.Register(token);
                var node = new GraphNode();
                node.SetToken(token);
                holder.C.SetNode(node);
                var copy = GraphDeepCopy.Copy(holder);
                Assert.That(await copy.C.Evaluate(new Pack(), tokens), Is.EqualTo(expected[i]));
                var restored = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder));
                Assert.That(restored.C.SelectedSlot.FamilyType, Is.EqualTo(holder.C.SelectedSlot.FamilyType));
                Assert.That(await restored.C.Evaluate(new Pack(), tokens), Is.EqualTo(expected[i]));
                restored.C.SetNode(null);
                copy.C.SetNode(null);
                Assert.That(await restored.C.Evaluate(new Pack(), tokens), Is.EqualTo("fallback"));
                Assert.That(await copy.C.Evaluate(new Pack(), tokens), Is.EqualTo("fallback"));
            }
            var shared = new GraphNode(new Number());
            holder.A.SetNode(shared);
            holder.B.SetNode(shared);
            var copied = GraphDeepCopy.Copy(holder);
            Assert.That(copied.A.Node, Is.SameAs(copied.B.Node));
            Assert.That(copied.A.Node, Is.Not.SameAs(shared));
            shared.SetBody(new Text());
            Assert.That(holder.A.ConfigurationError, Is.Not.Null);
            holder.A.SetNode(shared);
            Assert.That(holder.A.ConfigurationError, Is.Null);
            Assert.That(await holder.A.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("text"));
        }

        [Test]
        public void EditorCandidates_ContainBothFamilies_AndRejectUnlistedFamily()
        {
            // 正式候選池排除測試組件；直接測接受契約，避免測試型別污染作者選單。
            var slot = new Two();
            Assert.That(slot.AcceptsBody(new Number()), Is.True);
            Assert.That(slot.AcceptsBody(new Text()), Is.True);
            Assert.That(slot.AcceptsProperty(new GraphProperty("other", new BoolSlot())), Is.False);
            Assert.That(HGReflect.SlotKindName(slot), Does.Contain(" | "));
            Assert.Throws<ArgumentException>(() => slot.SetSelectedSlot(new BoolSlot()));
            Assert.That(slot.SelectedSlot.FamilyType, Is.EqualTo(typeof(NumberSlot)));
            var ambiguous = new Ambiguous();
            Assert.That(ambiguous.AcceptsBody(new Number()), Is.False);
            Assert.That(ambiguous.AcceptsToken(new GraphToken("id", new IdSlot())), Is.True);
        }

        [Test]
        public async Task EditorSlotSettings_EditTheUnionAndPersistAcrossBranchChangesAndSerialization()
        {
            var slot = new Two();
            slot.DefaultObject = "fallback";
            var view = HGGraph.Build(new HGModel(), new object[] { slot }, null, "union-settings", "Union");
            Assert.That(view.Diagnostics, Is.Empty);
            var row = view.Nodes[0].Rows[0];
            Assert.That(row.SlotSettingsRow.Collapsed, Is.True);
            Assert.That(row.SlotSettingsRow.Children, Has.Count.EqualTo(1));
            var setting = row.SlotSettingsRow.Children[0];
            Assert.That(setting.Target, Is.SameAs(slot));
            setting.Field.SetValue(setting.Target, "value:");
            Assert.That(await slot.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("fallback"));
            slot.SetNode(new GraphNode(new Number()));
            Assert.That(await slot.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("value:7"));

            slot.SetNode(new GraphNode(new Text()));
            var restored = JsonUtility.FromJson<Two>(JsonUtility.ToJson(slot));
            var copied = GraphDeepCopy.Copy(restored);
            Assert.That(await copied.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("value:text"));
            view = HGGraph.Build(new HGModel(), new object[] { copied }, null, "union-settings", "Union");
            Assert.That(view.Nodes[0].Rows[0].SlotSettingsRow.Path, Is.EqualTo(row.SlotSettingsRow.Path));
            Assert.That(copied.DefaultObject, Is.EqualTo("fallback"));
            copied.SetNode(null);
            Assert.That(await copied.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo("fallback"));
        }

        [Test]
        public void EditorSourceReplacement_RebindsBranch_AndUndoRestoresIt()
        {
            var owner = ScriptableObject.CreateInstance<Owner>();
            try
            {
                var action = new ReadUnion();
                action.Input.SetNode(new GraphNode(new Number()));
                owner.Graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack>
                {
                    Timing = Timing.Run,
                    Actions = new List<ActionSlot<Pack>> { new ActionSlot<Pack>(action) },
                });
                var binding = new HGDocumentBinding<LogicGraph<Timing, Pack>>("union.test",
                    value => ((Owner)value).Graph, (value, graph) => ((Owner)value).Graph = graph);
                Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, binding,
                    LogicGraphEditor.Context, out var session, out var error), Is.True, error?.Message);
                var input = session.Document.ActionGroups[0].Actions[0].Node.GetBody<ReadUnion>().Input;
                Assert.That(session.ReplaceSource(input.Node, HGCarrierSource.Body(new Text())), Is.EqualTo(HGSessionCommandResult.Changed));
                input = session.Document.ActionGroups[0].Actions[0].Node.GetBody<ReadUnion>().Input;
                Assert.That(input.SelectedSlot, Is.TypeOf<TextSlot>());
                Assert.That(session.Document.TryValidate(out var validation), Is.True, validation);
                Assert.That(session.Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
                input = session.Document.ActionGroups[0].Actions[0].Node.GetBody<ReadUnion>().Input;
                Assert.That(input.SelectedSlot, Is.TypeOf<NumberSlot>());
                Assert.That(owner.Graph.ActionGroups[0].Actions[0].Node.GetBody<ReadUnion>().Input.SelectedSlot,
                    Is.TypeOf<NumberSlot>());
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
