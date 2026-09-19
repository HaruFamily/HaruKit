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
        /// <summary>目前正在執行的管線。動作與公式靠它讀資產群組、登記 dynamic 資產。</summary>
        // 使用端可讀取目前管線；執行與預覽的狀態切換由 AP 組件管理。
        public static AssetPipeline current { get; internal set; }

        /// <summary>每執行一次管線加一，供執行紀錄識別輪次。</summary>
        public static int RunToken { get; private set; }

        internal static void BeginRun() => RunToken++;

        public static PipelineActionContext CurrentAction { get; internal set; }

        [NonSerialized] private PipelineRunResult lastRun;
        public PipelineRunResult LastRun => lastRun;
        [NonSerialized] private List<PipelineCatalogSnapshot> catalogPreview;
        public IReadOnlyList<PipelineCatalogSnapshot> CatalogPreview => catalogPreview;

        public void RefreshCatalogPreview()
        {
            AssetPipeline previous = current;
            current = this;
            try { catalogPreview = PipelineCatalogSnapshot.Capture(graph, new Dictionary<CatalogCell, PipelineValueSnapshot>(), true); }
            finally { current = previous; }
        }

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
        public Graph graph = new Graph();

        [NonSerialized]
        private string pipelineLog = string.Empty;

        [FormerlySerializedAs("groups")]
        public List<AssetPipelineAssetGroup> prototypeAssets = new List<AssetPipelineAssetGroup>();

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
