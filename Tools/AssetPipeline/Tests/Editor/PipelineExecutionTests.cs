using System;
using System.Collections.Generic;
using System.IO;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class PipelineApiBoundaryTests
    {
        [Test]
        public void AuthorsCanExecuteChildSlotsWithoutDirectActionOrTransactionAccess()
        {
            const System.Reflection.BindingFlags methods = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var execute = typeof(ActionBase).GetMethod("Execute", methods);
            var onExecute = typeof(ActionBase).GetMethod("OnExecute", methods);
            var slotExecute = typeof(ActionSlot).GetMethod("Execute", methods);

            Assert.That(execute, Is.Not.Null);
            Assert.That(execute.IsAssembly, Is.True);
            Assert.That(execute.IsVirtual, Is.False);
            Assert.That(onExecute, Is.Not.Null);
            Assert.That(onExecute.IsFamily, Is.True);
            Assert.That(onExecute.IsAbstract, Is.True);
            Assert.That(slotExecute, Is.Not.Null);
            Assert.That(slotExecute.IsPublic, Is.True);
            Assert.That(typeof(PipelineAssetTransaction).IsVisible, Is.False);
        }

        [TestCase("current")]
        [TestCase("CurrentAction")]
        public void ExecutionStateIsReadOnlyToConsumers(string name)
        {
            var property = typeof(AssetPipeline).GetProperty(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.GetGetMethod().IsPublic, Is.True);
            Assert.That(property.GetSetMethod(), Is.Null);
            Assert.That(property.GetSetMethod(true).IsAssembly, Is.True);
        }

        [Test]
        public void FormulaAuthorsOnlyOverrideOnEvaluate()
        {
            const System.Reflection.BindingFlags methods = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            Type type = typeof(FormulaBase<int, NullPack>);
            var execute = type.GetMethod("Evaluate", methods);
            var implementation = type.GetMethod("OnEvaluate", methods);
            Assert.That(execute.IsAssembly, Is.True);
            Assert.That(execute.IsVirtual, Is.False);
            Assert.That(implementation.IsFamily, Is.True);
            Assert.That(implementation.IsAbstract, Is.True);
            Assert.That(type.GetMethod("EvaluateObject"), Is.Null);
            Assert.That(typeof(IFormula).IsVisible, Is.False);
        }
    }

    public sealed class PipelineMissingTypeRepairTests
    {
        [TestCase(false, "inspector")]
        [TestCase(true, "inspector")]
        [TestCase(false, "graph")]
        [TestCase(true, "graph")]
        [TestCase(false, "empty")]
        [TestCase(false, "conflict")]
        [TestCase(false, "invalid")]
        public void MissingTypeRepairBacksUpOriginalAndPreservesValidDataOrStopsBeforeMutation(bool missingMeta, string mode)
        {
            string folder = "Assets/APMissingTypeTest_" + Guid.NewGuid().ToString("N");
            string path = folder + "/Pipeline.asset";
            string backupDirectory = null;
            var previousMode = EditorSettings.serializationMode;
            AssetPipeline pipeline = null;
            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                pipeline = ScriptableObject.CreateInstance<AssetPipeline>();
                pipeline.collectKey = "KeepThisKey";
                var root = (ActionGroup)((IGraphDocument)pipeline.graph).AddRoot(Graph.PipelineKey);
                root.Actions.Add(new ActionSlot(new RepairMissingBody()));
                root.Actions.Add(new ActionSlot(new RepairValidBody { value = 42 }));
                root.Actions[1].Node.EnsureId();
                string validId = root.Actions[1].Node.Id;
                AssetDatabase.CreateAsset(pipeline, path);
                AssetDatabase.SaveAssetIfDirty(pipeline);
                AssetDatabase.ForceReserializeAssets(new[] { path });

                // 僅改測試自行建立的資產，模擬消費端刪除／改名一種 SerializeReference 型別。
                string yaml = File.ReadAllText(path);
                Assert.That(yaml, Does.Contain(nameof(RepairMissingBody)));
                File.WriteAllText(path, yaml.Replace(nameof(RepairMissingBody), "DeletedRepairMissingBody"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                pipeline = AssetDatabase.LoadAssetAtPath<AssetPipeline>(path);
                Assert.That(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline), Is.True);
                byte[] before = File.ReadAllBytes(path);
                byte[] meta = File.ReadAllBytes(path + ".meta");
                string guid = AssetDatabase.AssetPathToGUID(path);
                var oldGraph = pipeline.graph;
                bool fromGraph = mode != "inspector";
                bool shouldFail = missingMeta || mode == "conflict" || mode == "invalid";
                HGModel model = null;
                Graph working = null;
                if (fromGraph)
                {
                    model = new HGModel();
                    Assert.That(model.Bind(pipeline), Is.True);
                    working = (Graph)model.Data;
                    var items = ((ActionGroup)((IGraphDocument)working).Roots[0]).Actions;
                    if (mode != "invalid") items.RemoveAt(0);
                    if (mode == "empty") items.Clear();
                    else ((RepairValidBody)items[items.Count - 1].Node.BodyObject).value = 99;
                    model.MarkDirty();
                    Assert.That(model.Save(), Is.False, "普通程式化提交不可默默放棄遺失內容。");
                    Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.serialize-reference.missing-type"));
                    if (mode == "conflict") pipeline.graph = GraphDeepCopy.Copy(pipeline.graph);
                    oldGraph = pipeline.graph;
                    if (mode == "invalid")
                    {
                        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[AssetPipeline\\] 驗證未通過"));
                        LogAssert.Expect(LogType.Error, "[GraphKit] Core Verify 未通過，Owner 未寫入。請查看 Console 的 Core 驗證訊息。");
                    }
                }

                if (missingMeta) File.Move(path + ".meta", path + ".meta.backup");
                string error = null;
                bool repaired;
                if (fromGraph)
                {
                    repaired = model.SaveDiscardingMissingTypes(out backupDirectory);
                    error = model.LastCommitDiagnostic?.Message;
                }
                else repaired = HaruFamily.Tools.AssetPipeline.Editor.AssetPipelineEditor.TryRepairMissingTypes(
                    pipeline, out backupDirectory, out error);

                Assert.That(repaired, Is.EqualTo(!shouldFail), error);
                if (!fromGraph || shouldFail)
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "Inspector 修復／失敗的圖提交不應存檔。");
                if (shouldFail)
                {
                    if (missingMeta) Assert.That(error, Does.Contain("備份"));
                    if (mode == "conflict") Assert.That(model.LastCommitDiagnostic.Code, Is.EqualTo("graphkit.commit.owner-changed"));
                    Assert.That(backupDirectory, Is.Null);
                    Assert.That(pipeline.graph, Is.SameAs(oldGraph));
                    Assert.That(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline), Is.True);
                    if (fromGraph)
                    {
                        Assert.That(model.Data, Is.SameAs(working));
                        Assert.That(model.Dirty, Is.True);
                        Assert.That(model.CanUndo, Is.True);
                    }
                    return;
                }

                Assert.That(error, Is.Null);
                Assert.That(File.ReadAllBytes(Path.Combine(backupDirectory, "Pipeline.asset")), Is.EqualTo(before));
                Assert.That(File.ReadAllBytes(Path.Combine(backupDirectory, "Pipeline.asset.meta")), Is.EqualTo(meta));
                Assert.That(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline), Is.False);
                Assert.That(pipeline.graph, Is.Not.SameAs(oldGraph), "舊 session 必須失去提交基準。");
                Assert.That(pipeline.graph.IsValidated, Is.EqualTo(fromGraph));
                int count = mode == "empty" ? 0 : fromGraph ? 1 : 2;
                Assert.That(pipeline.graph.Actions.Count, Is.EqualTo(count));
                if (!fromGraph) Assert.That(pipeline.graph.Actions[0].Node.BodyObject, Is.Null);
                if (count > 0)
                {
                    Assert.That(((RepairValidBody)pipeline.graph.Actions[count - 1].Node.BodyObject).value, Is.EqualTo(fromGraph ? 99 : 42));
                    Assert.That(pipeline.graph.Actions[count - 1].Node.Id, Is.EqualTo(validId));
                }
                if (fromGraph)
                {
                    Assert.That(model.Data, Is.SameAs(working), "圖存檔不可重載或捨棄使用者的工作副本。");
                    Assert.That(model.Dirty, Is.False);
                    Assert.That(model.Save(), Is.True, "修復後的 session 應可繼續正常存檔。");
                }
                Assert.That(pipeline.collectKey, Is.EqualTo("KeepThisKey"));
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));

                // 修補空節點後，驗證與保存可恢復，毋須重建 SO。
                if (!fromGraph)
                {
                    pipeline.graph.Actions[0].Node.SetBody(new RepairValidBody());
                    pipeline.graph.Verify();
                    Assert.That(pipeline.graph.IsValidated, Is.True);
                    AssetDatabase.SaveAssetIfDirty(pipeline);
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                pipeline = AssetDatabase.LoadAssetAtPath<AssetPipeline>(path);
                Assert.That(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline), Is.False);
                Assert.That(pipeline.collectKey, Is.EqualTo("KeepThisKey"));
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
                Assert.That(pipeline.graph.Actions.Count, Is.EqualTo(count));
                if (fromGraph && count > 0)
                    Assert.That(((RepairValidBody)pipeline.graph.Actions[0].Node.BodyObject).value, Is.EqualTo(99));
            }
            finally
            {
                if (File.Exists(path + ".meta.backup")) File.Move(path + ".meta.backup", path + ".meta");
                EditorSettings.serializationMode = previousMode;
                AssetDatabase.DeleteAsset(folder);
                if (pipeline != null && !EditorUtility.IsPersistent(pipeline)) Object.DestroyImmediate(pipeline);
                if (backupDirectory != null && Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            }
        }

        [Serializable] private sealed class RepairMissingBody : ActionBase
        { protected override void OnExecute(PipelineActionContext context) { } }
        [Serializable] private sealed class RepairValidBody : ActionBase
        {
            public int value;
            protected override void OnExecute(PipelineActionContext context) { }
        }
    }

    public sealed class PipelineExecutionTests
    {
        private string folder;
        private AssetPipeline pipeline;
        private ActionGroup root;

        [SetUp]
        public void SetUp()
        {
            Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Is.Empty,
                "執行測試前請先處理既有 AP 回復日誌，測試不會刪除其他交易。");
            folder = "Assets/APTransactionTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            pipeline = ScriptableObject.CreateInstance<AssetPipeline>();
            root = (ActionGroup)((IGraphDocument)pipeline.graph).AddRoot(Graph.PipelineKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (pipeline != null) Object.DestroyImmediate(pipeline);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
        }

        private void Add(ActionBase action) => root.Actions.Add(new ActionSlot(action));
        private string Prefab(string name)
        {
            string path = folder + "/" + name + ".prefab";
            var go = new GameObject(name);
            try { PrefabUtility.SaveAsPrefabAsset(go, path); }
            finally { Object.DestroyImmediate(go); }
            return path;
        }

        [Test]
        public void FailureRemovesCreatedPrefabAndDoesNotRunFollowingStep()
        {
            string source = Prefab("Source");
            string target = folder + "/Created.prefab";
            Add(new CopyAction { source = source, target = target });
            Add(new FailAction());
            Add(new CopyAction { source = source, target = folder + "/Never.prefab" });

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            Assert.That(result.Steps[0].Status, Is.EqualTo(PipelineStepStatus.Success));
            Assert.That(result.Steps[1].Status, Is.EqualTo(PipelineStepStatus.Failed));
            Assert.That(result.Steps[2].Status, Is.EqualTo(PipelineStepStatus.NotRun));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(File.Exists(target + ".meta"), Is.False);
            Assert.That(File.Exists(folder + "/Never.prefab"), Is.False);
            Assert.That(File.Exists(source), Is.True);
            Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChildSlotSharesParentResultAndTransaction(bool failAfterCopy)
        {
            string source = Prefab("Source");
            string target = folder + "/Child.prefab";
            Add(new ChildAction
            {
                child = new ActionSlot(new CopyAction
                {
                    source = source, target = target, failAfterCopy = failAfterCopy
                })
            });

            var result = pipeline.RunPipeline();

            Assert.That(result.Steps.Count, Is.EqualTo(1));
            Assert.That(result.Steps[0].Count(PipelineItemStatus.Created), Is.EqualTo(1));
            Assert.That(result.Steps[0].Status, Is.EqualTo(
                failAfterCopy ? PipelineStepStatus.Partial : PipelineStepStatus.Success));
            Assert.That(result.Transaction, Is.EqualTo(
                failAfterCopy ? PipelineTransactionStatus.RolledBack : PipelineTransactionStatus.Committed));
            Assert.That(File.Exists(target), Is.EqualTo(!failAfterCopy));
            Assert.That(File.Exists(target + ".meta"), Is.EqualTo(!failAfterCopy));
            Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Is.Empty);
        }

        [Test]
        public void RepeatedEditsRollbackToOriginalBytesAndGuid()
        {
            string path = Prefab("Original");
            byte[] before = File.ReadAllBytes(path);
            byte[] meta = File.ReadAllBytes(path + ".meta");
            string guid = AssetDatabase.AssetPathToGUID(path);
            Add(new EditAction { path = path, newName = "First" });
            Add(new EditAction { path = path, newName = "Second" });
            Add(new FailAction());

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).name, Is.EqualTo("Original"));
        }

        [Test]
        public void ReplacementCommitPreservesDestinationGuid()
        {
            string source = Prefab("Source");
            string target = Prefab("Destination");
            string guid = AssetDatabase.AssetPathToGUID(target);
            Add(new CopyAction { source = source, target = target });

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.Committed));
            Assert.That(AssetDatabase.AssetPathToGUID(target), Is.EqualTo(guid));
            Assert.That(result.Steps[0].Count(PipelineItemStatus.Modified), Is.EqualTo(1));
            Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Is.Empty);
        }

        [Test]
        public void ExceptionInsideAssetEditRestoresDiskAndLoadedObject()
        {
            string path = folder + "/Storage.asset";
            var storage = ScriptableObject.CreateInstance<PipelineTestStorage>();
            storage.value = 7;
            AssetDatabase.CreateAsset(storage, path);
            AssetDatabase.SaveAssetIfDirty(storage);
            byte[] before = File.ReadAllBytes(path);
            Add(new EditStorageAction { storage = storage });

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.LoadAssetAtPath<PipelineTestStorage>(path).value, Is.EqualTo(7));
            Assert.That(storage.value, Is.EqualTo(7));
        }

        [TestCase(DynamicAssetCatalog.InitializationMode.EachRun, 0)]
        [TestCase(DynamicAssetCatalog.InitializationMode.Retain, 1)]
        public void InitializationModeControlsDataAtRunStart(DynamicAssetCatalog.InitializationMode mode, int expected)
        {
            var catalog = new DynamicAssetCatalog { initialization = mode };
            var node = new GraphNode();
            node.SetCatalog(catalog);
            pipeline.graph.Orphans.Add(node);
            catalog.Write(new Object[] { pipeline }, false);
            Add(new NoOpAction());

            Assert.That(pipeline.RunPipeline().Transaction, Is.EqualTo(PipelineTransactionStatus.Committed));
            Assert.That(catalog.Read().Count, Is.EqualTo(expected));
        }

        [Test]
        public void FailureRestoresDynamicDataFromBeforeInitialization()
        {
            var catalog = new DynamicAssetCatalog();
            var node = new GraphNode();
            node.SetCatalog(catalog);
            pipeline.graph.Orphans.Add(node);
            catalog.Write(new Object[] { pipeline }, false);
            Add(new FailAction());

            Assert.That(pipeline.RunPipeline().Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            Assert.That(catalog.Read(), Is.EquivalentTo(new Object[] { pipeline }));
            Assert.That(AssetPipeline.current, Is.Null);
            Assert.That(AssetPipeline.CurrentAction, Is.Null);
        }

        [Test]
        public void ExplicitPartialFailureRollsBackAndSkipDoesNot()
        {
            string source = Prefab("Source");
            Add(new CopyAction { source = source, target = folder + "/Partial.prefab", failAfterCopy = true });
            var failed = pipeline.RunPipeline();
            Assert.That(failed.Steps[0].Status, Is.EqualTo(PipelineStepStatus.Partial));
            Assert.That(failed.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            root.Actions.Clear();
            Add(new SkipAction());
            var skipped = pipeline.RunPipeline();
            Assert.That(skipped.Steps[0].Status, Is.EqualTo(PipelineStepStatus.Skipped));
            Assert.That(skipped.Transaction, Is.EqualTo(PipelineTransactionStatus.Committed));
        }

        [Test]
        public void MissingBackupKeepsRecoveryJournalAndReportsFailure()
        {
            string path = Prefab("Original");
            var transaction = new PipelineAssetTransaction();
            try
            {
                transaction.Capture(path);
                foreach (string backup in Directory.GetFiles(transaction.DirectoryPath, "*.data")) File.Delete(backup);
                Assert.That(transaction.Rollback(), Is.Not.Empty);
                Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Does.Contain(transaction.DirectoryPath));
            }
            finally
            {
                // 僅清除此測試建立的負面案例日誌，不碰其他交易。
                if (Directory.Exists(transaction.DirectoryPath)) Directory.Delete(transaction.DirectoryPath, true);
            }
        }

        [Test]
        public void DirtyInputIsRejectedWithoutLosingUnsavedChanges()
        {
            string path = folder + "/Dirty.asset";
            var storage = ScriptableObject.CreateInstance<PipelineTestStorage>();
            storage.value = 7;
            AssetDatabase.CreateAsset(storage, path);
            AssetDatabase.SaveAssetIfDirty(storage);
            storage.value = 42;
            EditorUtility.SetDirty(storage);
            Add(new EditStorageAction { storage = storage });

            var result = pipeline.RunPipeline();

            Assert.That(result.Steps[0].Status, Is.EqualTo(PipelineStepStatus.Failed));
            Assert.That(storage.value, Is.EqualTo(42));
            Assert.That(EditorUtility.IsDirty(storage), Is.True);
            Assert.That(PipelineAssetTransaction.PendingRecoveryDirectories(), Is.Empty);
        }

        [Test]
        public void GeneratedAssetSetIsRemovedEvenWhenEditedAgainLater()
        {
            string path = folder + "/Settings.asset";
            var storage = ScriptableObject.CreateInstance<PipelineTestStorage>();
            storage.value = 7;
            AssetDatabase.CreateAsset(storage, path);
            AssetDatabase.SaveAssetIfDirty(storage);
            Add(new CreateSetAction { storage = storage, folder = folder });
            Add(new FailAction());

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
            Assert.That(Directory.Exists(folder + "/Generated"), Is.False);
            Assert.That(File.Exists(folder + "/Generated.meta"), Is.False);
            Assert.That(storage.value, Is.EqualTo(7));
        }

        [Test]
        public void LoggedErrorWithoutExceptionIsNotReportedAsSuccess()
        {
            Add(new LoggedErrorAction());
            LogAssert.Expect(LogType.Error, "AP 測試錯誤");
            var result = pipeline.RunPipeline();
            Assert.That(result.Steps[0].Status, Is.EqualTo(PipelineStepStatus.Failed));
            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.RolledBack));
        }

        [Test]
        public void RunSnapshotDoesNotReevaluateCellsAndExplicitRefreshDoes()
        {
            CountingFilter.Calls = 0;
            var catalog = new DynamicAssetCatalog { initialization = DynamicAssetCatalog.InitializationMode.Retain };
            catalog.Write(new Object[] { pipeline }, false);
            var carrier = new GraphNode();
            carrier.SetCatalog(catalog);
            pipeline.graph.Orphans.Add(carrier);
            GraphNode cell = ((IGraphNodeOwner)catalog).CreateChild();
            ((CatalogCell)cell.BodyObject).InputSlot.SetNode(new GraphNode(new CountingFilter()));
            var reader = new ReadCellAction();
            reader.value.SetNode(cell);
            Add(reader);

            var result = pipeline.RunPipeline();

            Assert.That(result.Transaction, Is.EqualTo(PipelineTransactionStatus.Committed));
            Assert.That(CountingFilter.Calls, Is.EqualTo(1));
            Assert.That(result.Catalogs[0].Cells[0].Value.Value, Is.EqualTo("1"));
            pipeline.RefreshCatalogPreview();
            Assert.That(CountingFilter.Calls, Is.EqualTo(2));
        }

        [Serializable] private sealed class ChildAction : ActionBase
        {
            public ActionSlot child = new();
            protected override void OnExecute(PipelineActionContext context) => child.Execute(context);
        }
        [Serializable] private sealed class NoOpAction : ActionBase
        { protected override void OnExecute(PipelineActionContext context) { } }
        [Serializable] private sealed class SkipAction : ActionBase
        { protected override void OnExecute(PipelineActionContext context) => context.Skip("符合跳過條件"); }
        [Serializable] private sealed class FailAction : ActionBase
        { protected override void OnExecute(PipelineActionContext context) => context.Fail("測試失敗"); }
        [Serializable] private sealed class LoggedErrorAction : ActionBase
        { protected override void OnExecute(PipelineActionContext context) => Debug.LogError("AP 測試錯誤"); }
        [Serializable] private sealed class CountingFilter : Formula_Int<List<Object>>
        {
            public static int Calls;
            protected override int OnEvaluate(List<Object> pack) { Calls++; return pack.Count; }
        }
        [Serializable] private sealed class ReadCellAction : ActionBase
        {
            public IntSlot value = new();
            protected override void OnExecute(PipelineActionContext context) => context.Result.Message(value.Evaluate().ToString());
        }
        [Serializable] private sealed class CopyAction : ActionBase
        {
            public string source, target;
            public bool failAfterCopy;
            protected override void OnExecute(PipelineActionContext context)
            {
                context.Assets.CopyPrefab(source, target);
                if (failAfterCopy) context.Fail("複製後失敗");
            }
        }
        [Serializable] private sealed class EditAction : ActionBase
        {
            public string path, newName;
            protected override void OnExecute(PipelineActionContext context) => context.Assets.EditPrefab(path, root => root.name = newName);
        }
        [Serializable] private sealed class EditStorageAction : ActionBase
        {
            public PipelineTestStorage storage;
            protected override void OnExecute(PipelineActionContext context)
            {
                context.Assets.EditAssetSet(new Object[] { storage }, storage, () =>
                {
                    storage.value = 99;
                    EditorUtility.SetDirty(storage);
                    throw new InvalidOperationException("寫入中失敗");
                });
            }
        }
        [Serializable] private sealed class CreateSetAction : ActionBase
        {
            public PipelineTestStorage storage;
            public string folder;
            protected override void OnExecute(PipelineActionContext context)
            {
                var created = (PipelineTestStorage)context.Assets.CreateAssetSet(new Object[] { storage }, folder, () =>
                {
                    AssetDatabase.CreateFolder(folder, "Generated");
                    var asset = ScriptableObject.CreateInstance<PipelineTestStorage>();
                    AssetDatabase.CreateAsset(asset, folder + "/Generated/Group.asset");
                    storage.value = 99;
                    EditorUtility.SetDirty(storage);
                    return asset;
                });
                context.Assets.EditAssetSet(new Object[] { created }, created, () => created.value = 17);
            }
        }
    }
}
