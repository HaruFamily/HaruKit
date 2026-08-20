using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace HaruFamily.Framework.Nexus
{
    /// <summary>
    /// <b>拆除契約（全員一致、永遠無參）。</b>Nexus 釋放任何受管實例時呼 <see cref="OnRelease"/>。
    /// <para>
    /// 從原 <c>INexusLifecycle</c> 抽出的「teardown 軸」：release 不分有無建立參數、不分純 C# / Container / Owned，
    /// 形狀單一。<b>軟失敗</b>——Nexus 吞例外、記錄、續行（一個壞回呼不可中斷 sibling 連鎖釋放）。
    /// </para>
    /// </summary>
    public interface INexusRelease
    {
        UniTask OnRelease();
    }

    /// <summary>
    /// <b>無參的完整生命週期（init + release）＝ <c>NEXUS.Resolve*&lt;T&gt;</c>（get-or-create）的型別約束目標。</b>
    /// Nexus 在建立 / 註冊時呼 <see cref="OnInitialize"/>、釋放時呼繼承自 <see cref="INexusRelease"/> 的 <c>OnRelease</c>。
    /// 兼任「Nexus 納管門檻」：可被無參 Resolve 建立的型別須是它。
    /// <para>
    /// <paramref name="ct"/>：建立途中被 Release / ClearAll / 初始化逾時取消時會被 signalled。
    /// 長時間 / 可中斷的 init（外部 IO、等待 gate）<b>應觀察此 token</b>（傳給 await、定期 ThrowIfCancellationRequested），
    /// 否則取消只能 abandon-and-continue（Nexus 放棄 await，但你的 task 仍在背景跑完）。
    /// 註：透過 <c>RegisterGlobal/RegisterLocal(initialize:true)</c> 收養時走 <see cref="CancellationToken.None"/>（adopt-init 不可取消）。
    /// </para>
    /// <para>宣告例：純 C# 服務 <c>class SaveService : INexusLifecycle</c>；容器 <c>class StageService : INexusContainer, INexusLifecycle</c>。漏宣告 → 呼叫端 Resolve 編譯失敗（自我修正、不靜默）。</para>
    /// </summary>
    public interface INexusLifecycle : INexusRelease
    {
        UniTask OnInitialize(CancellationToken ct);
    }

    /// <summary>
    /// <b>必須帶參才能誕生的完整生命週期＝ <c>NEXUS.Resolve*&lt;T,TParam&gt;</c> 帶 TParam 多載的型別約束目標。</b>
    /// 參數直接進 <see cref="OnInitialize"/> 簽章（取代舊 <c>SetInitParam</c> + 無參 OnInitialize 兩步），初始化不分裂。
    /// 釋放走繼承自 <see cref="INexusRelease"/> 的 <c>OnRelease</c>。
    /// <para>
    /// 🔴 <b>刻意「不」繼承 <see cref="INexusLifecycle"/></b>（與它平行繼承 <see cref="INexusRelease"/>）：故帶參型別無法滿足
    /// <c>Resolve&lt;T : INexusLifecycle&gt;</c> 的約束 → 被無參 Resolve lazy 建立＝<b>compile error</b>，不是 runtime throw。
    /// 此分隔即「該帶參的不可被當成可 lazy 產生」的編譯期保證（手法同 <see cref="INexusOwned{TOwner}"/> 對 untyped 的分隔）。
    /// </para>
    /// <para>
    /// 建立後換參數＝換實例：先 <c>Release</c> 再帶參 <c>Resolve*</c>，或用不同 identityKey（同 key＝同實例）。
    /// 池化重用會再走 CreateAsync → 參數重新套用，不殘留。讀取既有實例用 <c>Get*</c>。
    /// </para>
    /// </summary>
    public interface INexusLifecycle<TParam> : INexusRelease
    {
        UniTask OnInitialize(TParam param, CancellationToken ct);
    }

    /// <summary>
    /// 承載一個可查詢身分 id 的型別。<see cref="NexusID"/> 是該實例的對外識別碼，
    /// 方便打包後在他處用此 id 經 <c>NEXUS.ById&lt;T&gt;</c> 反查回實例。
    /// <para>
    /// Nexus 對<b>任何</b> INexusID 實例於建立時寫入其實例 id、釋放時歸零（不限 container）；
    /// <see cref="INexusContainer"/> 固定繼承此介面——container 的 <see cref="NexusID"/> 即其 Local 作用域 id，
    /// children 用它認 owner。非 container 型別單獨實作即可被 id 反查。
    /// </para>
    /// </summary>
    public interface INexusID
    {
        int NexusID { get; set; }
    }

    /// <summary>
    /// 可作為 Local 作用域的擁有者。釋放此 container 時，Nexus 會連鎖釋放其所有 children。
    /// 實作端須初始化 <see cref="LocalChildren"/>（或交由 Nexus lazy 建立）。
    /// 其 <see cref="INexusID.NexusID"/> 即作用域 id（Nexus 建立時寫入），children 以此認 owner。
    /// <para>
    /// 🔴 base <b>只繼承 <see cref="INexusRelease"/></b>（不含 init）：init 形狀正交、每具體型別自宣告——
    /// 無參容器加 <see cref="INexusLifecycle"/>、帶參容器加 <see cref="INexusLifecycle{TParam}"/>。
    /// 故帶參容器不會經 base 漏進無參 <c>Resolve</c>，編譯期排除完整保留。
    /// </para>
    /// </summary>
    public interface INexusContainer : INexusID, INexusRelease
    {
        HashSet<int> LocalChildren { get; set; }
    }

    /// <summary>
    /// <b>內部基底——勿直接實作。</b>請改實作非泛型 <see cref="INexusOwned"/>（untyped）或泛型
    /// <see cref="INexusOwned{TOwner}"/>（typed，建議）。本型別只負責承載 <see cref="OwnerId"/>，
    /// 並當 Nexus 內部寫 / 清 OwnerId 的唯一多型 match 點（C# 無法 pattern-match 開放泛型 <c>INexusOwned&lt;&gt;</c>，
    /// 故需一個非泛型基底讓 Nexus 在不知 TOwner 時也能 <c>is INexusOwnedBase</c>）。
    /// <para>
    /// 建立時 Nexus 把 owner 的 <see cref="INexusID.NexusID"/> 寫進 <see cref="OwnerId"/>，釋放時歸零；
    /// Global 作用域（無 owner）維持 0。
    /// </para>
    /// <para>
    /// 設計上**刻意拆出 Base**：讓 <see cref="INexusOwned"/> 與 <see cref="INexusOwned{TOwner}"/> 平行繼承它、
    /// 而泛型版<b>不</b>繼承非泛型版。如此 <c>TransferOwned</c> 的兩個 overload（吃 <see cref="INexusOwned"/> /
    /// 吃 <see cref="INexusOwned{TOwner}"/>）對 typed 服務只會落到泛型 overload，避免「換到非 TOwner 的 container」
    /// 靜默掉回非泛型 overload。
    /// </para>
    /// <para>
    /// 🔴 base <b>只繼承 <see cref="INexusRelease"/></b>（不含 init）：同 <see cref="INexusContainer"/>，
    /// init 形狀每具體型別自宣告（無參 sibling 加 <see cref="INexusLifecycle"/>、帶參 sibling 加 <see cref="INexusLifecycle{TParam}"/>）。
    /// </para>
    /// </summary>
    public interface INexusOwnedBase : INexusRelease
    {
        int OwnerId { get; set; }
    }

    /// <summary>
    /// Local 子服務的 <b>untyped</b> 版：只要拿得到 owner id（跨查 owner 本身或同 owner 下 sibling），
    /// 不在意 owner 具體型別時實作這個。owner 反查走 <c>NEXUS.ById&lt;T&gt;(OwnerId)</c> 手動 cast。
    /// 要編譯期強型別 <c>this.Owner()</c> 請改實作 <see cref="INexusOwned{TOwner}"/>。
    /// </summary>
    public interface INexusOwned : INexusOwnedBase
    {

    }

    /// <summary>
    /// Local 子服務的 <b>typed</b> 版（建議）：把 owner 的具體型別 <typeparamref name="TOwner"/> 綁進泛型參數，
    /// 子服務內即可用 <c>this.Owner()</c>（見 <see cref="NexusOwnedExtensions.Owner{TOwner}"/>）取回強型別 owner
    /// （型別在編譯期確定，免手動 cast）。
    /// <para>
    /// 刻意「不存 owner 參考」——owner 由 <see cref="INexusOwnedBase.OwnerId"/> 經 Nexus 權威的 id→實例 map
    /// 即時解析（SSOT）。故無 dangling，池化重用換 owner 時也自動正確（每次建立都重寫 OwnerId）。
    /// Nexus 建立流程無需為此特別處理：泛型版 IS-A <see cref="INexusOwnedBase"/>，既有
    /// <c>owned.OwnerId = owner.NexusID</c> 已涵蓋。
    /// </para>
    /// <para>
    /// <b>不</b>繼承非泛型 <see cref="INexusOwned"/>（只與它平行繼承 <see cref="INexusOwnedBase"/>）：
    /// 此分隔讓 <c>TransferOwned</c> 對 typed 服務只能走 typed overload，誤換到非 <typeparamref name="TOwner"/>
    /// 的 container 會編譯失敗而非靜默。
    /// </para>
    /// <para>
    /// 維持「純 marker」（無欄位、無 default member）刻意配合 Mono/IL2CPP：owner 反查改走擴充方法
    /// <see cref="NexusOwnedExtensions.Owner{TOwner}"/>，可直接在具體型別上呼叫 <c>this.Owner()</c>
    /// （default interface member 只能透過 interface 參考存取，concrete 型別看不到，故不採用）。
    /// </para>
    /// </summary>
    public interface INexusOwned<TOwner> : INexusOwnedBase where TOwner : class, INexusContainer
    {

    }

    /// <summary>
    /// 可回池重用者。<see cref="OnSpawn"/> 在每次「啟用」時跑（新建或從池取回都跑）；
    /// <see cref="OnDespawn"/> 在每次釋放回池前跑（重置狀態避免下次 stale）。未實作此介面者，釋放後直接交 GC（不回池）。
    /// <para>
    /// prefab-mono service（標 <see cref="NexusPrefabAttribute"/>）同時實作此介面 → 連 GameObject 一起池化：
    /// 回池保留 GO（OnDespawn 建議 SetActive(false)），下次取回重用同一實例與 GO、跳過 InstantiateAsync、改跑 OnSpawn。
    /// </para>
    /// <para>
    /// 若同時是 <see cref="INexusContainer"/>，釋放時 Nexus 已在 <c>Release</c> 內自動歸零
    /// <c>NexusID</c> 並清空 <c>LocalChildren</c>（不分有無 children），故 <see cref="OnDespawn"/>
    /// 只需重置「服務自己的」業務狀態，不必再碰這兩個容器欄位。
    /// </para>
    /// </summary>
    public interface INexusPoolable
    {
        UniTask OnSpawn();
        UniTask OnDespawn();
    }
}
