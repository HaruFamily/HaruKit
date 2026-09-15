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

        private void OnEnable()
        {
            graph = serializedObject.FindProperty("graph");
        }

        // 資產的編輯入口只剩節點圖：原型群組在左欄的目錄庫編，動態產出由步驟寫進動態目錄節點。
        // Inspector 只留「開圖」「執行」「看結果」這三件事，避免同一份資料有第二個會打架的編輯路徑。
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var pipeline = (AssetPipeline)target;

            // 節點圖與驗證都在卡片上，這裡只留「執行」這顆真正會改資產的按鈕。
            EditorGUILayout.PropertyField(graph);
            DrawRunButton(pipeline);

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
