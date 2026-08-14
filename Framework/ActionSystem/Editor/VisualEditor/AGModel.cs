namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>一筆 Token 的可編輯視圖：Key、結果型別、所屬清單與索引。</summary>
public class AGToken
{
    public string Key;
    public Type ResultType;
    public object Entry;        // ITokenEntry 實例
    public object Slot;         // entry.Slot（FormulaSlotBase）
    public IList List;          // 所屬的 List<XEntry>
    public int Index;

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
    public object Data { get; private set; }        // ActionSystem<TTiming, TPack, TTokenEntryPack> 工作副本
    public Type TimingType { get; private set; }
    public Type PackType { get; private set; }
    public bool Dirty { get; private set; }

    private FieldInfo systemField;                    // Owner 上放 ActionSystem 的欄位

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
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ActionSystem<,,>)) return f;
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

    // ===== Token =====

    public object TokenPack => AGReflect.Get(Data, "TokenEntry");

    /// <summary>列出所有 kind 的 Token（依 TokenEntryPack 上的 List 欄位順序）。</summary>
    public List<AGToken> ReadTokens()
    {
        var result = new List<AGToken>();
        var pack = TokenPack;
        if (pack == null) return result;

        foreach (var f in AGReflect.Fields(pack.GetType()))
        {
            if (!AGReflect.IsList(f.FieldType, out var elem)) continue;
            if (!typeof(ITokenEntry).IsAssignableFrom(elem)) continue;

            var list = f.GetValue(pack) as IList;
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not ITokenEntry entry) continue;
                var slot = entry.Slot;
                result.Add(new AGToken
                {
                    Key = entry.Key,
                    ResultType = slot != null ? AGReflect.ResultType(slot.GetType()) : null,
                    Entry = entry,
                    Slot = slot,
                    List = list,
                    Index = i,
                });
            }
        }
        return result;
    }

    /// <summary>可新增的 Token 型別：(結果型別, 所屬清單)。</summary>
    public List<(Type resultType, IList list)> TokenKinds()
    {
        var kinds = new List<(Type, IList)>();
        var pack = TokenPack;
        if (pack == null) return kinds;

        foreach (var f in AGReflect.Fields(pack.GetType()))
        {
            if (!AGReflect.IsList(f.FieldType, out var elem)) continue;
            if (!typeof(ITokenEntry).IsAssignableFrom(elem)) continue;

            var list = AGReflect.EnsureList(pack, f);
            var probe = AGReflect.CreateInstance(elem) as ITokenEntry;
            var rt = probe?.Slot != null ? AGReflect.ResultType(probe.Slot.GetType()) : null;
            if (rt == null) continue;
            kinds.Add((rt, list));
        }
        return kinds;
    }

    /// <summary>新增 Token。名稱重複回 false。</summary>
    public bool AddToken(Type resultType, string key, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(key)) { error = "名稱不可為空。"; return false; }
        key = key.Trim();

        foreach (var t in ReadTokens())
            if (t.Key == key) { error = $"已存在名為 '{key}' 的 Token。"; return false; }

        foreach (var (rt, list) in TokenKinds())
        {
            if (rt != resultType) continue;
            var elem = list.GetType().GetGenericArguments()[0];
            if (AGReflect.CreateInstance(elem) is not ITokenEntry entry) { error = $"建立 {elem.Name} 失敗。"; return false; }
            entry.Key = key;
            list.Add(entry);
            MarkDirty();
            return true;
        }
        error = $"找不到 {AGReflect.ResultTypeName(resultType)} 的 Token 清單。";
        return false;
    }

    /// <summary>改名並同步所有引用處。</summary>
    public bool RenameToken(AGToken token, string newKey, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(newKey)) { error = "名稱不可為空。"; return false; }
        newKey = newKey.Trim();
        if (newKey == token.Key) return true;

        foreach (var t in ReadTokens())
            if (t.Key == newKey) { error = $"已存在名為 '{newKey}' 的 Token。"; return false; }

        // 節點的座標與焦點歸屬跟著載體走，改名只要換掉引用它的節點內容。
        string oldKey = token.Key;
        foreach (var slot in AllFormulaSlots())
        {
            if (AGReflect.UseType(slot) != 3) continue;
            if (AGReflect.GetTokenKey(slot) != oldKey) continue;
            if (AGReflect.ResultType(slot.GetType()) != token.ResultType) continue;
            AGReflect.SetTokenKey(slot, newKey);
        }
        foreach (var node in AllOrphanNodes())
            if (node.Kind == NodeKind.Token && node.TokenKey == oldKey) node.SetToken(newKey);

        if (token.Entry is ITokenEntry entry) entry.Key = newKey;
        token.Key = newKey;
        MarkDirty();
        return true;
    }

    public void RemoveToken(AGToken token)
    {
        // 候選池裡引用這個變數的獨立節點一起移除；仍接在欄位上的引用留著，由驗證報「引用不存在的變數」。
        foreach (var head in Heads())
        {
            var list = AGReflect.Orphans(head);
            if (list == null) continue;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] != null && list[i].Kind == NodeKind.Token && list[i].TokenKey == token.Key)
                    list.RemoveAt(i);
        }
        token.List?.Remove(token.Entry);
        MarkDirty();
    }

    /// <summary>某 Token 被幾個參數欄位引用。</summary>
    public int CountReferences(AGToken token)
    {
        int n = 0;
        foreach (var slot in AllFormulaSlots())
        {
            if (AGReflect.UseType(slot) != 3) continue;
            if (AGReflect.GetTokenKey(slot) != token.Key) continue;
            if (AGReflect.ResultType(slot.GetType()) != token.ResultType) continue;
            n++;
        }
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
        Orphans?.Remove(node);
        MarkDirty();
    }

    // ===== 座標記憶 =====
    // 座標與備註住在載體（GraphNode）與頭端上。建圖時登記 id → 載體，讓視窗仍可用 nodeId 讀寫。

    private readonly Dictionary<string, object> carriers = new();

    public void ClearCarriers() => carriers.Clear();

    /// <summary>建圖時登記一個節點的載體：GraphNode，或頭端（ActionSlot / TokenEntryBase）。</summary>
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
        foreach (var t in ReadTokens())
            if (t.Slot != null) yield return t.Slot;

        foreach (var node in AllOrphanNodes())
            if (node.Kind == NodeKind.Inline && node.BodyObject != null) yield return node.BodyObject;
    }

    /// <summary>所有頭端：時機群組裡的動作欄位，以及每個 Token 宣告。候選池掛在它們身上。</summary>
    public IEnumerable<object> Heads()
    {
        foreach (var g in ReadGroups())
        {
            if (g.Actions == null) continue;
            foreach (var a in g.Actions)
                if (a != null) yield return a;
        }
        foreach (var t in ReadTokens())
            if (t.Entry != null) yield return t.Entry;
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

        var pack = AGReflect.Get(system, "TokenEntry");
        if (pack == null) yield break;
        foreach (var f in AGReflect.Fields(pack.GetType()))
        {
            if (!AGReflect.IsList(f.FieldType, out var elem)) continue;
            if (!typeof(ITokenEntry).IsAssignableFrom(elem)) continue;
            if (f.GetValue(pack) is not IList list) continue;
            foreach (var e in list)
            {
                if (e is not ITokenEntry entry || entry.Slot == null) continue;
                foreach (var s in WalkSlots(entry.Slot, visited)) yield return s;
            }
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
            var inner = AGReflect.GetFormula(node);
            if (inner != null)
                foreach (var s in WalkSlots(inner, visited)) yield return s;
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
}

/// <summary>依參考位址比對的集合比較器：走訪節點圖時避免值相等造成誤判。</summary>
public sealed class AGRefComparer : IEqualityComparer<object>
{
    public static readonly AGRefComparer Instance = new();
    public new bool Equals(object a, object b) => ReferenceEquals(a, b);
    public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
}

}
