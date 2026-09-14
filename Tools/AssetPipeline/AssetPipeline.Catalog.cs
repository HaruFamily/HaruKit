using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using HaruFamily.DependencyCore.GraphKit;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 節點圖左欄「目錄庫」的資料來源：把既有的原型資產群組（<see cref="prototypeAssets"/>）
    /// 接成 GraphKit 的 <see cref="ICatalogOwner"/>。
    /// </summary>
    // 實作在 Owner（本 SO）上而不是 APGraph 上：目錄的內容是「專案資產的分組」，不是圖的內容，
    // 不進編輯器的工作副本，改了就直接寫這份 SO。SetDirty 由呼叫端（視窗）負責，
    // 這裡只改資料——否則資產分頁那些既有操作也得各自再 SetDirty 一次。
    //
    // 資產分頁與目錄庫編的是同一份 prototypeAssets，兩個入口沒有各自的快取，所以不會不同步。
    public partial class AssetPipeline : ICatalogOwner
    {
        private const string DefaultCatalogPrefix = "Catalog";

        IReadOnlyList<IGraphCatalog> ICatalogOwner.Catalogs
        {
            get
            {
                EnsureCatalogIds();
                return prototypeAssets;
            }
        }

        IGraphCatalog ICatalogOwner.CreateCatalog()
        {
            EnsureCatalogIds();
            var group = new AssetPipelineAssetGroup
            {
                key = NextCatalogName(),
                id = Guid.NewGuid().ToString("N"),
            };
            prototypeAssets.Add(group);
            return group;
        }

        bool ICatalogOwner.RenameCatalog(string id, string name, out string error)
        {
            error = null;
            var group = FindCatalog(id);
            if (group == null) { error = "找不到這個目錄。"; return false; }

            name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (name == null) { error = "名稱不可為空。"; return false; }
            if (name == group.key) return true;

            // key 是步驟／公式用字串引用目錄的鍵，重名會讓其中一個永遠查不到，所以擋在這裡。
            foreach (var other in prototypeAssets)
            {
                if (other == null || ReferenceEquals(other, group)) continue;
                if (other.key != name) continue;
                error = $"已存在名為 '{name}' 的目錄。";
                return false;
            }

            group.key = name;
            return true;
        }

        void ICatalogOwner.DeleteCatalog(string id)
        {
            var group = FindCatalog(id);
            if (group == null) return;
            prototypeAssets.Remove(group);
        }

        int ICatalogOwner.AddToCatalog(string id, IReadOnlyList<Object> assets)
        {
            var group = FindCatalog(id);
            if (group == null || assets == null) return 0;

            int added = 0;
            foreach (Object obj in assets)
            {
                if (obj == null) continue;
                if (obj == this) continue;                       // 不讓管線把自己收進去
                if (!AssetDatabase.Contains(obj)) continue;

                // 去重比路徑不比參照：同一個 .asset 的不同子資產各自是 Object，比參照會重複收。
                string path = AssetDatabase.GetAssetPath(obj);
                if (HasAssetPath(group, path)) continue;

                group.assets.Add(obj);
                added++;
            }

            if (added > 0) RefreshGroupInfo(group);
            return added;
        }

        void ICatalogOwner.RemoveFromCatalog(string id, Object asset)
        {
            var group = FindCatalog(id);
            if (group == null) return;
            if (!group.assets.Remove(asset)) return;
            RefreshGroupInfo(group);
        }

        /// <summary>
        /// 執行期依 id 取目錄內容。找不到目錄回 null，呼叫端走保底值。
        /// </summary>
        // 用 AssetPipeline.current 而不是實例方法：求值發生在步驟執行中，那時候只有 current 拿得到，
        // 與 AssetPipelineSource.GetAssets 同一條路。current 為空代表不在管線執行流程裡。
        public static IReadOnlyList<Object> ResolveCatalog(string catalogId)
        {
            var pipeline = current;
            if (pipeline == null || string.IsNullOrEmpty(catalogId)) return null;
            var group = pipeline.FindCatalog(catalogId);
            return group?.assets;
        }

        /// <summary>
        /// 把目錄內容裝成欄位要的 `List&lt;T&gt;`，順便做型別過濾。
        /// <typeparamref name="TResult"/> 不是 `List&lt;&gt;` 就回 false，呼叫端走保底值並回報。
        /// </summary>
        // 節點存的型別過濾是 AssemblyQualifiedName：目錄裡放什麼由專案決定，短名同名不同 namespace 撞得到。
        // 解不回 Type 時當作不過濾——那多半是型別所在的組件被移掉，讓它退回「整個目錄」比整條回空好。
        public static bool TryBuildCatalogResult<TResult>(IReadOnlyList<Object> items, string typeName,
            out TResult result)
        {
            result = default;
            Type target = typeof(TResult);
            if (!target.IsGenericType || target.GetGenericTypeDefinition() != typeof(List<>)) return false;

            Type element = target.GetGenericArguments()[0];
            Type filter = string.IsNullOrEmpty(typeName) ? null : Type.GetType(typeName);

            var list = (IList)Activator.CreateInstance(target);
            if (items != null)
            {
                foreach (Object obj in items)
                {
                    if (obj == null) continue;
                    if (!element.IsInstanceOfType(obj)) continue;
                    if (filter != null && !filter.IsInstanceOfType(obj)) continue;
                    list.Add(obj);
                }
            }

            result = (TResult)list;
            return true;
        }

        private AssetPipelineAssetGroup FindCatalog(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var group in prototypeAssets)
                if (group != null && group.id == id) return group;
            return null;
        }

        /// <summary>補上舊資料沒有的 id。只補空的，已經有 id 的一律不動。</summary>
        private void EnsureCatalogIds()
        {
            foreach (var group in prototypeAssets)
            {
                if (group == null || !string.IsNullOrEmpty(group.id)) continue;
                group.id = Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>自動命名 Catalog1、Catalog2…，跳過已存在的號碼。與 Token 庫的新增行為一致。</summary>
        private string NextCatalogName()
        {
            for (int i = 1; i <= prototypeAssets.Count + 1; i++)
            {
                string candidate = DefaultCatalogPrefix + i;
                bool taken = false;
                foreach (var group in prototypeAssets)
                {
                    if (group == null || group.key != candidate) continue;
                    taken = true;
                    break;
                }
                if (!taken) return candidate;
            }
            return DefaultCatalogPrefix + (prototypeAssets.Count + 1);
        }
    }
}
