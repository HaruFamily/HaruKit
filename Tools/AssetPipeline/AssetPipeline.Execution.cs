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
            // 值狀態與清單內容分開備份；清單依引用去重，回復原地增刪時保留所有別名。
            var propertiesBefore = new List<(GraphProperty Property, object Value, bool Written)>();
            var listsBefore = new Dictionary<object, List<object>>(ReferenceComparer.Instance);
            void CaptureList(object value)
            {
                if (value is not IList live || listsBefore.ContainsKey(live)) return;
                var items = new List<object>(live.Count);
                foreach (object item in live) items.Add(item);
                listsBefore.Add(live, items);
            }
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
                    // Set 後目前值與 Proto 初始內容可指向不同清單，兩者都可能被本次執行修改。
                    CaptureList(property.CurrentValue);
                    CaptureList(property.InitialValue);
                    propertiesBefore.Add((property, value, written));
                }
                began = true;
                run.RestoreProperties = () =>
                {
                    var errors = new List<string>();
                    foreach (var before in listsBefore)
                    {
                        try
                        {
                            // 先還原清單內容再換回指標：只換指標會把已被原地修改的清單留給仍持有它的讀取者。
                            var live = (IList)before.Key;
                            if (live.IsReadOnly)
                            {
                                bool unchanged = live.Count == before.Value.Count;
                                for (int i = 0; unchanged && i < live.Count; i++)
                                    unchanged = Equals(live[i], before.Value[i]);
                                if (!unchanged) throw new NotSupportedException("唯讀清單的內容已變更，無法原地回復。");
                                continue;
                            }
                            if (live.IsFixedSize)
                            {
                                for (int i = 0; i < before.Value.Count; i++) live[i] = before.Value[i];
                            }
                            else
                            {
                                live.Clear();
                                foreach (object item in before.Value) live.Add(item);
                            }
                        }
                        catch (Exception exception) { errors.Add("Property 回復失敗：" + exception.Message); }
                    }
                    foreach (var before in propertiesBefore)
                        before.Property.RestoreValue((before.Value, before.Written));
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
