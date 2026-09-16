using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    public partial class AssetPipeline
    {
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
                $"確定執行 {graph.Actions.Count} 個管線動作？\n此操作會實際修改資產，無法自動復原（請確認已 commit）。",
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
            // 換一個執行號：動態目錄節點靠它把上一次留下的內容當成空的，不必先走訪整張圖清一遍。
            BeginRun();
            // 格子的 owner 不序列化，Domain Reload 之後是 null。走一次驗證的走訪把它們接回母目錄，
            // 順便確保這次執行用的是最新的圖。
            List<string> graphErrors = GraphVerifier.Collect(graph);
            if (graphErrors.Count > 0)
            {
                // 擋執行看這一趟算出來的結果，不看圖上存下來的 IsValidated：那個旗標跟著資料序列化，
                // 換一版程式、外部改過資產之後仍然是「已驗證」，而它從來沒有擋在執行路徑上。
                current = previousCurrent;
                var report = new StringBuilder($"{actionName}已鎖定：節點圖驗證未通過：");
                foreach (string error in graphErrors) report.Append('\n').Append("  • ").Append(error);

                pipelineLog = report.ToString();
                Debug.LogError($"[AssetPipeline] {pipelineLog}");
                return pipelineLog;
            }

            pipelineReport = new StringBuilder();
            formulaWarningHandler = message =>
            {
                formulaWarningCount++;
                formulaWarnings.AppendLine(message);
            };

            try
            {
                // 順序仍然是唯一真相：節點圖只換了編輯方式，動作還是嚴格依 root 底下的清單順序跑。
                foreach (ActionSlot action in graph.Actions)
                {
                    if (action == null)
                    {
                        errorCount++;
                        string message = $"[AssetPipeline] {actionName} 管線動作失敗：元素為 null。";
                        Debug.LogWarning(message);
                        pipelineReport.AppendLine(message);
                        continue;
                    }

                    try
                    {
                        // 停用、空槽、型別不符回 false，那是「跳過」不是「成功」。
                        if (action.Execute()) successCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        string message = $"[AssetPipeline] {actionName} 管線動作失敗：{action.DisplayName}\n{ex}";
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
