using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    [CreateAssetMenu(fileName = "AssetPipelineGroup", menuName = "HaruFamily/Asset Pipeline/Asset Pipeline Group")]
    public class AssetPipelineGroup : ScriptableObject
    {
        public List<AssetPipeline> pipelines = new List<AssetPipeline>();

        [NonSerialized]
        private string groupLog = string.Empty;

        [NonSerialized]
        private List<bool> pipelineValidationResults = new List<bool>();

        [NonSerialized]
        private string validationSnapshot;

        internal void ValidateAll()
        {
            pipelineValidationResults.Clear();
            int passedCount = 0;
            int failedCount = 0;
            var sb = new StringBuilder();

            for (int i = 0; i < pipelines.Count; i++)
            {
                AssetPipeline pipeline = pipelines[i];
                if (pipeline == null)
                {
                    failedCount++;
                    pipelineValidationResults.Add(false);
                    sb.AppendLine($"[失敗] 管線[{i}] 為 null。");
                    continue;
                }

                bool passed = pipeline.ValidatePipelinePrototypeSources();
                pipelineValidationResults.Add(passed);
                if (passed)
                {
                    passedCount++;
                    sb.AppendLine($"[通過] {pipeline.name}");
                    sb.AppendLine(pipeline.PrototypeValidationLog);
                }
                else
                {
                    failedCount++;
                    sb.AppendLine($"[失敗] {pipeline.name}");
                    sb.AppendLine(pipeline.PrototypeValidationLog);
                }
            }

            validationSnapshot = EditorJsonUtility.ToJson(this);
            groupLog = $"群組驗證完成：通過 {passedCount}，失敗 {failedCount}。\n{sb}";
            Debug.Log($"[AssetPipelineGroup] {groupLog}");
        }

        internal void RunAll()
        {
            if (!CanRunValidatedPipelines()) return;

            int validCount = GetPassedPipelineCount();
            bool confirmed = EditorUtility.DisplayDialog(
                "執行 AssetPipelineGroup",
                $"確定執行 {validCount} 個 AssetPipeline？\n此操作會實際修改資產，無法自動復原（請確認已 commit）。",
                "執行",
                "取消");

            if (!confirmed) return;

            ExecuteAll("群組執行");
        }

        private void ExecuteAll(string actionName)
        {
            int successCount = 0;
            int errorCount = 0;
            int skippedCount = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"[AssetPipelineGroup] {actionName}開始：{name}");

            for (int i = 0; i < pipelines.Count; i++)
            {
                AssetPipeline pipeline = pipelines[i];
                if (pipeline == null)
                {
                    skippedCount++;
                    sb.AppendLine("[SKIP] AssetPipeline 為 null。");
                    continue;
                }

                if (!pipelineValidationResults[i] || !pipeline.IsPrototypeSourceValidationCurrent())
                {
                    skippedCount++;
                    sb.AppendLine($"[SKIP] {pipeline.name} 未通過目前驗證。");
                    continue;
                }

                try
                {
                    string log = pipeline.ExecutePipelineAssets(actionName);
                    successCount++;
                    sb.AppendLine($"[OK] {pipeline.name}");
                    sb.AppendLine(log);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    sb.AppendLine($"[ERROR] {pipeline.name}\n{ex}");
                    Debug.LogError($"[AssetPipelineGroup] {pipeline.name} 執行失敗\n{ex}");
                }
            }

            groupLog = $"群組執行完成：成功 {successCount}，跳過 {skippedCount}，錯誤 {errorCount}。\n{sb}";
            Debug.Log($"[AssetPipelineGroup] {groupLog}");
            AssetDatabase.SaveAssets();
        }

        private bool IsGroupValidationCurrent()
        {
            if (string.IsNullOrEmpty(validationSnapshot)) return false;
            if (validationSnapshot != EditorJsonUtility.ToJson(this)) return false;
            if (pipelineValidationResults == null || pipelineValidationResults.Count != pipelines.Count) return false;

            for (int i = 0; i < pipelines.Count; i++)
                if (pipelineValidationResults[i] && (pipelines[i] == null || !pipelines[i].IsPrototypeSourceValidationCurrent())) return false;

            return true;
        }

        internal bool CanRunValidatedPipelines()
        {
            return IsGroupValidationCurrent() && GetPassedPipelineCount() > 0;
        }

        internal string GroupLog => groupLog;

        private int GetPassedPipelineCount()
        {
            if (pipelineValidationResults == null) return 0;

            int count = 0;
            foreach (bool passed in pipelineValidationResults)
                if (passed) count++;

            return count;
        }
    }
}
