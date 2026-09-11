using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public sealed class PipelineGraphStepAnalysis
    {
        public int Index { get; internal set; }
        public string Name { get; internal set; }
        public List<string> PrototypeInputs { get; } = new List<string>();
        public List<string> DynamicInputs { get; } = new List<string>();
        public List<string> DynamicOutputs { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class PipelineGraphAnalysis
    {
        public List<PipelineGraphStepAnalysis> Steps { get; } = new List<PipelineGraphStepAnalysis>();
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, List<string>> PrototypeKeyUsages { get; } = new Dictionary<string, List<string>>();
        public List<string> SourceIssues { get; } = new List<string>();
    }

    public static class PipelineGraphAnalyzer
    {
        public static PipelineGraphAnalysis Analyze(AssetPipeline pipeline)
        {
            var analysis = new PipelineGraphAnalysis();
            if (pipeline == null) return analysis;

            var prototypeKeys = new HashSet<string>();
            foreach (AssetPipelineAssetGroup group in pipeline.prototypeAssets)
                if (group != null && !string.IsNullOrWhiteSpace(group.key)) prototypeKeys.Add(group.key.Trim());

            var producedDynamicKeys = new HashSet<string>();
            for (int i = 0; i < pipeline.pipelineAssets.Count; i++)
            {
                IPipelineAsset pipelineAsset = pipeline.pipelineAssets[i];
                var step = new PipelineGraphStepAnalysis
                {
                    Index = i,
                    Name = pipelineAsset == null ? "Missing Step" : pipelineAsset.GetType().Name
                };
                analysis.Steps.Add(step);

                if (pipelineAsset == null)
                {
                    AddWarning(analysis, step, $"Step {i} 為 null。");
                    continue;
                }

                var visited = new HashSet<object>();
                CollectSources(pipelineAsset, $"管線[{i}] {step.Name}", step, analysis, visited);

                foreach (string key in step.PrototypeInputs)
                    if (!prototypeKeys.Contains(key)) AddWarning(analysis, step, $"缺少 prototype key [{key}]。");

                foreach (string key in step.DynamicInputs)
                    if (!producedDynamicKeys.Contains(key)) AddWarning(analysis, step, $"dynamic key [{key}] 在 producer 前被讀取。");

                if (pipelineAsset is IDynamicAssetProducer producer && producer.TryGetDynamicOutputKey(out string outputKey))
                {
                    step.DynamicOutputs.Add(outputKey);
                    producedDynamicKeys.Add(outputKey);
                }
            }

            return analysis;
        }

        private static void CollectSources(object value, string path, PipelineGraphStepAnalysis step, PipelineGraphAnalysis analysis, HashSet<object> visited)
        {
            if (value == null || value is Object || value is string) return;

            if (value is AssetPipelineSource source)
            {
                CollectSource(source, path, step, analysis);
                return;
            }

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return;
            if (!type.IsValueType && !visited.Add(value)) return;

            if (value is IEnumerable collection)
            {
                int index = 0;
                foreach (object item in collection)
                {
                    CollectSources(item, $"{path}[{index}]", step, analysis, visited);
                    index++;
                }
                return;
            }

            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
                foreach (FieldInfo field in currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null && field.GetCustomAttribute<SerializeReference>() == null) continue;
                    if (!ShouldCollectField(value, field)) continue;
                    CollectSources(field.GetValue(value), $"{path}.{field.Name}", step, analysis, visited);
                }
        }

        private static bool ShouldCollectField(object owner, FieldInfo field)
        {
            if (field.Name == "assetSource") return GetFormulaMode(owner, "assetData") == "AssetSource";
            if (field.Name != "formula") return true;

            string assetMode = GetFormulaMode(owner, "assetData");
            if (assetMode != null) return assetMode == "Formula";

            string dataMode = GetFormulaMode(owner, "data");
            return dataMode == null || dataMode == "Formula";
        }

        private static string GetFormulaMode(object owner, string fieldName)
        {
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner)?.ToString();
            }
            return null;
        }

        private static void CollectSource(AssetPipelineSource source, string path, PipelineGraphStepAnalysis step, PipelineGraphAnalysis analysis)
        {
            bool prototype = (source.sourceFlags & AssetPipelineSourceFlags.Prototype) != 0;
            bool dynamic = (source.sourceFlags & AssetPipelineSourceFlags.Dynamic) != 0;
            if (!prototype && !dynamic) return;

            if (source.keys == null || source.keys.Count == 0)
            {
                analysis.SourceIssues.Add($"{path} 使用資產來源，但 Key 清單為空。");
                return;
            }

            foreach (string rawKey in source.keys)
            {
                if (string.IsNullOrWhiteSpace(rawKey))
                {
                    analysis.SourceIssues.Add($"{path} 使用資產來源，但包含空 Key。");
                    continue;
                }

                string key = rawKey.Trim();
                if (prototype)
                {
                    AddUnique(step.PrototypeInputs, key);
                    if (!analysis.PrototypeKeyUsages.TryGetValue(key, out List<string> usages))
                    {
                        usages = new List<string>();
                        analysis.PrototypeKeyUsages.Add(key, usages);
                    }
                    usages.Add(path);
                }
                if (dynamic) AddUnique(step.DynamicInputs, key);
            }
        }

        private static void AddWarning(PipelineGraphAnalysis analysis, PipelineGraphStepAnalysis step, string warning)
        {
            step.Warnings.Add(warning);
            analysis.Warnings.Add($"Step {step.Index}: {warning}");
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!values.Contains(value)) values.Add(value);
        }
    }
}
