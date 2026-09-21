using System;
using System.IO;
using System.Text;
using HaruFamily.DependencyCore.GraphKit;
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
        private bool showCatalogs;
        private bool showPreview;
        private Vector2 resultScroll;
        private System.Collections.Generic.List<string> recoveryDirectories = new();
        private double nextRecoveryScan;
        private string missingTypeBackupDirectory;
        private string missingTypeRepairError;

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

            if (!string.IsNullOrEmpty(missingTypeBackupDirectory))
            {
                EditorGUILayout.HelpBox("遺失型別修復備份：" + missingTypeBackupDirectory
                    + "\n備份是磁碟上的原檔，不含未儲存修改。請保留備份，重新開圖修正空節點後存檔。", MessageType.Info);
                if (GUILayout.Button("顯示修復備份")) EditorUtility.RevealInFinder(missingTypeBackupDirectory);
            }
            if (!string.IsNullOrEmpty(missingTypeRepairError))
                EditorGUILayout.HelpBox(missingTypeRepairError, MessageType.Error);
            // 不讓 Inspector 的套用／自動存檔繞過 Missing Type 保護。
            if (DrawMissingTypeRecovery(pipeline)) return;

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

        private bool DrawMissingTypeRecovery(AssetPipeline pipeline)
        {
            if (!UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline)) return false;
            var details = new StringBuilder();
            foreach (var missing in UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(pipeline))
                details.AppendLine($"{missing.namespaceName}.{missing.className} ({missing.assemblyName}), id={missing.referenceId}");

            EditorGUILayout.HelpBox("SO 本體含有遺失型別記錄；只刪除畫布節點不會清除這些記錄。\n"
                + details + "\n可直接開圖刪除或補接空節點，按存檔後確認備份並放棄遺失內容，保留目前圖的修改。"
                + "若只是改名或搬移型別，也可先修正型別遷移。下方獨立清除按鈕供無法開圖時使用。",
                MessageType.Error);
            if (GUILayout.Button("開啟節點圖修正並存檔")) GraphDrawer.Open(pipeline);
            if (!GUILayout.Button("備份並清除遺失型別記錄…")) return true;
            if (!EditorUtility.DisplayDialog("清除遺失型別記錄",
                $"對象：{AssetDatabase.GetAssetPath(pipeline)}\n\n{details}\n"
                + "會先將磁碟上的 .asset 與 .meta 備份至 Library/AssetPipelineMissingTypes，再清除上述遺失型別的序列化資料。"
                + "\n備份不含未儲存修改；已開啟的圖工作副本需捨棄並重新開啟。"
                + "\n其他有效內容保留；清除後不自動存檔，仍需補接或刪除空節點。\n\n確定放棄上述遺失型別的內容？",
                "備份並清除", "取消")) return true;

            if (!TryRepairMissingTypes(pipeline, out missingTypeBackupDirectory, out missingTypeRepairError))
            {
                Debug.LogError($"[AssetPipeline] '{AssetDatabase.GetAssetPath(pipeline)}' 遺失型別修復失敗："
                    + missingTypeRepairError + " 備份：" + missingTypeBackupDirectory, pipeline);
                return true;
            }
            Debug.Log($"[AssetPipeline] '{AssetDatabase.GetAssetPath(pipeline)}' 已清除遺失型別記錄，尚未存檔。"
                + $"請重新開圖修正空節點；原檔備份：{missingTypeBackupDirectory}", pipeline);
            serializedObject.Update();
            graph = serializedObject.FindProperty("graph");
            GUIUtility.ExitGUI();
            return true;
        }

        /// <summary>先備份磁碟原檔，再明確清除 Owner 的遺失型別記錄；不儲存修復結果。</summary>
        internal static bool TryRepairMissingTypes(AssetPipeline pipeline, out string backupDirectory, out string error)
        {
            backupDirectory = null;
            error = null;
            try
            {
                string path = AssetDatabase.GetAssetPath(pipeline);
                if (pipeline == null || !AssetDatabase.IsMainAsset(pipeline)
                    || !path.StartsWith("Assets/", StringComparison.Ordinal)
                    || !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只支援 Assets 內已儲存的 AssetPipeline 主資產。");
                if (AssetPipeline.CurrentAction != null)
                    throw new InvalidOperationException("管線執行中不可修復型別。");
                if (!UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline))
                    throw new InvalidOperationException("此 SO 沒有遺失型別記錄。");

                // 複製可還原的圖內容；替換文件引用使既有視窗的提交基準失效，避免舊工作副本覆寫修復。
                var repairedGraph = pipeline.graph == null ? null : GraphDeepCopy.Copy(pipeline.graph);
                if (pipeline.graph != null && repairedGraph == null)
                    throw new InvalidOperationException("無法複製有效圖內容，原資料未清除。");
                string source = Path.GetFullPath(path);
                if (!File.Exists(source) || !File.Exists(source + ".meta"))
                    throw new IOException("原 .asset 或 .meta 不存在，無法完整備份；原資料未清除。");
                backupDirectory = Path.GetFullPath(Path.Combine("Library", "AssetPipelineMissingTypes",
                    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(backupDirectory);
                string backup = Path.Combine(backupDirectory, Path.GetFileName(source));
                File.Copy(source, backup);
                File.Copy(source + ".meta", backup + ".meta");

                if (!UnityEditor.SerializationUtility.ClearAllManagedReferencesWithMissingTypes(pipeline)
                    || UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(pipeline))
                    throw new InvalidOperationException("Unity 未完成清除；已停止修復且未存檔，請保留原檔與備份。");
                pipeline.graph = repairedGraph;
                pipeline.graph?.MarkDirty();
                EditorUtility.SetDirty(pipeline);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
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
                showCatalogs = EditorGUILayout.Foldout(showCatalogs, "執行結束快照（回復前，Cell 僅列實際求值結果）", true);
                if (showCatalogs) DrawCatalogs(pipeline, run.Catalogs);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("刷新目前 Catalog／Cell 內容（使用已儲存圖）"))
            {
                pipeline.RefreshCatalogPreview();
                showPreview = true;
            }
            showPreview = EditorGUILayout.Foldout(showPreview, "手動刷新快照（不隨 Repaint 重算）", true);
            if (showPreview && pipeline.CatalogPreview != null) DrawCatalogs(pipeline, pipeline.CatalogPreview);
        }

        private static string StepLabel(PipelineStepStatus status) => status switch
        {
            PipelineStepStatus.Success => "成功",
            PipelineStepStatus.Skipped => "跳過",
            PipelineStepStatus.Partial => "部分完成",
            PipelineStepStatus.Failed => "失敗",
            _ => "未執行",
        };

        private static void DrawCatalogs(AssetPipeline pipeline, System.Collections.Generic.IReadOnlyList<PipelineCatalogSnapshot> catalogs)
        {
            if (catalogs.Count == 0) EditorGUILayout.LabelField("沒有目錄。");
            foreach (var catalog in catalogs)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{catalog.Name} · {catalog.State}", EditorStyles.boldLabel);
                DrawNavigate(pipeline, catalog.NodeId);
                EditorGUILayout.EndHorizontal();
                DrawValue(catalog.Contents);
                for (int i = 0; i < catalog.Cells.Count; i++)
                {
                    var cell = catalog.Cells[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Cell {i + 1}");
                    DrawNavigate(pipeline, cell.NodeId);
                    EditorGUILayout.EndHorizontal();
                    DrawValue(cell.Value);
                }
            }
        }

        private static void DrawValue(PipelineValueSnapshot value)
        {
            if (!string.IsNullOrEmpty(value.Value)) EditorGUILayout.LabelField(value.Value, EditorStyles.wordWrappedLabel);
            if (value.Observed && value.Assets.Count == 0 && value.Value == null) EditorGUILayout.LabelField("空資料");
            foreach (var item in value.Assets) DrawAsset(item, false);
        }

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
                var errors = pipeline.RecoverCatalogState();
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
            bool ready = pipeline.IsPrototypeSourceValidationCurrent();
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
            string log = string.IsNullOrEmpty(pipeline.PipelineLog) ? pipeline.PrototypeValidationLog : pipeline.PipelineLog;
            if (string.IsNullOrEmpty(log)) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(log, MessageType.None);
        }
    }
}
