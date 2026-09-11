using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// Asset pipeline editor tool entry and operation panel.
    /// </summary>
    [CreateAssetMenu(fileName = "AssetPipeline", menuName = "HaruFamily/Asset Pipeline/Asset Pipeline")]
    public partial class AssetPipeline : ScriptableObject
    {
        private const string DefaultAssetPath = "Assets/Editor/HaruFamily/AssetPipeline/AssetPipeline.asset";
        internal static Action<string> formulaWarningHandler;
        internal static AssetPipeline current;

        [MenuItem("HaruFamily/Asset Pipeline/Open")]
        private static void OpenTool()
        {
            var tool = AssetDatabase.LoadAssetAtPath<AssetPipeline>(DefaultAssetPath);
            if (tool == null)
            {
                string dir = Path.GetDirectoryName(DefaultAssetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                tool = CreateInstance<AssetPipeline>();
                AssetDatabase.CreateAsset(tool, DefaultAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Selection.activeObject = tool;
            EditorGUIUtility.PingObject(tool);
        }

        public string collectKey = "Default";

        public string clearKey = "Default";

        [SerializeReference]
        public List<IPipelineAsset> pipelineAssets = new List<IPipelineAsset>();

        [NonSerialized]
        private string pipelineLog = string.Empty;

        [FormerlySerializedAs("groups")]
        public List<AssetPipelineAssetGroup> prototypeAssets = new List<AssetPipelineAssetGroup>();

        public List<AssetPipelineAssetGroup> dynamicAssets = new List<AssetPipelineAssetGroup>();

        [NonSerialized]
        private string assetLog = string.Empty;

        internal string PrototypeValidationLog => assetLog;
        internal string PipelineLog => pipelineLog;

        internal void MarkDirty()
        {
            EditorUtility.SetDirty(this);
        }

        internal static void ReportFormulaWarning(string message)
        {
            Debug.LogWarning($"[AssetPipeline] {message}");
            formulaWarningHandler?.Invoke(message);
        }
    }
}
