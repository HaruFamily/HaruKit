using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// Pipeline item interface used by AssetPipeline pipeline tab.
    /// </summary>
    public interface IPipelineAsset
    {
        void Execute();
    }

    public interface IDynamicAssetProducer
    {
        bool TryGetDynamicOutputKey(out string key);
    }

    /// <summary>
    /// Creates prefab asset copies from one prefab formula into a target project folder.
    /// </summary>
    [Serializable]
    public class PipelineAsset_CreatePrefabCopies : IPipelineAsset, IDynamicAssetProducer, ISerializationCallbackReceiver
    {
        public enum NameOverflowMode
        {
            StopAtNameListEnd,
            CycleNameList,
            UsePrefabNameAfterNameListEnd
        }

        public enum ExistingPrefabMode
        {
            Skip,
            Replace,
            CreateUniqueName
        }

        [Flags]
        public enum SerialNameFlags
        {
            None = 0,
            AddSerialWhenDuplicate = 1,
            AlwaysAddSerial = 2,
            UseGlobalIndex = 4
        }

        public FormulaAsset_GameObject prefab = new FormulaAsset_GameObject();

        public FormulaAsset_Int count = new FormulaAsset_Int();

        public FormulaAsset_StringList names = new FormulaAsset_StringList();

        public FormulaAsset_Folder targetFolderFolder = new FormulaAsset_Folder("Assets");

        [HideInInspector]
        public FormulaAsset_String targetFolderFormula = new FormulaAsset_String("Assets");

        [HideInInspector]
        public string targetFolder = "Assets";

        [HideInInspector]
        public bool targetFolderMigrated;

        [HideInInspector]
        public bool targetFolderFolderMigrated;

        public NameOverflowMode nameOverflowMode = NameOverflowMode.StopAtNameListEnd;

        public ExistingPrefabMode existingPrefabMode = ExistingPrefabMode.Skip;

        public SerialNameFlags serialNameFlags = SerialNameFlags.None;

        public string serialNameFormat = "{name}_{index:000}";

        public bool registerToDynamic = false;

        public string dynamicKey = string.Empty;

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            bool needsLegacyMigration = targetFolderFolder == null
                || (targetFolderFolder.assetData == FormulaAsset_Folder.FolderData.None
                    && targetFolderFolder.@default == null
                    && string.IsNullOrWhiteSpace(targetFolderFolder.defaultPath));
            if (targetFolderFolderMigrated && !needsLegacyMigration) return;

            targetFolderFolderMigrated = true;
            if (!needsLegacyMigration && targetFolderFolder != null && targetFolderFolder.assetData != FormulaAsset_Folder.FolderData.None) return;
            if (!needsLegacyMigration && targetFolderFolder?.@default != null) return;
            if (!needsLegacyMigration && targetFolderFolder != null && !string.IsNullOrWhiteSpace(targetFolderFolder.defaultPath) && targetFolderFolder.defaultPath != "Assets") return;

            string legacyFolder = GetLegacyTargetFolder();
            targetFolderFolder = new FormulaAsset_Folder(legacyFolder);
        }

        public void Execute()
        {
            GameObject prefabAsset = prefab?.Caculate();
            if (prefabAsset == null)
            {
                Debug.LogError("[PipelineAsset_CreatePrefabCopies] Prefab 為空，停止生成。");
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath) || !AssetDatabase.Contains(prefabAsset))
            {
                Debug.LogError($"[PipelineAsset_CreatePrefabCopies] Prefab 不是 Project Asset：{prefabAsset.name}");
                return;
            }

            if (PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.NotAPrefab)
            {
                Debug.LogError($"[PipelineAsset_CreatePrefabCopies] 來源不是 Prefab：{prefabPath}");
                return;
            }

            DefaultAsset targetFolderAsset = targetFolderFolder?.Caculate();
            string rawTargetFolder = FormulaAsset_Folder.GetFolderPath(targetFolderAsset);
            string assetFolder = NormalizeAssetFolder(rawTargetFolder);
            if (string.IsNullOrWhiteSpace(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
            {
                Debug.LogError($"[PipelineAsset_CreatePrefabCopies] 目標資料夾不存在：{rawTargetFolder}");
                return;
            }

            int createCount = Mathf.Max(0, count?.Caculate() ?? 0);
            if (createCount == 0)
            {
                Debug.LogWarning("[PipelineAsset_CreatePrefabCopies] 生成數量為 0。");
                return;
            }

            List<string> nameList = names?.Caculate() ?? new List<string>();
            if (nameOverflowMode == NameOverflowMode.StopAtNameListEnd)
                createCount = Mathf.Min(createCount, nameList.Count);

            int successCount = 0;
            int skippedCount = 0;
            int replacedCount = 0;
            int uniqueNameCount = 0;
            int failedCount = 0;
            var createdPaths = new List<string>();

            for (int i = 0; i < createCount; i++)
            {
                string baseName = ResolveBaseName(prefabAsset.name, nameList, i);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    skippedCount++;
                    continue;
                }

                string assetPath = ResolveTargetPath(assetFolder, baseName.Trim(), i, out bool usedUniqueName);
                bool existed = AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null;

                if (existed && existingPrefabMode == ExistingPrefabMode.Skip)
                {
                    skippedCount++;
                    continue;
                }

                if (existed && existingPrefabMode == ExistingPrefabMode.Replace && !AssetDatabase.DeleteAsset(assetPath))
                {
                    failedCount++;
                    Debug.LogError($"[PipelineAsset_CreatePrefabCopies] 取代失敗，無法刪除既有 Prefab：{assetPath}");
                    continue;
                }

                if (!AssetDatabase.CopyAsset(prefabPath, assetPath))
                {
                    failedCount++;
                    Debug.LogError($"[PipelineAsset_CreatePrefabCopies] 複製失敗：{prefabPath} -> {assetPath}");
                    continue;
                }

                createdPaths.Add(assetPath);
                successCount++;
                if (existed && existingPrefabMode == ExistingPrefabMode.Replace) replacedCount++;
                if (usedUniqueName) uniqueNameCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int registeredCount = RegisterCreatedToDynamic(createdPaths);

            string log = $"[PipelineAsset_CreatePrefabCopies] 完成：成功 {successCount}，取代 {replacedCount}，流水號新建 {uniqueNameCount}，跳過 {skippedCount}，失敗 {failedCount}。";
            if (registerToDynamic) log += $" 註冊動態 Key [{GetDynamicKey()}]：{registeredCount} 個。";
            AssetPipeline.Report(log);
        }

        public bool TryGetDynamicOutputKey(out string key)
        {
            key = GetDynamicKey();
            return registerToDynamic && !string.IsNullOrEmpty(key);
        }

        private string GetLegacyTargetFolder()
        {
            if (targetFolderFormula?.formula is Formula_String_FolderPath folderPath && !string.IsNullOrWhiteSpace(folderPath.folder))
                return folderPath.folder;

            if (targetFolderFormula != null && !string.IsNullOrWhiteSpace(targetFolderFormula.@default))
                return targetFolderFormula.@default;

            return string.IsNullOrWhiteSpace(targetFolder) ? "Assets" : targetFolder;
        }

        // 生成後把新 prefab 註冊進 AssetPipeline 動態資產；current 為空或未開啟時安全跳過，不中斷主流程
        private int RegisterCreatedToDynamic(List<string> createdPaths)
        {
            if (!registerToDynamic) return 0;

            AssetPipeline pipeline = AssetPipeline.current;
            if (pipeline == null)
            {
                Debug.LogWarning("[PipelineAsset_CreatePrefabCopies] 註冊動態資產失敗：AssetPipeline.current 為空（非管線執行流程）。");
                return 0;
            }

            var assets = new List<Object>();
            foreach (string path in createdPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                assets.Add(asset);
            }

            pipeline.RegisterDynamicAssets(GetDynamicKey(), assets);
            return assets.Count;
        }

        private string GetDynamicKey()
        {
            return dynamicKey == null ? string.Empty : dynamicKey.Trim();
        }

        private bool NeedSerialSettings()
        {
            return nameOverflowMode == NameOverflowMode.CycleNameList || existingPrefabMode == ExistingPrefabMode.CreateUniqueName;
        }

        private string ResolveBaseName(string prefabName, List<string> nameList, int index)
        {
            if (nameList.Count == 0) return prefabName;

            if (index < nameList.Count) return nameList[index];

            if (nameOverflowMode == NameOverflowMode.CycleNameList) return nameList[index % nameList.Count];
            if (nameOverflowMode == NameOverflowMode.UsePrefabNameAfterNameListEnd) return prefabName;

            return string.Empty;
        }

        private string ResolveTargetPath(string assetFolder, string baseName, int index, out bool usedUniqueName)
        {
            usedUniqueName = false;
            string name = ShouldAlwaysAddSerial()
                ? ApplySerialFormat(baseName, index)
                : baseName;

            string path = Path.Combine(assetFolder, name + ".prefab").Replace("\\", "/");
            if (existingPrefabMode != ExistingPrefabMode.CreateUniqueName)
                return path;

            int duplicateIndex = 1;
            while (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                string duplicateName = ApplySerialFormat(baseName, duplicateIndex);
                path = Path.Combine(assetFolder, duplicateName + ".prefab").Replace("\\", "/");
                usedUniqueName = true;
                duplicateIndex++;
            }

            return path;
        }

        private bool ShouldAlwaysAddSerial()
        {
            if ((serialNameFlags & SerialNameFlags.AlwaysAddSerial) != 0) return true;
            return false;
        }

        private string ApplySerialFormat(string baseName, int index)
        {
            int serialIndex = (serialNameFlags & SerialNameFlags.UseGlobalIndex) != 0 ? index : index + 1;

            try
            {
                return serialNameFormat
                    .Replace("{name}", baseName)
                    .Replace("{index:000}", serialIndex.ToString("000"))
                    .Replace("{index}", serialIndex.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PipelineAsset_CreatePrefabCopies] 流水號格式錯誤：{serialNameFormat}\n{ex}");
                return $"{baseName}_{serialIndex:000}";
            }
        }

        private string NormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return string.Empty;

            string normalized = folder.Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");
            if (normalized.StartsWith(dataPath))
                return "Assets" + normalized.Substring(dataPath.Length);

            return normalized;
        }
    }

    /// <summary>
    /// 將 FormulaAsset_ObjectList 的資產標註為 Addressable，加入指定 Group（新建或既有）。
    /// </summary>
    [Serializable]
    public class PipelineAsset_SetAddressable : IPipelineAsset, ISerializationCallbackReceiver
    {
        /// <summary>舊資料遷移用。新 UI 改用 FormulaAsset_AddressableGroup。</summary>
        public enum GroupSource
        {
            New,
            Existing
        }

        /// <summary>資產已是 Addressable 時的處理策略。</summary>
        public enum ExistingEntryMode
        {
            Skip,
            MoveToGroup
        }

        public FormulaAsset_ObjectList objects = new FormulaAsset_ObjectList();

        public FormulaAsset_AddressableGroup group = new FormulaAsset_AddressableGroup();

        public ExistingEntryMode existingMode = ExistingEntryMode.Skip;

        [HideInInspector]
        public GroupSource groupSource = GroupSource.New;

        [HideInInspector]
        public string newGroupName = "Default";

        [HideInInspector]
        public AddressableAssetGroup existingGroup;

        [HideInInspector]
        public bool groupMigrated;

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (groupMigrated) return;

            groupMigrated = true;
            if (groupSource == GroupSource.Existing && existingGroup != null)
            {
                group = new FormulaAsset_AddressableGroup(existingGroup);
                return;
            }

            group = new FormulaAsset_AddressableGroup();
            if (group.formula is Formula_AddressableGroup_ByName byName)
                byName.groupName = new FormulaAsset_String(string.IsNullOrWhiteSpace(newGroupName) ? "Default" : newGroupName.Trim());
        }

        public void Execute()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[PipelineAsset_SetAddressable] Addressable Settings 為空，停止。");
                return;
            }

            AddressableAssetGroup targetGroup = ResolveGroup(settings);
            if (targetGroup == null) return;

            List<Object> targets = objects?.Caculate();
            if (targets == null || targets.Count == 0)
            {
                Debug.LogWarning("[PipelineAsset_SetAddressable] 無目標資產。");
                return;
            }

            int addedCount = 0;
            int movedCount = 0;
            int skippedCount = 0;

            foreach (Object asset in targets)
            {
                if (asset == null) continue;

                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                AddressableAssetEntry existing = settings.FindAssetEntry(guid);
                if (existing != null)
                {
                    // 已在目標 Group → 無動作
                    if (existing.parentGroup == targetGroup)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (existingMode == ExistingEntryMode.Skip)
                    {
                        skippedCount++;
                        continue;
                    }

                    settings.CreateOrMoveEntry(guid, targetGroup);
                    movedCount++;
                    continue;
                }

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, targetGroup);
                entry.address = asset.name;
                addedCount++;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetPipeline.Report($"[PipelineAsset_SetAddressable] 完成 Group [{targetGroup.Name}]：新增 {addedCount}，移動 {movedCount}，跳過 {skippedCount}。");
        }

        // Group 由 FormulaAsset 提供，讓 SetAddressable 可接固定值或管線公式。
        private AddressableAssetGroup ResolveGroup(AddressableAssetSettings settings)
        {
            AddressableAssetGroup targetGroup = group?.Caculate();
            if (targetGroup == null)
            {
                Debug.LogError("[PipelineAsset_SetAddressable] Group 未指定或公式計算失敗，停止。");
                return null;
            }

            if (settings.groups.Contains(targetGroup)) return targetGroup;

            Debug.LogError($"[PipelineAsset_SetAddressable] Group [{targetGroup.Name}] 不屬於目前 Addressable Settings，停止。");
            return null;
        }
    }

    /// <summary>
    /// 掃描資料夾內指定型別資產，註冊至 AssetPipeline 動態資產 Key。
    /// </summary>
    [Serializable]
    public abstract class PipelineAsset_RegisterAssetsFromFolder<T> : IPipelineAsset, IDynamicAssetProducer where T : Object
    {
        public FormulaAsset_Folder folder = new FormulaAsset_Folder("Assets");

        public bool includeSubfolders = true;

        public string dynamicKey = "Audios";

        public void Execute()
        {
            AssetPipeline pipeline = AssetPipeline.current;
            if (pipeline == null)
            {
                Debug.LogWarning($"[{GetType().Name}] AssetPipeline.current 為空，停止註冊。");
                return;
            }

            string folderPath = FormulaAsset_Folder.GetFolderPath(folder?.Caculate());
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogWarning($"[{GetType().Name}] 目標資料夾無效，停止註冊。");
                return;
            }

            string key = dynamicKey?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"[{GetType().Name}] 動態資產 Key 為空，停止註冊。");
                return;
            }

            var paths = new List<string>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!includeSubfolders && Path.GetDirectoryName(path)?.Replace("\\", "/") != folderPath) continue;

                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            var assets = new List<T>();
            foreach (string path in paths)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) assets.Add(asset);
            }

            pipeline.RegisterDynamicAssets(key, assets);
            AssetPipeline.Report($"[{GetType().Name}] 完成：找到 {assets.Count} 個 {typeof(T).Name}，已註冊至動態 Key [{key}]。");
        }

        public bool TryGetDynamicOutputKey(out string key)
        {
            key = dynamicKey?.Trim();
            return !string.IsNullOrEmpty(key);
        }
    }

    /// <summary>
    /// 掃描資料夾內的 AudioClip，註冊至 AssetPipeline 動態資產 Key。
    /// </summary>
    [Serializable]
    public class PipelineAsset_RegisterAudioClipsFromFolder : PipelineAsset_RegisterAssetsFromFolder<AudioClip>
    {
    }

    /// <summary>
    /// 抓出 Formula_GameObjectList 的 GameObject，將其 AudioSource 的 Clip 依索引替換為 Formula_AudioClipList 對應項。
    /// </summary>
    [Serializable]
    public class PipelineAsset_ReplaceAudioClip : IPipelineAsset
    {
        /// <summary>Clip 數量不足 GameObject 時的處理策略。</summary>
        public enum ClipOverflowMode
        {
            StopAtListEnd,
            CycleList,
            UseLastAfterEnd
        }

        public FormulaAsset_GameObjectList targets = new FormulaAsset_GameObjectList();

        public FormulaAsset_AudioClipList clips = new FormulaAsset_AudioClipList();

        public ClipOverflowMode clipOverflowMode = ClipOverflowMode.StopAtListEnd;

        public void Execute()
        {
            List<GameObject> gameObjects = targets?.Caculate();
            if (gameObjects == null || gameObjects.Count == 0)
            {
                Debug.LogWarning("[PipelineAsset_ReplaceAudioClip] 無目標 GameObject。");
                return;
            }

            List<AudioClip> clipList = clips?.Caculate() ?? new List<AudioClip>();
            if (clipList.Count == 0)
            {
                Debug.LogWarning("[PipelineAsset_ReplaceAudioClip] AudioClip 清單為空。");
                return;
            }

            // StopAtListEnd：clip 用完即停，只處理前 clipList.Count 個
            int processCount = gameObjects.Count;
            if (clipOverflowMode == ClipOverflowMode.StopAtListEnd)
                processCount = Mathf.Min(processCount, clipList.Count);

            int replacedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < processCount; i++)
            {
                GameObject go = gameObjects[i];
                if (go == null)
                {
                    skippedCount++;
                    continue;
                }

                // AudioSource 可能在子物件；true 連 inactive 一併找
                AudioSource audioSource = go.GetComponentInChildren<AudioSource>(true);
                if (audioSource == null)
                {
                    skippedCount++;
                    Debug.LogWarning($"[PipelineAsset_ReplaceAudioClip] 找不到 AudioSource：{go.name}");
                    continue;
                }

                audioSource.clip = ResolveClip(clipList, i);
                EditorUtility.SetDirty(audioSource);

                // 目標為 Prefab 資產→存回；否則僅標髒
                if (PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                    PrefabUtility.SavePrefabAsset(go);

                replacedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetPipeline.Report($"[PipelineAsset_ReplaceAudioClip] 完成：替換 {replacedCount}，跳過 {skippedCount}。");
        }

        // 依 clipOverflowMode 取索引 i 對應的 clip；比照 PipelineAsset_CreatePrefabCopies.ResolveBaseName
        private AudioClip ResolveClip(List<AudioClip> clipList, int index)
        {
            if (index < clipList.Count) return clipList[index];

            if (clipOverflowMode == ClipOverflowMode.CycleList) return clipList[index % clipList.Count];
            if (clipOverflowMode == ClipOverflowMode.UseLastAfterEnd) return clipList[clipList.Count - 1];

            return null; // StopAtListEnd 已 clamp processCount，不會走到
        }
    }
}
