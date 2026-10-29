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
        public sealed class NumberAsset : FormulaAsset<int, NumericInput, Pack> { }
        [Serializable] public class NumberSlot : FormulaSlot<int, NumberFormula, NumberToken, NumberAsset, Pack> { }
        [Serializable] public sealed class IdToken : FormulaToken<int, IdSlot, Pack> { }
        [Serializable] public sealed class IdSlot : FormulaSlot<int, NumberFormula, IdToken, NumberAsset, Pack> { }
        [Serializable] public abstract class TextFormula : FormulaBase<string, Pack> { }
        [Serializable] public sealed class TextToken : FormulaToken<string, TextSlot, Pack> { }
        public sealed class TextAsset : FormulaAsset<string, TextSlot, Pack> { }
        [Serializable] public sealed class TextSlot : FormulaSlot<string, TextFormula, TextToken, TextAsset, Pack> { }
        [Serializable] public abstract class RealFormula : FormulaBase<float, Pack> { }
        [Serializable] public sealed class RealToken : FormulaToken<float, RealSlot, Pack> { }
        public sealed class RealAsset : FormulaAsset<float, RealSlot, Pack> { }
        [Serializable] public sealed class RealSlot : FormulaSlot<float, RealFormula, RealToken, RealAsset, Pack> { }
        [Serializable] public abstract class BoolFormula : FormulaBase<bool, Pack> { }
        [Serializable] public sealed class BoolToken : FormulaToken<bool, BoolSlot, Pack> { }
        public sealed class BoolAsset : FormulaAsset<bool, BoolSlot, Pack> { }
        [Serializable] public sealed class BoolSlot : FormulaSlot<bool, BoolFormula, BoolToken, BoolAsset, Pack> { }
        [Serializable] public abstract class LongFormula : FormulaBase<long, Pack> { }
        [Serializable] public sealed class LongToken : FormulaToken<long, LongSlot, Pack> { }
        public sealed class LongAsset : FormulaAsset<long, LongSlot, Pack> { }
        [Serializable] public sealed class LongSlot : FormulaSlot<long, LongFormula, LongToken, LongAsset, Pack> { }

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
                => ((IFormulaSlot<int, Pack>)SelectedSlot).Evaluate(pack, tokens);
        }

        [Serializable, HGNode("Union test action")]
        public sealed class ReadUnion : ActionBase<Pack>
        {
            public Two Input = new();
            protected override async UniTask OnExecute(Pack pack, TokenTable<Pack> tokens)
            { await Input.Evaluate(pack, tokens); }
        }
        public sealed class Owner : ScriptableObject { public LogicGraph<Timing, Pack> Graph = new(); }

        [Serializable]
        public sealed class NumericInput : UnionSlot<int, Pack, NumberSlot, RealSlot>
        {
            [HGSlotSetting] public int Offset;
            protected override async UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
                => Offset + (SelectedSlot is NumberSlot integer ? await integer.Evaluate(pack, tokens)
                    : Mathf.FloorToInt(await ((RealSlot)SelectedSlot).Evaluate(pack, tokens)));
        }

        [Serializable]
        public sealed class NumberToken : FormulaToken<int, NumericInput, Pack> { }

        [Serializable] public sealed class DuplicateToken : FormulaToken<int, NumberSlot, Pack> { }
        public sealed class DuplicateSlotA : FormulaSlot<int, NumberFormula, DuplicateToken, NumberAsset, Pack> { }
        public sealed class DuplicateSlotB : FormulaSlot<int, NumberFormula, DuplicateToken, NumberAsset, Pack> { }

        [Test]
        public void DuplicateTokenFamily_IsReportedInsteadOfChoosingDiscoveryOrder()
        {
            var graph = new LogicGraph<Timing, Pack>();
            graph.Tokens.Add(new DuplicateToken { Name = "duplicate" });
            Assert.That(graph.TryValidate(out var error), Is.False);
            Assert.That(error, Does.Contain("多個公式族"));
        }

        [Serializable, HGNode("Union test real")]
        public sealed class Real : RealFormula
        {
            protected override UniTask<float> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
                => UniTask.FromResult(3.75f);
        }

        [Serializable, HGNode("Union test token reader")]
        public sealed class ReadNumberToken : NumberFormula
        {
            public NumberSlot Input = new();
            protected override UniTask<int> OnEvaluate(Pack pack, TokenTable<Pack> tokens)
                => Input.Evaluate(pack, tokens);
        }

        [Test]
        public async Task TokenInput_KeepsFamilyReferencesAndSerializedConversion()
        {
            var source = new GraphNode(new Number { Value = 12 });
            var original = new NumberSlot { Default = 9 };
            original.SetNode(source);
            var token = new NumberToken { Name = "value" };
            token.Input.Default = original.Default;
            token.Input.SetSelectedSlot(original);
            Assert.That(token.FamilyType, Is.EqualTo(typeof(NumberSlot)));
            Assert.That(token.Slot.Node, Is.SameAs(source));
            Assert.That(token.Slot.DefaultObject, Is.EqualTo(9));
            var input = token.Slot;
            token.SetSlot(input);
            Assert.That(token.Slot, Is.SameAs(input));
            Assert.Throws<ArgumentException>(() => token.SetSlot(new RealSlot()));
            Assert.That(token.Input, Is.SameAs(input));
            Assert.That(token.Slot.FamilyType, Is.EqualTo(typeof(NumericInput)));
            Assert.That(token.OutputSlot.FamilyType, Is.EqualTo(typeof(NumberSlot)));

            var reference = new GraphNode();
            reference.SetToken(token);
            var reader = new NumberSlot();
            reader.SetNode(reference);
            Assert.That(reader.AcceptsToken(token), Is.True);
            Assert.That(new IdSlot().AcceptsToken(token), Is.False);
            token.Slot.SetNode(new GraphNode(new Real()));

            var graph = new LogicGraph<Timing, Pack>();
            graph.Tokens.Add(token);
            graph.Tokens.Add(GraphTestTokens.Create("value", new RealSlot { Default = 5.5f }));
            Assert.That(graph.TryValidate(out var error), Is.True, error);
            Assert.That(await reader.Evaluate(new Pack(), graph.CreateTokenTable()), Is.EqualTo(3));
            Assert.That(await graph.CreateTokenTable().Resolve<float>(typeof(RealSlot), "value", new Pack()), Is.EqualTo(5.5f));
            foreach (var copy in new[] { graph.DeepCopy(), JsonUtility.FromJson<LogicGraph<Timing, Pack>>(JsonUtility.ToJson(graph)) })
            {
                Assert.That(copy.TryValidate(out error), Is.True, error);
                Assert.That(await copy.CreateTokenTable().Resolve<int>(typeof(NumberSlot), "value", new Pack()), Is.EqualTo(3));
                copy.Tokens[0].Slot.Node.Disabled = true;
                Assert.That(await copy.CreateTokenTable().Resolve<int>(typeof(NumberSlot), "value", new Pack()), Is.EqualTo(9));
            }
        }

        [Test]
        public async Task TokenInput_AssetOverrideStillMatchesDeclaredFamily()
        {
            var asset = ScriptableObject.CreateInstance<NumberAsset>();
            try
            {
                var token = new NumberToken { Name = "value" };
                token.Input.Default = 9;
                token.Slot.SetNode(new GraphNode(new Real()));
                asset.Tokens.Add(token);
                var root = new GraphNode();
                root.SetToken(token);
                var body = new ReadNumberToken();
                body.Input.SetNode(root);
                asset.SetRoot(new GraphNode(body));
                var carrier = new GraphNode();
                carrier.SetAsset(asset);
                var reader = new NumberSlot();
                reader.SetNode(carrier);
                Assert.That(await reader.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo(3));
                carrier.Bindings.Add(new NamedFormulaSlot("value", new NumberSlot { Default = 21 }) { OverrideEnabled = true });
                Assert.That(await reader.Evaluate(new Pack(), new TokenTable<Pack>()), Is.EqualTo(21));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); AssetGraphSchema.InvalidateCache(); }
        }

        [Test]
        public async Task FormulaAsset_UnionInputPreservesSettingsAndSupportsScopedTokenAndPropertyRoots()
        {
            var asset = ScriptableObject.CreateInstance<NumberAsset>();
            try
            {
                asset.RootInput.Default = 9;
                Assert.That(await asset.Evaluate(new Pack(), null), Is.EqualTo(9));
                asset.SetRoot(new GraphNode(new Real()));
                asset.RootInput.Offset = 2;
                Assert.That(await asset.Evaluate(new Pack(), null), Is.EqualTo(5));
                var copy = GraphDeepCopy.Copy(asset.Input);
                Assert.That(((NumericInput)copy).Offset, Is.EqualTo(2));
                asset.SetInput(copy);
                Assert.That(await asset.Evaluate(new Pack(), null), Is.EqualTo(5));

                asset.RootInput.Offset = 0;
                var token = new NumberToken { Name = "value" };
                token.Input.Default = 17;
                asset.Tokens.Add(token);
                AssetGraphSchema.InvalidateCache();
                var proxy = new GraphNode();
                proxy.SetToken(token);
                asset.SetRoot(proxy);
                var caller = new TokenTable<Pack>();
                caller.Register(GraphTestTokens.Create("value", new NumberSlot { Default = 99 }));
                Assert.That(await asset.Evaluate(new Pack(), caller), Is.EqualTo(17));
                var bindings = new List<NamedFormulaSlot> {
                    new("value", new NumberSlot { Default = 21 }) { OverrideEnabled = true },
                };
                Assert.That(await asset.Evaluate(new Pack(), caller, bindings), Is.EqualTo(21));

                var property = new GraphProperty("initial", new NumberSlot { Default = 23 });
                asset.Properties.Add(property);
                var propertyNode = new GraphNode();
                propertyNode.SetProperty(property);
                asset.SetRoot(propertyNode);
                var graph = new LogicGraph<Timing, Pack>();
                var assetNode = new GraphNode();
                assetNode.SetAsset(asset);
                var result = new NumberToken { Name = "result" };
                result.Input.SetNode(assetNode);
                graph.Tokens.Add(result);
                Assert.That(graph.TryValidate(out var error), Is.True, error);
                Assert.That(await graph.CreateTokenTable().Resolve<int>(typeof(NumberSlot), "result", new Pack()), Is.EqualTo(23));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); AssetGraphSchema.InvalidateCache(); }
        }

        [Test]
        public void FormulaAssetEditor_UndoPreservesUnionSettingsAndLeavesStoredAssetUntouched()
        {
            var owner = ScriptableObject.CreateInstance<Owner>();
            var asset = ScriptableObject.CreateInstance<NumberAsset>();
            var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
            var selection = UnityEditor.Selection.activeObject;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                asset.RootInput.Default = 9;
                asset.RootInput.Offset = 2;
                asset.SetRoot(new GraphNode(new Real()));
                var binding = LogicGraphEditor.CreateBinding(typeof(Owner).GetField(nameof(Owner.Graph)));
                Assert.That(window.BindDocument(owner, binding, LogicGraphEditor.Context), Is.True);
                typeof(HaruGraphWindow).GetMethod("EnterAsset", flags, null,
                    new[] { typeof(UnityEngine.Object), typeof(Type), typeof(bool) }, null).Invoke(window,
                    new object[] { asset, typeof(NumberSlot), false });
                HGFocus Focus() => (HGFocus)typeof(HaruGraphWindow).GetField("focus", flags).GetValue(window);
                var working = (NumericInput)Focus().AssetHostSlot;
                Assert.That(working, Is.Not.SameAs(asset.Input));
                Assert.That(working.Offset, Is.EqualTo(2));
                working.Offset = 7;
                typeof(HaruGraphWindow).GetMethod("Invalidate", flags).Invoke(window, null);
                Assert.That(asset.RootInput.Offset, Is.EqualTo(2));
                Assert.That(window.GetDocumentCommands().Undo(), Is.EqualTo(HGSessionCommandResult.Changed));
                Assert.That(((NumericInput)Focus().AssetHostSlot).Offset, Is.EqualTo(2));
                Assert.That(window.GetDocumentCommands().Redo(), Is.EqualTo(HGSessionCommandResult.Changed));
                Assert.That(((NumericInput)Focus().AssetHostSlot).Offset, Is.EqualTo(7));
                Assert.That(((NumericInput)Focus().AssetHostSlot).Default, Is.EqualTo(9));
            }
            finally
            {
                typeof(HaruGraphWindow).GetMethod("ExitAsset", flags).Invoke(window, null);
                window.GetDocumentCommands()?.Cancel();
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEditor.Selection.activeObject = selection;
            }
        }

        [Test]
        public async Task TwoBranches_PreserveSourceSemanticsAndFamilyIdentity()
        {
            var pack = new Pack();
            var graph = new LogicGraph<Timing, Pack>();
            var property = new GraphProperty("value", new NumberSlot { Default = 23 });
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

            var textToken = GraphTestTokens.Create("same", new TextSlot { Default = "token" });
            var intToken = GraphTestTokens.Create("same", new NumberSlot { Default = 11 });
            tokens.Register(textToken);
            tokens.Register(intToken);
            var tokenNode = new GraphNode();
            tokenNode.SetToken(textToken);
            slot.SetNode(tokenNode);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("token"));
            tokenNode.SetToken(intToken);
            slot.SetNode(tokenNode);
            Assert.That(await slot.Evaluate(pack, tokens), Is.EqualTo("11"));
            Assert.That(slot.AcceptsToken(GraphTestTokens.Create("id", new IdSlot())), Is.False);

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
            Assert.CatchAsync<OperationCanceledException>(async () =>
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
                var token = GraphTestTokens.Create("source", branches[i]);
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
            Assert.That(ambiguous.AcceptsToken(GraphTestTokens.Create("id", new IdSlot())), Is.True);
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
        public void ObsoleteTokens_AreRejectedAndCleanedOnlyInTheWorkingDocument()
        {
            var owner = ScriptableObject.CreateInstance<Owner>();
            try
            {
                var source = new GraphNode(new Number { Value = 12 });
                source.EnsureId();
                source.Pos = new Vector2(120, 80);
                var input = new NumberSlot();
                input.SetNode(source);
                var obsolete = new GraphToken("old", input);
                var candidate = new GraphNode(new Text());
                candidate.EnsureId();
                obsolete.Orphans.Add(candidate);
                owner.Graph.Tokens.Add(obsolete);
                var proxy = new GraphNode();
                proxy.SetToken(obsolete);
                owner.Graph.Orphans.Add(proxy);
                var action = new ReadUnion();
                action.Input.SelectedSlot.SetNode(proxy); // 模擬載入的舊接線。
                owner.Graph.ActionGroups.Add(new ActionTimingGroup<Timing, Pack>
                {
                    Timing = Timing.Run,
                    Actions = new List<ActionSlot<Pack>> { new ActionSlot<Pack>(action) },
                });
                Assert.That(owner.Graph.TryValidate(out var invalid), Is.False);
                Assert.That(invalid, Does.Contain("目前宣告"));
                Assert.Throws<InvalidOperationException>(() => new TokenTable<Pack>().Register(obsolete));

                var binding = new HGDocumentBinding<LogicGraph<Timing, Pack>>("obsolete.test",
                    value => ((Owner)value).Graph, (value, graph) => ((Owner)value).Graph = graph);
                Assert.That(HGDocumentSession<LogicGraph<Timing, Pack>>.TryOpen(owner, binding,
                    LogicGraphEditor.Context, out var session, out var error), Is.True, error?.Message);
                Assert.That(session.IsDirty, Is.True);
                Assert.That(session.Document.Tokens, Is.Empty);
                Assert.That(session.Document.ActionGroups[0].Actions[0].Node.GetBody<ReadUnion>().Input.Node, Is.Null);
                Assert.That(session.Document.Orphans.Count, Is.EqualTo(2));
                Assert.That(session.Document.Orphans.Exists(node => node.Id == source.Id && node.Pos == source.Pos), Is.True);
                Assert.That(session.Document.Orphans.Exists(node => node.Id == candidate.Id), Is.True);
                Assert.That(owner.Graph.Tokens[0], Is.SameAs(obsolete));
                Assert.That(action.Input.Node, Is.SameAs(proxy));
                Assert.That(proxy.Token, Is.SameAs(obsolete));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ObsoleteAssetToken_CleanupPreservesStoredAssetAndMarksWorkingCopyDirty()
        {
            var owner = ScriptableObject.CreateInstance<Owner>();
            var asset = ScriptableObject.CreateInstance<NumberAsset>();
            var window = ScriptableObject.CreateInstance<HaruGraphWindow>();
            var selection = UnityEditor.Selection.activeObject;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var source = new GraphNode(new Number());
                source.EnsureId();
                var input = new NumberSlot();
                input.SetNode(source);
                var obsolete = new GraphToken("old", input);
                asset.Tokens.Add(obsolete);
                var proxy = new GraphNode();
                proxy.SetToken(obsolete);
                asset.RootInput.SelectedSlot.SetNode(proxy);
                var binding = LogicGraphEditor.CreateBinding(typeof(Owner).GetField(nameof(Owner.Graph)));
                Assert.That(window.BindDocument(owner, binding, LogicGraphEditor.Context), Is.True);
                typeof(HaruGraphWindow).GetMethod("EnterAsset", flags, null,
                    new[] { typeof(UnityEngine.Object), typeof(Type), typeof(bool) }, null).Invoke(window,
                    new object[] { asset, typeof(NumberSlot), false });
                var focus = (HGFocus)typeof(HaruGraphWindow).GetField("focus", flags).GetValue(window);
                Assert.That(focus.AssetTokens, Is.Empty);
                Assert.That(focus.AssetHostSlot.Node, Is.Null);
                Assert.That(focus.AssetOrphans.Exists(node => node.Id == source.Id), Is.True);
                Assert.That(typeof(HaruGraphWindow).GetField("assetDirty", flags).GetValue(window), Is.True);
                Assert.That(asset.Tokens[0], Is.SameAs(obsolete));
                Assert.That(asset.Root, Is.SameAs(proxy));
            }
            finally
            {
                typeof(HaruGraphWindow).GetMethod("ExitAsset", flags).Invoke(window, null);
                window.GetDocumentCommands()?.Cancel();
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEditor.Selection.activeObject = selection;
            }
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
