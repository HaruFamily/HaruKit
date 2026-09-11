using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline.Editor
{
    public sealed class AssetPipelineGraphWindow : EditorWindow
    {
        private AssetPipeline pipeline;
        private SerializedObject serializedPipeline;
        private int selectedIndex = -1;
        private Vector2 graphScroll;
        private Vector2 inspectorScroll;

        [MenuItem("HaruFamily/Asset Pipeline/Graph")]
        private static void OpenSelected()
        {
            AssetPipeline selected = Selection.activeObject as AssetPipeline;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Asset Pipeline", "請先選取 AssetPipeline asset。", "OK");
                return;
            }
            Open(selected);
        }

        public static void Open(AssetPipeline target)
        {
            AssetPipelineGraphWindow window = GetWindow<AssetPipelineGraphWindow>("Asset Pipeline Graph");
            window.SetPipeline(target);
            window.Show();
        }

        private void OnEnable()
        {
            if (pipeline != null) serializedPipeline = new SerializedObject(pipeline);
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (pipeline == null)
            {
                EditorGUILayout.HelpBox("Select an AssetPipeline in the Project window, then open this window again.", MessageType.Info);
                return;
            }

            if (serializedPipeline == null || serializedPipeline.targetObject == null)
                serializedPipeline = new SerializedObject(pipeline);
            serializedPipeline.Update();

            PipelineGraphAnalysis analysis = PipelineGraphAnalyzer.Analyze(pipeline);
            EditorGUILayout.BeginHorizontal();
            DrawGraph(analysis);
            DrawInspector();
            EditorGUILayout.EndHorizontal();

            if (serializedPipeline.ApplyModifiedProperties())
            {
                pipeline.MarkDirty();
                AssetDatabase.SaveAssets();
                Repaint();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            AssetPipeline selected = (AssetPipeline)EditorGUILayout.ObjectField(pipeline, typeof(AssetPipeline), false, GUILayout.Width(260f));
            if (selected != pipeline) SetPipeline(selected);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Step", EditorStyles.toolbarDropDown, GUILayout.Width(90f))) ShowAddMenu();
            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70f)) && pipeline != null)
            {
                pipeline.VerifyPipelineAssets();
                Repaint();
            }
            using (new EditorGUI.DisabledScope(pipeline == null || !pipeline.IsPrototypeSourceValidationCurrent()))
                if (GUILayout.Button("Run", EditorStyles.toolbarButton, GUILayout.Width(70f))) pipeline.RunPipelineAssets();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraph(PipelineGraphAnalysis analysis)
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(420f));
            graphScroll = EditorGUILayout.BeginScrollView(graphScroll);
            for (int i = 0; i < analysis.Steps.Count; i++)
            {
                PipelineGraphStepAnalysis step = analysis.Steps[i];
                GUIStyle style = new GUIStyle(EditorStyles.helpBox);
                if (i == selectedIndex) style.normal.background = Texture2D.grayTexture;

                EditorGUILayout.BeginVertical(style);
                if (GUILayout.Button($"{i + 1}. {ManagedReferencePropertyGUI.Nicify(step.Name)}", EditorStyles.boldLabel)) selectedIndex = i;
                DrawKeys("Prototype In", step.PrototypeInputs);
                DrawKeys("Dynamic In", step.DynamicInputs);
                DrawKeys("Dynamic Out", step.DynamicOutputs);
                foreach (string warning in step.Warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                EditorGUILayout.EndVertical();

                if (i < analysis.Steps.Count - 1)
                    EditorGUILayout.LabelField("↓", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawInspector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(Mathf.Max(330f, position.width * 0.42f)));
            EditorGUILayout.LabelField("Step Properties", EditorStyles.boldLabel);
            if (selectedIndex < 0 || selectedIndex >= pipeline.pipelineAssets.Count)
            {
                EditorGUILayout.HelpBox("Select a step card.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedIndex == 0))
                if (GUILayout.Button("Move Up")) MoveStep(selectedIndex, selectedIndex - 1);
            using (new EditorGUI.DisabledScope(selectedIndex >= pipeline.pipelineAssets.Count - 1))
                if (GUILayout.Button("Move Down")) MoveStep(selectedIndex, selectedIndex + 1);
            if (GUILayout.Button("Delete")) DeleteStep();
            EditorGUILayout.EndHorizontal();

            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            SerializedProperty steps = serializedPipeline.FindProperty("pipelineAssets");
            if (selectedIndex >= 0 && selectedIndex < steps.arraySize)
                ManagedReferencePropertyGUI.Draw(steps.GetArrayElementAtIndex(selectedIndex));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            List<Type> types = ManagedReferencePropertyGUI.GetConcreteTypes(typeof(IPipelineAsset));
            if (types.Count == 0) menu.AddDisabledItem(new GUIContent("No concrete steps found"));
            foreach (Type type in types)
            {
                Type stepType = type;
                menu.AddItem(new GUIContent(ManagedReferencePropertyGUI.Nicify(type.Name)), false, () => AddStep(stepType));
            }
            menu.ShowAsContext();
        }

        private void AddStep(Type type)
        {
            if (pipeline == null) return;
            try
            {
                Undo.RecordObject(pipeline, "Add Pipeline Step");
                pipeline.pipelineAssets.Add((IPipelineAsset)Activator.CreateInstance(type));
                selectedIndex = pipeline.pipelineAssets.Count - 1;
                PersistPipeline();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssetPipelineGraph] 無法建立 {type.FullName}\n{ex}");
            }
        }

        private void MoveStep(int from, int to)
        {
            serializedPipeline.ApplyModifiedProperties();
            Undo.RecordObject(pipeline, "Move Pipeline Step");
            IPipelineAsset step = pipeline.pipelineAssets[from];
            pipeline.pipelineAssets.RemoveAt(from);
            pipeline.pipelineAssets.Insert(to, step);
            selectedIndex = to;
            PersistPipeline();
        }

        private void DeleteStep()
        {
            Undo.RecordObject(pipeline, "Delete Pipeline Step");
            pipeline.pipelineAssets.RemoveAt(selectedIndex);
            selectedIndex = Mathf.Min(selectedIndex, pipeline.pipelineAssets.Count - 1);
            PersistPipeline();
        }

        private void PersistPipeline()
        {
            pipeline.MarkDirty();
            AssetDatabase.SaveAssets();
            serializedPipeline = new SerializedObject(pipeline);
            Repaint();
        }

        private void SetPipeline(AssetPipeline target)
        {
            pipeline = target;
            serializedPipeline = pipeline == null ? null : new SerializedObject(pipeline);
            selectedIndex = pipeline != null && pipeline.pipelineAssets.Count > 0 ? 0 : -1;
            Repaint();
        }

        private static void DrawKeys(string label, List<string> keys)
        {
            EditorGUILayout.LabelField(label, keys.Count == 0 ? "-" : string.Join(", ", keys));
        }
    }
}
