using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit.Editor;
using HaruFamily.Framework.LogicGraph.Editor;
using HaruFamily.Tools.AssetPipeline;
using HaruKitEvi.Fixtures.Logic;
using HaruKitEvi.Fixtures.Plain;
using HaruKitEvi.PipelineSteps;
using UnityEditor;
using UnityEngine;

namespace HaruKitEvi.FixtureTools
{
    /// <summary>
    /// 開發宿主專案自己的頂層選單（不放進 HaruFamily）：建立／重置範例資產、開啟對應視窗、
    /// 在 Edit Mode 直接觸發 LogicGraph 範例。
    /// </summary>
    internal static class FixtureMenu
    {
        private const string MenuRoot = "HaruKitEvi/Fixtures/";
        private const string DataFolder = "Assets/Fixtures/Data";
        private const string LogicPath = DataFolder + "/LogicSample.asset";
        private const string PlainPath = DataFolder + "/PlainSample.asset";
        private const string PipelinePath = DataFolder + "/PipelineSample.asset";
        private const string CounterPath = DataFolder + "/PipelineCounter.asset";

        [MenuItem(MenuRoot + "Create or Reset All Samples", false, 0)]
        private static void CreateOrResetAll()
        {
            bool exists = File.Exists(LogicPath) || File.Exists(PlainPath) || File.Exists(PipelinePath);
            if (exists && !EditorUtility.DisplayDialog("重置範例資產",
                    "會覆寫 " + DataFolder + " 內範例資產的圖內容與計數器。\n請先關閉正在編輯這些資產的節點圖視窗。",
                    "重置", "取消"))
                return;

            EnsureFolder(DataFolder);

            var logic = LoadOrCreate<EviGraphDefinition>(LogicPath);
            LogicSampleBuilder.Fill(logic);
            EditorUtility.SetDirty(logic);

            var plain = LoadOrCreate<PlainGraphOwner>(PlainPath);
            PlainSampleBuilder.Fill(plain);
            EditorUtility.SetDirty(plain);

            var counter = LoadOrCreate<PipelineCounter>(CounterPath);
            counter.value = 0;
            EditorUtility.SetDirty(counter);

            var pipeline = LoadOrCreate<AssetPipeline>(PipelinePath);
            PipelineSampleBuilder.Fill(pipeline, counter);
            EditorUtility.SetDirty(pipeline);

            AssetDatabase.SaveAssets();

            bool verified = false;
            try { verified = LogicGraphEditor.Verify(logic); }
            catch (Exception exception) { Debug.LogException(exception, logic); }

            Debug.Log("[HaruKitEvi] 範例資產已建立於 " + DataFolder
                + (verified ? "；LogicGraph 範例已通過驗證。" : "；LogicGraph 範例尚未通過驗證，請在 Inspector 按驗證查看原因。")
                + " AssetPipeline 範例需在 Inspector 按「驗證」後才能執行。");

            Selection.activeObject = logic;
            EditorGUIUtility.PingObject(logic);
        }

        [MenuItem(MenuRoot + "Open LogicGraph Sample", false, 20)]
        private static void OpenLogicSample()
        {
            var logic = Require<EviGraphDefinition>(LogicPath);
            if (logic != null) LogicGraphEditor.Open(logic);
        }

        [MenuItem(MenuRoot + "Open Plain GraphKit Sample", false, 21)]
        private static void OpenPlainSample()
        {
            var plain = Require<PlainGraphOwner>(PlainPath);
            if (plain != null) HaruGraphWindow.OpenFor(plain);
        }

        [MenuItem(MenuRoot + "Select AssetPipeline Sample", false, 22)]
        private static void SelectPipelineSample()
        {
            var pipeline = Require<AssetPipeline>(PipelinePath);
            if (pipeline == null) return;
            Selection.activeObject = pipeline;
            EditorGUIUtility.PingObject(pipeline);
        }

        [MenuItem(MenuRoot + "Run LogicGraph Sample (OnStart)", false, 40)]
        private static void RunLogicStart() => RunLogic(EviTiming.OnStart).Forget();

        [MenuItem(MenuRoot + "Run LogicGraph Sample (OnEnd)", false, 41)]
        private static void RunLogicEnd() => RunLogic(EviTiming.OnEnd).Forget();

        private static async UniTaskVoid RunLogic(EviTiming timing)
        {
            var logic = Require<EviGraphDefinition>(LogicPath);
            if (logic == null) return;

            // 執行的是「已儲存的圖」的副本；視窗裡還沒存檔的修改不會被執行。
            var pack = new EviPack { Seed = 7, Name = "Evi" };
            try
            {
                await logic.CreateRuntimeGraph().TriggerAction(timing, pack, CancellationToken.None, "Fixture " + timing);
                Debug.Log($"[HaruKitEvi] {timing} 完成（Seed={pack.Seed}），共 {pack.Log.Count} 則訊息。", logic);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, logic);
            }
        }

        private static T Require<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogWarning("[HaruKitEvi] 找不到 " + path + "，請先執行 " + MenuRoot + "Create or Reset All Samples。");
            return asset;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
