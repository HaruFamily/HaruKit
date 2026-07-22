#if NEXUS_EXAMPLE
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PinPlugin.Nexus
{
    // 使用範例。預設不編譯；要試跑在 Player Settings → Scripting Define Symbols 加 NEXUS_EXAMPLE。
    #region === Example ===

    // 1) 純 C# 服務：只要生命週期，無 GameObject。
    public class SaveService : INexusLifecycle
    {
        public UniTask OnInitialize(CancellationToken ct) { Debug.Log("[Save] load"); return UniTask.CompletedTask; }
        public UniTask OnRelease()    { Debug.Log("[Save] flush"); return UniTask.CompletedTask; }
        public void Set(string k, int v) { /* ... */ }
    }

    // 2) prefab-mono：service 是 prefab 上的 MonoBehaviour。[NexusPrefab] → InstantiateAsync → GetComponent；teardown 走 ReleaseInstance（非 Destroy）。
    [NexusPrefab("Nexus/Examples/Audio")]
    public class AudioService : MonoBehaviour, INexusLifecycle
    {
        private AudioSource src;
        public UniTask OnInitialize(CancellationToken ct) { src = GetComponent<AudioSource>(); return UniTask.CompletedTask; }
        public UniTask OnRelease()    => UniTask.CompletedTask;
        public void Play(AudioClip c) => src.PlayOneShot(c);
    }

    // 3) prefab-mono + 池化：OnSpawn/OnDespawn 控 SetActive。回池保留 GO，再取重用同一實例與 GO（跳過 InstantiateAsync）。
    [NexusPrefab("Nexus/Examples/EnemyView")]
    public class EnemyView : MonoBehaviour, INexusLifecycle, INexusPoolable
    {
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()  => UniTask.CompletedTask;
        public UniTask OnSpawn()    { gameObject.SetActive(true);  return UniTask.CompletedTask; }
        public UniTask OnDespawn()  { gameObject.SetActive(false); return UniTask.CompletedTask; }
    }

    // 4) copy:true SO-service（有狀態）：[NexusScriptable] → LoadAsset → Instantiate 副本 → 立即 Release handle；teardown 走 Destroy。
    //    必複製：改副本不汙染磁碟資產。
    [NexusScriptable("Nexus/Examples/RunRule")]   // copy 預設 true
    public class RunRuleService : ScriptableObject, INexusLifecycle
    {
        [SerializeField] private int critRate = 15;   // 企劃在 SO inspector 調；副本獨立
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()  => UniTask.CompletedTask;
        public bool RollCrit(int roll) => roll < critRate;
    }

    // 5) 標準 B 組裝：Container 下放 Data / Logic / View sibling，皆可獨立抽換（規則見 SKILL §8b）。

    // 5a) 唯讀資料 SO：copy:false → 共用磁碟資產、零複製、禁寫。lifecycle 為納管門檻所需 → no-op（無建構/釋放副作用）。
    [NexusScriptable("Nexus/Examples/UnitConfig", copy: false)]
    public class UnitConfigSO : ScriptableObject, INexusLifecycle
    {
        public int MaxHp = 30; public float Speed = 5f;
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease() => UniTask.CompletedTask;
    }

    // 5b) Container = 組裝點 + 釋放邊界。釋放它 → 連鎖回收其下 sibling。
    public class UnitService : INexusContainer, INexusLifecycle
    {
        public int NexusID { get; set; }
        public HashSet<int> LocalChildren { get; set; }
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()    => UniTask.CompletedTask;
    }

    // 5c) Data = 可變狀態 → 純 C# sibling 節點（此處要 OwnerId 故實作 INexusOwned）。
    public class UnitState : INexusOwned<UnitService>, INexusLifecycle
    {
        public int OwnerId { get; set; }
        public int Hp;   // 可變黑板：Logic 讀寫、View 反映
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()    => UniTask.CompletedTask;
    }

    // 5d) View = prefab-mono sibling，反映 Data（影子）。
    [NexusPrefab("Nexus/Examples/UnitView")]
    public class UnitView : MonoBehaviour, INexusOwned<UnitService>, INexusLifecycle
    {
        public int OwnerId { get; set; }
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()    => UniTask.CompletedTask;
        public void SetHp(int hp) { /* 更新血條 */ }
    }

    // 5e) Logic = sibling，邊界內直連 Data/View。組裝期 ResolveSibling 建齊、熱路徑 GetSibling 重取（禁快取）。
    //     locator 不吃 creation 參數 → 初值在此自取（讀 SO、寫節點）。
    public class UnitLogic : INexusOwned<UnitService>, INexusLifecycle
    {
        public int OwnerId { get; set; }

        public async UniTask OnInitialize(CancellationToken ct)
        {
            var cfg = await NEXUS.ResolveGlobal<UnitConfigSO>();      // 唯讀共用設定（copy:false）
            (await this.ResolveSibling<UnitState>()).Hp = cfg.MaxHp;  // 建 Data + 抄初值（讀 SO、寫 C# 節點）
            await this.ResolveSibling<UnitView>();                   // 建 View，組裝齊全 → 之後熱路徑用 GetSibling
        }
        public UniTask OnRelease() => UniTask.CompletedTask;

        public UniTask TakeDamage(int dmg)
        {
            var state = this.GetSibling<UnitState>();   // sync 重取（禁快取）；不建立
            var view  = this.GetSibling<UnitView>();
            if (state == null || view == null) return UniTask.CompletedTask;   // 未建 / 已釋放 → 跳過
            state.Hp -= dmg;
            view.SetHp(state.Hp);                       // Data → View（影子）
            return UniTask.CompletedTask;
        }
    }

    // 6) 同型別異路徑：要「同 service 型別、依 identityKey 載不同資產」→ 掛 AddressResolver（細節見 SKILL §4c）。
    //    🔴 元件不變式：所有變體 prefab 必掛同一 component（此處 SkinView），能換素材不能換 class。
    [NexusPrefab("Nexus/Examples/Skin/Default")]   // 預設變體：resolver miss / 無 identityKey 時用
    public class SkinView : MonoBehaviour, INexusLifecycle
    {
        public UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask OnRelease()    => UniTask.CompletedTask;
    }

    // 7) 帶建立參數（create 專用）：型別實作 INexusLifecycle<TParam>（OnInitialize 直接吃 param）→ 只能經 NEXUS.Resolve* 帶 TParam 多載誕生。
    //    無參 Resolve<TurretService> 會「編譯失敗」（TurretService 非 INexusLifecycle）→ 杜絕「該帶參的被 lazy 產生」。
    public readonly struct TurretSpawn
    {
        public readonly int Level;
        public readonly Vector3 Pos;
        public TurretSpawn(int level, Vector3 pos) { Level = level; Pos = pos; }
    }

    public class TurretService : INexusContainer, INexusLifecycle<TurretSpawn>
    {
        public int NexusID { get; set; }
        public HashSet<int> LocalChildren { get; set; }
        // 參數直接進 OnInitialize（無 SetInitParam 兩步）；Nexus 由帶 TParam 多載帶入。
        public UniTask OnInitialize(TurretSpawn spawn, CancellationToken ct) { Debug.Log($"[Turret] lv{spawn.Level} @ {spawn.Pos}"); return UniTask.CompletedTask; }
        public UniTask OnRelease() => UniTask.CompletedTask;
    }

    public static class NexusExample
    {
        public static async UniTask Run()
        {
            // 全域：get-or-create + identityKey 分多份 + 收養既有實例
            var save = await NEXUS.ResolveGlobal<SaveService>();
            var same = await NEXUS.ResolveGlobal<SaveService>();          // 同一份（save == same）
            var bgm  = await NEXUS.ResolveGlobal<AudioService>("bgm");    // identityKey → 另一份（同 prefab 兩實例）
            await NEXUS.RegisterGlobal(new SaveService(), identityKey: "ext");   // 收養 + OnInitialize
            save.Set("hp", 100);

            // 標準 B 組裝：Container + Local + Sibling
            var u1    = await NEXUS.ResolveGlobal<UnitService>("u1");
            var logic = await NEXUS.ResolveLocal<UnitLogic>(u1);          // OnInitialize 取 config + ResolveSibling 建 UnitState/UnitView
            await logic.TakeDamage(5);                            // 邊界內直連：GetSibling 重取 Data/View → 改 Data → 同步 View

            // 抽換 View：Release 舊 + Resolve 新（Transfer 是搬家非抽換；換不同實作用 ResolveLocal<IView, ImplB>）
            await NEXUS.ReleaseLocal<UnitView>(u1);
            await NEXUS.ResolveLocal<UnitView>(u1);
            await logic.TakeDamage(3);                            // Logic GetSibling 重取 → 自動打到新 View

            // Transfer：reparent 同一實例到別 owner（同 id、不重建、不跑生命週期）
            var u2 = await NEXUS.ResolveGlobal<UnitService>("u2");
            NEXUS.TransferLocal<UnitState>(u1, u2);              // UnitState u1 → u2
            Debug.Assert(NEXUS.TryGetLocal<UnitState>(u2, out _) && !NEXUS.TryGetLocal<UnitState>(u1, out _));

            // 池化重用
            var e = await NEXUS.ResolveGlobal<EnemyView>("e");
            await NEXUS.ReleaseGlobal<EnemyView>("e");           // OnDespawn 停用 → 回池（GO 留）
            var reused = await NEXUS.ResolveGlobal<EnemyView>("e");      // 取回同一實例與 GO

            // copy:true SO-service
            var rule = await NEXUS.ResolveGlobal<RunRuleService>();
            rule.RollCrit(10);

            // 帶參建立（create 專用）：帶 TParam 多載傳參 → 直接進 OnInitialize(param, ct)。
            await NEXUS.ResolveGlobal<TurretService, TurretSpawn>(new TurretSpawn(3, Vector3.zero), "t1");
            // await NEXUS.ResolveGlobal<TurretService>("t1");          // ← 編譯失敗：TurretService 非 INexusLifecycle（必帶參）
            // await NEXUS.ResolveGlobal<TurretService, int>(5, "t2"); // ← 編譯失敗：int 非 TurretService 宣告的 TParam
            await NEXUS.ReleaseGlobal<TurretService>("t1");             // 換參＝換實例：先 Release 再 Create，或用不同 identityKey

            // 同型別異路徑：此處內嵌示範 hook（接後綴）；專案實務走查表，見 GameEntry + AssetRegistry.ResolveNexusAddress。
            // resolver 第二參數是 assetKey（資產軸），非 identityKey——資產路由與身分分通道。
            Nexus.Instance.AddressResolver = (type, assetKey, baseSpec) =>
                string.IsNullOrEmpty(assetKey) || baseSpec is not { } b || string.IsNullOrEmpty(b.Address)
                    ? (NexusAddress?)null                                        // 無 assetKey/base → 用 base（預設變體）
                    : new NexusAddress($"{b.Address}:{assetKey}", b.Copy);         // 示範：SkinView "red" → "Nexus/Examples/Skin/Default:red"
            var red  = await NEXUS.ResolveGlobal<SkinView>(assetKey: "red");   // assetKey 路由變體位址（身分 identityKey 留空）
            var def  = await NEXUS.ResolveGlobal<SkinView>();        // 無 assetKey → 用 base [NexusPrefab] "Skin/Default"
            Nexus.Instance.AddressResolver = null;                   // 範例收尾自清（正式碼由 ClearAll 自動清）

            // 連鎖釋放 + 一鍵清空
            await NEXUS.ReleaseGlobal<UnitService>("u1");        // u1 + 其下 sibling 一起放
            await NEXUS.ReleaseGlobal<UnitService>("u2");
            await NEXUS.ClearAll();
        }
    }
    #endregion
}
#endif
