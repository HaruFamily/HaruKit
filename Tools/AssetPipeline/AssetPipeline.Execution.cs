using System;
using System.Collections;
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
            // Property 的執行前狀態。Live 是當時的清單實體，Items 是它當時的內容——
            // 只記目前值的引用回復不了「原地增刪」，而仍持有同一份引用的讀取者看的就是那個實體。
            var propertiesBefore = new List<(GraphProperty Property, object Value, bool Written, IList Live, List<object> Items)>();
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
                if (!VerifyGraph())
                {
                    run.Errors.Add(PipelineLog);
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

                foreach (GraphProperty property in GraphPropertyWalk.Collect(graph))
                {
                    (object value, bool written) = property.CaptureValue();
                    // 讀 CurrentValue 而不是 value：未寫入時目前值是初始內容，而 Proto 的初始清單
                    // 與目前值共用引用，本次執行的原地修改一樣會改到它。
                    var live = property.CurrentValue as IList;
                    List<object> items = null;
                    if (live != null)
                    {
                        items = new List<object>(live.Count);
                        foreach (object item in live) items.Add(item);
                    }
                    propertiesBefore.Add((property, value, written, live, items));
                }
                began = true;
                run.RestoreProperties = () =>
                {
                    var errors = new List<string>();
                    foreach (var before in propertiesBefore)
                    {
                        try
                        {
                            // 先還原清單內容再換回指標：只換指標會把已被原地修改的清單留給仍持有它的讀取者。
                            if (before.Live != null && before.Items != null)
                            {
                                before.Live.Clear();
                                foreach (object item in before.Items) before.Live.Add(item);
                            }
                            before.Property.RestoreValue((before.Value, before.Written));
                        }
                        catch (Exception exception) { errors.Add("Property 回復失敗：" + exception.Message); }
                    }
                    return errors;
                };
                BeginRun();
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
                    CurrentAction = new PipelineActionContext(transaction, step);
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
                    var propertyErrors = run.RestoreProperties?.Invoke() ?? new List<string>();
                    run.Errors.AddRange(propertyErrors);
                    if (propertyErrors.Count > 0) run.Transaction = PipelineTransactionStatus.RecoveryRequired;
                }
                formulaWarningHandler = previousWarnings;
                current = previous;
                executing = false;
                if (run.Transaction != PipelineTransactionStatus.RecoveryRequired) run.RestoreProperties = null;
                run.CaptureProperties(GraphPropertyWalk.Collect(graph));
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
            return RecoverPropertyState();
        }

        internal List<string> RecoverPropertyState()
        {
            var errors = lastRun?.RestoreProperties?.Invoke() ?? new List<string>();
            if (lastRun == null) return errors;
            lastRun.Transaction = errors.Count == 0 ? PipelineTransactionStatus.RolledBack : PipelineTransactionStatus.RecoveryRequired;
            if (errors.Count == 0) lastRun.RestoreProperties = null;
            lastRun.CaptureProperties(GraphPropertyWalk.Collect(graph));
            pipelineLog = lastRun.Summary;
            foreach (string error in errors) pipelineLog += "\n" + error;
            return errors;
        }
    }
}
