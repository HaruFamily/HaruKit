using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    [Flags]
    public enum AssetPipelineSourceFlags
    {
        None = 0,
        Prototype = 1,
    }

    [Serializable]
    public class AssetPipelineSource
    {
        public AssetPipelineSourceFlags sourceFlags = AssetPipelineSourceFlags.Prototype;

        public List<string> keys = new List<string>();

        public List<T> GetAssets<T>() where T : Object
        {
            return GetAssets<T>(AssetPipeline.current);
        }

        /// <summary>
        /// 這個來源在指定模式下實際會讀到的 key（已去掉空白與前後空格）。
        /// 模式沒開就回空，驗證器據此檢查 key 存不存在與 dynamic 的產出時序。
        /// </summary>
        public IEnumerable<string> ReadKeys(AssetPipelineSourceFlags mode)
        {
            if ((sourceFlags & mode) == 0) yield break;
            if (keys == null) yield break;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                yield return key.Trim();
            }
        }

        public List<T> GetAssets<T>(AssetPipeline targetPipeline) where T : Object
        {
            var results = new List<T>();
            if (targetPipeline == null) return results;

            var addedAssets = new HashSet<Object>();

            if ((sourceFlags & AssetPipelineSourceFlags.Prototype) != 0)
                AddAssetsFromGroups(targetPipeline.prototypeAssets, results, addedAssets);

            return results;
        }

        private void AddAssetsFromGroups<T>(List<AssetPipelineAssetGroup> groups, List<T> results, HashSet<Object> addedAssets) where T : Object
        {
            foreach (AssetPipelineAssetGroup group in groups)
            {
                if (group == null) continue;
                if (!ContainsKey(group.key)) continue;

                foreach (Object asset in group.assets)
                {
                    if (asset == null) continue;
                    if (!(asset is T typedAsset)) continue;
                    if (!addedAssets.Add(asset)) continue;

                    results.Add(typedAsset);
                }
            }
        }

        private bool ContainsKey(string groupKey)
        {
            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (key.Trim() == groupKey) return true;
            }

            return false;
        }
    }
}
