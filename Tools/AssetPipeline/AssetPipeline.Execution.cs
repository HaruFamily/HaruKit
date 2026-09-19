using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public partial class AssetPipeline
    {
        private static bool executing;
        /// <summary>執行已儲存圖；所有啟用的 Action 必須使用交易寫入契約。</summary>
        public PipelineRunResult RunPipeline()
        {
            var run = new PipelineRunResult();
            if (executing)
            {
                run.Errors.Add("不支援巢狀執行 AP。");
                CurrentAction?.Fail("不支援巢狀執行 AP。");
                return run;
            }
            if (lastRun?.Transaction == PipelineTransactionStatus.RecoveryRequired && string.IsNullOrEmpty(lastRun.RecoveryDirectory))
                return lastRun;
            lastRun = run;
            catalogPreview = null;
            var pending = PipelineAssetTransaction.PendingRecoveryDirectories();
            if (pending.Count > 0)
            {
                run.Transaction = PipelineTransactionStatus.RecoveryRequired;
                run.RecoveryDirectory = pending[0];
                run.Errors.Add("有尚未回復的資產交易，請先在結果面板回復：" + pending[0]);
                pipelineLog = run.Summary + "\n" + run.Errors[0];
                return run;
            }

            AssetPipeline previous = current;
            Action<string> previousWarnings = formulaWarningHandler;
            var transaction = new PipelineAssetTransaction();
            var observations = new Dictionary<CatalogCell, PipelineValueSnapshot>();
            var dynamicBefore = new List<(DynamicAssetCatalog Catalog, List<PipelineAssetRecord> Data, bool Initialized)>();
            bool began = false;
            bool committed = false;
            executing = true;
            current = this;
            Application.LogCallback captureError = (message, stack, kind) =>
            {
                if (kind is LogType.Error or LogType.Exception or LogType.Assert) CurrentAction?.Result.Fail(message);
            };
            try
            {
                if (!VerifyPipelineAssets())
                {
                    run.Errors.Add(PrototypeValidationLog);
                    return run;
                }
                List<ActionSlot> actions = graph.Actions;
                for (int i = 0; i < actions.Count; i++)
                {
                    ActionSlot slot = actions[i];
                    string name = slot?.Node?.BodyObject != null && string.IsNullOrWhiteSpace(slot.Label)
                        ? HGReflect.TypeName(slot.Node.BodyObject.GetType()) : slot?.DisplayName ?? "空動作";
                    var step = new PipelineActionResult(i, slot?.Node?.Id, name) { WasNotRun = true };
                    run.Steps.Add(step);
                }

                foreach (GraphNode node in PipelineCatalogSnapshot.FindCatalogs(graph))
                {
                    if (node.CatalogObject is not DynamicAssetCatalog catalog) continue;
                    var data = new List<PipelineAssetRecord>();
                    foreach (Object asset in catalog.Read()) data.Add(new PipelineAssetRecord(asset, null, PipelineItemStatus.Collected, ""));
                    dynamicBefore.Add((catalog, data, catalog.IsInitialized));
                }
                began = true;
                run.RestoreCatalogs = () =>
                {
                    var errors = new List<string>();
                    foreach (var before in dynamicBefore)
                    {
                        try
                        {
                            var data = new List<Object>();
                            foreach (var item in before.Data)
                            {
                                Object asset = item.Resolve();
                                if (asset == null) throw new InvalidOperationException("無法回綁目錄原始資產：" + item.Path);
                                data.Add(asset);
                            }
                            before.Catalog.RestoreData(data, before.Initialized);
                        }
                        catch (Exception exception) { errors.Add("目錄回復失敗：" + exception.Message); }
                    }
                    return errors;
                };
                BeginRun();
                foreach (var before in dynamicBefore) before.Catalog.InitializeForRun();
                Application.logMessageReceived += captureError;
                formulaWarningHandler = message => CurrentAction?.Result.Message("公式警告：" + message);

                for (int i = 0; i < actions.Count; i++)
                {
                    ActionSlot slot = actions[i];
                    PipelineActionResult step = run.Steps[i];
                    step.WasNotRun = false;
                    if (slot.Disabled || slot.Node.Disabled)
                    {
                        step.WasSkipped = true;
                        step.Message("動作已停用。");
                        continue;
                    }
                    CurrentAction = new PipelineActionContext(transaction, step, observations);
                    double start = EditorApplication.timeSinceStartup;
                    try { ((ActionBase)slot.Node.BodyObject).Execute(CurrentAction); }
                    catch (Exception exception) { step.Fail(exception.ToString(), exception.Data["AssetPipeline.AssetPath"] as string); }
                    finally
                    {
                        step.Seconds = EditorApplication.timeSinceStartup - start;
                        CurrentAction = null;
                    }
                    if (!step.HasFailure) continue;
                    run.Errors.Add($"第 {i + 1} 步「{step.Name}」失敗，後續未執行。");
                    break;
                }
                // 只讀目錄與已觀察值，不為顯示重新執行篩選公式。
                run.Catalogs.AddRange(PipelineCatalogSnapshot.Capture(graph, observations, false));
                if (run.Errors.Count == 0)
                {
                    transaction.Commit();
                    committed = true;
                    run.Transaction = PipelineTransactionStatus.Committed;
                }
            }
            catch (Exception exception) { run.Errors.Add(exception.ToString()); }
            finally
            {
                CurrentAction = null;
                Application.logMessageReceived -= captureError;
                if (began && !committed)
                {
                    List<string> recoveryErrors;
                    try { recoveryErrors = transaction.Rollback(); }
                    catch (Exception exception) { recoveryErrors = new List<string> { exception.ToString() }; }
                    run.Errors.AddRange(recoveryErrors);
                    run.Transaction = recoveryErrors.Count == 0 ? PipelineTransactionStatus.RolledBack : PipelineTransactionStatus.RecoveryRequired;
                    if (recoveryErrors.Count > 0) run.RecoveryDirectory = transaction.DirectoryPath;
                    var catalogErrors = run.RestoreCatalogs?.Invoke() ?? new List<string>();
                    run.Errors.AddRange(catalogErrors);
                    if (catalogErrors.Count > 0) run.Transaction = PipelineTransactionStatus.RecoveryRequired;
                }
                formulaWarningHandler = previousWarnings;
                current = previous;
                executing = false;
                if (run.Transaction != PipelineTransactionStatus.RecoveryRequired) run.RestoreCatalogs = null;
                pipelineLog = run.Summary;
                foreach (string error in run.Errors) pipelineLog += "\n" + error;
            }
            return run;
        }

        internal List<string> RecoverTransaction(string directory)
        {
            var errors = PipelineAssetTransaction.Recover(directory);
            if (errors.Count > 0 || lastRun?.RecoveryDirectory != directory) return errors;
            lastRun.RecoveryDirectory = null;
            return RecoverCatalogState();
        }

        internal List<string> RecoverCatalogState()
        {
            var errors = lastRun?.RestoreCatalogs?.Invoke() ?? new List<string>();
            if (lastRun == null) return errors;
            lastRun.Transaction = errors.Count == 0 ? PipelineTransactionStatus.RolledBack : PipelineTransactionStatus.RecoveryRequired;
            if (errors.Count == 0) lastRun.RestoreCatalogs = null;
            pipelineLog = lastRun.Summary;
            foreach (string error in errors) pipelineLog += "\n" + error;
            return errors;
        }
    }
}
