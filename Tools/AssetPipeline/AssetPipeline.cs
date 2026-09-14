using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using HaruFamily.DependencyCore.GraphKit;

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
        /// <summary>目前正在執行的管線。步驟與公式靠它讀資產群組、登記 dynamic 資產。</summary>
        // 步驟的具體實作住在使用端專案，所以這幾個給步驟用的成員必須是 public，不是 internal。
        public static AssetPipeline current;

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

        /// <summary>
        /// 管線的節點圖。編輯器靠「欄位型別實作 IGraphDocument」找到它，不必認識 AssetPipeline。
        /// </summary>
        public APGraph graph = new APGraph();

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

        /// <summary>公式回報一則警告：寫進本次執行的公式警告區並輸出 Console。</summary>
        public static void ReportFormulaWarning(string message)
        {
            Debug.LogWarning($"[AssetPipeline] {message}");
            formulaWarningHandler?.Invoke(message);
        }
    }
}
