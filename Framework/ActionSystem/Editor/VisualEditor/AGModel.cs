namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>一個標註（Token）的視圖：名字掛在哪顆載體上，以及那顆載體算出什麼型別。</summary>
// 標註化之後 Token 不再是獨立宣告，而是「某顆節點掛了一個名字」，所以這裡只是查詢結果，不持有資料。
public class AGToken
{
    public string Key;
    public Type ResultType;
    public GraphNode Node;      // 被標註的載體

    public string TypeName => AGReflect.ResultTypeName(ResultType);
}

/// <summary>一個時機群組：Timing 值與其動作清單。</summary>
public class AGTimingGroup
{
    public object Group;        // ActionTimingGroup<TTiming, TPack>
    public Enum Timing;
    public IList Actions;       // List<ActionSlot<TPack>>
}

/// <summary>
/// 視覺化編輯器的資料模型：綁定 Owner SO，持有一份 ActionSystem 工作副本，所有編輯都改副本，存檔才寫回。
/// </summary>
// 「取消要能捨棄自上次存檔以來的所有修改」→ 只有工作副本能乾淨做到，順便讓 Undo 可以用整份快照實作。
public class AGModel
{
    // Owner 不限 ScriptableObject：Hierarchy 上掛 ActionSystem 的 MonoBehaviour 也能編。
    public UnityEngine.Object Owner { get; private set; }
    public object Data { get; private set; }        // ActionSystem<TTiming, TPack> 工作副本
    public Type TimingType { get; private set; }
    public Type PackType { get; private set; }
    public bool Dirty { get; private set; }
    public bool TrackChanges { get; set; } = true;

    private FieldInfo systemField;                    // Owner 上放 ActionSystem 的欄位
    private readonly Dictionary<ScriptableObject, List<AssetParameterDefinition>> assetParameterCache = new();

    // ===== 綁定 =====

    /// <summary>在任意 SO 上找出 ActionSystem 欄位；找不到回 null。</summary>
    public static FieldInfo FindSystemField(UnityEngine.Object owner)
        => owner == null ? null : FindSystemField(owner.GetType());

