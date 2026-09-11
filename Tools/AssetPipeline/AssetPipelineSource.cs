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
        Dynamic = 2,
        Both = Prototype | Dynamic
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

        public List<T> GetAssets<T>(AssetPipeline targetPipeline) where T : Object
        {
            var results = new List<T>();
            if (targetPipeline == null) return results;

            var addedAssets = new HashSet<Object>();

            if ((sourceFlags & AssetPipelineSourceFlags.Prototype) != 0)
                AddAssetsFromGroups(targetPipeline.prototypeAssets, results, addedAssets);

            if ((sourceFlags & AssetPipelineSourceFlags.Dynamic) != 0)
                AddAssetsFromGroups(targetPipeline.dynamicAssets, results, addedAssets);

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
