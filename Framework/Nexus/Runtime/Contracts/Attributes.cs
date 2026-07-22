using System;

namespace PinPlugin.Nexus
{
    /// <summary>
    /// 標註「MonoBehaviour service」的 Addressable 位址。Nexus 建立此型別時，pool miss 會以此位址
    /// <c>Addressables.InstantiateAsync</c> 生出 prefab → <c>GetComponent&lt;T&gt;</c> 取得實例。
    /// <para>
    /// 因 MonoBehaviour 無法 <c>new</c>，prefab-mono service 的實例必須來自 prefab 實例化（順序與純 C# service 相反：
    /// 先 Instantiate 才有實例）。它的 GameObject 就是 prefab 根，Nexus 直接用 <c>MonoBehaviour.gameObject</c> 管理
    /// （teardown 走 <c>Addressables.ReleaseInstance</c>，非 Destroy）。這是 Nexus 唯一帶 GameObject 的服務型態。
    /// 要池化 / 重置就實作 <see cref="INexusPoolable"/>（OnSpawn=SetActive(true)、OnDespawn=SetActive(false)）。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class NexusPrefabAttribute : Attribute
    {
        public string Address { get; }
        // address 可省略（位址改由 Nexus.AddressResolver 以 (型別,assetKey) 供給；見 nexus-usage §4c）
        public NexusPrefabAttribute(string address = null) => Address = address;
    }

    /// <summary>
    /// 標註「ScriptableObject service」的 Addressable 位址。Nexus 建立此型別時，pool miss 會以此位址
    /// <c>Addressables.LoadAssetAsync</c> 取 SO 資產 → <c>Object.Instantiate</c> 複製出『執行期副本』→ 立即 <c>Release</c> load handle。
    /// <para>
    /// 與 prefab-mono 對稱：SO 同樣無法 <c>new</c>、由 Addressables 產出。差別在動詞——prefab 是 <c>InstantiateAsync</c> 生新 GO；
    /// SO 是 <c>LoadAsset</c> 取『共用磁碟資產』，故必須 <c>Instantiate</c> 複製成獨立副本，否則改到副本=改到磁碟資產（狀態滲漏）。
    /// 副本與 Addressables 無關，teardown 走 <c>Object.Destroy</c>（非 ReleaseInstance）。要池化 / 重置就實作 <see cref="INexusPoolable"/>。
    /// </para>
    /// <para>
    /// <b>copy</b>（預設 true）決定要不要複製副本：
    /// <list type="bullet">
    /// <item><c>copy:true</c>＝有狀態的 SO 服務。LoadAsset → Instantiate 副本 → 立即 Release load handle，teardown 走 <c>Object.Destroy</c>。
    /// 改副本不影響磁碟資產。</item>
    /// <item><c>copy:false</c>＝唯讀資料 SO。<b>不複製</b>，直接共用 Addressables 載入的磁碟資產（零複製成本），
    /// load handle 持有至釋放、teardown 走 <c>Addressables.Release</c>。多方共用同一份、<b>禁止寫入</b>；
    /// 磁碟資料保護由呼叫端自律（Nexus 不強制）。唯讀資料才用，且不應再實作 <see cref="INexusPoolable"/>。</item>
    /// </list>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class NexusScriptableAttribute : Attribute
    {
        public string Address { get; }

        /// <summary>true=複製執行期副本（teardown Destroy）；false=共用磁碟資產免複製（teardown Addressables.Release，唯讀自律）。</summary>
        public bool Copy { get; }

        // address 可省略（位址改由 Nexus.AddressResolver 以 (型別,assetKey) 供給；見 nexus-usage §4c）
        public NexusScriptableAttribute(string address = null, bool copy = true)
        {
            Address = address;
            Copy = copy;
        }
    }
}
