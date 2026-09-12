using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    public partial class AssetPipeline
    {
        /// <summary>管線執行前後清除動態資產的時機。</summary>
        public enum DynamicClearTiming
        {
            None,
            Before,
            After,
            Both
        }

        public DynamicClearTiming dynamicClearTiming = DynamicClearTiming.None;

        // 管線資產執行期的回報緩衝；透過 current 讓 Execute() 寫入 pipelineLog
        internal StringBuilder pipelineReport;

        [NonSerialized]
        private string prototypeValidationSnapshot;

        /// <summary>管線資產回報訊息：寫入 pipelineLog 並輸出 Console。</summary>
        public static void Report(string message)
        {
            Debug.Log(message);
            current?.pipelineReport?.AppendLine(message);
        }

        internal bool VerifyPipelineAssets()
        {
            return ValidatePipelinePrototypeSources();
        }

        internal void RunPipelineAssets()
        {
            // 二次確認，防止誤觸實際修改資產
            bool confirmed = EditorUtility.DisplayDialog(
                "執行管線",
                $"確定執行 {graph.Steps.Count} 個管線步驟？\n此操作會實際修改資產，無法自動復原（請確認已 commit）。",
                "執行",
                "取消");

            if (!confirmed) return;

            ExecutePipelineAssets("執行");
        }

        internal string ExecutePipelineAssets(string actionName)
        {
            if (!IsPrototypeSourceValidationCurrent())
            {
                pipelineLog = $"{actionName}已鎖定：請先驗證管線原型資產來源。";
                Debug.LogWarning($"[AssetPipeline] {pipelineLog}");
                return pipelineLog;
            }

            int successCount = 0;
            int errorCount = 0;
            int formulaWarningCount = 0;
            var formulaWarnings = new StringBuilder();
            Action<string> previousFormulaWarningHandler = formulaWarningHandler;
            AssetPipeline previousCurrent = current;
            current = this;
            pipelineReport = new StringBuilder();
            formulaWarningHandler = message =>
            {
                formulaWarningCount++;
                formulaWarnings.AppendLine(message);
            };

            if (dynamicClearTiming == DynamicClearTiming.Before || dynamicClearTiming == DynamicClearTiming.Both)
            {
                dynamicAssets.Clear();
                pipelineReport.AppendLine("執行前已清除動態資產。");
            }

            try
            {
                // 順序仍然是唯一真相：節點圖只換了編輯方式，步驟還是嚴格依 root 底下的清單順序跑。
                foreach (APActionSlot step in graph.Steps)
                {
                    if (step == null)
                    {
                        errorCount++;
                        string message = $"[AssetPipeline] {actionName} 管線步驟失敗：元素為 null。";
                        Debug.LogWarning(message);
                        pipelineReport.AppendLine(message);
                        continue;
                    }

                    try
                    {
                        // 停用、空槽、型別不符回 false，那是「跳過」不是「成功」。
                        if (step.Execute()) successCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        string message = $"[AssetPipeline] {actionName} 管線步驟失敗：{step.DisplayName}\n{ex}";
                        Debug.LogError(message);
                        pipelineReport.AppendLine(message);
                    }
                }
            }
            finally
            {
                formulaWarningHandler = previousFormulaWarningHandler;
                current = previousCurrent;
            }

            if (dynamicClearTiming == DynamicClearTiming.After || dynamicClearTiming == DynamicClearTiming.Both)
            {
                dynamicAssets.Clear();
                pipelineReport.AppendLine("執行後已清除動態資產。");
            }

            pipelineLog = $"管線{actionName}完成：成功 {successCount}，錯誤 {errorCount}，公式警告 {formulaWarningCount}。";
            if (pipelineReport.Length > 0)
                pipelineLog += $"\n{pipelineReport}";
            if (formulaWarningCount > 0)
                pipelineLog += $"\n{formulaWarnings}";

            pipelineReport = null;
            Debug.Log($"[AssetPipeline] {pipelineLog}");
            return pipelineLog;
        }

        internal bool IsPrototypeSourceValidationCurrent()
        {
            return !string.IsNullOrEmpty(prototypeValidationSnapshot)
                && prototypeValidationSnapshot == EditorJsonUtility.ToJson(this);
        }

        private void MarkPrototypeSourceValidationPassed()
        {
            prototypeValidationSnapshot = EditorJsonUtility.ToJson(this);
        }

        private void ClearPrototypeSourceValidation()
        {
            prototypeValidationSnapshot = null;
        }
    }
}
