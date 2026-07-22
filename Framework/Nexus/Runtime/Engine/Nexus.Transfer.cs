using System;

namespace PinPlugin.Nexus
{
    // 把既有 Local 服務從一個 owner 轉移到另一個 owner（reparent）。同一實例、同一 id，不重建、不跑生命週期回呼。
    // 兩個入口：TransferLocal（由 owner+型別+key 定位）、TransferOwned（直接給 Owned 實例）。共用 RebindLocal 核心。
    public partial class Nexus
    {
        /// <summary>
        /// 把 <typeparamref name="T"/>（identityKey=<paramref name="identityKey"/>）的 Local 服務由 <paramref name="from"/> reparent 到
        /// <paramref name="to"/>。轉移後 <paramref name="to"/> 釋放會連鎖放它、<paramref name="from"/> 不再擁有。
        /// <para>
        /// 兩 owner 都須已受管（NexusID != 0，否則 parentId=0 撞全域 key）。服務須已建好（建立中 / 不存在 → 例外）。
        /// <paramref name="to"/> 已有同 (T,key) 的活 / 建立中服務 → 拒絕覆蓋。已知「實例」而非 (owner,key) 時用 <see cref="TransferOwned"/>。
        /// </para>
        /// </summary>
        public void TransferLocal<T>(INexusContainer from, INexusContainer to, string identityKey = null) where T : class
        {
            ThrowIfMisuse();
            if (_clearing)
                throw new InvalidOperationException("Nexus 正在 ClearAll/Pop 拆除中，拒絕轉移 Local 服務。");
            RequireManaged(from);
            RequireManaged(to);

            var oldKey = LocalKey<T>(from, identityKey);
            if (!_keyToId.TryGetValue(oldKey, out var id) || !_instances.ContainsKey(id))
                throw new InvalidOperationException(
                    $"找不到可轉移的 Local<{typeof(T).Name}>（owner={from.GetType().Name}#{from.NexusID}, key='{identityKey}'），或它還在建立中。");

            RebindLocal(id, oldKey, to);
        }

        /// <summary>
        /// 轉移 <b>typed</b> <see cref="INexusOwned{TOwner}"/> 服務實例到 <paramref name="to"/>。<paramref name="to"/>
        /// 釘死成同型 <typeparamref name="TOwner"/> → 誤換到別型 container <b>編譯失敗</b>（而非靜默讓 <c>Owner()</c> 回 null）。
        /// <para>typed 只落本 overload（<see cref="INexusOwned{TOwner}"/> 不繼承非泛型）。守衛同 <see cref="TransferLocal{T}"/>。</para>
        /// </summary>
        public void TransferOwned<TOwner>(INexusOwned<TOwner> owned, TOwner to) where TOwner : class, INexusContainer
            => TransferOwnedCore(owned, to);

        /// <summary>
        /// 轉移 <b>untyped</b> <see cref="INexusOwned"/> 服務「實例」到 <paramref name="to"/>（同 <see cref="TransferLocal{T}"/>
        /// 語意，改用實例定位）。typed 服務改用泛型 overload <see cref="TransferOwned{TOwner}"/>（有編譯期型別檢查）。
        /// <para>
        /// <paramref name="owned"/> 須為受管的 <b>Local</b> 服務（全域無 owner 可換、未受管 → 例外）。
        /// <paramref name="to"/> 須已受管且未持有同 (型別,key)。
        /// </para>
        /// </summary>
        public void TransferOwned(INexusOwned owned, INexusContainer to)
            => TransferOwnedCore(owned, to);

        // 兩 overload 共用：反查實例 id、取 oldKey、檢查非全域，再進 RebindLocal。owned 型別吃 Base（兩 overload 都涵蓋）。
        private void TransferOwnedCore(INexusOwnedBase owned, INexusContainer to)
        {
            ThrowIfMisuse();
            if (_clearing)
                throw new InvalidOperationException("Nexus 正在 ClearAll/Pop 拆除中，拒絕轉移 Local 服務。");
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            RequireManaged(to);

            // OwnerId 是「owner 的 id」非服務自身 id，故反查 _instances 取服務自己的 id。
            var id = FindInstanceId(owned);
            if (id < 0)
                throw new InvalidOperationException(
                    $"{owned.GetType().Name} 不在 Nexus 管理中（未建立 / 已釋放 / 建立中），無法轉移。");
            if (!_idToKey.TryGetValue(id, out var oldKey))
                throw new InvalidOperationException($"{owned.GetType().Name} 無 key 映射（內部狀態異常）。");
            if (oldKey.ParentId == 0)
                throw new InvalidOperationException($"{owned.GetType().Name} 是全域服務（非 Local），無 owner 可替換。");

            RebindLocal(id, oldKey, to);
        }

        // 共用核心：把 id 對應的 Local 服務（現 key=oldKey）改掛到 to 名下。只動 SSOT，不碰生命週期。
        private void RebindLocal(int id, Key oldKey, INexusContainer to)
        {
            if (oldKey.ParentId == to.NexusID) return;   // 同 owner → no-op

            var newKey = new Key(oldKey.ComponentType, oldKey.IdentityKey, to.NexusID);
            if (_keyToId.TryGetValue(newKey, out var existingId) && existingId != id)
            {
                if (_instances.ContainsKey(existingId) || _pending.ContainsKey(existingId))
                    throw new InvalidOperationException(
                        $"目標 owner（{to.GetType().Name}#{to.NexusID}）已有 Local<{oldKey.ComponentType.Name}>(key='{oldKey.IdentityKey}')，拒絕覆蓋。請先 ReleaseLocal 再轉移。");
                PruneKey(existingId);   // 殘留死 key（極少見）→ 清掉再沿用 newKey
            }

            // 1) re-key：搬到新 parentId 名下（id 不變）。
            _keyToId.Remove(oldKey);
            _keyToId[newKey] = id;
            _idToKey[id] = newKey;

            // 2) 搬 child link：舊 owner 經 oldKey.ParentId 反查 → 不再擁有；to 擁有 → 連鎖釋放跟著走 to。
            var from = ById<INexusContainer>(oldKey.ParentId);
            UnlinkChild(from, id);
            LinkChild(to, id);

            // 3) owner 反查欄位指向新 owner（INexusOwnedBase 才有）。
            if (_instances[id] is INexusOwnedBase owned) owned.OwnerId = to.NexusID;

            Log($"transfer #{id} ({oldKey.ComponentType.Name}) → {to.GetType().Name}#{to.NexusID}");
            // 結構已改（reparent）→ 發事件，讓 telemetry / Service Tree 視窗即時重抓。同 owner no-op 已於上方 early-return，不會到這。
            RaiseLifecycle(NexusLifecyclePhase.Transferred, _instances[id]);
        }

        // 反查實例的 id（_instances 是 id→instance，無反向 map；轉移屬低頻操作，線性掃可接受）。不存在回 -1。
        private int FindInstanceId(object instance)
        {
            foreach (var kv in _instances)
                if (ReferenceEquals(kv.Value, instance)) return kv.Key;
            return -1;
        }
    }
}
