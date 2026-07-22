using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace PinPlugin.Nexus
{
    /// <summary>
    /// 帶作用域的 async service locator + 可選物件池。純 C# 物件（非 MonoBehaviour）、Unity 主執行緒專用、不上鎖。
    ///
    /// <para>作用域：<c>Global&lt;T&gt;(identityKey)</c> = 全 app 每個 (T, identityKey) 一個實例；
    /// <c>Local&lt;T&gt;(owner, identityKey)</c> = 綁某 INexusContainer，owner 釋放時連鎖釋放其子服務。</para>
    ///
    /// <para>async 安全：同 identityKey 在建立完成前重入會共用同一個 in-flight task，不重複實例化；
    /// 建立中途被 Release / Register / ClearAll 取消，半成品會被清乾淨，不殘留進 _instances。</para>
    ///
    /// <para>測試替換（mock）：<see cref="Instance"/> 解析自一個 context stack——正式碼用全域 <c>_default</c>，
    /// 測試 / 子流程用 <see cref="CreateScope"/>（<c>await using</c>）推入隔離的 Nexus，離開區塊自動拆除還原。
    /// 因所有公開 API 都走 <see cref="Instance"/>，被測系統不需改任何一行就會打到推入的 mock context。</para>
    ///
    /// <para>分檔（皆 partial，在 Engine/）：Nexus.cs(單例/scope/欄位/事件/Diagnostics)、Nexus.Keys.cs(Key/Id/Query)、
    /// Nexus.GetOrCreate.cs(建立/循環偵測)、Nexus.Register.cs(收養)、Nexus.Release.cs(釋放/ClearAll)、
    /// Nexus.Pool.cs(物件池)、Nexus.Dependency.cs(依賴圖)。</para>
    /// </summary>
    public partial class Nexus
    {
        private static Nexus _default = new();
        private static readonly Stack<Nexus> _stack = new();

        /// <summary>目前作用中的 Nexus：stack 頂端（測試 scope）優先，否則全域 default。</summary>
        public static Nexus Instance => _stack.Count > 0 ? _stack.Peek() : _default;

        /// <summary>推入一個隔離 context。需與 <see cref="Pop"/> 成對；對外請走 <see cref="CreateScope"/> 的 await using。</summary>
        internal static void Push(Nexus ctx = null)
        {
            ThrowIfMisuse();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // scope 用全域靜態 stack，無法跨 async 流程隔離（UniTask 不流 ExecutionContext）。
            // 故 scope 僅對「循序」使用安全。這裡抓最常見的誤用：還有 in-flight 建立就 Push，代表流程交錯了。
            if (Instance.PendingCount > 0)
                UnityEngine.Debug.LogWarning(
                    "[Nexus] 仍有 in-flight 建立時 Push 新 scope：scope 非 async-flow 隔離，並發/交錯流程共用會污染 Instance。" +
                    "scope 僅供循序使用，勿在並發 gameplay 流程用。");
#endif
            _stack.Push(ctx ?? new Nexus());
        }

        /// <summary>RAII scope：<c>await using var s = Nexus.CreateScope();</c> 離開區塊（含例外）自動 Pop。</summary>
        public static NexusScope CreateScope(Nexus ctx = null)
        {
            Push(ctx);
            return new NexusScope();
        }

        /// <summary>彈出最上層 scope 並 ClearAll 拆除其資源。空 stack 會丟例外（避免誤清全域 default）。</summary>
        internal static async UniTask<Nexus> Pop()
        {
            ThrowIfMisuse();
            if (_stack.Count == 0)
                throw new InvalidOperationException(
                    "Nexus.Pop 沒有對應的 Push（stack 為空）。Push/Pop 必須成對，且不可用 Pop 去清全域 default。");
            var ctx = _stack.Pop();
            await ctx.ClearAll();
            UnityEngine.Debug.Assert(ctx.ActiveCount == 0, "Pop 後仍有殘留實例，請確認有正確 await");
            return ctx;
        }

        private static int _mainThreadId = -1;   // 主執行緒守衛基準（-1 = 尚未捕捉，guard 不作動）

        // 進 PlayMode 時重置靜態，相容「Enter Play Mode without Domain Reload」。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;   // 此 hook 跑在主緒
            _stack.Clear();
            _default = new Nexus();
        }

#if UNITY_EDITOR
        // 退出 PlayMode 也重置：ResetStatics 是 RuntimeInitializeOnLoad（只進場觸發），不在退場跑，
        // 故 _default 會把 play 期間的服務圖殘留進 Edit Mode（若 teardown 沒走完 ClearAll，場景 Mono 被
        // Unity 直接 Destroy 不發 Released）→ Nexus Service Tree 窗「結束 PlayMode 沒清空」。Edit Mode 不存在
        // runtime 服務，直接丟棄整張圖（純 C# 服務交 GC）即正確、且不依賴遊戲端 teardown。
        // 但 prefab-mono 的 GO 經 Addressables.InstantiateAsync 生 → Addressables 自管生命週期，未 ReleaseInstance
        // 時「不保證」隨場景卸載銷毀（尤其 teardown 沒走完 ClearAll：如服務在無限迴圈中、停 Play 時仍存活）→
        // 孤兒殘留 Hierarchy。故丟棄圖前先逐一 ReleaseInstance（已銷毀者 mb==null → no-op，存活孤兒 → 卸載）。
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookEditorPlayModeReset()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                {
                    _default.EditorReleasePrefabInstances();
                    _stack.Clear();
                    _default = new Nexus();
                }
            };
        }

        // 退場兜底：清掉 play 期間經 Addressables 生、未 ReleaseInstance 的 prefab GO，避免殘留 Hierarchy。
        // 退場時 Addressables 已 teardown → 對殘留 GO 呼 ReleaseInstance 回 false（不認得）不銷毀 → 退而 DestroyImmediate。
        private void EditorReleasePrefabInstances()
        {
            foreach (var obj in _prefabOwned)
            {
                if (obj is MonoBehaviour mb && mb != null)
                {
                    var go = mb.gameObject;
                    if (!UnityEngine.AddressableAssets.Addressables.ReleaseInstance(go))
                        UnityEngine.Object.DestroyImmediate(go);   // Addressables 不認得 → 直接毀，免孤兒殘留
                }
            }
            _prefabOwned.Clear();

            // copy:true SO 副本不歸 Addressables 管 → DestroyImmediate。
            foreach (var obj in _scriptableOwned)
                if (obj is UnityEngine.Object uo && uo != null) UnityEngine.Object.DestroyImmediate(uo);
            _scriptableOwned.Clear();

            // copy:false 唯讀共用磁碟資產 → Addressables.Release，切勿 Destroy。
            foreach (var obj in _scriptableShared)
                if (obj is UnityEngine.Object uo && uo != null) UnityEngine.AddressableAssets.Addressables.Release(uo);
            _scriptableShared.Clear();
        }
