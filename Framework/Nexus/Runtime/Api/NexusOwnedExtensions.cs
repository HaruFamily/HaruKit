using Cysharp.Threading.Tasks;
using System;

namespace HaruFamily.Framework.Nexus
{
    /// <summary>
    /// <see cref="INexusOwned{TOwner}"/> 的 owner 反查入口（取代舊 <c>NEXUS.OwnerOf(child)</c>）。
    /// 與介面同檔，視為其「內部」用法。
    /// </summary>
    public static class NexusOwnedExtensions
    {
        /// <summary>
        /// 取回 <paramref name="child"/> 的強型別 owner。<typeparamref name="TOwner"/> 由
        /// <see cref="INexusOwned{TOwner}"/> 推斷，編譯期確定。經 <see cref="INexusOwnedBase.OwnerId"/> 解析
        /// id→實例 map（SSOT）；owner 已釋放 / Global 取用（OwnerId==0）時回 null。
        /// <para>勿快取結果——池化換 owner 後會 stale；要快取請在 OnDespawn 清。</para>
        /// </summary>
        public static TOwner Owner<TOwner>(this INexusOwned<TOwner> child) where TOwner : class, INexusContainer
            => NEXUS.ById<TOwner>(child.OwnerId);

        /// <summary>
        /// 取同一 owner 底下的另一個 Local 服務（sibling），<b>async get-or-create</b>：不存在就建立。
        /// 等同 <c>await NEXUS.ResolveLocal&lt;TSibling&gt;(owner, identityKey)</c>，owner 經 <see cref="INexusOwnedBase.OwnerId"/> 解析（與 <see cref="Owner{TOwner}"/> 同 SSOT）。
        /// 用於<b>組裝</b>：Logic 在 OnInitialize 首次建它的 Data/View sibling。
        /// <para>
        /// 刻意只吃 <b>單一</b> 型別參數 <typeparamref name="TSibling"/>——owner 用 id 取、不需其型別，故
        /// <c>this.ResolveSibling&lt;DeckRuntime&gt;()</c> 即可（免再寫 owner 型別）。撐扁平 sibling 架構
        /// （Global owner → 同層 Local Data/Runtime/View 互取）。
        /// </para>
        /// <para><paramref name="child"/> 非 Local（OwnerId==0）/ owner 已釋放 → 丟例外（無 owner 可掛 sibling）。</para>
        /// </summary>
        public static UniTask<TSibling> ResolveSibling<TSibling>(this INexusOwnedBase child, string identityKey = null)
            where TSibling : class, INexusLifecycle, new()
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            var owner = NEXUS.ById<INexusContainer>(child.OwnerId);
            if (owner == null)
                throw new InvalidOperationException(
                    $"{child.GetType().Name} 無 owner 可取 sibling（OwnerId={child.OwnerId}：非 Local 服務或 owner 已釋放）。");
            return NEXUS.ResolveLocal<TSibling>(owner, identityKey);
        }

        /// <summary>
        /// 取同一 owner 底下已建好的另一個 Local 服務（sibling），<b>sync、不建立</b>：抓不到回 null。
        /// 等同 <c>NEXUS.GetLocal&lt;TSibling&gt;(owner, identityKey)</c>。用於<b>熱路徑重取</b>：抽換/池化下禁快取 sibling，每次操作重取才看得到新身（§9.4）。
        /// <para><paramref name="child"/> 為 null / 非 Local（OwnerId==0）/ owner 已釋放 / sibling 未建 → 回 null（不丟例外，sync 查詢契約）。</para>
        /// </summary>
        public static TSibling GetSibling<TSibling>(this INexusOwnedBase child, string identityKey = null)
            where TSibling : class
        {
            if (child == null) return null;
            // 用 child 自帶的 OwnerId 直接定址 sibling（GetLocal 的 id 多載），不經 ById(owner)：owner 在「自身 OnInitialize 內建齊 child」時尚未進 _instances，
            // ById 反查會回 null；但 sibling 只需 owner 的 id 來組 key，故 id 直查即可（組裝期、熱路徑皆可用）。前提：sibling 已先建好（建立順序由組裝端控制）。
            return NEXUS.GetLocal<TSibling>(child.OwnerId, identityKey);
        }

        /// <summary>
        /// <see cref="GetSibling{TSibling}"/> 的 bool 版（對齊 <c>TryGetLocal/TryGetGlobal</c>）：抓到回 true + out 實例，否則 false + null。
        /// 供需分辨「有無」的判定式用（<c>if (this.TryGetSibling(out var v)) ...</c>）；<b>sync、不建立</b>。
        /// <para><paramref name="child"/> 為 null / 非 Local（OwnerId==0）/ owner 已釋放 / sibling 未建 → false（不丟例外）。</para>
        /// </summary>
        public static bool TryGetSibling<TSibling>(this INexusOwnedBase child, out TSibling sibling, string identityKey = null)
            where TSibling : class
        {
            sibling = null;
            if (child == null) return false;
            // 同 GetSibling：用 OwnerId 直查（TryGetLocal 的 id 多載），組裝期（owner 未 commit）亦可。
            return NEXUS.TryGetLocal(child.OwnerId, out sibling, identityKey);
        }
    }
}
