namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>左欄清單用的一筆Token視圖。資料住在 <see cref="GraphToken"/>，這裡只是查詢結果。</summary>
public class HGToken
{
    public GraphToken Token;

    public string Key => Token?.Name;
    public Type ResultType => Token?.ResultType;

    /// <summary>族身份（＝Slot 型別）。撞名判定與候選過濾都用它。</summary>
    public Type FamilyType => Token?.FamilyType;

    // 走端點自己的 Slot：同結果型別的不同族（String / Key）在清單裡才分得出來。
    public string TypeName => HGReflect.SlotKindName(Token?.Slot);
}

/// <summary>畫布上的一個 root：識別值與它底下的項目清單。</summary>
// RootKey 宣告成 object 而不是 Enum：編輯器只做相等比較與 ToString()，
// 「識別值是什麼型別」由 IGraphDocument 的實作決定（LogicGraph 給的是時機 enum）。
public class HGRootGroupView
{
    public object Root;
    public object RootKey;
    public IList Items;
    /// <summary>Stable Tool-defined identity for keys other than strings, enums and scalar values.</summary>
    public string StableId;
}

/// <summary>一步 Undo／Redo 實際換掉了什麼。呼叫端靠它決定要不要重建畫布與焦點。</summary>
public enum HGStepKind
{
    /// <summary>沒得退／沒得進，什麼都沒換。</summary>
    None,

    /// <summary>圖的工作副本整份被換掉。</summary>
    Graph,

    /// <summary>只換了 Owner 上的目錄，圖沒動。</summary>
    Catalogs,
}

/// <summary>
/// 視覺化編輯器的資料模型：綁定 Owner SO，持有一份 LogicGraph 工作副本，所有編輯都改副本，存檔才寫回。
/// </summary>
// 「取消要能捨棄自上次存檔以來的所有修改」→ 只有工作副本能乾淨做到，順便讓 Undo 可以用整份快照實作。
public class HGModel
{
    // Owner 不限 ScriptableObject：Hierarchy 上掛 LogicGraph 的 MonoBehaviour 也能編。
    public UnityEngine.Object Owner { get; private set; }
    public IGraphDocument Data { get; private set; } // 圖的工作副本
    public Type PackType { get; private set; }
    public string DocumentId => documentBinding?.DocumentId;

    /// <summary>工作副本的圖契約。所有 root／時機操作都經過它，編輯器不認識具體圖型別。</summary>
    public IGraphDocument Doc => Data;

    /// <summary>可建立或跳轉的 root 識別值。過濾由 session root adapter 決定。</summary>
    // 快取在這一層而不是選單那一層：兩個選單入口共用同一份，不會有一邊漏過濾。
    public IReadOnlyList<object> AvailableRootKeys { get; private set; }
    public bool Dirty { get; private set; }
    public bool TrackChanges { get; set; } = true;
    public GraphDiagnostic LastCommitDiagnostic { get; internal set; }

    private FieldInfo systemField;                    // Owner 上放 LogicGraph 的欄位
    private HGDocumentBinding documentBinding;
    private HGDocumentCommitGuard commitGuard;
    private IHGRootAdapter rootAdapter = HGDocumentRootAdapter.Instance;
    private readonly Dictionary<ScriptableObject, List<AssetParameterDefinition>> assetParameterCache = new();

    // 建不出參數列的那些參數只吼一次：EnsureAssetBindings 每次重建圖都會跑，不去重會洗版。
    private readonly HashSet<string> loggedUnbindableParameters = new();
    private readonly Dictionary<object, string> transientRootIds = new(HGRefComparer.Instance);

    // ===== 綁定 =====

    /// <summary>在任意 SO 上找出唯一可編輯的圖欄位（<see cref="IGraphDocument"/>）；零個或多個都回 null。</summary>
    public static FieldInfo FindSystemField(UnityEngine.Object owner)
        => owner == null ? null : FindSystemField(owner.GetType());

    /// <summary>只看型別就能判斷是否有唯一 legacy 圖欄位，掃描專案時不必先載入資產。</summary>
    public static FieldInfo FindSystemField(Type ownerType)
        => FindSystemField(ownerType, out _);

    private static FieldInfo FindSystemField(Type ownerType, out int candidateCount)
    {
        candidateCount = 0;
        if (ownerType == null) return null;
        FieldInfo candidate = null;
        foreach (var f in HGReflect.Fields(ownerType))
        {
            if (!typeof(IGraphDocument).IsAssignableFrom(f.FieldType)) continue;
            candidateCount++;
            if (candidateCount > 1) return null;
            candidate = f;
        }
        return candidate;
    }

    public static bool CanEdit(UnityEngine.Object owner) => FindSystemField(owner) != null;

    /// <summary>綁定 Owner 並複製一份工作副本。失敗回 false 並記 Log。</summary>
    public bool Bind(UnityEngine.Object owner)
        => Bind(owner, null, null);

    /// <summary>以指定的文件 binding 綁定 Owner；未指定時才使用 legacy 欄位探索。</summary>
    public bool Bind(UnityEngine.Object owner, HGDocumentBinding binding, IHGRootAdapter rootAdapter = null)
    {
        Owner = owner;
        transientRootIds.Clear();
        documentBinding = binding;
        this.rootAdapter = rootAdapter ?? HGDocumentRootAdapter.Instance;
        int candidateCount = 0;
        systemField = binding == null ? FindSystemField(owner?.GetType(), out candidateCount) : null;
        if (binding == null && systemField == null)
        {
            string reason = candidateCount > 1
                ? "有多個圖欄位；請用 OpenForDocument 指定要編輯的文件。"
                : "沒有可編輯的圖欄位。";
            Debug.LogError($"[GraphKit] '{(owner != null ? owner.name : "null")}' {reason}");
            return false;
        }

        if (!TryReload()) return false;
        if (Data == null)
        {
            Debug.LogError($"[GraphKit] '{(owner != null ? owner.name : "null")}' 的圖文件取不到內容，無法編輯。");
            return false;
        }

        PackType = Doc.PackType;
        AvailableRootKeys = this.rootAdapter.RootKeys(Doc, owner);
        return true;
    }

