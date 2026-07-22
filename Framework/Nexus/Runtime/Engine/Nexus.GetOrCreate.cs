using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PinPlugin.Nexus
{
    // 取得 / 建立服務的主路徑。負責：async 去重、半成品清理、循環依賴偵測、把 child 掛到 owner。
    public partial class Nexus
    {
        /// <summary>取或建全域服務（型別須為 <see cref="INexusLifecycle"/>＝無參；帶參型別請用帶 TParam 的多載 <c>ResolveGlobal</c>）。<paramref name="identityKey"/>=身分鍵；<paramref name="assetKey"/>=資產鍵（可選）。詳 nexus-usage §3。</summary>
        public UniTask<T> ResolveGlobal<T>(string identityKey = null, string assetKey = null) where T : class, INexusLifecycle, new()
        {
            var id = GetId(GlobalKey<T>(identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;   // 空/null 不記：未指定 assetKey 即路由空鍵 ""（identityKey 永不參與資產路由，兩軸不互換）
            return GetOrCreate<T, T>(id, null);
        }

        /// <summary>介面綁定：以 <typeparamref name="TInterface"/> 當 key，建立時 <c>new TImpl()</c>（具體型別須為 <see cref="INexusLifecycle"/>），呼叫端只看到 interface。<paramref name="assetKey"/> 分離資產軸（見 nexus-usage §3）。</summary>
        public UniTask<TInterface> ResolveGlobal<TInterface, TImpl>(string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle, new()
        {
            var id = GetId(GlobalKey<TInterface>(identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;   // 空/null 不記：未指定 assetKey 即路由空鍵 ""（identityKey 永不參與資產路由，兩軸不互換）
            return GetOrCreate<TInterface, TImpl>(id, null);
        }

        /// <summary>
        /// 取或建 local 服務。<paramref name="identityKey"/>=身分鍵；<paramref name="assetKey"/>=資產鍵（可選）。
        /// identityKey 純身分（如序號）、assetKey 純資產路由，兩軸完全獨立不互換；未傳 assetKey 即路由空鍵 ""（不借 identityKey）。詳 nexus-usage §3。
        /// </summary>
        public UniTask<T> ResolveLocal<T>(INexusContainer owner, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle, new()
        {
            RequireManaged(owner);
            var id = GetId(LocalKey<T>(owner, identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;   // 空/null 不記：未指定 assetKey 即路由空鍵 ""（identityKey 永不參與資產路由，兩軸不互換）
            return GetOrCreate<T, T>(id, owner);
        }

        /// <summary>介面綁定（local 作用域；具體型別須為 <see cref="INexusLifecycle"/>）。<paramref name="assetKey"/> 分離資產軸（見 nexus-usage §3）。</summary>
        public UniTask<TInterface> ResolveLocal<TInterface, TImpl>(INexusContainer owner, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle, new()
        {
            RequireManaged(owner);
            var id = GetId(LocalKey<TInterface>(owner, identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;   // 空/null 不記：未指定 assetKey 即路由空鍵 ""（identityKey 永不參與資產路由，兩軸不互換）
            return GetOrCreate<TInterface, TImpl>(id, owner);
        }

        /// <summary>
        /// 建立全域服務並把參數 <paramref name="param"/> 餵進 <c>OnInitialize(param, ct)</c>（型別須為 <see cref="INexusLifecycle{TParam}"/>）。
        /// <b>create 專用</b>（非 get-or-create）：同 key 已存在 / 建立中即丟 <see cref="InvalidOperationException"/>，
        /// 杜絕「第二次帶參數被靜默忽略」。讀取請用 <c>Get*</c>，換實例請先 Release 或用不同 identityKey。詳 nexus-usage §3 傳參。
        /// </summary>
        public UniTask<T> ResolveGlobal<T, TParam>(TParam param, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle<TParam>, new()
        {
            var id = GetId(GlobalKey<T>(identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;
            return GetOrCreate<T, T>(id, null, ParamInit(param), createOnly: true);
        }

        /// <summary>建立 local 服務並把參數餵進 <c>OnInitialize(param, ct)</c>（型別須為 <see cref="INexusLifecycle{TParam}"/>）。create 專用語義同 <see cref="ResolveGlobal{T,TParam}"/>。</summary>
        public UniTask<T> ResolveLocal<T, TParam>(INexusContainer owner, TParam param, string identityKey = null, string assetKey = null) where T : class, INexusLifecycle<TParam>, new()
        {
            RequireManaged(owner);
            var id = GetId(LocalKey<T>(owner, identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;
            return GetOrCreate<T, T>(id, owner, ParamInit(param), createOnly: true);
        }

        /// <summary>介面綁定 + 傳參：以 <typeparamref name="TInterface"/> 當 key，建立時 <c>new TImpl()</c> 並把 <paramref name="param"/> 餵進 <c>OnInitialize(param, ct)</c>（具體型別須為 <see cref="INexusLifecycle{TParam}"/>）。create 專用語義同 <see cref="ResolveGlobal{T,TParam}"/>。</summary>
        public UniTask<TInterface> ResolveGlobal<TInterface, TImpl, TParam>(TParam param, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle<TParam>, new()
        {
            var id = GetId(GlobalKey<TInterface>(identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;
            return GetOrCreate<TInterface, TImpl>(id, null, ParamInit(param), createOnly: true);
        }

        /// <summary>介面綁定 + 傳參（local 作用域；具體型別須為 <see cref="INexusLifecycle{TParam}"/>）。create 專用語義同 <see cref="ResolveGlobal{T,TParam}"/>。</summary>
        public UniTask<TInterface> ResolveLocal<TInterface, TImpl, TParam>(INexusContainer owner, TParam param, string identityKey = null, string assetKey = null)
            where TInterface : class where TImpl : class, TInterface, INexusLifecycle<TParam>, new()
        {
            RequireManaged(owner);
            var id = GetId(LocalKey<TInterface>(owner, identityKey));
            if (!string.IsNullOrEmpty(assetKey)) _idToAssetKey[id] = assetKey;
            return GetOrCreate<TInterface, TImpl>(id, owner, ParamInit(param), createOnly: true);
        }

        // 把建立參數包成「帶參 OnInitialize 呼叫」的閉包。型別由帶 TParam 的 Resolve* 多載 INexusLifecycle<TParam> 約束保證吻合，故直接 cast。
        // 回傳 init UniTask，由 CreateAsync 在 _syncInit 同步段守衛下執行（與無參 OnInitialize 同一守衛，循環即時報錯）。
        private static Func<object, CancellationToken, UniTask> ParamInit<TParam>(TParam param) =>
            (inst, ct) => ((INexusLifecycle<TParam>)inst).OnInitialize(param, ct);

        // TKey = 對外型別（key / 回傳，可為 interface）；TImpl = 實際 new 出來的具體型別。單型別時兩者相同。
        // paramInit：帶參版的 init 閉包（null＝無參型別，改走 INexusLifecycle.OnInitialize(ct)）。
        // createOnly：帶 TParam 多載專用——已存在 / 建立中即丟例外（不 get-or-create）。其餘路徑維持 get-or-create。
        private async UniTask<TKey> GetOrCreate<TKey, TImpl>(int id, INexusContainer owner, Func<object, CancellationToken, UniTask> paramInit = null, bool createOnly = false)
            where TKey : class where TImpl : class, TKey, new()
        {
            ThrowIfMisuse();
            ThrowIfNotAcceptingNew();

            // create 專用守衛：必須在 LinkChild 之前丟。否則既有 live child 的 id 經 catch 的 UnlinkChild 被誤刪 owner 連結
            // → Release 漏放。在此早退即無連結副作用。循環（同步段內自我請求）優先報循環、語意更準。
            if (createOnly)
            {
                if (_syncInit.Contains(id))
                    throw new NexusCircularDependencyException(BuildChain(id));
                if (_instances.ContainsKey(id) || _pending.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"Nexus 帶參 Resolve*：#{id}({TypeName(id)}) 已存在 / 建立中，create 專用 API 不可重複建立。" +
                        "讀取請用 Get*，換實例請先 Release 或用不同 identityKey。");
            }

            // LinkChild 必須在任何 await 之前。否則 owner 在 child 建立途中被 Release 時，連鎖釋放找不到這個
            // 還沒掛上的 child → child 建完後 orphan。這行的位置是正確性關鍵，別往下移。
            LinkChild(owner, id);

            // 若此呼叫發生在別的服務 OnInitialize 同步段內，記一條「那個服務 → 本 id」的依賴邊（給依賴圖 / 釋放排序）。
            if (_initChain.Count > 0)
            {
                var from = _initChain[_initChain.Count - 1];
                if (from != id) AddDep(from, id);
            }
            try
            {
                // 已建好 → 直接回（即使在環上，已完成的實例可安全取得）。createOnly 已在前置守衛擋下，到此必非 createOnly。
                if (_instances.TryGetValue(id, out var existing))
                    return (TKey)existing;

                // 循環依賴：請求一個「正在跑 OnInitialize 同步段」的 id（A→A 或 A→B→A）→ 立即報錯帶完整鏈。
                if (_syncInit.Contains(id))
                    throw new NexusCircularDependencyException(BuildChain(id));

                // 建立中 → 共用同一個 in-flight task，不重複實例化（中途被取消則這裡丟 OperationCanceledException）。
                if (_pending.TryGetValue(id, out var inflight))
                    return (TKey)await inflight;

                // 自己建立。cts 讓 Release / Register / ClearAll 可中途取消這次建立。
                var cts = new CancellationTokenSource();
                _pendingCts[id] = cts;
                var task = CreateAsync<TImpl>(id, owner, cts, paramInit).Preserve();
                _pending[id] = task;
                try
                {
                    return (TKey)await task;
                }
                finally
                {
                    // 只清「自己這次的」pending：若途中被取消、已有新的建立接手登錄同一 id，別誤刪它的。
                    if (_pendingCts.TryGetValue(id, out var owned) && ReferenceEquals(owned, cts))
                    {
                        _pending.Remove(id);
                        _pendingCts.Remove(id);
                    }
                    cts.Dispose();
                }
            }
            catch
            {
                UnlinkChild(owner, id);   // 建立失敗 / 取消 → 別把死 child 留在 owner.LocalChildren
                throw;
            }
        }

        // 實際建立流程。任何一步丟例外或被取消，catch 會把半成品清乾淨，絕不留進 _instances。
        // paramInit：帶參版的 init 閉包（null＝無參型別，改走 INexusLifecycle.OnInitialize(ct)）。
        private async UniTask<object> CreateAsync<T>(int id, INexusContainer owner, CancellationTokenSource cts, Func<object, CancellationToken, UniTask> paramInit = null) where T : class, new()
        {
            var ct = cts.Token;
            // 餵 resolver 的是 assetKey（資產軸：決定載哪個資產）；身分 identityKey 不參與資產路由，兩通道完全獨立、不互換。
            // 未指定 assetKey → AssetKeyOf 回空字串 ""＝路由 (型別,"") 登記（不借 identityKey）。
            var assetKey = AssetKeyOf(id) ?? "";
            var (instance, fromPool) = PoolPop<T>();
            GameObject spawned = null;
            UnityEngine.Object scriptableCopy = null;     // copy:true 的執行期副本（失敗清理走 Destroy）
            UnityEngine.Object scriptableShared = null;   // copy:false 的共用磁碟資產（失敗清理走 Addressables.Release）
            try
            {
                // Unity Object service（PoolPop miss 回 null）：由 Addressables 產出實例。兩條對稱分支——
                //   SO  ：[NexusScriptable] 位址 LoadAsset → Instantiate 複製副本 → 立即 Release load handle。
                //   prefab：[NexusPrefab] 位址 InstantiateAsync → GetComponent。先有 GO 才有實例（與純 C# 相反）。
                if (instance == null)
                {
                    if (typeof(ScriptableObject).IsAssignableFrom(typeof(T)))
                    {
                        var spec = ResolveScriptable(typeof(T), assetKey);
                        var asset = await Addressables.LoadAssetAsync<ScriptableObject>(spec.Address).ToUniTask(cancellationToken: ct);
                        if (asset == null)
                            throw new InvalidOperationException(
                                $"Nexus scriptable '{spec.Address}' 載不到 {typeof(T).Name}（[NexusScriptable] / resolver 位址錯？）。");
                        if (spec.Copy)
                        {
                            var copy = UnityEngine.Object.Instantiate(asset);   // 執行期副本，與磁碟資產隔離
                            Addressables.Release(asset);                        // 副本獨立，立即放 load handle
                            instance = (T)(object)copy;
                            scriptableCopy = copy;
                            _scriptableOwned.Add(instance);   // 標記為 Nexus 擁有 → teardown 走 Object.Destroy
                        }
                        else
                        {
                            // copy:false 唯讀資料：不複製，直接共用磁碟資產；handle 持有至釋放（不立即 Release）。
                            instance = (T)(object)asset;
                            scriptableShared = asset;
                            _scriptableShared.Add(instance);   // teardown 走 Addressables.Release（非 Destroy，否則毀磁碟資產）
                        }
                    }
                    else
                    {
                        var addr = ResolvePrefabAddress(typeof(T), assetKey);
                        var go0 = await Addressables.InstantiateAsync(addr).ToUniTask(cancellationToken: ct);
                        instance = (T)(object)go0.GetComponent(typeof(T));
                        if (instance == null)
                        {
                            Addressables.ReleaseInstance(go0);
                            throw new InvalidOperationException(
                                $"Nexus prefab '{addr}' 上找不到 {typeof(T).Name} component（[NexusPrefab] 指到錯 prefab？）。");
                        }
                        spawned = go0;
                        _prefabOwned.Add(instance);   // 標記為 Nexus 擁有 → teardown 走 ReleaseInstance
                    }
                }
                else if (instance is MonoBehaviour mbHit)
                    spawned = mbHit.gameObject;        // pool hit 的 prefab-mono：記著 GO 供失敗清理
                else if (instance is ScriptableObject soHit)
                {
                    // pool hit 的 SO：依來源歸位，供失敗清理走正確 teardown（共用資產絕不可 Destroy）。
                    if (_scriptableShared.Contains(soHit)) scriptableShared = soHit;
                    else scriptableCopy = soHit;
                }

                if (instance is INexusID identified) identified.NexusID = id;
                // Local 子服務反查 owner：寫入 owner 的 NexusID（Global 時 owner==null → 不寫，OwnerId 維持 0）。
                if (owner != null && instance is INexusOwnedBase owned) owned.OwnerId = owner.NexusID;
                // 池化服務的每次「啟用」鉤子（新建或從池取回都會跑），與 PoolPush 的 OnDespawn 成對。
                if (instance is INexusPoolable poolable)
                    await poolable.OnSpawn();
                ct.ThrowIfCancellationRequested();

                // init 派發：帶參型別走 paramInit 閉包（INexusLifecycle<TParam>.OnInitialize(param,ct)，參數只在真正建立時消費，
                // 含池化重用再建；命中既有實例的 Resolve 不走 CreateAsync）；無參型別走 INexusLifecycle.OnInitialize(ct)。
                // 兩路徑型別正確性由帶 TParam / 無參的 Resolve* 多載約束在編譯期保證。lifecycle-less node（如唯讀資料 SO）兩者皆非 → 跳過 init。
                if (paramInit != null || instance is INexusLifecycle)
                {
                    // 把 id 標進 _syncInit 涵蓋 OnInitialize 的「同步段」：這段內任何成環請求即時報錯。
                    // OnInitialize 一回傳（同步段結束、進入真 await）就移除 → 之後合法的 join 不會被誤判成環。
                    _syncInit.Add(id);
                    _initChain.Add(id);
                    UniTask initTask;
                    try
                    {
                        initTask = paramInit != null
                            ? paramInit(instance, ct)
                            : ((INexusLifecycle)instance).OnInitialize(ct);
                    }
                    finally
                    {
                        _syncInit.Remove(id);
                        _initChain.RemoveAt(_initChain.Count - 1);
                    }
                    await WithTimeout(initTask, id, cts);
                }
                ct.ThrowIfCancellationRequested();

                _instances[id] = instance;   // 走到這裡才算「活著」。
                Log($"create #{id} = {typeof(T).Name}");
                RaiseLifecycle(NexusLifecyclePhase.Created, instance);
                return instance;
            }
            catch (Exception e)
            {
                // 半成品「丟棄」（不回池）：先連鎖釋放它在 init 內已建的 children，再毀掉它的 GameObject、歸零欄位。
                // 刻意不 PoolPush —— 壞掉 / 半啟用的實例（尤其 pool 取回又失敗者）不該回池污染下次重用。
                await ReleaseChildrenOf(instance);
                // prefab-mono 的 GO 由 InstantiateAsync 生 → ReleaseInstance（spawned 非 null ⟺ prefab-mono）。
                if (spawned != null)
                {
                    _prefabOwned.Remove(instance);
                    Addressables.ReleaseInstance(spawned);
                }
                if (scriptableCopy != null)
                {
                    _scriptableOwned.Remove(instance);
                    UnityEngine.Object.Destroy(scriptableCopy);
                }
                if (scriptableShared != null)
                {
                    _scriptableShared.Remove(instance);
                    Addressables.Release(scriptableShared);   // 共用磁碟資產：還 load handle，不 Destroy
                }
                if (instance is INexusID idn) idn.NexusID = 0;
                if (instance is INexusOwnedBase ow) ow.OwnerId = 0;

                // 使用者 OnSpawn/OnInitialize 丟例外 = 硬失敗：通報 OnError + prune key 後往上拋。
                // 取消(OCE) 與循環依賴是 typed 例外，不通報、不 prune：
                //   - 取消多半來自 Register 覆蓋（CancelPending→Release(prune:false) 要原地沿用同一 id），prune 會破壞重用。
                //   - 循環依賴往上逐層拋已足夠，再 log 會重複。
                if (e is not OperationCanceledException and not NexusCircularDependencyException)
                {
                    RaiseError(NexusErrorPhase.Initialize, instance, e);
                    PruneKey(id);
                }
                throw;
            }
        }

        // 逾時安全網：上面的同步段偵測只抓得到「同步成環」；先做真 async 才成環的、或單純卡死的 init，靠這裡逾時兜底。
        // 用 UniTask .Timeout（PlayerLoop 計時）。
        // Realtime 計時，timeScale=0 也會走。EditMode 無 PlayerLoop 故不觸發（測試一律把 InitTimeout 設 null）。
        private async UniTask WithTimeout(UniTask initTask, int id, CancellationTokenSource cts)
        {
            if (InitTimeout is not { } to) { await initTask; return; }
            try
            {
                await initTask.Timeout(to, DelayType.Realtime, taskCancellationTokenSource: cts);
            }
            // .Timeout 自己逾時前會先 Cancel(cts)，故「cts 已取消」才是真逾時。
            // 使用者 init 內部自拋的 TimeoutException（cts 未取消）不在此攔，原樣往上拋當硬失敗，避免誤報成循環依賴。
            catch (TimeoutException) when (cts.IsCancellationRequested)
            {
                throw new NexusCircularDependencyException(
                    $"Nexus 初始化逾時（>{to.TotalSeconds:0.#}s）：{TypeName(id)} 的 OnInitialize 未完成，疑似 post-yield 循環依賴或卡住。");
            }
        }

        // 從同步初始化鏈組出 "A → B → A" 字串（id 在 _initChain 首次出現處起算）。
        private string BuildChain(int repeatedId)
        {
            var start = _initChain.IndexOf(repeatedId);
            var names = new List<string>();
            for (var i = (start < 0 ? 0 : start); i < _initChain.Count; i++) names.Add(TypeName(_initChain[i]));
            names.Add(TypeName(repeatedId));
            return $"Nexus 偵測到循環依賴：{string.Join(" → ", names)}";
        }

        private string TypeName(int id) => _idToKey.TryGetValue(id, out var key) ? key.ComponentType.Name : $"#{id}";

        // 取 prefab-mono service 的 Addressable 位址。先讀 attribute 取 base → 餵 resolver（依 assetKey 同型別異路徑）→ resolver 命中用之、否則回退 base；皆無 = 硬失敗。
        private string ResolvePrefabAddress(Type t, string assetKey)
        {
            var attr = (NexusPrefabAttribute)Attribute.GetCustomAttribute(t, typeof(NexusPrefabAttribute));
            // prefab 的 copy 無意義（建立時忽略），填 true 只為湊 NexusAddress 把 base 位址餵進 resolver。
            NexusAddress? baseSpec = attr == null || string.IsNullOrEmpty(attr.Address) ? null : new NexusAddress(attr.Address, true);

            if (AddressResolver?.Invoke(t, assetKey, baseSpec) is { } ov && !string.IsNullOrEmpty(ov.Address))
                return ov.Address;
            if (baseSpec is { } b) return b.Address;
            throw new InvalidOperationException(
                $"{t.Name} 是 MonoBehaviour service，但 resolver 未覆寫 assetKey '{assetKey}' 且缺 [NexusPrefab(\"位址\")] 標註，Nexus 不知要 Instantiate 哪個 prefab。");
        }

        // 取 SO service 的位址規格（位址 + copy，同源避免 teardown 錯配）。解析序同 ResolvePrefabAddress。
        private NexusAddress ResolveScriptable(Type t, string assetKey)
        {
            var attr = (NexusScriptableAttribute)Attribute.GetCustomAttribute(t, typeof(NexusScriptableAttribute));
            // copy 一律由 attribute 決定（與 address 解耦）：address 留空走 resolver 時仍取得正確 copy
            var copy = attr?.Copy ?? true;
            NexusAddress? baseSpec = attr == null || string.IsNullOrEmpty(attr.Address) ? null : new NexusAddress(attr.Address, copy);

            // 位址走 resolver（依 assetKey 同型別異路徑），copy 仍用 attribute
            if (AddressResolver?.Invoke(t, assetKey, baseSpec) is { } ov && !string.IsNullOrEmpty(ov.Address))
                return new NexusAddress(ov.Address, copy);
            if (baseSpec is { } b) return b;
            throw new InvalidOperationException(
                $"{t.Name} 是 ScriptableObject service，但 resolver 未覆寫 assetKey '{assetKey}' 且缺 [NexusScriptable(\"位址\")] 標註，Nexus 不知要 LoadAsset 哪個資產。");
        }

        private static void LinkChild(INexusContainer owner, int childId)
        {
            if (owner == null) return;
            owner.LocalChildren ??= new HashSet<int>();
            owner.LocalChildren.Add(childId);
        }

        private static void UnlinkChild(INexusContainer owner, int childId)
        {
            if (owner?.LocalChildren != null) owner.LocalChildren.Remove(childId);
        }

        // 連鎖釋放某 container 的所有 Local children。snapshot→clear→release，避免釋放途中集合被改。
        // 正常 Release 與 init 失敗清理都走這支。
        private async UniTask ReleaseChildrenOf(object instance)
        {
            if (instance is INexusContainer cont && cont.LocalChildren is { Count: > 0 })
            {
                var children = cont.LocalChildren.ToArray();
                cont.LocalChildren.Clear();
                foreach (var childId in children) await Release(childId);
            }
        }
    }
}
