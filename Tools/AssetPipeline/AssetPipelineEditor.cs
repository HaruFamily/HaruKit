using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    [CustomEditor(typeof(AssetPipeline))]
    public sealed class AssetPipelineEditor : UnityEditor.Editor
    {
        private const string FoldoutPrefix = "HaruFamily.AssetPipeline.Inspector.";
        private const float RunHeight = 26f;

        private static readonly Color RunTint = new Color(0.5f, 0.85f, 0.55f);
        private static readonly Color DangerTint = new Color(1f, 0.5f, 0.5f);

        private SerializedProperty graph;
        private SerializedProperty collectKey;
        private SerializedProperty clearKey;
        private SerializedProperty prototypeAssets;
        private SerializedProperty dynamicAssets;
        private SerializedProperty dynamicClearTiming;

        private void OnEnable()
        {
            graph = serializedObject.FindProperty("graph");
            collectKey = serializedObject.FindProperty("collectKey");
            clearKey = serializedObject.FindProperty("clearKey");
            prototypeAssets = serializedObject.FindProperty("prototypeAssets");
            dynamicAssets = serializedObject.FindProperty("dynamicAssets");
            dynamicClearTiming = serializedObject.FindProperty("dynamicClearTiming");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var pipeline = (AssetPipeline)target;

            // 節點圖與驗證都在卡片上，這裡只留「執行」這顆真正會改資產的按鈕。
            EditorGUILayout.PropertyField(graph);
            DrawRunButton(pipeline);

            DrawPrototypeSection(pipeline);
            DrawDynamicSection(pipeline);
            DrawMaintenanceSection(pipeline);

            if (serializedObject.ApplyModifiedProperties())
            {
                pipeline.MarkDirty();
                AssetDatabase.SaveAssets();
            }

            DrawLog(pipeline);
        }

        private void DrawRunButton(AssetPipeline pipeline)
        {
            bool ready = pipeline.IsPrototypeSourceValidationCurrent();
            var content = ready
                ? new GUIContent("執行管線", "依節點圖上的步驟順序實際修改資產。")
                : new GUIContent("執行管線（需先驗證）", "卡片上的「驗證」通過之後才能執行；改過圖或資產就要再驗一次。");

            Color previous = GUI.backgroundColor;
            if (ready) GUI.backgroundColor = RunTint;
            using (new EditorGUI.DisabledScope(!ready))
                if (GUILayout.Button(content, GUILayout.Height(RunHeight))) pipeline.RunPipelineAssets();
            GUI.backgroundColor = previous;
        }

        private void DrawPrototypeSection(AssetPipeline pipeline)
        {
            if (Section("原型資產"))
            {
                EditorGUILayout.PropertyField(collectKey, new GUIContent("收集 Key"));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("收集選取")) RunAssetAction(pipeline.CollectSelection);
                if (GUILayout.Button("替換選取")) RunAssetAction(pipeline.ReplaceSelection);
                if (GUILayout.Button("依型別替換")) RunAssetAction(pipeline.ReplaceSelectionByType);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(clearKey, new GUIContent("清除 Key"));
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = DangerTint;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("清除這個 Key")) RunAssetAction(pipeline.ClearOperationKey);
                if (GUILayout.Button("清空全部原型")) RunAssetAction(pipeline.ClearPrototypeAssets);
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = previous;

                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(prototypeAssets, new GUIContent("原型群組"), true);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDynamicSection(AssetPipeline pipeline)
        {
            if (Section("動態資產"))
            {
                EditorGUILayout.PropertyField(dynamicClearTiming, new GUIContent("清除時機"));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("併入原型")) RunAssetAction(pipeline.MergeDynamicAssetsToPrototypeAssets);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = DangerTint;
                if (GUILayout.Button("清空動態")) RunAssetAction(pipeline.ClearDynamicAssets);
                GUI.backgroundColor = previous;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(dynamicAssets, new GUIContent("動態群組"), true);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMaintenanceSection(AssetPipeline pipeline)
        {
            if (Section("維護"))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("刷新資產資訊")) RunAssetAction(pipeline.RefreshAllAssetInfo);
                if (GUILayout.Button("檢查資產有效性")) RunAssetAction(pipeline.ValidateAssets);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawLog(AssetPipeline pipeline)
        {
            string log = string.IsNullOrEmpty(pipeline.PipelineLog) ? pipeline.PrototypeValidationLog : pipeline.PipelineLog;
            if (string.IsNullOrEmpty(log)) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(log, MessageType.None);
        }

        /// <summary>分區標題。展開狀態存在 EditorPrefs，換選取或重編譯都不會全部彈回來。</summary>
        // BeginFoldoutHeaderGroup 必須配一次 EndFoldoutHeaderGroup，收起時也要配。
        private static bool Section(string title)
        {
            string key = FoldoutPrefix + title;
            bool open = EditorPrefs.GetBool(key, false);
            bool next = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (next != open) EditorPrefs.SetBool(key, next);
            return next;
        }

        private void RunAssetAction(System.Action action)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "Edit Asset Pipeline");
            action();
            AssetDatabase.SaveAssets();
            serializedObject.Update();
            Repaint();
        }
    }
}