#endif

        // === 實例狀態（所有 partial 共用這份欄位）===
        private int _nextId = 0;
        private readonly Dictionary<Key, int> _keyToId = new();
        private readonly Dictionary<int, Key> _idToKey = new();                         // id→key 反查，供 prune
        private readonly Dictionary<int, string> _idToAssetKey = new();                  // id→資產鍵（純資產軸旁路；未指定不記＝AssetKeyOf 回空字串 ""，與 identityKey 完全獨立、不互換）
        private readonly Dictionary<int, object> _instances = new();                    // 活著的實例
        private readonly Dictionary<int, UniTask<object>> _pending = new();             // 建立中（去重）
        private readonly Dictionary<int, CancellationTokenSource> _pendingCts = new();  // 建立中的取消來源
        private readonly HashSet<int> _registering = new();                            // 偵測同 key 並發 Register
        private readonly HashSet<int> _releasing = new();                              // Release 重入保護
        private readonly HashSet<int> _syncInit = new();                               // 正在跑 OnInitialize 同步段的 id（環偵測）
        private readonly List<int> _initChain = new();                                 // 同步初始化呼叫鏈（組環的鏈條訊息）
        private readonly Dictionary<Type, Stack<object>> _pools = new();                // 物件池（per runtime type）
        private readonly HashSet<object> _prefabOwned = new();                          // prefab-mono service（GO 由 InstantiateAsync 生）；teardown 走 ReleaseInstance
        private readonly HashSet<object> _scriptableOwned = new();                       // copy:true SO 副本（Instantiate 複製）；teardown 走 Object.Destroy
        private readonly HashSet<object> _scriptableShared = new();                       // copy:false 唯讀 SO（共用磁碟資產）；teardown 走 Addressables.Release　// teardown why 見 Nexus.Pool.cs
        private readonly Dictionary<int, HashSet<int>> _deps = new();                   // 依賴邊 from→{to}（best-effort）
        private bool _clearing;    // ClearAll 拆除中：拒收新建立 / 註冊
        private int _releaseSync;  // >0 = 正在某 OnRelease/OnDestroy 的同步段內：拒收新建立 / 註冊

        /// <summary>
        /// 位址覆寫鉤子，解「同型別異路徑」：給定 <c>(型別, assetKey, attribute 的 base 位址規格)</c> 回變體
        /// <see cref="NexusAddress"/>，回 null = 用 base。預設 null（全走 attribute）。用法與不變式見 nexus-usage §4c。
        /// <para>第二參數是 <b>assetKey（資產軸）</b>，非 identityKey——資產路由與身分分通道（未分離時 AssetKeyOf 回退 identityKey）。</para>
        /// <para>baseSpec 由 Nexus 餵入（已獨佔 attribute 讀取與 prefab/SO 分支），resolver 只變形，不自行反射。</para>
        /// </summary>
        public Func<Type, string, NexusAddress?, NexusAddress?> AddressResolver;

        /// <summary>
        /// 生命週期回呼丟例外時觸發（接 telemetry）。Nexus 一律先 LogError 再呼此事件。
        /// Initialize（prefab InstantiateAsync/OnSpawn/OnInitialize）= 硬失敗：半成品清乾淨、例外往上拋（取消不算錯、不觸發本事件）。
        /// Release/Destroy/Return = 軟失敗：吞掉、記錄、續行（釋放不可被單一壞回呼中斷，否則 sibling 漏放）。
        /// </summary>
        public event Action<NexusError> OnError;

        /// <summary>結構化生命週期事件（建立 / 釋放 / 回池），給測試斷言或 telemetry。無訂閱者零成本。</summary>
        public event Action<NexusLifecyclePhase, Type> OnLifecycle;

        public int PoolLimit = 5;

        /// <summary>OnInitialize 逾時上限（安全網，抓 post-yield 環與任何 hang）。設 null 全域關閉。</summary>
        public TimeSpan? InitTimeout = TimeSpan.FromSeconds(30);

        // === Diagnostics ===
        /// <summary>目前在管的實例數（含 local children）。</summary>
        public int ActiveCount => _instances.Count;

        /// <summary>建立中（尚未完成）的數量。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>唯讀快照：目前所有活著的 (id → instance)，回傳 copy。</summary>
        public IReadOnlyDictionary<int, object> ActiveInstances => new Dictionary<int, object>(_instances);

        // 要把 NEXUS_LOG 加進 Scripting Define Symbols 才會編譯這些呼叫，正式 build 零開銷。
        [Conditional("NEXUS_LOG")]
        private static void Log(string msg) => UnityEngine.Debug.Log($"[Nexus] {msg}");

        // 統一錯誤出口：永遠先 LogError，再觸發 OnError。訂閱者自己丟例外也吞掉。
        private void RaiseError(NexusErrorPhase phase, object instance, Exception e)
        {
            var type = instance?.GetType();
            UnityEngine.Debug.LogError($"[Nexus] {phase}（{type?.Name}）丟出例外：{e}");
            if (OnError != null)
            {
                try { OnError(new NexusError(phase, type, e)); }
                catch (Exception cb) { UnityEngine.Debug.LogError($"[Nexus] OnError 訂閱者自己丟例外：{cb}"); }
            }
        }

        private void RaiseLifecycle(NexusLifecyclePhase phase, object instance)
        {
            if (OnLifecycle == null) return;
            try { OnLifecycle(phase, instance?.GetType()); }
            catch (Exception e) { UnityEngine.Debug.LogError($"[Nexus] OnLifecycle 訂閱者自己丟例外：{e}"); }
        }

        // 主執行緒守衛。Nexus 碰 GameObject + 裸集合 + 環偵測假設循序，全不上鎖。
        // 只在 Editor / Development build 編譯，從背景緒呼叫即丟，當場抓到誤用而非默默 race。
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private static void ThrowIfMisuse()
        {
            if (_mainThreadId != -1 && Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                throw new InvalidOperationException(
                    "Nexus 為主執行緒專用：偵測到背景緒呼叫。請先 `await UniTask.SwitchToMainThread()` 再呼叫。");
        }
    }
}