    /// <summary>Switches the session root contract after its Tool context has been resolved.</summary>
    public void SetRootAdapter(IHGRootAdapter adapter)
    {
        rootAdapter = adapter ?? HGDocumentRootAdapter.Instance;
        AvailableRootKeys = rootAdapter.RootKeys(Doc, Owner);
    }


    /// <summary>從 Owner 重新抓一份工作副本（開啟與「取消」共用）。</summary>
    public void Reload() => TryReload();

    internal bool TryReadOwnerDocument(out IGraphDocument document, out string error)
    {
        document = null;
        error = null;
        if (Owner == null) return false;
        try
        {
            if (documentBinding != null) return documentBinding.TryRead(Owner, out document);
            document = systemField?.GetValue(Owner) as IGraphDocument;
            return document != null;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    internal bool TryReload()
    {
        try
        {
            if (Owner == null) throw new InvalidOperationException("Owner 已不存在。");
            var binding = documentBinding ?? new HGDocumentBinding<IGraphDocument>(
                $"{systemField.DeclaringType?.FullName}.{systemField.Name}",
                owner => systemField.GetValue(owner) as IGraphDocument,
                (owner, document) => systemField.SetValue(owner, document),
                () => Activator.CreateInstance(systemField.FieldType) as IGraphDocument);
            binding.TryRead(Owner, out var live);
            var nextGuard = new HGDocumentCommitGuard(Owner, binding, live);
            if (live == null && !binding.TryCreate(out live))
                throw new InvalidOperationException("文件無法讀取或建立。");
            var copy = DeepCopy(live);
            if (copy == null) return false;
            var nextBaseline = DeepCopy(copy);
            if (nextBaseline == null) return false;

            Data = copy;
            commitGuard = nextGuard;
            LastCommitDiagnostic = null;
            Dirty = false;
            undoStack.Clear();
            redoStack.Clear();
            baseline = nextBaseline;
            lastPushTime = 0d;
            lastCatalogPush = 0d;
            return true;
        }
        catch (Exception exception)
        {
            LastCommitDiagnostic = new GraphDiagnostic("graphkit.commit.reload-failed", GraphDiagnosticSeverity.Error,
                "文件重載失敗，目前工作副本與歷程保留：" + exception.Message,
                new GraphDiagnosticLocation(documentId: DocumentId));
            Debug.LogError($"[GraphKit] 文件 '{DocumentId}' 重載失敗，目前工作副本與歷程保留：{exception.Message}");
            return false;
        }
    }

    private IGraphDocument DeepCopy(IGraphDocument document)
    {
        if (document == null)
        {
            Debug.LogError("[GraphKit] 無法建立圖的工作副本，來源為 null。");
            return null;
        }

        if (documentBinding != null)
        {
            if (!TryCloneBoundDocument(document, out var boundCopy)) return null;
            return boundCopy;
        }

        var copy = document.DeepCopy() as IGraphDocument;
        if (copy == null || ReferenceEquals(copy, document))
        {
            Debug.LogError("[GraphKit] 圖的 DeepCopy 失敗或回傳原實例，已停止編輯以避免直接修改 Owner。");
            return null;
        }
        return copy;
    }

    internal Action CaptureRollback()
    {
        var copy = DeepCopy(Data);
        if (copy == null) return null;
        var undo = undoStack.ToArray();
        var redo = redoStack.ToArray();
        var previousBaseline = baseline;
        double push = lastPushTime, catalogPush = lastCatalogPush;
        bool dirty = Dirty;
        return () =>
        {
            Data = copy;
            undoStack.Clear(); undoStack.AddRange(undo);
            redoStack.Clear(); redoStack.AddRange(redo);
            baseline = previousBaseline;
            lastPushTime = push; lastCatalogPush = catalogPush;
            Dirty = dirty;
        };
    }

    // ===== Undo / Redo（整份工作副本快照）=====
    // 圖是 SerializeReference 多型樹，逐項記錄變更比整份快照還難維護；節點數是幾十個等級，快照最直接。
    // 快照掛在 MarkDirty：每個修改點本來就要呼叫它，不會有「忘了記錄 Undo」的漏洞。
    //
    // 目錄與圖共用這一個堆疊，但**一步只記變動的那一半**（見 <see cref="HGStep"/>）：目錄住在 Owner 上，
    // 視窗外還有 Inspector 那個入口會改它，每一步都連目錄一起抄的話，退一步圖的編輯會把視窗外改的目錄
    // 一起還原掉。反過來也一樣——退一步目錄不該把圖整份換掉、清掉選取。

    private const int UndoLimit = 40;
    private const double MergeWindow = 0.4;          // 連續輸入合併成一步

    /// <summary>一步復原的內容。兩個欄位只有一個有值，另一個是 null＝這一步沒動它。</summary>
    private sealed class HGStep
    {
        public IGraphDocument Graph;                  // 圖的工作副本快照
        public object Catalogs;                       // Owner 上的目錄快照（型別由 ICatalogOwner 自己決定）
    }

    private readonly List<HGStep> undoStack = new();
    private readonly List<HGStep> redoStack = new();
    private IGraphDocument baseline;                  // 圖在上一次記錄點的狀態（＝本次修改前的狀態）
    private double lastPushTime;
    private double lastCatalogPush;                   // 上一個目錄步的時間，只給目錄步之間的合併用

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    private ICatalogOwner CatalogOwner => Owner as ICatalogOwner;

    public void MarkDirty()
    {
        if (!TrackChanges) return;
        if (Data == null) return;
        double now = EditorApplication.timeSinceStartup;

        if (baseline != null && now - lastPushTime >= MergeWindow)
        {
            Push(new HGStep { Graph = baseline });
            lastPushTime = now;
        }
        else if (baseline == null)
        {
            lastPushTime = now;
        }

        baseline = DeepCopy(Data);
        Dirty = true;
    }

    /// <summary>強制切一個 Undo 記錄點，讓下一次修改不會跟前一次合併。</summary>
    public void BreakUndoMerge() => lastPushTime = 0d;

    /// <summary>抄一份目前的目錄，給 <see cref="PushCatalogStep"/> 當「修改前」。Owner 沒有目錄時回 null。</summary>
    public object CaptureCatalogs() => CatalogOwner?.CaptureCatalogs();

    /// <summary>
    /// 把一次目錄修改記成一步。<paramref name="before"/> 是修改**之前**抄的快照。
    /// </summary>
    // 目錄是先抄再改，圖是改完才抄 baseline：目錄直接寫在 Owner 上、沒有工作副本，記著的 baseline 會被
    // 視窗外的入口改掉，只有當場抄的那份一定對得上。
    public void PushCatalogStep(object before)
    {
        if (!TrackChanges || before == null) return;
        double now = EditorApplication.timeSinceStartup;

        // 只跟「緊接著的上一個目錄步」合併：把資產拖到「＋ 新增目錄」上是一次手勢，卻會跑 Create 與
        // Add 兩條命令，分成兩步就得按兩次 Ctrl+Z 才回得到原狀。併進前一步記的是更早的狀態，退回去仍正確。
        bool merge = undoStack.Count > 0
                     && undoStack[undoStack.Count - 1].Catalogs != null
                     && now - lastCatalogPush < MergeWindow;

        if (merge) redoStack.Clear();
        else Push(new HGStep { Catalogs = before });

        lastCatalogPush = now;
        lastPushTime = 0d;                            // 下一次圖的修改不跟這一步合併
    }

    public HGStepKind Undo()
    {
        if (undoStack.Count == 0) return HGStepKind.None;

        var step = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        redoStack.Add(Capture(step));
        return Apply(step);
    }

    public HGStepKind Redo()
    {
        if (redoStack.Count == 0) return HGStepKind.None;

        var step = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        undoStack.Add(Capture(step));
        return Apply(step);
    }

    private void Push(HGStep step)
    {
        undoStack.Add(step);
        if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    /// <summary>抄一份現在的狀態進另一個堆疊。範圍跟著 step 走：它沒動過的那一半不記，也就不會被退回。</summary>
    private HGStep Capture(HGStep step) => new()
    {
        Graph = step.Graph != null ? DeepCopy(Data) : null,
        Catalogs = step.Catalogs != null ? CatalogOwner?.CaptureCatalogs() : null,
    };

    private HGStepKind Apply(HGStep step)
    {
        lastCatalogPush = 0d;                         // 剛退回來的那一步不再吃合併

        if (step.Graph != null)
        {
            Data = step.Graph;
            baseline = DeepCopy(Data);
            lastPushTime = 0d;
            Dirty = true;
            return HGStepKind.Graph;
        }

        // 目錄不走存檔交易，退回去就是立刻寫回 Owner，所以這裡不碰 Dirty，直接 SetDirty。
        CatalogOwner?.RestoreCatalogs(step.Catalogs);
        if (Owner != null) EditorUtility.SetDirty(Owner);
        lastPushTime = 0d;
        return HGStepKind.Catalogs;
    }

    /// <summary>先以 Core 規則驗證副本；通過後才寫回 Owner。</summary>
    public bool Save()
    {
        LastCommitDiagnostic = null;
        try { return SaveCore(); }
        catch (Exception exception)
        {
            LastCommitDiagnostic = new GraphDiagnostic("graphkit.commit.save-failed", GraphDiagnosticSeverity.Error,
                "文件保存失敗，工作副本與歷程保留：" + exception.Message,
                new GraphDiagnosticLocation(documentId: DocumentId));
            Debug.LogError($"[GraphKit] 文件 '{DocumentId}' 保存失敗，工作副本與歷程保留：{exception.Message}");
            return false;
        }
    }

    private bool SaveCore()
    {
        if (commitGuard == null) return false;
        LastCommitDiagnostic = commitGuard.Check();
        if (LastCommitDiagnostic != null) return false;
        var toStore = DeepCopy(Data);
        if (toStore == null) return false;
        toStore.MarkDirty();
        toStore.Verify();
        if (!toStore.IsValidated)
        {
            Debug.LogError("[GraphKit] Core Verify 未通過，Owner 未寫入。請查看 Console 的 Core 驗證訊息。");
            return false;
        }

        if (!commitGuard.TryWrite(toStore, out var failure))
        {
            LastCommitDiagnostic = failure;
            return false;
        }
        EditorUtility.SetDirty(Owner);
        if (Owner is Component component && component.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        AssetDatabase.SaveAssets();
        Dirty = false;
        return true;
    }

    private bool TryCloneBoundDocument(IGraphDocument document, out IGraphDocument clone)
    {
        try
        {
            if (documentBinding.TryClone(document, out clone)) return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[GraphKit] 複製文件 '{documentBinding.DocumentId}' 失敗：{exception.Message}");
            clone = null;
            return false;
        }

        Debug.LogError($"[GraphKit] 文件 '{documentBinding.DocumentId}' 的工作副本型別不符或與來源共用實例。");
        clone = null;
        return false;
    }

    // ===== Root groups =====

    /// <summary>Root 項目欄位的型別。空 root 時也要建得出新項目，所以問契約而不是從現有內容推。</summary>
    public Type RootItemSlotType => rootAdapter.ItemType(Doc);

    public List<HGRootGroupView> ReadRootGroups()
    {
        return new List<HGRootGroupView>(rootAdapter.ReadRoots(Doc) ?? Array.Empty<HGRootGroupView>());
    }

    /// <summary>Separates persisted root identity from its display title and collection index.</summary>
    public string RootId(object root)
    {
        if (Doc != null)
        {
            foreach (var view in ReadRootGroups())
            {
                if (!ReferenceEquals(view.Root, root)) continue;
                if (!string.IsNullOrWhiteSpace(view.StableId)) return "root:tool:" + Uri.EscapeDataString(view.StableId);
                object key = view.RootKey;
                if (key is string text) return "root:string:" + Uri.EscapeDataString(text);
                if (key is Enum value) return "root:" + key.GetType().FullName + ":" + value.ToString("D");
                if (key is Guid guid) return "root:guid:" + guid.ToString("N");
                if (key != null && (key.GetType().IsPrimitive || key is decimal))
                    return "root:" + key.GetType().FullName + ":" + Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture);
                throw new InvalidOperationException("Root adapter must provide StableId for non-scalar keys.");
            }
            throw new InvalidOperationException("Root is not owned by the active root adapter.");
        }
        if (!transientRootIds.TryGetValue(root, out var id))
            transientRootIds.Add(root, id = "root:transient:" + Guid.NewGuid().ToString("N"));
        return id;
    }

    /// <summary>這個識別值是否已經有 root（識別值不可重複，新增選單靠它決定哪些還能選）。</summary>
    public bool HasRoot(object rootKey)
    {
        foreach (var root in ReadRootGroups())
            if (Equals(root.RootKey, rootKey)) return true;
        return false;
    }

    /// <summary>新增一個 root；已存在同一個識別值則回傳既有的。</summary>
    public HGRootGroupView AddRoot(object rootKey)
    {
        return rootAdapter.AddRoot(Doc, rootKey);
    }

    public void RemoveRoot(HGRootGroupView root)
    {
        rootAdapter.RemoveRoot(Doc, root?.Root);
    }

    /// <summary>建立空的 root 項目；可先加入清單，稍後再由空 Node 選擇型別。</summary>
    public object NewRootItem(IList items) => rootAdapter.CreateItem(Doc);

    /// <summary>Compatibility adapter for documents that have not supplied a Tool-specific root contract.</summary>
    public sealed class HGDocumentRootAdapter : IHGRootAdapter
    {
        public static HGDocumentRootAdapter Instance { get; } = new();

        public Type ItemType(IGraphDocument document) => document?.ItemSlotType;

        public IReadOnlyList<object> RootKeys(IGraphDocument document, UnityEngine.Object owner)
            => document?.RootKeys(owner) ?? Array.Empty<object>();

        public IReadOnlyList<HGRootGroupView> ReadRoots(IGraphDocument document)
        {
            var roots = new List<HGRootGroupView>();
            if (document?.Roots == null) return roots;
            foreach (var root in document.Roots)
            {
                if (root == null) continue;
                roots.Add(new HGRootGroupView
                {
                    Root = root,
                    RootKey = document.KeyOf(root),
                    Items = document.ItemsOf(root),
                });
            }
            return roots;
        }

        public HGRootGroupView AddRoot(IGraphDocument document, object rootKey)
        {
            var root = document?.AddRoot(rootKey);
            return root == null ? null : new HGRootGroupView
            {
                Root = root,
                RootKey = document.KeyOf(root),
                Items = document.ItemsOf(root),
            };
        }

        public bool RemoveRoot(IGraphDocument document, object root)
        {
            if (document?.Roots == null || root == null) return false;
            if (!document.Roots.Contains(root)) return false;
            document.Roots.Remove(root);
            return true;
        }

        public object CreateItem(IGraphDocument document) => HGReflect.CreateInstance(ItemType(document));
    }

    /// <summary>
    /// 本 pack 的所有公式族：(結果型別, 具體 Slot 型別)。掃專案裡所有具體 FormulaSlot 子類，
    /// 與資料內容無關，也不會漏掉尚未被使用的公式族。
    /// </summary>
    public List<(Type resultType, Type slotType)> FormulaKinds()
    {
        var kinds = new List<(Type, Type)>();
        if (PackType == null) return kinds;

        // 不以結果型別去重：同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
        // 族的身份是 Slot 型別本身。需要「唯一挑一個」的呼叫端必須自己帶 Slot 型別來，不能用結果型別反查。
        foreach (var t in UnityEditor.TypeCache.GetTypesDerivedFrom<FormulaSlotBase>())
        {
            if (t.IsAbstract || t.ContainsGenericParameters) continue;
            if (HGReflect.FormulaSlotPack(t) != PackType) continue;
            var rt = HGReflect.ResultType(t);
            if (rt == null) continue;
            kinds.Add((rt, t));
        }
        return kinds;
    }

    // 連求值端的 schema 快取一起清：編輯期資產內容會變，求值端那份不清就會拿到舊參數列。
    public void ClearAssetParameterCache()
    {
        assetParameterCache.Clear();
        AssetGraphSchema.InvalidateCache();
    }

    public List<AssetParameterDefinition> AssetParameters(ScriptableObject asset)
    {
        if (asset == null) return new List<AssetParameterDefinition>();
        if (assetParameterCache.TryGetValue(asset, out var cached)) return cached;
        cached = AssetGraphSchema.Read(asset, out _);
        assetParameterCache[asset] = cached;
        return cached;
    }

    /// <summary>補齊資產節點的參數列。新列預設不覆蓋，因此不改變執行結果。</summary>
    public bool EnsureAssetBindings(GraphNode carrier)
    {
        if (carrier?.Kind != NodeKind.Asset || carrier.AssetObject == null) return false;
        bool changed = false;
        foreach (var parameter in AssetParameters(carrier.AssetObject))
        {
            // 配對鍵是（族, 名稱）：同名不同族是兩個參數，只比名字會少長一列。
            NamedFormulaSlot binding = null;
            foreach (var current in carrier.Bindings)
                if (current?.Name == parameter.Name && current.Slot?.FamilyType == parameter.Slot?.FamilyType) { binding = current; break; }
            if (binding != null) continue;

            // 直接用參數自己那格的 Slot 型別，不要拿結果型別去反查族：同一個結果型別可能有多個族
            // （例：string 同時有 String 與 Key），反查會挑到錯的那個，參數列型別就跟資產對不上。
            Type slotType = parameter.Slot?.GetType();
            if (HGReflect.CreateInstance(slotType) is FormulaSlotBase slot)
            {
                carrier.Bindings.Add(new NamedFormulaSlot(parameter.Name, slot));
                changed = true;
                continue;
            }

            // 沒有對應的 FormulaSlot 型別就生不出參數列，企劃只會看到「這個參數不見了」。
            string key = $"{carrier.AssetObject.name}/{parameter.Name}";
            if (loggedUnbindableParameters.Add(key))
                Debug.LogWarning($"[GraphKit] 資產 '{carrier.AssetObject.name}' 的參數 '{parameter.Name}' " +
                    $"找不到對應的 FormulaSlot 型別（結果 {HGReflect.ResultTypeName(parameter.ResultType)}），無法建立參數列。" +
                    "請補上這個結果型別的 Formula / Asset / Slot 三件組。");
        }
        return changed;
    }

    // ===== 具名Token（端點）=====
    // 一個Token＝一顆 GraphToken：自己的名字、自己的取值欄位、自己的畫布與候選池。
    // 圖裡引用它的是 NodeKind.Token 節點，存的是物件參照，不是名字字串。

    /// <summary>走訪整張圖的所有載體：動作樹上的、候選池裡的，以及它們的子樹。</summary>
    public IEnumerable<GraphNode> AllCarriers()
    {
        var seen = new HashSet<GraphNode>();
        foreach (var slot in AllSlots())
        {
            var node = HGReflect.GetNode(slot);
            if (node != null && seen.Add(node)) yield return node;
        }
        foreach (var node in AllOrphanNodes())
            if (seen.Add(node)) yield return node;
    }

    /// <summary>指定圖域內的所有載體。資產焦點用它隔離 Owner 與資產的標註名稱作用域。</summary>
    public IEnumerable<GraphNode> CarriersOf(IEnumerable<object> roots, IEnumerable<GraphNode> orphans)
    {
        var seen = new HashSet<GraphNode>();
        var visited = new HashSet<object>(HGRefComparer.Instance);
        if (roots != null)
        {
            foreach (var root in roots)
                foreach (var carrier in WalkCarriers(root, visited))
                    if (seen.Add(carrier)) yield return carrier;
        }
        if (orphans == null) yield break;
        foreach (var orphan in orphans)
        {
            foreach (var carrier in WalkCarriers(orphan, visited))
                if (seen.Add(carrier)) yield return carrier;
        }
    }

    /// <summary>Owner 工作副本的Token清單。資產焦點請改用焦點自己那份工作副本。</summary>
    public List<GraphToken> OwnerTokens
        => HGReflect.Tokens(Data) ?? new List<GraphToken>();

    /// <summary>把一份端點清單讀成顯示用的視圖，依名稱排序。</summary>
    public static List<HGToken> ReadTokens(IEnumerable<GraphToken> endpoints)
    {
        var result = new List<HGToken>();
        if (endpoints == null) return result;
        foreach (var endpoint in endpoints)
            if (endpoint != null) result.Add(new HGToken { Token = endpoint });
        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

    /// <summary>載體算得出什麼型別：內嵌看公式型別，資產看資產型別，Token看端點，空節點無從得知。</summary>
    public static Type CarrierResultType(GraphNode node)
    {
        if (node == null) return null;
        if (node.Kind == NodeKind.Inline && node.BodyObject != null)
            return HGReflect.FormulaResultType(node.BodyObject.GetType());
        if (node.Kind == NodeKind.Asset && node.AssetObject != null)
            return HGReflect.AssetResultType(node.AssetObject);
        if (node.Kind == NodeKind.Token) return node.Token?.ResultType;
        return null;
    }

    /// <summary>
    /// 建一個新Token並加進清單。名稱唯一性是「族＋名稱」，所以同名不同族可以並存。
    /// slotType 就是族，建立後不再更動——要換族就刪掉重建。
    /// </summary>
    public GraphToken CreateToken(List<GraphToken> scope, Type slotType, out string error)
    {
        error = null;
        if (scope == null) { error = "這張圖沒有 Token 清單。"; return null; }
        if (HGReflect.CreateInstance(slotType) is not FormulaSlotBase slot)
        {
            error = "建不出這一族的取值欄位。";
            return null;
        }

        var endpoint = new GraphToken(NextTokenName(scope, slot.FamilyType), slot);
        endpoint.EnsureId();
        scope.Add(endpoint);
        MarkDirty();
        return endpoint;
    }

    /// <summary>
    /// 複製一個Token：新的頭端、新名字、內容整棵深拷貝（含候選池）。
    /// 清單上的**其他Token一律共用**——子樹裡的 Token 節點還是指向原本那一個，不會抄出孤兒端點。
    /// 資產是 UnityEngine.Object，深複製本來就只抄參考，共用資產不會被複製成第二份。
    /// </summary>
    public GraphToken DuplicateToken(GraphToken source, List<GraphToken> scope, out string error)
    {
        error = null;
        if (source == null) { error = "沒有可複製的 Token。"; return null; }
        if (scope == null) { error = "這張圖沒有 Token 清單。"; return null; }

        var shared = new List<object>();
        foreach (var other in scope)
            if (other != null && !ReferenceEquals(other, source)) shared.Add(other);

        var copy = GraphDeepCopy.Copy(source, shared);
        if (copy == null) { error = "複製這個 Token 失敗，詳見 Console。"; return null; }

        // 識別碼一定要換：頭端的 Id 決定焦點與座標，載體的 Id 決定節點座標與選取。
        copy.ResetId();
        copy.EnsureId();
        ResetNodeIds(copy, shared);
        copy.Name = CopyName(scope, source.Name, copy.FamilyType);

        scope.Add(copy);
        MarkDirty();
        return copy;
    }

    /// <summary>複本的名字：「原名 複本」，撞名就往後加號碼。唯一性和別處一樣是「族＋名稱」。</summary>
    private static string CopyName(IEnumerable<GraphToken> scope, string sourceName, Type kind)
    {
        var used = new HashSet<string>();
        foreach (var other in scope ?? new List<GraphToken>())
            if (other != null && other.FamilyType == kind && !string.IsNullOrEmpty(other.Name))
                used.Add(other.Name);

        string root = string.IsNullOrEmpty(sourceName) ? "Token" : sourceName;
        string candidate = root + " 複本";
        for (int i = 2; used.Contains(candidate); i++) candidate = $"{root} 複本{i}";
        return candidate;
    }

    /// <summary>替Token改名。同族內不可重複；空名稱不允許（外部是用名字查的）。</summary>
    public bool RenameToken(GraphToken endpoint, string name, List<GraphToken> scope, out string error)
    {
        error = null;
        if (endpoint == null) { error = "沒有可改名的 Token。"; return false; }
        if (string.IsNullOrWhiteSpace(name)) { error = "名稱不可為空。"; return false; }
        name = name.Trim();
        if (name == endpoint.Name) return true;

        foreach (var other in scope ?? new List<GraphToken>())
        {
            if (other == null || ReferenceEquals(other, endpoint)) continue;
            // 重名比對以族為準：TokenTable 的登記鍵就是（族, 名稱），
            // 所以同結果型別的不同族（String / Key）可以同名，各自查各自那格。
            if (other.Name != name || other.FamilyType != endpoint.FamilyType) continue;
            error = $"已存在名為 '{name}' 的 {HGReflect.SlotKindName(other.Slot)} Token。";
            return false;
        }

        endpoint.Name = name;
        MarkDirty();
        return true;
    }

    /// <summary>取一個在 scope 內同族不重複的預設名（Token1、Token2…）。</summary>
    public string NextTokenName(IEnumerable<GraphToken> scope, Type kind)
    {
        var used = new HashSet<string>();
        foreach (var other in scope ?? new List<GraphToken>())
            if (other != null && other.FamilyType == kind && !string.IsNullOrEmpty(other.Name))
                used.Add(other.Name);

        for (int i = 1; ; i++)
        {
            string key = "Token" + i;
            if (!used.Contains(key)) return key;
        }
    }

    /// <summary>
    /// 刪掉一個Token。指著它的節點會一起清空——留著會變成「參照得到但查不到值」的靜默失效，
    /// 清空後那些節點是空節點，存檔驗證擋得住。
    /// </summary>
    public void DeleteToken(GraphToken endpoint, List<GraphToken> scope, IEnumerable<GraphNode> carriers)
    {
        if (endpoint == null) return;
        scope?.Remove(endpoint);
        foreach (var node in carriers ?? AllCarriers())
            if (node != null && ReferenceEquals(node.Token, endpoint)) node.Clear();
        MarkDirty();
    }

    /// <summary>這個Token在圖內被幾個欄位接著。0＝純對外端點，不是錯誤。</summary>
    public static int CountReferences(GraphToken endpoint, IEnumerable<GraphSlotBase> slots)
    {
        if (endpoint == null || slots == null) return 0;
        int n = 0;
        foreach (var slot in slots)
            if (ReferenceEquals(slot?.Node?.Token, endpoint)) n++;
        return n;
    }

    // ===== 候選節點 =====
    // 候選池屬於「目前焦點的頭端」（動作頭端 / Token 頭端 / 資產），不再有全系統共用的一池 + FocusId 歸屬。

    /// <summary>目前焦點的頭端物件，由視窗切焦點時指定。</summary>
    public object OrphanHead { get; set; }

    public List<GraphNode> Orphans => HGReflect.Orphans(OrphanHead);

    public void AddOrphan(GraphNode node)
    {
        if (node == null) return;
        node.EnsureId();
        var list = Orphans;
        if (list != null && !list.Contains(node)) list.Add(node);
        MarkDirty();
    }

    public void RemoveOrphan(GraphNode node)
    {
        if (node == null) return;
        if (Orphans?.Remove(node) != true)
        {
            // 時機畫布合併前的候選掛在個別動作頭端上，不在目前頭端的池裡；不掃就會刪不掉。
            foreach (var head in Heads())
                if (HGReflect.Orphans(head)?.Remove(node) == true) break;
        }
        MarkDirty();
    }

    // ===== 座標記憶 =====
    // 座標與備註住在載體（GraphNode）與頭端上。建圖時登記 id → 載體，讓視窗仍可用 nodeId 讀寫。

    private readonly Dictionary<string, object> carriers = new();

    public void ClearCarriers() => carriers.Clear();

    /// <summary>建圖時登記一個節點的載體：GraphNode、ActionSlot 或 ActionTimingGroup 頭端。</summary>
    public void RegisterCarrier(string nodeId, object carrier)
    {
        if (string.IsNullOrEmpty(nodeId) || carrier == null) return;
        carriers[nodeId] = carrier;
    }

    public object Carrier(string nodeId)
        => !string.IsNullOrEmpty(nodeId) && carriers.TryGetValue(nodeId, out var c) ? c : null;

    public bool TryGetPosition(string nodeId, out Vector2 pos)
    {
        pos = Vector2.zero;
        var carrier = Carrier(nodeId);
        if (carrier == null) return false;
        if (carrier is GraphNode node)
        {
            if (!node.HasPos) return false;
            pos = node.Pos;
            return true;
        }
        return HGReflect.GetHeadPos(carrier, out pos);
    }

    public void SetPosition(string nodeId, Vector2 pos)
    {
        var carrier = Carrier(nodeId);
        if (carrier == null) return;
        if (TryGetPosition(nodeId, out var current) && current == pos) return;

        if (carrier is GraphNode node) node.Pos = pos;
        else HGReflect.SetHeadPos(carrier, pos);
        MarkDirty();
    }

    public bool TryGetNodeView(string nodeId, out string tips)
    {
        tips = "";
        if (Carrier(nodeId) is not GraphNode node) return false;
        tips = node.Note ?? "";
        return true;
    }

    public void SetNodeTips(string nodeId, string tips)
    {
        if (Carrier(nodeId) is not GraphNode node || node.Note == tips) return;
        node.Note = tips;
        MarkDirty();
    }

    /// <summary>
    /// 切換節點停用。停用的載體不求值，所有指著它的欄位一律取自己的保底值（Action 直接跳過）。
    /// 這是資料變更不是視覺狀態，所以走 MarkDirty；HEAD 沒有載體，改不到。
    /// </summary>
    public void SetNodeDisabled(string nodeId, bool disabled)
    {
        if (Carrier(nodeId) is not GraphNode node || node.Disabled == disabled) return;
        node.Disabled = disabled;
        MarkDirty();
    }

    /// <summary>忘掉手動座標，讓自動排版重新接手（整理版面）。</summary>
    public void ClearPosition(string nodeId)
    {
        var carrier = Carrier(nodeId);
        if (carrier == null) return;
        if (carrier is GraphNode node) node.ClearPos();
        else HGReflect.ClearHeadPos(carrier);
        MarkDirty();
    }

    // ===== 全圖走訪 =====

    /// <summary>走訪整份工作副本裡的所有 FormulaSlot（動作、Token、未連接節點）。不下沉到 Asset 內部。</summary>
    public IEnumerable<FormulaSlotBase> AllFormulaSlots()
    {
        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var root in Roots())
            foreach (var slot in WalkSlots(root, visited))
                if (slot is FormulaSlotBase formulaSlot) yield return formulaSlot;
    }

    /// <summary>走訪整份工作副本裡的所有 Slot（含 ActionSlot）。</summary>
    public IEnumerable<GraphSlotBase> AllSlots()
    {
        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var root in Roots())
            foreach (var slot in WalkSlots(root, visited))
                yield return slot;
    }

    private IEnumerable<object> Roots()
    {
        foreach (var g in ReadRootGroups())
        {
            if (g.Items == null) continue;
            foreach (var a in g.Items)
                if (a != null) yield return a;
        }
        // Token的取值欄位也是根：它的子樹是正式資料，走訪、驗證與資產引用都要算進來。
        foreach (var endpoint in OwnerTokens)
            if (endpoint?.Slot != null) yield return endpoint.Slot;
        foreach (var node in AllOrphanNodes())
            yield return node;
    }

    /// <summary>
    /// 所有候選池掛點：時機畫布本身（LogicGraph）、每個Token端點，與時機群組裡的動作欄位。
    /// 動作頭端上那份只為了讀回合併畫布之前存下來的候選。
    /// </summary>
    public IEnumerable<object> Heads()
    {
        if (Data != null) yield return Data;
        foreach (var endpoint in OwnerTokens)
            if (endpoint != null) yield return endpoint;
        foreach (var g in ReadRootGroups())
        {
            if (g.Items == null) continue;
            foreach (var a in g.Items)
                if (a != null) yield return a;
        }
    }

    /// <summary>所有頭端的候選節點。</summary>
    public IEnumerable<GraphNode> AllOrphanNodes()
    {
        foreach (var head in Heads())
        {
            var list = HGReflect.Orphans(head);
            if (list == null) continue;
            foreach (var node in list)
                if (node != null) yield return node;
        }
    }

    /// <summary>走訪任意一份 LogicGraph 的所有 Slot（重建資產引用清單用，對象不是工作副本）。</summary>
    public static IEnumerable<GraphSlotBase> SlotsOfSystem(object system)
    {
        if (system == null) yield break;
        var visited = new HashSet<object>(HGRefComparer.Instance);

        var walkDoc = system as IGraphDocument;
        if (walkDoc?.Roots is IList groups)
        {
            foreach (var g in groups)
            {
                if (g == null) continue;
                if (walkDoc.ItemsOf(g) is not IList actions) continue;
                foreach (var a in actions)
                {
                    if (a == null) continue;
                    foreach (var s in WalkSlots(a, visited)) yield return s;
                }
            }
        }

        // Token的子樹是正式資料（對外端點），資產引用要算它一份；候選池不算。
        var endpoints = HGReflect.Tokens(system);
        if (endpoints == null) yield break;
        foreach (var e in endpoints)
        {
            if (e is not GraphToken endpoint || endpoint.Slot == null) continue;
            foreach (var s in WalkSlots(endpoint.Slot, visited)) yield return s;
        }
    }

    /// <summary>複製節點後清掉整棵樹的識別碼，避免新舊節點共用座標記錄。</summary>
    public static void ResetNodeIds(object root) => ResetNodeIds(root, null);

    /// <summary>
    /// 同上，但 <paramref name="skip"/> 裡的物件當作走過了——走訪會在那裡停住。
    /// 複製單一Token時要把清單上**其他Token**丟進來：子樹裡指向它們的 Token 節點是共用引用，
    /// 一路走進去會把別人的載體識別碼一起清掉，那些圖的座標當場全部重來。
    /// </summary>
    public static void ResetNodeIds(object root, IEnumerable<object> skip)
    {
        var visited = new HashSet<object>(HGRefComparer.Instance);
        if (skip != null)
            foreach (var item in skip)
                if (item != null) visited.Add(item);
        ResetNodeIdsInternal(root, visited);
    }

    private static void ResetNodeIdsInternal(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) return;
        if (node is GraphNode carrier) carrier.ResetId();
        else if (HGReflect.IsActionSlotType(node.GetType())) HGReflect.ResetSlotEditorId(node);

        foreach (var f in HGReflect.Fields(node.GetType()))
        {
            if (f.IsStatic || f.IsNotSerialized) continue;
            var val = f.GetValue(node);
            if (val == null) continue;
            var t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || val is string || val is UnityEngine.Object) continue;

            if (val is IList list)
            {
                foreach (var item in list) ResetNodeIdsInternal(item, visited);
                continue;
            }
            ResetNodeIdsInternal(val, visited);
        }
    }

