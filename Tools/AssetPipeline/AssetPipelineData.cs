using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// Asset group keyed by a pipeline operation key.
    /// </summary>
    [Serializable]
    public class AssetPipelineAssetGroup
    {
        public string key;

        public List<Object> assets = new List<Object>();

        public List<AssetPipelineItem> assetInfos = new List<AssetPipelineItem>();

        public List<AssetPipelineTypeGroup> typeGroups = new List<AssetPipelineTypeGroup>();
    }

    /// <summary>
    /// Assets grouped by Unity object type inside one operation key.
    /// </summary>
    [Serializable]
    public class AssetPipelineTypeGroup
    {
        public string type;

        public int count;

        public List<Object> assets = new List<Object>();

        public List<AssetPipelineItem> assetInfos = new List<AssetPipelineItem>();
    }

    /// <summary>
    /// Serialized asset metadata for AssetPipeline validation and future operations.
    /// </summary>
    [Serializable]
    public class AssetPipelineItem
    {
        public string name;

        public string type;

        public string path;

        public string guid;
    }
}
