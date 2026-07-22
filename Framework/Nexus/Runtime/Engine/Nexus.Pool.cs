using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PinPlugin.Nexus
{
    // 物件池（opt-in：實作 INexusPoolable 才回池）。prefab-mono service 同時 poolable 時連 GameObject 一起池化
    //（回池=OnDespawn 停用、保留 GO；取回=OnSpawn 重啟用，不再 InstantiateAsync）。
    public partial class Nexus
    {
        // pop 以 typeof(T) 取、push 以 runtime type 存。回報 fromPool：
        // 命中（true）= 重用池中實例與其既有 GameObject → CreateAsync 跳過 prefab InstantiateAsync；
        // 未命中（false）= 全新。純 C# service → new T()；prefab-mono service（MonoBehaviour）無法 new，
        //                  回 (null,false)，由 CreateAsync 走 async Addressables.InstantiateAsync 取 component。
        private (T instance, bool fromPool) PoolPop<T>() where T : class, new()
        {
            if (_pools.TryGetValue(typeof(T), out var pool) && pool.Count > 0)
                return ((T)pool.Pop(), true);

            // Unity Object service（MonoBehaviour=prefab component / ScriptableObject=資產副本）無法 new，
            // 實例只能由 Addressables 產出 → 回 (null,false)，交 CreateAsync 走 async 載入分支。
            if (typeof(MonoBehaviour).IsAssignableFrom(typeof(T)) || typeof(ScriptableObject).IsAssignableFrom(typeof(T)))
                return (null, false);

            return (new T(), false);
        }

        // 釋放時回池。回 true = 已入池（保留實例，含其 GameObject）；false = 沒入池（caller 走 ReleaseInstance / 交 GC）。
        // 入池前跑 OnDespawn（停用）；其同步段比照 OnRelease 用 _releaseSync 包夾，擋「釋放途中再生服務」。
        private async UniTask<bool> PoolPush(object obj)
        {
            if (obj is not INexusPoolable poolable) return false;   // 不可池 → 交回 caller 處理

            try
            {
                UniTask t;
                _releaseSync++;
                try { t = poolable.OnDespawn(); } finally { _releaseSync--; }
                await t;
            }
            catch (Exception e) { RaiseError(NexusErrorPhase.Return, obj, e); return false; }   // 停用失敗就別入池

            var type = obj.GetType();
            if (!_pools.TryGetValue(type, out var pool))
            {
                pool = new Stack<object>();
                _pools[type] = pool;
            }
            // 防禦性：池正常只裝同型；若殘留異型（mock / keying 漂移），清空再推——清前先 ReleaseInstance 其中 prefab GO 免洩漏。
            else if (pool.Count > 0 && pool.Peek().GetType() != type)
            {
                DestroyPooledGameObjects(pool);
                pool.Clear();
            }
            if (pool.Count < PoolLimit)
            {
                pool.Push(obj);
                RaiseLifecycle(NexusLifecyclePhase.PoolReturned, obj);
                return true;
            }
            return false;   // 池滿 → 不入池
        }

        // 把一池中保留的 Nexus-owned Unity Object 全部卸載（池化保留著，丟棄池內容前須卸載免洩漏）。
        // 供 ClearAll 與型別汙染清池用。prefab-mono → ReleaseInstance；SO 副本 → Destroy；純 C# poolable 無 Unity Object，略過。
        private void DestroyPooledGameObjects(IEnumerable<object> pooled)
        {
            foreach (var obj in pooled)
            {
                if (_prefabOwned.Remove(obj)) ReleasePrefabInstance(obj);
                else if (_scriptableOwned.Remove(obj)) ReleaseScriptableInstance(obj);
                else if (_scriptableShared.Remove(obj)) ReleaseScriptableShared(obj);
            }
        }

        // prefab-mono service 的 GameObject 由 Addressables.InstantiateAsync 生 → 必須 ReleaseInstance：
        // 直接 Object.Destroy 會讓 prefab 的 asset handle ref-count 不歸零、asset 永不卸載（洩漏）。
        private static void ReleasePrefabInstance(object obj)
        {
            if (obj is MonoBehaviour mb && mb != null)
                Addressables.ReleaseInstance(mb.gameObject);
        }

        // SO service 副本由 Object.Instantiate 複製出（load handle 建立時已 Release）→ 與 Addressables 無關，直接 Destroy。
        private static void ReleaseScriptableInstance(object obj)
        {
            if (obj is UnityEngine.Object uo && uo != null)
                UnityEngine.Object.Destroy(uo);
        }

        // copy:false 唯讀 SO 直接是 Addressables 載入的磁碟資產（未複製）→ Addressables.Release 還 load handle。
        // 切勿 Object.Destroy：那會毀掉磁碟資產本身（其他持有者一起壞）。
        private static void ReleaseScriptableShared(object obj)
        {
            if (obj is UnityEngine.Object uo && uo != null)
                Addressables.Release(uo);
        }
    }
}
