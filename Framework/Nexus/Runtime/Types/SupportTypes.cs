using System;

namespace PinPlugin.Nexus
{
    /// <summary>
    /// 偵測到循環依賴（A→B→A 或自我相依），或初始化逾時（疑似環 / 卡住）時丟出。訊息含完整依賴鏈條。
    /// </summary>
    public sealed class NexusCircularDependencyException : Exception
    {
        public NexusCircularDependencyException(string message) : base(message) { }
    }

    /// <summary>
    /// <c>Nexus.AddressResolver</c> 回傳的位址規格：Addressable 位址 + copy 旗標。
    /// <para>
    /// 解「同型別需依 identityKey 載不同資產」：resolver 以 <c>(型別, identityKey)</c> 查表回傳此規格，Nexus 用它取代
    /// type-only 的 <see cref="NexusPrefabAttribute"/> / <see cref="NexusScriptableAttribute"/> 位址。
    /// <c>Copy</c> 僅 SO 有意義（prefab 忽略），且 SO 分支由 Nexus <b>一律改用 <see cref="NexusScriptableAttribute.Copy"/></b>
    /// （resolver 只負責位址、本欄 Copy 被忽略）——copy 單一來源在 attribute，免兩處不一致造成 teardown 分支錯配。
    /// </para>
    /// </summary>
    public readonly struct NexusAddress
    {
        public readonly string Address;
        public readonly bool Copy;
        public NexusAddress(string address, bool copy = true) { Address = address; Copy = copy; }
    }

    /// <summary>生命週期回呼出錯的階段，供 <c>Nexus.OnError</c> 區分。</summary>
    public enum NexusErrorPhase { Initialize, Release, Destroy, Return }

    /// <summary>生命週期事件階段，供 <c>Nexus.OnLifecycle</c>（測試斷言 / telemetry）。Transferred＝owner reparent（同實例、不重建）。</summary>
    public enum NexusLifecyclePhase { Created, Released, PoolReturned, Transferred }

    /// <summary>依賴圖的一條有向邊。<c>IsOwnership</c>=true 表 owner→local child；false 表 OnInitialize 內解析的服務依賴。</summary>
    public readonly struct NexusEdge
    {
        public readonly Type From;
        public readonly Type To;
        public readonly bool IsOwnership;
        public NexusEdge(Type from, Type to, bool isOwnership) { From = from; To = to; IsOwnership = isOwnership; }
        public override string ToString() => $"{From?.Name} -> {To?.Name}{(IsOwnership ? " [owns]" : "")}";
    }

    /// <summary>
    /// 單一服務節點的診斷快照（id 層級，不壓扁同型多 owner 的 Local 實例）。供 Runtime 視覺化視窗組 owner→child 樹用。
    /// 樹關係靠 <see cref="ParentId"/>：0 = Global（根）；否則 = 其 owner container 的 id。
    /// best-effort：反映呼叫當下的內部狀態快照（回 copy），不持有實例參考。
    /// </summary>
    public readonly struct NexusNode
    {
        /// <summary>實例 id（= owner 的 <see cref="INexusID.NexusID"/> / child 的 <see cref="INexusOwnedBase.OwnerId"/> 來源）。</summary>
        public readonly int Id;
        public readonly string TypeName;
        public readonly string IdentityKey;
        /// <summary>0 = Global；否則為 owner container 的 id（用此組樹）。</summary>
        public readonly int ParentId;
        public readonly bool IsGlobal;
        /// <summary>在 _pending、尚未進 _instances（建立中）。</summary>
        public readonly bool IsPending;
        public readonly bool IsContainer;
        /// <summary>由 Nexus 經 Addressables 生 GO 的 prefab-mono service（帶 GameObject）。</summary>
        public readonly bool IsPrefabMono;
        /// <summary>由 Nexus 經 Addressables LoadAsset + Instantiate 複製出副本的 SO service。</summary>
        public readonly bool IsScriptable;
        public readonly bool IsPoolable;

        public NexusNode(int id, string typeName, string identityKey, int parentId,
            bool isPending, bool isContainer, bool isPrefabMono, bool isScriptable, bool isPoolable)
        {
            Id = id; TypeName = typeName; IdentityKey = identityKey; ParentId = parentId;
            IsGlobal = parentId == 0;
            IsPending = isPending; IsContainer = isContainer; IsPrefabMono = isPrefabMono; IsScriptable = isScriptable; IsPoolable = isPoolable;
        }

        public override string ToString()
        {
            var sub = string.IsNullOrEmpty(IdentityKey) ? "" : $"('{IdentityKey}')";
            return $"{TypeName}{sub} #{Id}{(IsGlobal ? " [G]" : " [L]")}{(IsPending ? " [pending]" : "")}";
        }
    }

    /// <summary>
    /// Nexus 生命週期回呼丟例外時的事件資料。透過 <c>Nexus.OnError</c> 訂閱，讓上層可接 telemetry / 自訂處理。
    /// 錯誤策略見 <c>Nexus.OnError</c> 文件：Initialize = 硬失敗（建立中止 + 例外往上拋）；Release/Destroy/Return = 軟失敗（吞掉續行）。
    /// </summary>
    public readonly struct NexusError
    {
        public readonly NexusErrorPhase Phase;
        public readonly Type ServiceType;
        public readonly Exception Exception;
        public NexusError(NexusErrorPhase phase, Type serviceType, Exception exception)
        {
            Phase = phase; ServiceType = serviceType; Exception = exception;
        }
    }

    /// <summary>
    /// 身分鍵：(型別, 子鍵, 父Id)。ParentId=0 → 全域；否則為某 container 之下的 local 實例。
    /// </summary>
    internal readonly struct Key : IEquatable<Key>
    {
        public readonly Type ComponentType;
        public readonly string IdentityKey;   // 同型多實例用的子鍵（可為 ""）
        public readonly int ParentId;    // 0 = global

        private readonly int _hash;

        public Key(Type type, string identityKey, int parentId = 0)
        {
            ComponentType = type ?? throw new ArgumentNullException(nameof(type));
            IdentityKey = identityKey ?? "";
            ParentId = parentId;
            _hash = HashCode.Combine(type, IdentityKey, parentId);
        }

        public override int GetHashCode() => _hash;

        public override bool Equals(object obj) => obj is Key other && Equals(other);

        public bool Equals(Key other) =>
            ParentId == other.ParentId &&
            ReferenceEquals(ComponentType, other.ComponentType) &&
            string.Equals(IdentityKey, other.IdentityKey, StringComparison.Ordinal);

        public override string ToString() =>
            ParentId == 0
                ? $"Global<{ComponentType.Name}>('{IdentityKey}')"
                : $"Local<{ComponentType.Name}>[#{ParentId}]('{IdentityKey}')";
    }
}
