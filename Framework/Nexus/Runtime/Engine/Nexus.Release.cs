using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace HaruFamily.Framework.Nexus
{
    // 釋放單一服務（連鎖 children、跑 OnRelease、回池 / prefab GO ReleaseInstance）與整包 ClearAll。
    public partial class Nexus
    {
        // === Release ===
        public UniTask ReleaseGlobal<T>(string identityKey = null)
            => _keyToId.TryGetValue(GlobalKey<T>(identityKey), out var id) ? Release(id) : UniTask.CompletedTask;

        public UniTask ReleaseLocal<T>(INexusContainer owner, string identityKey = null)
            => owner != null && owner.NexusID != 0 && _keyToId.TryGetValue(LocalKey<T>(owner, identityKey), out var id)
                ? Release(id) : UniTask.CompletedTask;

        public UniTask Release(int id) => Release(id, prune: true);

        // prune=false 只給 Register 覆蓋用（要原地沿用同一 id，不可抹掉 key 映射）。
        private async UniTask Release(int id, bool prune)
        {
            ThrowIfMisuse();

            CancelPending(id);   // 還在建立就被釋放 → 取消那次建立，半成品由 CreateAsync 的 catch 清掉。

            if (!_instances.TryGetValue(id, out var obj)) return;
            if (!_releasing.Add(id)) return;   // 重入保護：child 的 OnRelease 又觸發本 id 的 Release 時，第二次直接跳過。
            try
            {
                _instances.Remove(id);
                _deps.Remove(id);                                   // 清出邊
                foreach (var set in _deps.Values) set.Remove(id);   // 清入邊。漏清的話 prune:false 重用同一 id 時依賴圖會接到舊邊。

                if (prune) PruneKey(id);

                // 連鎖釋放 local children（container 才有），確保可池化容器回池前不殘留死 children。
                if (obj is INexusContainer container) await ReleaseChildrenOf(container);
                if (obj is INexusID identified) identified.NexusID = 0;   // 自身 id 歸零（不分有無 children，回池前清乾淨）
                if (obj is INexusOwnedBase owned) owned.OwnerId = 0;      // 子服務的 owner 反查欄位歸零（回池前清乾淨）

                // 回呼各自 try：一個壞回呼不可中斷 cascade（否則 sibling 漏放 / 漏回池）。
                // _releaseSync 只在「回呼同步段」內 +1（取 UniTask 前），進 await 後立刻 -1：
                // 期間禁止建立新服務（ThrowIfNotAcceptingNew 會擋），精準抓「釋放途中又生服務」而不誤傷 post-yield 的合法交錯。
                try
                {
                    // OnRelease 在 INexusRelease（teardown 軸）：涵蓋無參 INexusLifecycle、帶參 INexusLifecycle<TParam>、Container/Owned。
                    if (obj is INexusRelease rel)
                    {
                        UniTask relTask;
                        _releaseSync++;
                        try { relTask = rel.OnRelease(); } finally { _releaseSync--; }
                        await relTask;
                    }
                }
                catch (Exception e) { RaiseError(NexusErrorPhase.Release, obj, e); }

                // 先嘗試回池（內含 OnDespawn 停用）。入池=true → 保留實例與其 GameObject / SO 副本；
                // 沒入池（不可池 / 池滿）→ prefab-mono 走 ReleaseInstance、SO 副本走 Destroy（why 見各方法）。
                bool pooled = await PoolPush(obj);
                if (!pooled && _prefabOwned.Remove(obj)) ReleasePrefabInstance(obj);
                else if (!pooled && _scriptableOwned.Remove(obj)) ReleaseScriptableInstance(obj);
                else if (!pooled && _scriptableShared.Remove(obj)) ReleaseScriptableShared(obj);   // copy:false 唯讀 SO：還 Addressables handle

                Log($"release #{id} = {obj.GetType().Name}");
                RaiseLifecycle(NexusLifecyclePhase.Released, obj);
            }
            finally { _releasing.Remove(id); }
        }

        // 取消某 id 的 in-flight 建立並立刻移除 pending 條目：
        // 不立刻移除的話，後續 caller 會 join 到這個垂死 task 而一起吃到 OperationCanceledException。
        private void CancelPending(int id)
        {
            if (_pendingCts.TryGetValue(id, out var cts))
            {
                cts.Cancel();
                _pending.Remove(id);
                _pendingCts.Remove(id);
            }
        }

        private void PruneKey(int id)
        {
            if (_idToKey.TryGetValue(id, out var key))
            {
                _keyToId.Remove(key);
                _idToKey.Remove(id);
            }
            _idToAssetKey.Remove(id);   // 資產鍵旁路隨 key 生命週期一同清，避免 id 重用時讀到舊值
        }

        // === ClearAll ===
        // 全程 _clearing=true：期間拒絕一切新建立 / 註冊，杜絕「拆除 await 中又生出新實例」的 resurrection。
        public async UniTask ClearAll()
        {
            ThrowIfMisuse();
            if (_clearing) return;   // 重入兜底（例：OnRelease 內又呼 ClearAll）。
            _clearing = true;
            try
            {
                // 1) 取消所有 in-flight 建立。
                foreach (var cts in _pendingCts.Values.ToArray()) cts.Cancel();

                // 2) 等它們真正結束（含半成品清理）再往下，確保回傳即代表拆乾淨。各自吞例外（取消 / 逾時 / 失敗皆預期）。
                //    若某 init 不理會取消、又關了 InitTimeout（或 EditMode 無 PlayerLoop），這裡會等到它自然結束——屬呼叫端責任。
                foreach (var task in _pending.Values.ToArray())
                {
                    try { await task; } catch { /* 預期 */ }
                }

                // 3) 依「依賴反序」釋放：先放依賴別人的、後放被依賴的。snapshot 避免 await 期間集合被改。
                foreach (var id in ReleaseOrder()) await Release(id);

                if (_instances.Count > 0)
                    UnityEngine.Debug.LogWarning($"[Nexus] ClearAll 後仍殘留 {_instances.Count} 個實例（非預期）。");

                _keyToId.Clear();
                _idToKey.Clear();
                _idToAssetKey.Clear();
                _instances.Clear();
                _pending.Clear();
                _pendingCts.Clear();
                _registering.Clear();
                _releasing.Clear();
                _syncInit.Clear();
                _initChain.Clear();
                _deps.Clear();
                // 池中保留的 prefab GO 必須先 ReleaseInstance 再丟棄池內容，否則 GO 洩漏在場景。
                foreach (var pool in _pools.Values) DestroyPooledGameObjects(pool);
                _pools.Clear();
                _prefabOwned.Clear();
                _scriptableOwned.Clear();
                _scriptableShared.Clear();
                _nextId = 0;
                // resolver 多半指向某 Registry 服務的表；ClearAll 已連鎖放掉 Registry → 委派指向死表（stale），故一併清掉。
                AddressResolver = null;
                Log("clear all");
            }
            finally { _clearing = false; }
        }

        // 釋放順序：對 _deps（from 依賴 to）做 Kahn 拓樸排序，使 from 排在 to 前 → 先放依賴方、後放被依賴方，
        // 避免某服務的 OnRelease 還在用尚未釋放的依賴。沒人依賴（in-degree 0）者先出列；
        // 無邊節點與環殘留 fallback 到 id 升序。注意 _deps 是 best-effort，故此排序也只 best-effort。
        private List<int> ReleaseOrder()
        {
            var live = new HashSet<int>(_instances.Keys);
            var indeg = new Dictionary<int, int>(live.Count);
            foreach (var id in live) indeg[id] = 0;
            foreach (var kv in _deps)
            {
                if (!live.Contains(kv.Key)) continue;
                foreach (var to in kv.Value)
                    if (live.Contains(to)) indeg[to]++;
            }

            var ready = new SortedSet<int>();   // id 升序當穩定 tiebreak
            foreach (var id in live) if (indeg[id] == 0) ready.Add(id);

            var order = new List<int>(live.Count);
            var placed = new HashSet<int>();
            while (ready.Count > 0)
            {
                var n = ready.Min; ready.Remove(n);
                order.Add(n); placed.Add(n);
                if (_deps.TryGetValue(n, out var outs))
                    foreach (var to in outs)
                        if (live.Contains(to) && --indeg[to] == 0) ready.Add(to);
            }

            if (placed.Count < live.Count)   // 環殘留 → 插入序補上，保證全覆蓋
                foreach (var id in live.OrderBy(x => x))
                    if (!placed.Contains(id)) order.Add(id);

            return order;
        }
    }
}
