using Cysharp.Threading.Tasks;

namespace HaruFamily.Framework.Nexus
{
    /// <summary>
    /// 靜態捷徑，等同 <c>Nexus.Instance.*</c>。
    /// </summary>
    public static class NEXUS
    {
        // === Resolve = Get or Create（async；不存在則建立 + OnInitialize；型別須為 INexusLifecycle＝無參）===
        public static UniTask<T> ResolveGlobal<T>(string identityKey = null, string assetKey = null) where T : class, INexusLifecycle, new()
            => Nexus.Instance.ResolveGlobal<T>(identityKey, assetKey);

        public static UniTask<T> ResolveLocal<T>(INexusContainer owner, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle, new()
            => Nexus.Instance.ResolveLocal<T>(owner, identityKey, assetKey);

        /// <summary>介面綁定：identityKey 走 TInterface，建立 new TImpl()（具體型別須為 INexusLifecycle），呼叫端只看到 interface。</summary>
        public static UniTask<TInterface> ResolveGlobal<TInterface, TImpl>(string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle, new()
            => Nexus.Instance.ResolveGlobal<TInterface, TImpl>(identityKey, assetKey);

        public static UniTask<TInterface> ResolveLocal<TInterface, TImpl>(INexusContainer owner, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle, new()
            => Nexus.Instance.ResolveLocal<TInterface, TImpl>(owner, identityKey, assetKey);

        // === 帶 TParam 多載 = 帶建立參數（create 專用；型別須為 INexusLifecycle<TParam>；參數進 OnInitialize(param,ct)）===
        /// <summary>建立並把 <paramref name="param"/> 餵給 <c>INexusLifecycle&lt;TParam&gt;</c> 服務的 <c>OnInitialize(param, ct)</c>。<b>create 專用</b>：同 key 已存在 / 建立中丟例外（讀取用 Get*）。見 nexus-usage §3。</summary>
        public static UniTask<T> ResolveGlobal<T, TParam>(TParam param, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle<TParam>, new()
            => Nexus.Instance.ResolveGlobal<T, TParam>(param, identityKey, assetKey);

        public static UniTask<T> ResolveLocal<T, TParam>(INexusContainer owner, TParam param, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle<TParam>, new()
            => Nexus.Instance.ResolveLocal<T, TParam>(owner, param, identityKey, assetKey);

        /// <summary>介面綁定 + 傳參：key=TInterface，建立 <c>new TImpl()</c> 並把 <paramref name="param"/> 餵給 <c>OnInitialize(param, ct)</c>。create 專用（見 nexus-usage §3）。</summary>
        public static UniTask<TInterface> ResolveGlobal<TInterface, TImpl, TParam>(TParam param, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle<TParam>, new()
            => Nexus.Instance.ResolveGlobal<TInterface, TImpl, TParam>(param, identityKey, assetKey);

        public static UniTask<TInterface> ResolveLocal<TInterface, TImpl, TParam>(INexusContainer owner, TParam param, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle<TParam>, new()
            => Nexus.Instance.ResolveLocal<TInterface, TImpl, TParam>(owner, param, identityKey, assetKey);

        // === Register（adopt 既有實例）===
        public static UniTask RegisterGlobal<T>(T instance, string identityKey = null, bool initialize = true) where T : class
            => Nexus.Instance.RegisterGlobal(instance, identityKey, initialize);

        public static UniTask RegisterLocal<T>(INexusContainer owner, T instance, string identityKey = null, bool initialize = true) where T : class
            => Nexus.Instance.RegisterLocal(owner, instance, identityKey, initialize);

        // === Release ===
        public static UniTask ReleaseGlobal<T>(string identityKey = null)
            => Nexus.Instance.ReleaseGlobal<T>(identityKey);

        public static UniTask ReleaseLocal<T>(INexusContainer owner, string identityKey = null)
            => Nexus.Instance.ReleaseLocal<T>(owner, identityKey);

        /// <summary>用 NexusID 直接釋放（Global/Local 通用，免反查 owner+identityKey）。id 不存在 = no-op。連鎖回收 children、跑 OnRelease、回池/釋放實例。</summary>
        public static UniTask ReleaseById(int id)
            => Nexus.Instance.Release(id);

        /// <summary>把既有 Local 服務從 <paramref name="from"/> owner 轉移到 <paramref name="to"/> owner（由型別+identityKey 定位；同一實例、不重建）。</summary>
        public static void TransferLocal<T>(INexusContainer from, INexusContainer to, string identityKey = null) where T : class
            => Nexus.Instance.TransferLocal<T>(from, to, identityKey);

        /// <summary>把 typed <see cref="INexusOwned{TOwner}"/> 服務實例轉移到同型 owner（誤換非 TOwner 編譯失敗；同一實例、不重建）。</summary>
        public static void TransferOwned<TOwner>(INexusOwned<TOwner> owned, TOwner to) where TOwner : class, INexusContainer
            => Nexus.Instance.TransferOwned(owned, to);

        /// <summary>把 untyped <see cref="INexusOwned"/> 服務實例轉移到 <paramref name="to"/> owner（由實例定位；同一實例、不重建）。</summary>
        public static void TransferOwned(INexusOwned owned, INexusContainer to)
            => Nexus.Instance.TransferOwned(owned, to);

        public static UniTask ClearAll()
            => Nexus.Instance.ClearAll();

        // === Query（同步，不觸發建立）===
        /// <summary>同步取現有 Global 實例；不存在 / 建立中 / 型別不符回 null（不觸發建立）。</summary>
        public static T GetGlobal<T>(string identityKey = null) where T : class
            => Nexus.Instance.GetGlobal<T>(identityKey);

        /// <summary>同步取現有 Local 實例；不存在 / 建立中 / 型別不符回 null（不觸發建立）。</summary>
        public static T GetLocal<T>(INexusContainer owner, string identityKey = null) where T : class
            => Nexus.Instance.GetLocal<T>(owner, identityKey);

        public static bool TryGetGlobal<T>(out T instance, string identityKey = null) where T : class
            => Nexus.Instance.TryGetGlobal(out instance, identityKey);

        public static bool TryGetLocal<T>(INexusContainer owner, out T instance, string identityKey = null) where T : class
            => Nexus.Instance.TryGetLocal(owner, out instance, identityKey);

        /// <summary><see cref="GetLocal{T}(INexusContainer,string)"/> 的 id 多載：owner 以 NexusID 指定（非實例）；供組裝期 sibling 查詢（owner 尚未 commit 仍可定位已建好的 sibling）。</summary>
        public static T GetLocal<T>(int ownerId, string identityKey = null) where T : class
            => Nexus.Instance.GetLocal<T>(ownerId, identityKey);

        public static bool TryGetLocal<T>(int ownerId, out T instance, string identityKey = null) where T : class
            => Nexus.Instance.TryGetLocal(ownerId, out instance, identityKey);

        /// <summary>同步用 id 取已存在實例；不存在 / 建立中 / 型別不符回 null。owner 反查用 <c>child.Owner()</c>。</summary>
        public static T ById<T>(int id) where T : class
            => Nexus.Instance.ById<T>(id);

        /// <summary>同步取某實例自身被建立時的 identityKey（Global / 無子鍵回 ""，id 不存在回 null）。</summary>
        public static string IdentityKeyOf(int id)
            => Nexus.Instance.IdentityKeyOf(id);

        /// <summary>同步取某實例的資產鍵 assetKey（與身分 identityKey 分離；未設則回退 identityKey，id 不存在回 null）。</summary>
        public static string AssetKeyOf(int id)
            => Nexus.Instance.AssetKeyOf(id);
    }
}
