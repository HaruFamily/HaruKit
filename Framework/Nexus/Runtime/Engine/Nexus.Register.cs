using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace PinPlugin.Nexus
{
    // 收養既有實例（自己 new 好再交給 Nexus 管）。走與 CreateAsync 相同的硬失敗契約。
    public partial class Nexus
    {
        public UniTask RegisterGlobal<T>(T instance, string identityKey = null, bool initialize = true) where T : class
            => InternalRegister(GetId(GlobalKey<T>(identityKey)), instance, null, initialize);

        public UniTask RegisterLocal<T>(INexusContainer owner, T instance, string identityKey = null, bool initialize = true) where T : class
        {
            RequireManaged(owner);
            return InternalRegister(GetId(LocalKey<T>(owner, identityKey)), instance, owner, initialize);
        }

        private async UniTask InternalRegister(int id, object instance, INexusContainer owner, bool initialize)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            ThrowIfMisuse();
            ThrowIfNotAcceptingNew();

            // 防呆：一實例只能綁一個 key。已在別的 key 下受管還拿來收養 → 兩 key 同指一物，但 id 戳記只剩後者，
            // 造成 stale id / 雙重 OnRelease / ById 反查錯亂。純 class 服務無 INexusID 戳記、無法靠欄位判斷，
            // 故掃 _instances 認物件本身（Register 非熱路徑，O(n) 可接受）；排除 id 自身以放行同 key 重註冊。
            foreach (var kv in _instances)
                if (kv.Key != id && ReferenceEquals(kv.Value, instance))
                    throw new InvalidOperationException(
                        $"Register 拒收已受管實例（{instance.GetType().Name} 已綁 #{kv.Key}）。" +
                        "一實例只能有一個 key：先 Release 舊 key，或用 Transfer 搬遷。");

            if (!_registering.Add(id))
                throw new InvalidOperationException(
                    $"Nexus 偵測到同 key 並發 Register（{TypeName(id)} #{id}）：前一個 Register 尚未完成。" +
                    "請先 await 前一個 Register（或對應的 Global）再覆蓋。");
            try
            {
                CancelPending(id);                 // 同 id 正在建立 → 先取消，免得它建完後蓋掉這次 register。
                await Release(id, prune: false);   // 覆蓋舊的，但保留 key 映射（id 原地沿用）。

                // 上面 await 期間 owner 可能已被釋放。若不重查就寫入，新實例會 orphan 在死 owner 下。
                if (owner != null && !_instances.ContainsKey(owner.NexusID))
                {
                    PruneKey(id);
                    throw new InvalidOperationException(
                        $"RegisterLocal 期間 owner（{owner.GetType().Name}）已被釋放，取消註冊以免 orphan。");
                }

                if (instance is INexusID identified) identified.NexusID = id;
                if (owner != null && instance is INexusOwnedBase owned) owned.OwnerId = owner.NexusID;
                _instances[id] = instance;
                LinkChild(owner, id);
                Log($"register #{id} = {instance.GetType().Name}");

                // OnInitialize 失敗 = 硬失敗：把剛寫進去的半成品整個撤掉再往上拋，不留可被 TryGet 取到的半初始化實例。
                // adopt 不參與 in-flight 取消機制，故傳 None。
                if (initialize && instance is INexusLifecycle life)
                {
                    try { await life.OnInitialize(CancellationToken.None); }
                    catch (Exception e)
                    {
                        await ReleaseChildrenOf(instance);
                        _instances.Remove(id);
                        UnlinkChild(owner, id);
                        if (instance is INexusID idn) idn.NexusID = 0;
                        if (instance is INexusOwnedBase ow) ow.OwnerId = 0;
                        PruneKey(id);
                        if (e is not OperationCanceledException and not NexusCircularDependencyException)
                            RaiseError(NexusErrorPhase.Initialize, instance, e);
                        throw;
                    }
                }

                // Created 在 init 成功後才發（與 CreateAsync 一致）：失敗的不發。
                RaiseLifecycle(NexusLifecyclePhase.Created, instance);
            }
            finally { _registering.Remove(id); }
        }
    }
}