    /// <summary>由任一節點或 Slot 往下收集所有 Slot。Asset（ScriptableObject）視為 leaf。</summary>
    public static IEnumerable<GraphSlotBase> WalkSlots(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) yield break;

        // 走訪入口收任意物件（節點、內容、清單），但只有 Slot 會被吐出來。
        if (node is GraphSlotBase slot)
        {
            yield return slot;
            var carrier = slot.Node;
            if (carrier != null)
                foreach (var s in WalkSlots(carrier, visited)) yield return s;
            yield break;
        }

        foreach (var f in HGReflect.Fields(node.GetType()))
        {
            var val = f.GetValue(node);
            if (val == null) continue;
            var vt = val.GetType();
            if (vt.IsPrimitive || vt.IsEnum || val is string || val is UnityEngine.Object) continue;

            if (val is IList list)
            {
                foreach (var item in list)
                {
                    if (item == null) continue;
                    foreach (var s in WalkSlots(item, visited)) yield return s;
                }
                continue;
            }

            foreach (var s in WalkSlots(val, visited)) yield return s;
        }
    }

    /// <summary>
    /// 正式資料引用的資產：動作執行樹，以及每個Token端點的子樹。
    /// 候選池只是編輯暫存，不得污染 subscriber。
    /// </summary>
    public static HashSet<ScriptableObject> ReferencedAssetsOfSystem(object system)
    {
        var result = new HashSet<ScriptableObject>();
        if (system == null) return result;

        var visited = new HashSet<object>(HGRefComparer.Instance);
        var assetDoc = system as IGraphDocument;
        if (assetDoc?.Roots is IList groups)
        {
            foreach (var group in groups)
            {
                if (group == null || assetDoc.ItemsOf(group) is not IList actions) continue;
                foreach (var action in actions) CollectFormalAssets(action, visited, result);
            }
        }

        if (HGReflect.Tokens(system) is List<GraphToken> endpoints)
        {
            foreach (var e in endpoints)
                if (e is GraphToken endpoint && endpoint.Slot != null)
                    CollectFormalAssets(endpoint.Slot, visited, result);
        }
        return result;
    }

    private static void CollectFormalAssets(object node, HashSet<object> visited, HashSet<ScriptableObject> result)
    {
        if (node == null || !visited.Add(node)) return;

        if (node is GraphSlotBase slot)
        {
            CollectFormalAssets(slot.Node, visited, result);
            return;
        }
        if (node is GraphNode carrier)
        {
            if (carrier.Kind == NodeKind.Asset && carrier.AssetObject != null) result.Add(carrier.AssetObject);
            else if (carrier.Kind == NodeKind.Inline) CollectFormalAssets(carrier.BodyObject, visited, result);
            foreach (var binding in carrier.Bindings)
                if (binding?.Slot != null) CollectFormalAssets(binding.Slot, visited, result);
            return;
        }
        if (node is UnityEngine.Object) return;

        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is string) return;
        if (node is IList list)
        {
            foreach (var item in list) CollectFormalAssets(item, visited, result);
            return;
        }

        foreach (var field in HGReflect.Fields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            CollectFormalAssets(field.GetValue(node), visited, result);
        }
    }

    private static IEnumerable<GraphNode> WalkCarriers(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) yield break;

        if (node is GraphSlotBase slot)
        {
            var carrier = slot.Node;
            if (carrier != null)
                foreach (var found in WalkCarriers(carrier, visited)) yield return found;
            yield break;
        }

        if (node is GraphNode graphNode)
        {
            yield return graphNode;
            if (graphNode.Kind == NodeKind.Inline && graphNode.BodyObject != null)
                foreach (var found in WalkCarriers(graphNode.BodyObject, visited)) yield return found;
            foreach (var binding in graphNode.Bindings)
                if (binding?.Slot != null)
                    foreach (var found in WalkCarriers(binding.Slot, visited)) yield return found;
            yield break;
        }

        if (node is UnityEngine.Object) yield break;
        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is string) yield break;

        if (node is IList list)
        {
            foreach (var item in list)
                foreach (var found in WalkCarriers(item, visited)) yield return found;
            yield break;
        }

        foreach (var field in HGReflect.Fields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            foreach (var found in WalkCarriers(field.GetValue(node), visited)) yield return found;
        }
    }
}

/// <summary>依參考位址比對的集合比較器：走訪節點圖時避免值相等造成誤判。</summary>
public sealed class HGRefComparer : IEqualityComparer<object>
{
    public static readonly HGRefComparer Instance = new();
    public new bool Equals(object a, object b) => ReferenceEquals(a, b);
    public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
}

}
