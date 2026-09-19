using System;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    public partial class AssetPipeline
    {
        [NonSerialized]
        private string prototypeValidationSnapshot;

        /// <summary>管線資產回報訊息：寫入 pipelineLog 並輸出 Console。</summary>
        public static void Report(string message)
        {
            Debug.Log(message);
            CurrentAction?.Result.Message(message);
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
                $"依已儲存圖執行 {graph.Actions.Count} 個管線動作？\n內建資產操作共用一份交易，失敗時回復本次變更。",
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

            PipelineRunResult result = RunPipeline();
            pipelineLog = result.Summary;
            foreach (string error in result.Errors) pipelineLog += "\n" + error;
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
