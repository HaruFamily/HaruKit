using System.Text;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    [CustomEditor(typeof(AssetPipeline))]
    public sealed class AssetPipelineEditor : UnityEditor.Editor
    {
        private const float RunHeight = 26f;

        private static readonly Color RunTint = new Color(0.5f, 0.85f, 0.55f);

        private SerializedProperty graph;
        private int selectedStep = -1;
        private Vector2 resultScroll;
        private System.Collections.Generic.List<string> recoveryDirectories = new();
        private double nextRecoveryScan;

        private void OnEnable()
        {
            graph = serializedObject.FindProperty("graph");
        }

        // 資產的編輯入口只剩節點圖：原型群組在左欄的目錄庫編，動態產出由動作寫進動態目錄節點。
        // Inspector 只留「開圖」「執行」「看結果」這三件事，避免同一份資料有第二個會打架的編輯路徑。
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var pipeline = (AssetPipeline)target;

            // 放棄遺失內容由圖內明確存檔處理，不在 Inspector 重繪時自動清除。
            if (DrawMissingTypes(pipeline)) return;

            // 節點圖與驗證都在卡片上，這裡只留「執行」這顆真正會改資產的按鈕。
            EditorGUILayout.PropertyField(graph);
            DrawRunButton(pipeline);

            if (serializedObject.ApplyModifiedProperties())
            {
                pipeline.MarkDirty();
                AssetDatabase.SaveAssets();
            }

            DrawLog(pipeline);
            DrawResults(pipeline);
            DrawRecovery(pipeline);
        }

        private bool DrawMissingTypes(AssetPipeline pipeline)
        {
            if (!UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline)) return false;
            var details = new StringBuilder();
            foreach (var missing in UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(pipeline))
                details.AppendLine($"{missing.namespaceName}.{missing.className} ({missing.assemblyName}), id={missing.referenceId}");

            EditorGUILayout.HelpBox("SO 含有遺失型別：\n" + details
                + "\n開啟圖時會自動清除遺失內容與相關失效連線，不備份、不詢問。"
                + "清理後維持正常驗證，其他錯誤必須修正才能存檔。",
                MessageType.Error);
            if (GUILayout.Button("開啟節點圖修正並存檔")) GraphDrawer.Open(pipeline);
            return true;
        }

        private void DrawResults(AssetPipeline pipeline)
        {
            var run = pipeline.LastRun;
            if (run != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"最近一次執行 · {run.StartedAt:HH:mm:ss}", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(run.Summary, run.Transaction == PipelineTransactionStatus.Committed ? MessageType.Info : MessageType.Warning);
                resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.MaxHeight(420f));
                foreach (var step in run.Steps)
                {
                    EditorGUILayout.BeginHorizontal();
                    bool open = EditorGUILayout.Foldout(selectedStep == step.Index,
                        $"{step.Index + 1}. {StepLabel(step.Status)} · {step.Name} · {step.Seconds:0.00}s", true);
                    if (open) selectedStep = step.Index;
                    else if (selectedStep == step.Index) selectedStep = -1;
                    DrawNavigate(pipeline, step.NodeId);
                    EditorGUILayout.EndHorizontal();
                    if (!open) continue;
                    foreach (string message in step.Messages) EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
                    foreach (var item in step.Items) DrawAsset(item, true);
                }
                DrawProperties(run);
                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawProperties(PipelineRunResult run)
        {
            if (run.Properties.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Property 目前值快照", EditorStyles.miniBoldLabel);
            foreach (PipelinePropertySnapshot property in run.Properties)
            {
                string source = property.HasValue ? "目前值" : "初始值";
                EditorGUILayout.LabelField($"{property.Name} · {property.TypeName} · {source}");
                EditorGUILayout.SelectableLabel(property.Value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static string StepLabel(PipelineStepStatus status) => status switch
        {
            PipelineStepStatus.Success => "成功",
            PipelineStepStatus.Skipped => "跳過",
            PipelineStepStatus.Partial => "部分完成",
            PipelineStepStatus.Failed => "失敗",
            _ => "未執行",
        };

        private static void DrawAsset(PipelineAssetRecord item, bool showStatus)
        {
            string label = (showStatus ? $"[{item.Status}] " : "") + $"{item.Name} · {item.TypeName}";
            if (GUILayout.Button(new GUIContent(label, item.Path), EditorStyles.linkLabel))
            {
                var asset = item.Resolve();
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            if (!string.IsNullOrEmpty(item.Path)) EditorGUILayout.SelectableLabel(item.Path, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (!string.IsNullOrEmpty(item.Message)) EditorGUILayout.LabelField(item.Message, EditorStyles.wordWrappedLabel);
        }

        private static void DrawNavigate(AssetPipeline pipeline, string nodeId)
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(nodeId)))
                if (GUILayout.Button("定位", GUILayout.Width(44f))) GraphDrawer.Navigate(pipeline, nodeId);
        }

        private void DrawRecovery(AssetPipeline pipeline)
        {
            if (pipeline.LastRun?.Transaction == PipelineTransactionStatus.RecoveryRequired
                && string.IsNullOrEmpty(pipeline.LastRun.RecoveryDirectory)
                && GUILayout.Button("重試回復目錄資料"))
            {
                var errors = pipeline.RecoverPropertyState();
                if (errors.Count > 0) Debug.LogError(string.Join("\n", errors));
            }
            if (EditorApplication.timeSinceStartup >= nextRecoveryScan)
            {
                recoveryDirectories = PipelineAssetTransaction.PendingRecoveryDirectories();
                nextRecoveryScan = EditorApplication.timeSinceStartup + 2d;
            }
            foreach (string directory in recoveryDirectories)
            {
                EditorGUILayout.HelpBox("尚未完成的 AP 資產交易：" + directory, MessageType.Error);
                if (!GUILayout.Button("重試回復此交易")) continue;
                var errors = pipeline.RecoverTransaction(directory);
                nextRecoveryScan = 0d;
                if (errors.Count > 0) Debug.LogError(string.Join("\n", errors));
                else Debug.Log("[AssetPipeline] 資產交易已回復。");
            }
        }

        private void DrawRunButton(AssetPipeline pipeline)
        {
            bool ready = pipeline.IsGraphValidationCurrent();
            var content = ready
                ? new GUIContent("執行管線", "依節點圖上的動作順序實際修改資產。")
                : new GUIContent("執行管線（需先驗證）", "卡片上的「驗證」通過之後才能執行；改過圖或資產就要再驗一次。");

            Color previous = GUI.backgroundColor;
            if (ready) GUI.backgroundColor = RunTint;
            using (new EditorGUI.DisabledScope(!ready))
                if (GUILayout.Button(content, GUILayout.Height(RunHeight))) pipeline.RunPipelineAssets();
            GUI.backgroundColor = previous;
        }

        private static void DrawLog(AssetPipeline pipeline)
        {
            string log = pipeline.PipelineLog;
            if (string.IsNullOrEmpty(log)) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(log, MessageType.None);
        }
    }
}
