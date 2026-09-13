using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
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
