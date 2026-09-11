using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public partial class AssetPipeline
    {
        // 按鈕配色規則（依使用情境，新增按鈕請沿用）：
        //   主要執行  GUIColor(0.5, 0.85, 0.55)  主綠
        //   附加寫入  GUIColor(0.55, 0.8, 1.0)   藍   （安全累加，如收集）
        //   替換覆蓋  GUIColor(1.0, 0.82, 0.45)  琥珀 （注意，如替換 / 依型別替換）
        //   搬移整合  GUIColor(0.55, 0.85, 0.85) 青綠 （中性改結構，如動態整合進原型）
        //   唯讀資訊  GUIColor(0.82, 0.88, 0.85) 淡灰綠（不改或安全，如刷新 / 驗證）
        //   刪除危險  GUIColor(1.0, 0.5, 0.5)    紅   （破壞，如清空 / 清除）
        internal void CollectSelection()
        {
            collectKey = GetValidKey(collectKey);
            string key = collectKey;
            var group = GetOrCreateGroup(prototypeAssets, key);
            int addedCount = 0;

            foreach (Object obj in Selection.objects)
            {
                if (obj == null) continue;
                if (obj == this) continue;
                if (!AssetDatabase.Contains(obj)) continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (HasAssetPath(group, path)) continue;

                group.assets.Add(obj);
                addedCount++;
            }

            RefreshGroupInfo(group);
            assetLog = $"Key [{key}] 收集完成：新增 {addedCount} 個，總數 {group.assets.Count} 個。";
            MarkDirty();
        }

        internal void ReplaceSelection()
        {
            collectKey = GetValidKey(collectKey);
            string key = collectKey;
            var group = GetOrCreateGroup(prototypeAssets, key);

            // 替換：先清空該 Key 舊資產，再收集當前選取
            group.assets.Clear();
            int addedCount = 0;

            foreach (Object obj in Selection.objects)
            {
                if (obj == null) continue;
                if (obj == this) continue;
                if (!AssetDatabase.Contains(obj)) continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (HasAssetPath(group, path)) continue;

                group.assets.Add(obj);
                addedCount++;
            }

            RefreshGroupInfo(group);
            assetLog = $"Key [{key}] 替換完成：{addedCount} 個。";
            MarkDirty();
        }

        internal void ReplaceSelectionByType()
        {
            collectKey = GetValidKey(collectKey);
            string key = collectKey;
            var group = GetOrCreateGroup(prototypeAssets, key);

            // 收集合法選取資產與其型別集合
            var selected = new List<Object>();
            var types = new HashSet<string>();
            foreach (Object obj in Selection.objects)
            {
                if (obj == null) continue;
                if (obj == this) continue;
                if (!AssetDatabase.Contains(obj)) continue;

                selected.Add(obj);
                types.Add(obj.GetType().Name);
            }

            if (selected.Count == 0)
            {
                assetLog = $"Key [{key}] 依型別替換：無合法選取資產。";
                return;
            }

            // 只移除選取型別的舊資產，保留其他型別
            int removedCount = group.assets.RemoveAll(a => a != null && types.Contains(a.GetType().Name));

            int addedCount = 0;
            foreach (Object obj in selected)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (HasAssetPath(group, path)) continue;

                group.assets.Add(obj);
                addedCount++;
            }

            RefreshGroupInfo(group);
            assetLog = $"Key [{key}] 依型別替換完成：移除 {removedCount}，新增 {addedCount}，型別 [{string.Join(",", types)}]。";
            MarkDirty();
        }

        internal void RefreshAllAssetInfo()
        {
            foreach (AssetPipelineAssetGroup group in prototypeAssets)
                RefreshGroupInfo(group);

            foreach (AssetPipelineAssetGroup group in dynamicAssets)
                RefreshGroupInfo(group);

            assetLog = $"刷新完成：原型 {prototypeAssets.Count} 個 Key，動態 {dynamicAssets.Count} 個 Key。";
            MarkDirty();
        }

        internal void ValidateAssets()
        {
            RefreshAllAssetInfo();

            var sb = new StringBuilder();
            int errorCount = 0;

            foreach (AssetPipelineAssetGroup group in prototypeAssets)
                errorCount += ValidateGroup(group, sb, "原型");

            foreach (AssetPipelineAssetGroup group in dynamicAssets)
                errorCount += ValidateGroup(group, sb, "動態");

            int totalGroupCount = prototypeAssets.Count + dynamicAssets.Count;
            assetLog = errorCount == 0
                ? $"驗證通過：{totalGroupCount} 個 Key。"
                : $"驗證失敗：{errorCount} 個問題。\n{sb}";

            Debug.Log($"[AssetPipeline] {assetLog}");
            MarkDirty();
        }

        internal bool ValidatePipelinePrototypeSources()
        {
            PipelineGraphAnalysis analysis = PipelineGraphAnalyzer.Analyze(this);
            var keyUsages = analysis.PrototypeKeyUsages;
            var sourceIssues = analysis.SourceIssues;

            var groupsByKey = new Dictionary<string, List<AssetPipelineAssetGroup>>();
            int invalidGroupCount = 0;
            foreach (AssetPipelineAssetGroup group in prototypeAssets)
            {
                if (group == null)
                {
                    invalidGroupCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(group.key))
                {
                    invalidGroupCount++;
                    continue;
                }

                if (!groupsByKey.TryGetValue(group.key, out List<AssetPipelineAssetGroup> groups))
                {
                    groups = new List<AssetPipelineAssetGroup>();
                    groupsByKey.Add(group.key, groups);
                }

                groups.Add(group);
            }

            int missingKeyCount = 0;
            int emptyAssetCount = 0;
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> pair in keyUsages)
            {
                if (!groupsByKey.TryGetValue(pair.Key, out List<AssetPipelineAssetGroup> groups))
                {
                    missingKeyCount++;
                    sb.AppendLine($"[缺少原型 Key] {pair.Key} <- {string.Join("、", pair.Value)}");
                    continue;
                }

                int assetCount = 0;
                foreach (AssetPipelineAssetGroup group in groups)
                    if (group.assets != null)
                        foreach (Object asset in group.assets)
                            if (asset != null && AssetDatabase.Contains(asset)) assetCount++;

                if (assetCount > 0) continue;

                emptyAssetCount++;
                sb.AppendLine($"[原型資產為空] {pair.Key} <- {string.Join("、", pair.Value)}");
            }

            int duplicateKeyCount = 0;
            foreach (KeyValuePair<string, List<AssetPipelineAssetGroup>> pair in groupsByKey)
                if (pair.Value.Count > 1)
                {
                    duplicateKeyCount++;
                    sb.AppendLine($"[重複原型 Key] {pair.Key}：{pair.Value.Count} 個群組。");
                }

            foreach (string sourceIssue in sourceIssues)
                sb.AppendLine($"[來源設定錯誤] {sourceIssue}");

            int graphWarningCount = 0;
            foreach (string warning in analysis.Warnings)
            {
                if (warning.Contains("缺少 prototype key")) continue;
                graphWarningCount++;
                sb.AppendLine($"[管線順序錯誤] {warning}");
            }

            if (invalidGroupCount > 0)
                sb.AppendLine($"[原型資產設定錯誤] 空群組或空 Key：{invalidGroupCount} 個。");

            int issueCount = missingKeyCount + emptyAssetCount + sourceIssues.Count + invalidGroupCount + graphWarningCount;
            assetLog = issueCount == 0
                ? $"管線原型資產來源驗證通過：{keyUsages.Count} 個使用中的 Key 均已設置資產。"
                : $"管線驗證失敗：缺少 Key {missingKeyCount}，空資產 {emptyAssetCount}，來源設定錯誤 {sourceIssues.Count}，原型設定錯誤 {invalidGroupCount}，順序錯誤 {graphWarningCount}。\n{sb}";

            if (issueCount == 0 && duplicateKeyCount > 0)
                assetLog += $"\n警告：發現 {duplicateKeyCount} 個重複原型 Key。\n{sb}";

            if (issueCount == 0) MarkPrototypeSourceValidationPassed();
            else ClearPrototypeSourceValidation();

            pipelineLog = assetLog;
            Debug.Log($"[AssetPipeline] {assetLog}");
            return issueCount == 0;
        }

        internal void ClearPrototypeAssets()
        {
            prototypeAssets.Clear();
            assetLog = "已清空原型資產。";
            MarkDirty();
        }

        internal void ClearDynamicAssets()
        {
            dynamicAssets.Clear();
            assetLog = "已清空動態資產。";
            MarkDirty();
        }

        internal void MergeDynamicAssetsToPrototypeAssets()
        {
            int addedCount = 0;

            foreach (AssetPipelineAssetGroup dynamicGroup in dynamicAssets)
            {
                if (dynamicGroup == null) continue;

                string key = GetValidKey(dynamicGroup.key);
                AssetPipelineAssetGroup prototypeGroup = GetOrCreateGroup(prototypeAssets, key);

                foreach (Object asset in dynamicGroup.assets)
                {
                    if (asset == null) continue;

                    string path = AssetDatabase.GetAssetPath(asset);
                    if (HasAssetPath(prototypeGroup, path)) continue;

                    prototypeGroup.assets.Add(asset);
                    addedCount++;
                }

                RefreshGroupInfo(prototypeGroup);
            }

            assetLog = $"動態資產整合完成：新增 {addedCount} 個到原型資產。";
            MarkDirty();
        }

        internal void ClearOperationKey()
        {
            clearKey = GetValidKey(clearKey);
            string key = clearKey;
            int removedCount = prototypeAssets.RemoveAll(group => group.key == key);
            assetLog = removedCount > 0 ? $"已清除原型 Key [{key}]。" : $"找不到原型 Key [{key}]。";
            MarkDirty();
        }

        /// <summary>將資產註冊進動態資產指定 Key（去重、刷新資訊）。供管線資產（如 PipelineAsset_CreatePrefabCopies）呼叫。</summary>
        internal void RegisterDynamicAssets(string key, IEnumerable<Object> assets)
        {
            if (assets == null) return;

            key = key == null ? string.Empty : key.Trim();
            AssetPipelineAssetGroup group = GetOrCreateGroup(dynamicAssets, key);

            foreach (Object asset in assets)
            {
                if (asset == null) continue;

                string path = AssetDatabase.GetAssetPath(asset);
                if (HasAssetPath(group, path)) continue;

                group.assets.Add(asset);
            }

            RefreshGroupInfo(group);
            MarkDirty();
        }

        private string GetValidKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? "Default" : key.Trim();
        }

        private AssetPipelineAssetGroup GetOrCreateGroup(List<AssetPipelineAssetGroup> targetGroups, string key)
        {
            foreach (AssetPipelineAssetGroup group in targetGroups)
                if (group.key == key) return group;

            var newGroup = new AssetPipelineAssetGroup { key = key };
            targetGroups.Add(newGroup);
            return newGroup;
        }

        private bool HasAssetPath(AssetPipelineAssetGroup group, string path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            foreach (Object asset in group.assets)
            {
                if (asset == null) continue;
                if (AssetDatabase.GetAssetPath(asset) == path) return true;
            }

            return false;
        }

        private void RefreshGroupInfo(AssetPipelineAssetGroup group)
        {
            group.assetInfos.Clear();
            group.typeGroups.Clear();

            foreach (Object asset in group.assets)
            {
                if (asset == null) continue;

                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;

                string type = asset.GetType().Name;
                var item = new AssetPipelineItem
                {
                    name = asset.name,
                    type = type,
                    path = path,
                    guid = AssetDatabase.AssetPathToGUID(path)
                };

                group.assetInfos.Add(item);

                AssetPipelineTypeGroup typeGroup = GetOrCreateTypeGroup(group, type);
                typeGroup.assets.Add(asset);
                typeGroup.assetInfos.Add(item);
                typeGroup.count = typeGroup.assets.Count;
            }
        }

        private AssetPipelineTypeGroup GetOrCreateTypeGroup(AssetPipelineAssetGroup group, string type)
        {
            foreach (AssetPipelineTypeGroup typeGroup in group.typeGroups)
                if (typeGroup.type == type) return typeGroup;

            var newTypeGroup = new AssetPipelineTypeGroup { type = type };
            group.typeGroups.Add(newTypeGroup);
            return newTypeGroup;
        }

        private int ValidateGroup(AssetPipelineAssetGroup group, StringBuilder sb, string sourceName)
        {
            var guids = new HashSet<string>();
            int errorCount = 0;

            foreach (AssetPipelineItem item in group.assetInfos)
            {
                if (string.IsNullOrEmpty(item.path) || AssetDatabase.LoadAssetAtPath<Object>(item.path) == null)
                {
                    errorCount++;
                    sb.AppendLine($"[{sourceName}:{group.key}] Missing asset: {item.path}");
                    continue;
                }

                if (string.IsNullOrEmpty(item.guid))
                {
                    errorCount++;
                    sb.AppendLine($"[{sourceName}:{group.key}] Missing GUID: {item.path}");
                    continue;
                }

                if (!guids.Add(item.guid))
                {
                    errorCount++;
                    sb.AppendLine($"[{sourceName}:{group.key}] Duplicate asset: {item.path}");
                }
            }

            return errorCount;
        }

    }
}
