using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// Asset group keyed by a pipeline operation key. 在節點圖上它就是一份「目錄」。
    /// </summary>
    // 兩種指法並存：舊的目錄節點以 id 引用（改名不斷），新的目錄節點以 key 指定原型來源，
    // 與 AssetPipelineSource、資產分頁一致。
    [Serializable]
    public class AssetPipelineAssetGroup : IGraphCatalogLibrary
    {
        public string key;

        /// <summary>
        /// 穩定識別碼。目錄節點引用目錄靠它，所以改 <see cref="key"/> 不會讓引用失聯。
        /// </summary>
        // 舊資料沒有這個欄位，反序列化後是空字串；由 AssetPipeline.EnsureCatalogIds 第一次讀到時補上，
        // 不做資料遷移。不可重新產生：一旦有節點引用，換 id 等於斷開那些引用。
        public string id;

        public List<Object> assets = new List<Object>();

        public List<AssetPipelineItem> assetInfos = new List<AssetPipelineItem>();

        public List<AssetPipelineTypeGroup> typeGroups = new List<AssetPipelineTypeGroup>();

        string IGraphCatalogLibrary.Id => id;

        string IGraphCatalogLibrary.Name => key;

        /// <summary>這個庫裝的是 Project 資產。空的庫也要答得出來，所以寫死而不是從 assets 推。</summary>
        Type IGraphCatalogLibrary.ItemType => typeof(Object);

        // IReadOnlyList<out T> 是協變的，List<Object> 直接就是 IReadOnlyList<object>，不必另抄一份。
        IReadOnlyList<object> IGraphCatalogLibrary.Items => assets;
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