    /// <summary>只看型別就能判斷支不支援，掃描專案時不必先載入資產。</summary>
    public static FieldInfo FindSystemField(Type ownerType)
    {
        if (ownerType == null) return null;
        foreach (var f in AGReflect.Fields(ownerType))
        {
            var t = f.FieldType;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ActionSystem<,>)) return f;
        }
        return null;
    }

    public static bool CanEdit(UnityEngine.Object owner) => FindSystemField(owner) != null;

    /// <summary>綁定 Owner 並複製一份工作副本。失敗回 false 並記 Log。</summary>
    public bool Bind(UnityEngine.Object owner)
    {
        Owner = owner;
        systemField = FindSystemField(owner);
        if (systemField == null)
        {
            Debug.LogError($"[ActionGraph] '{(owner != null ? owner.name : "null")}' 沒有 ActionSystem 欄位，無法編輯。");
            return false;
        }

        var args = systemField.FieldType.GetGenericArguments();
        TimingType = args[0];
        PackType = args[1];

        Reload();
        return Data != null;
    }

    /// <summary>從 Owner 重新抓一份工作副本（開啟與「取消」共用）。</summary>
    public void Reload()
    {
        var live = systemField.GetValue(Owner);
        if (live == null)
            live = Activator.CreateInstance(systemField.FieldType);
        Data = DeepCopy(live);
        Dirty = false;
        ClearHistory();
    }

    private static object DeepCopy(object system)
    {
        if (system == null)
        {
            Debug.LogError("[ActionGraph] 無法建立 ActionSystem 工作副本，來源為 null。");
            return null;
        }
        var m = system.GetType().GetMethod("DeepCopy");
        var copy = m?.Invoke(system, null);
        if (copy == null) Debug.LogError("[ActionGraph] ActionSystem.DeepCopy 失敗，已停止編輯以避免直接修改 Owner。");
        return copy;
    }

    // ===== Undo / Redo（整份工作副本快照）=====
    // 圖是 SerializeReference 多型樹，逐項記錄變更比整份快照還難維護；節點數是幾十個等級，快照最直接。
    // 快照掛在 MarkDirty：每個修改點本來就要呼叫它，不會有「忘了記錄 Undo」的漏洞。

    private const int UndoLimit = 40;
    private const double MergeWindow = 0.4;          // 連續輸入合併成一步

    private readonly List<object> undoStack = new();
    private readonly List<object> redoStack = new();
    private object baseline;                          // 上一次記錄點的狀態（＝本次修改前的狀態）
    private double lastPushTime;

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public void MarkDirty()
    {
        if (!TrackChanges) return;
        if (Data == null) return;
        double now = EditorApplication.timeSinceStartup;

        if (baseline != null && now - lastPushTime >= MergeWindow)
        {
            undoStack.Add(baseline);
            if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
            redoStack.Clear();
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

    public bool Undo()
    {
        if (undoStack.Count == 0) return false;
        redoStack.Add(DeepCopy(Data));
        Data = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        baseline = DeepCopy(Data);
        lastPushTime = 0d;
        Dirty = true;
        return true;
    }

    public bool Redo()
    {
        if (redoStack.Count == 0) return false;
        undoStack.Add(DeepCopy(Data));
        Data = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        baseline = DeepCopy(Data);
        lastPushTime = 0d;
        Dirty = true;
        return true;
    }

    private void ClearHistory()
    {
        undoStack.Clear();
        redoStack.Clear();
        baseline = Data != null ? DeepCopy(Data) : null;
        lastPushTime = 0d;
    }

    /// <summary>先以 Core 規則驗證副本；通過後才寫回 Owner。</summary>
    public bool Save()
    {
        var toStore = DeepCopy(Data);
        if (toStore == null) return false;

        var markDirty = toStore.GetType().GetMethod("MarkDirty");
        markDirty?.Invoke(toStore, null);
        var verify = toStore.GetType().GetMethod("Verify");
        verify?.Invoke(toStore, null);
        var isValidated = toStore.GetType().GetProperty("IsValidated");
        if (isValidated?.GetValue(toStore) is not true)
        {
            Debug.LogError("[ActionGraph] Core Verify 未通過，Owner 未寫入。請查看 Console 的 Core 驗證訊息。");
            return false;
        }

        systemField.SetValue(Owner, toStore);
        EditorUtility.SetDirty(Owner);
        if (Owner is Component component && component.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        AssetDatabase.SaveAssets();
        Dirty = false;
        return true;
    }

    // ===== 時機群組 =====

    public IList Groups => AGReflect.Get(Data, "ActionGroups") as IList;

    /// <summary>從 ActionGroups 的泛型型別取得動作 Slot 型別，空群組時也能新增空動作。</summary>
    public Type ActionSlotType
    {
        get
        {
            var groups = Groups;
            if (groups == null || !groups.GetType().IsGenericType) return null;
            var groupType = groups.GetType().GetGenericArguments()[0];
            var actions = AGReflect.Find(groupType, "Actions");
            return actions != null && AGReflect.IsList(actions.FieldType, out var slotType) ? slotType : null;
        }
    }

    public List<AGTimingGroup> ReadGroups()
    {
        var result = new List<AGTimingGroup>();
        var list = Groups;
        if (list == null) return result;
        foreach (var g in list)
        {
            if (g == null) continue;
            result.Add(new AGTimingGroup
            {
                Group = g,
                Timing = AGReflect.Get(g, "Timing") as Enum,
                Actions = AGReflect.Get(g, "Actions") as IList,
            });
        }
        return result;
    }

    /// <summary>時機是否已經有群組（enum 不可重複，新增選單靠它決定哪些還能選）。</summary>
    public bool HasGroup(Enum timing)
    {
        foreach (var g in ReadGroups())
            if (Equals(g.Timing, timing)) return true;
        return false;
    }

    /// <summary>新增一個時機群組；已存在同一個時機則回傳既有的。</summary>
    public AGTimingGroup AddGroup(Enum timing)
    {
        foreach (var g in ReadGroups())
            if (Equals(g.Timing, timing)) return g;

        var list = Groups;
        if (list == null) return null;
        var groupType = list.GetType().GetGenericArguments()[0];
        var group = AGReflect.CreateInstance(groupType);
        if (group == null) return null;

        AGReflect.Set(group, "Timing", timing);
        var actionsField = AGReflect.Find(groupType, "Actions");
        var actions = AGReflect.EnsureList(group, actionsField);
        list.Add(group);
        return new AGTimingGroup { Group = group, Timing = timing, Actions = actions };
    }

    public void RemoveGroup(AGTimingGroup group)
    {
        Groups?.Remove(group.Group);
    }

    /// <summary>建立空 ActionSlot；可先加入清單，稍後再由空 Node 選擇動作型別。</summary>
    public object NewActionSlot(IList actionList)
    {
        var slotType = actionList.GetType().GetGenericArguments()[0];
        return AGReflect.CreateInstance(slotType);
    }

    /// <summary>
    /// 本 pack 的所有公式族：(結果型別, 具體 Slot 型別)。掃專案裡所有具體 FormulaSlot 子類，
    /// 與資料內容無關，也不會漏掉尚未被使用的公式族。
    /// </summary>
    public List<(Type resultType, Type slotType)> FormulaKinds()
    {
        var kinds = new List<(Type, Type)>();
        if (PackType == null) return kinds;

        var seen = new HashSet<Type>();
        foreach (var t in UnityEditor.TypeCache.GetTypesDerivedFrom<FormulaSlotBase>())
        {
            if (t.IsAbstract || t.ContainsGenericParameters) continue;
            if (AGReflect.FormulaSlotPack(t) != PackType) continue;
            var rt = AGReflect.ResultType(t);
            if (rt == null || !seen.Add(rt)) continue;
            kinds.Add((rt, t));
        }
        return kinds;
    }

    public void ClearAssetParameterCache() => assetParameterCache.Clear();

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
            NamedFormulaSlot binding = null;
            foreach (var current in carrier.Bindings)
                if (current?.Name == parameter.Name) { binding = current; break; }
            if (binding != null) continue;

            Type slotType = null;
            foreach (var kind in FormulaKinds())
            {
                if (kind.resultType != parameter.ResultType || AGReflect.FormulaSlotPack(kind.slotType) != parameter.PackType) continue;
                slotType = kind.slotType;
                break;
            }
            if (AGReflect.CreateInstance(slotType) is FormulaSlotBase slot)
            {
                carrier.Bindings.Add(new NamedFormulaSlot(parameter.Name, slot));
                changed = true;
            }
        }
        return changed;
    }

    // ===== Token（標註）=====
    // Token 不再是獨立宣告，而是「某顆載體掛了一個名字」。左欄只是索引，資料住在節點上。

    /// <summary>走訪整張圖的所有載體：動作樹上的、候選池裡的，以及它們的子樹。</summary>
    public IEnumerable<GraphNode> AllCarriers()
    {
        var seen = new HashSet<GraphNode>();
        foreach (var slot in AllSlots())
        {
            var node = AGReflect.GetNode(slot);
            if (node != null && seen.Add(node)) yield return node;
        }
        foreach (var node in AllOrphanNodes())
            if (seen.Add(node)) yield return node;
    }

    /// <summary>指定圖域內的所有載體。資產焦點用它隔離 Owner 與資產的標註名稱作用域。</summary>
    public IEnumerable<GraphNode> CarriersOf(IEnumerable<object> roots, IEnumerable<GraphNode> orphans)
    {
        var seen = new HashSet<GraphNode>();
        var visited = new HashSet<object>(AGRefComparer.Instance);
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

    /// <summary>圖上所有被標註的節點。順序不保證穩定，顯示端自行排序。</summary>
    public List<AGToken> ReadTokens()
        => ReadTokens(AllCarriers());

    public List<AGToken> ReadTokens(IEnumerable<GraphNode> carriers)
    {
        var result = new List<AGToken>();
        if (carriers == null) return result;
        foreach (var node in carriers)
        {
            if (!node.IsToken) continue;
            result.Add(new AGToken
            {
                Key = node.TokenName,
                ResultType = CarrierResultType(node),
                Node = node,
            });
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

    /// <summary>載體算得出什麼型別：內嵌看公式型別，資產看資產型別，空節點無從得知。</summary>
    public static Type CarrierResultType(GraphNode node)
    {
        if (node == null) return null;
        if (node.Kind == NodeKind.Inline && node.BodyObject != null)
            return AGReflect.FormulaResultType(node.BodyObject.GetType());
        if (node.Kind == NodeKind.Asset && node.AssetObject != null)
            return AGReflect.AssetResultType(node.AssetObject);
        return null;
    }

    /// <summary>
    /// 標註一顆節點，或替既有標註改名。名稱在一張圖內全域唯一（不分結果型別）——
    /// 外部是用裸字串查的，同名不同型會讓「查到哪一個」變成隱式規則。
    /// </summary>
    public bool SetTokenName(GraphNode node, string key, IEnumerable<GraphNode> scope, out string error)
    {
        error = null;
        if (node == null) { error = "沒有可標註的節點。"; return false; }
        if (CarrierResultType(node) == null) { error = "只有可求值的公式或公式資產節點能標註。"; return false; }
        if (string.IsNullOrWhiteSpace(key)) { error = "名稱不可為空。"; return false; }
        key = key.Trim();
        if (key == node.TokenName) return true;

        foreach (var other in scope ?? AllCarriers())
        {
            if (ReferenceEquals(other, node) || !other.IsToken) continue;
            if (other.TokenName != key) continue;
            error = $"已存在名為 '{key}' 的標註。";
            return false;
        }

        node.SetTokenName(key);
        MarkDirty();
        return true;
    }

    public bool SetTokenName(GraphNode node, string key, out string error)
        => SetTokenName(node, key, AllCarriers(), out error);

    /// <summary>取消標註。節點與它的子樹留在原地，只是不再是對外端點。</summary>
    public void ClearTokenName(GraphNode node)
    {
        if (node == null || !node.IsToken) return;
        node.SetTokenName(null);
        MarkDirty();
    }

    /// <summary>這顆標註節點在圖內被幾個參數欄位接著。0＝純對外端點，不是錯誤。</summary>
    public int CountReferences(AGToken token)
    {
        if (token?.Node == null) return 0;
        int n = 0;
        foreach (var slot in AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), token.Node)) n++;
        return n;
    }

    // ===== 候選節點 =====
    // 候選池屬於「目前焦點的頭端」（動作頭端 / Token 頭端 / 資產），不再有全系統共用的一池 + FocusId 歸屬。

    /// <summary>目前焦點的頭端物件，由視窗切焦點時指定。</summary>
    public object OrphanHead { get; set; }

    public List<GraphNode> Orphans => AGReflect.Orphans(OrphanHead);

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
                if (AGReflect.Orphans(head)?.Remove(node) == true) break;
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
        return AGReflect.GetHeadPos(carrier, out pos);
    }

    public void SetPosition(string nodeId, Vector2 pos)
    {
        var carrier = Carrier(nodeId);
        if (carrier == null) return;
        if (TryGetPosition(nodeId, out var current) && current == pos) return;

        if (carrier is GraphNode node) node.Pos = pos;
        else AGReflect.SetHeadPos(carrier, pos);
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
        else AGReflect.ClearHeadPos(carrier);
        MarkDirty();
    }

    // ===== 全圖走訪 =====

    /// <summary>走訪整份工作副本裡的所有 FormulaSlot（動作、Token、未連接節點）。不下沉到 Asset 內部。</summary>
    public IEnumerable<object> AllFormulaSlots()
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var root in Roots())
            foreach (var slot in WalkSlots(root, visited))
                if (slot is FormulaSlotBase) yield return slot;
    }

    /// <summary>走訪整份工作副本裡的所有 Slot（含 ActionSlot）。</summary>
    public IEnumerable<object> AllSlots()
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var root in Roots())
            foreach (var slot in WalkSlots(root, visited))
                yield return slot;
    }

    private IEnumerable<object> Roots()
    {
        foreach (var g in ReadGroups())
        {
            if (g.Actions == null) continue;
            foreach (var a in g.Actions)
                if (a != null) yield return a;
        }
        foreach (var node in AllOrphanNodes())
            yield return node;
    }

    /// <summary>
    /// 所有候選池掛點：時機畫布本身（ActionSystem）與時機群組裡的動作欄位。
    /// 動作頭端上那份只為了讀回合併畫布之前存下來的候選。
    /// </summary>
    public IEnumerable<object> Heads()
    {
        if (Data != null) yield return Data;
        foreach (var g in ReadGroups())
        {
            if (g.Actions == null) continue;
            foreach (var a in g.Actions)
                if (a != null) yield return a;
        }
    }

    /// <summary>所有頭端的候選節點。</summary>
    public IEnumerable<GraphNode> AllOrphanNodes()
    {
        foreach (var head in Heads())
        {
            var list = AGReflect.Orphans(head);
            if (list == null) continue;
            foreach (var node in list)
                if (node != null) yield return node;
        }
    }

    /// <summary>走訪任意一份 ActionSystem 的所有 Slot（重建資產引用清單用，對象不是工作副本）。</summary>
    public static IEnumerable<object> SlotsOfSystem(object system)
    {
        if (system == null) yield break;
        var visited = new HashSet<object>(AGRefComparer.Instance);

        if (AGReflect.Get(system, "ActionGroups") is IList groups)
        {
            foreach (var g in groups)
            {
                if (g == null) continue;
                if (AGReflect.Get(g, "Actions") is not IList actions) continue;
                foreach (var a in actions)
                {
                    if (a == null) continue;
                    foreach (var s in WalkSlots(a, visited)) yield return s;
                }
            }
        }

        // 標註節點多半沒有連入線、住在候選池裡，但它是正式資料（對外端點），資產引用要算它一份。
        if (AGReflect.Get(system, "Orphans") is not IList orphans) yield break;
        foreach (var o in orphans)
        {
            if (o is not GraphNode node || !node.IsToken) continue;
            if (node.Kind != NodeKind.Inline || node.BodyObject == null) continue;
            foreach (var s in WalkSlots(node.BodyObject, visited)) yield return s;
        }
    }

    /// <summary>複製節點後清掉整棵樹的識別碼，避免新舊節點共用座標記錄。</summary>
    public static void ResetNodeIds(object root)
    {
        var visited = new HashSet<object>(AGRefComparer.Instance);
        ResetNodeIdsInternal(root, visited);
    }

    private static void ResetNodeIdsInternal(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) return;
        if (node is GraphNode carrier) carrier.ResetId();
        else if (AGReflect.IsActionSlotType(node.GetType())) AGReflect.ResetSlotEditorId(node);

        foreach (var f in AGReflect.Fields(node.GetType()))
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
    public static IEnumerable<object> WalkSlots(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) yield break;

        var type = node.GetType();
        if (AGReflect.IsSlotType(type))
        {
            yield return node;
            var carrier = AGReflect.GetNode(node);
            if (carrier != null)
                foreach (var s in WalkSlots(carrier, visited)) yield return s;
            yield break;
        }

        foreach (var f in AGReflect.Fields(type))
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
    /// 正式資料引用的資產：動作執行樹，以及每個候選池中被標註的端點子樹。
    /// 未標註候選只是編輯暫存，不得污染 subscriber。
    /// </summary>
    public static HashSet<ScriptableObject> ReferencedAssetsOfSystem(object system)
    {
        var result = new HashSet<ScriptableObject>();
        if (system == null) return result;

        var visited = new HashSet<object>(AGRefComparer.Instance);
        if (AGReflect.Get(system, "ActionGroups") is IList groups)
        {
            foreach (var group in groups)
            {
                if (group == null || AGReflect.Get(group, "Actions") is not IList actions) continue;
                foreach (var action in actions) CollectFormalAssets(action, visited, result);
            }
        }

        CollectMarkedOrphanAssets(AGReflect.Orphans(system), visited, result);
        foreach (var slot in SlotsOfSystem(system))
        {
            if (!AGReflect.IsActionSlotType(slot.GetType())) continue;
            CollectMarkedOrphanAssets(AGReflect.Orphans(slot), visited, result);
        }
        return result;
    }

    private static void CollectMarkedOrphanAssets(IEnumerable<GraphNode> orphans,
        HashSet<object> visited, HashSet<ScriptableObject> result)
    {
        if (orphans == null) return;
        foreach (var orphan in orphans)
            if (orphan?.IsToken == true) CollectFormalAssets(orphan, visited, result);
    }

    private static void CollectFormalAssets(object node, HashSet<object> visited, HashSet<ScriptableObject> result)
    {
        if (node == null || !visited.Add(node)) return;

        if (AGReflect.IsSlotType(node.GetType()))
        {
            CollectFormalAssets(AGReflect.GetNode(node), visited, result);
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

        foreach (var field in AGReflect.Fields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            CollectFormalAssets(field.GetValue(node), visited, result);
        }
    }

    private static IEnumerable<GraphNode> WalkCarriers(object node, HashSet<object> visited)
    {
        if (node == null || !visited.Add(node)) yield break;

        if (AGReflect.IsSlotType(node.GetType()))
        {
            var carrier = AGReflect.GetNode(node);
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

        foreach (var field in AGReflect.Fields(type))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            foreach (var found in WalkCarriers(field.GetValue(node), visited)) yield return found;
        }
    }
}

/// <summary>依參考位址比對的集合比較器：走訪節點圖時避免值相等造成誤判。</summary>
public sealed class AGRefComparer : IEqualityComparer<object>
{
    public static readonly AGRefComparer Instance = new();
    public new bool Equals(object a, object b) => ReferenceEquals(a, b);
    public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
}

}
