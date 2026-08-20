using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace HaruFamily.Framework.Nexus
{
    // Key 鑄造 / id 配發、是否收新請求的閘，以及同步查詢（不觸發建立）。
    public partial class Nexus
    {
        // === Key / Id ===
        internal Key GlobalKey<T>(string identityKey = null) => new(typeof(T), identityKey ?? "");

        internal Key LocalKey<T>(INexusContainer owner, string identityKey = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return new(typeof(T), identityKey ?? "", owner.NexusID);
        }

        // owner 以 NexusID 指定的多載（不需 owner 實例）。
        // 為何需要：sibling 查詢只需 owner 的 id 來定址同 owner 下的 child；child 已帶 OwnerId。
        // 走 owner 實例（ById）會要求 owner 已進 _instances，但 owner 在「自身 OnInitialize 內建 child」時尚未 commit → 反查失敗。改用 id 直接組 key 免此限。
        internal Key LocalKey<T>(int ownerId, string identityKey = null)
            => new(typeof(T), identityKey ?? "", ownerId);

        internal int GetId(Key key)
        {
            if (_keyToId.TryGetValue(key, out var id)) return id;
            id = ++_nextId;
            _keyToId[key] = id;
            _idToKey[id] = key;
            return id;
        }

        // 拆除中不收新請求，兩種情境：
        //   _clearing    — ClearAll/Pop 全程，避免拆除 await 期間又生出新實例（resurrection）。
        //   _releaseSync — 在某 OnRelease/OnDestroy 的同步段內，擋「釋放途中再生服務」。
        private void ThrowIfNotAcceptingNew()
        {
            if (_clearing)
                throw new InvalidOperationException(
                    "Nexus 正在 ClearAll/Pop 拆除中，拒絕新的建立 / 註冊（勿在 OnRelease/OnDestroy 內再建立服務）。");
            if (_releaseSync > 0)
                throw new InvalidOperationException(
                    "勿在 OnRelease/OnDestroy 的同步段內建立 / 註冊服務。如確有需要，請改在釋放完成後再建立。");
        }

        // Local owner 必須先被 Nexus 管理（NexusID != 0）。否則 parentId=0 → Local key 會跟全域 key 撞名，默默拿錯實例。
        private static void RequireManaged(INexusContainer owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (owner.NexusID == 0)
                throw new InvalidOperationException(
                    $"Local owner（{owner.GetType().Name}）尚未被 Nexus 管理（NexusID==0）。請先 Global/RegisterGlobal/RegisterLocal 取得 owner，再對它開 Local 服務。");
        }

        // === Query（同步，不觸發建立）===
        public bool Contains(int id) => _instances.ContainsKey(id);

        /// <summary>
        /// 同步用 id 取已存在實例；id 不存在 / 建立中 / 型別不符回 null。
        /// id 來源為 <see cref="INexusID.NexusID"/> / <see cref="INexusOwnedBase.OwnerId"/>（SSOT）。
        /// </summary>
        public T ById<T>(int id) where T : class
            => _instances.TryGetValue(id, out var obj) ? obj as T : null;

        /// <summary>
        /// 同步取某實例自身的 identityKey（建立此實例時傳入的身分子鍵，同 owner 下區分多份；Global / 無子鍵回 ""）。id 不存在回 null。
        /// 純身分軸；資產路由請改讀 <see cref="AssetKeyOf"/>（未分離時兩者同值）。id 來源為 <see cref="INexusID.NexusID"/>。
        /// </summary>
        public string IdentityKeyOf(int id) => _idToKey.TryGetValue(id, out var k) ? k.IdentityKey : null;

        /// <summary>
        /// 同步取某實例的資產鍵 assetKey（決定下游資產位址）。建立時有記 assetKey 回該值，未指定即回空字串 ""。
        /// 純資產軸，與身分 identityKey 完全獨立、永不互換（assetKey 不借 identityKey，identityKey 不參與資產路由）。詳 nexus-usage §3。
        /// </summary>
        public string AssetKeyOf(int id)
            => _idToAssetKey.TryGetValue(id, out var ak) ? ak : "";

        public bool ContainsGlobal<T>(string identityKey = null) =>
            _keyToId.TryGetValue(GlobalKey<T>(identityKey), out var id) && _instances.ContainsKey(id);

        public bool ContainsLocal<T>(INexusContainer owner, string identityKey = null) =>
            owner != null && owner.NexusID != 0 &&
            _keyToId.TryGetValue(LocalKey<T>(owner, identityKey), out var id) && _instances.ContainsKey(id);

        /// <summary>同步取現有全域實例；不存在 / 建立中 / 型別不符回 null（不觸發建立）。</summary>
        public T GetGlobal<T>(string identityKey = null) where T : class
            => TryGet<T>(GlobalKey<T>(identityKey), out var instance) ? instance : null;

        /// <summary>同步取現有 local 實例；owner 未受管 / 不存在 / 建立中 / 型別不符回 null（不觸發建立）。</summary>
        public T GetLocal<T>(INexusContainer owner, string identityKey = null) where T : class
            => TryGetLocal<T>(owner, out var instance, identityKey) ? instance : null;

        /// <summary>同步取已存在的全域實例；不存在或還在建立中回 false。</summary>
        public bool TryGetGlobal<T>(out T instance, string identityKey = null) where T : class
            => TryGet(GlobalKey<T>(identityKey), out instance);

        /// <summary>同步取已存在的 local 實例；owner 未受管 / 不存在 / 建立中皆回 false，不丟例外。</summary>
        public bool TryGetLocal<T>(INexusContainer owner, out T instance, string identityKey = null) where T : class
        {
            instance = null;
            if (owner == null || owner.NexusID == 0) return false;   // 未受管 owner 視為沒有，避免撞全域 key
            return TryGet(LocalKey<T>(owner, identityKey), out instance);
        }

        /// <summary>
        /// <see cref="TryGetLocal{T}(INexusContainer,out T,string)"/> 的 id 多載：owner 以 <b>NexusID</b> 指定（非實例）。
        /// 供組裝期 sibling 查詢：owner 仍在自身 OnInitialize、尚未進 _instances 時，已建好的 sibling 仍定位得到（child 自帶 OwnerId）。
        /// ownerId==0（非 Local）/ 不存在 / 建立中回 false，不丟例外。
        /// </summary>
        public bool TryGetLocal<T>(int ownerId, out T instance, string identityKey = null) where T : class
        {
            instance = null;
            if (ownerId == 0) return false;   // 非 Local（Global owner）視為沒有，避免撞全域 key
            return TryGet(LocalKey<T>(ownerId, identityKey), out instance);
        }

        /// <summary><see cref="GetLocal{T}(INexusContainer,string)"/> 的 id 多載：抓不到回 null。見 <see cref="TryGetLocal{T}(int,out T,string)"/>。</summary>
        public T GetLocal<T>(int ownerId, string identityKey = null) where T : class
            => TryGetLocal<T>(ownerId, out var instance, identityKey) ? instance : null;

        private bool TryGet<T>(Key key, out T instance) where T : class
        {
            instance = null;
            if (!_keyToId.TryGetValue(key, out var id)) return false;
            if (!_instances.TryGetValue(id, out var obj)) return false;   // 建立中也算沒有
            instance = obj as T;
            return instance != null;
        }
    }
}
