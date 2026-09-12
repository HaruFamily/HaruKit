using HaruFamily.Framework.LogicGraph.Editor;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    [CustomEditor(typeof(AssetPipeline))]
    public sealed class AssetPipelineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var pipeline = (AssetPipeline)target;

            EditorGUILayout.LabelField("Asset Pipeline", EditorStyles.boldLabel);
            // 節點圖視窗來自 GraphKit：它靠「欄位型別實作 IGraphDocument」認出 pipeline.graph，
            // 不必知道 AssetPipeline 是什麼。
            if (GUILayout.Button("Open Graph", GUILayout.Height(28f)))
                LogicGraphWindow.OpenFor(pipeline);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate"))
            {
                pipeline.VerifyPipelineAssets();
                Repaint();
            }

            using (new EditorGUI.DisabledScope(!pipeline.IsPrototypeSourceValidationCurrent()))
                if (GUILayout.Button("Run")) pipeline.RunPipelineAssets();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dynamicClearTiming"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prototypeAssets"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dynamicAssets"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Prototype Assets", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("collectKey"));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Collect Selection")) RunAssetAction(pipeline.CollectSelection);
            if (GUILayout.Button("Replace Selection")) RunAssetAction(pipeline.ReplaceSelection);
            if (GUILayout.Button("Replace By Type")) RunAssetAction(pipeline.ReplaceSelectionByType);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("clearKey"));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh")) RunAssetAction(pipeline.RefreshAllAssetInfo);
            if (GUILayout.Button("Validate Assets")) RunAssetAction(pipeline.ValidateAssets);
            if (GUILayout.Button("Clear Key")) RunAssetAction(pipeline.ClearOperationKey);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Merge Dynamic To Prototype")) RunAssetAction(pipeline.MergeDynamicAssetsToPrototypeAssets);
            if (GUILayout.Button("Clear Prototype")) RunAssetAction(pipeline.ClearPrototypeAssets);
            if (GUILayout.Button("Clear Dynamic")) RunAssetAction(pipeline.ClearDynamicAssets);
            EditorGUILayout.EndHorizontal();

            if (serializedObject.ApplyModifiedProperties())
            {
                pipeline.MarkDirty();
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(pipeline.PipelineLog) ? pipeline.PrototypeValidationLog : pipeline.PipelineLog, MessageType.None);
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

    [CustomEditor(typeof(AssetPipelineGroup))]
    public sealed class AssetPipelineGroupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var group = (AssetPipelineGroup)target;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pipelines"), true);
            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(group);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate All", GUILayout.Height(26f)))
            {
                group.ValidateAll();
                Repaint();
            }
            using (new EditorGUI.DisabledScope(!group.CanRunValidatedPipelines()))
                if (GUILayout.Button("Run All", GUILayout.Height(26f))) group.RunAll();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(group.GroupLog, MessageType.None);
        }
    }
}
