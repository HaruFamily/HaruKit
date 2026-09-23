namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>Owner-side checks shared by window and non-window document commits.</summary>
internal sealed class HGDocumentCommitGuard
{
    private readonly Object owner;
    private readonly HGDocumentBinding binding;
    private IGraphDocument baseline;
    private string revision;
    private bool recoveryRequired;

    public HGDocumentCommitGuard(Object owner, HGDocumentBinding binding, IGraphDocument live)
    {
        this.owner = owner;
        this.binding = binding;
        baseline = live;
        revision = binding.ReadRevision(owner);
    }

    public GraphDiagnostic Check() => Check(false);

    internal GraphDiagnostic Check(bool allowMissingTypes)
    {
        try
        {
            if (owner == null) return Failure("owner-missing", "Owner 已不存在，提交已停止。");
            if (recoveryRequired) return Failure("recovery-required", "上次寫入無法確認已回復，提交已停止；請先檢查 Owner，再重新載入文件。");
            binding.TryRead(owner, out var current);
            if (!ReferenceEquals(current, baseline) || !string.Equals(binding.ReadRevision(owner), revision, StringComparison.Ordinal))
                return Failure("owner-changed", "Owner 的文件已被其他入口修改，提交已停止；工作副本保留。請保留所需修改後取消並重新載入。");
            if (!allowMissingTypes && SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                return new GraphDiagnostic("graphkit.serialize-reference.missing-type", GraphDiagnosticSeverity.Error,
                    "Owner 含有遺失型別的 SerializeReference，提交已停止以保留原資料。",
                    new GraphDiagnosticLocation(documentId: binding.DocumentId),
                    "請重新開啟圖以清理遺失資料；清理後仍須通過正常驗證才能存檔。");
            return null;
        }
        catch (Exception exception)
        {
            return Failure("check-failed", "無法確認 Owner 狀態，提交已停止：" + exception.Message);
        }
    }

    /// <summary>定位並清除遺失型別記錄，清理其節點與引用；不放寬後續驗證。</summary>
    internal bool TryCleanMissingTypes(IGraphDocument working, out bool changed, out GraphDiagnostic diagnostic)
    {
        changed = false;
        diagnostic = null;
        bool clearing = false;
        try
        {
            if (owner == null) { diagnostic = Check(true); return false; }
            bool hasMissingTypes = SerializationUtility.HasManagedReferencesWithMissingTypes(owner);
            if (!hasMissingTypes && !HGMissingTypeCleanup.HasLostContent(baseline)) return true;
            diagnostic = Check(true);
            if (diagnostic != null) return false;
            var cleanup = HGMissingTypeCleanup.Capture(owner, baseline);
            clearing = true;
            if (hasMissingTypes)
            {
                SerializationUtility.ClearAllManagedReferencesWithMissingTypes(owner);
                if (SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                    throw new InvalidOperationException("Unity 未完成清除。");
            }
            // Unity 清除 managed reference 時可能重建 Owner 的資料；只採納這次明確修復造成的基準。
            if (!binding.TryRead(owner, out var current) && baseline != null)
                throw new InvalidOperationException("清除後無法重新取得 Owner 文件。");
            cleanup.Apply(owner, current, working);
            baseline = current;
            baseline?.InvalidateValidation();
            working?.InvalidateValidation();
            revision = binding.ReadRevision(owner);
            EditorUtility.SetDirty(owner);
            changed = true;
            return true;
        }
        catch (Exception exception)
        {
            if (clearing) recoveryRequired = true;
            diagnostic = Failure("missing-type-recovery-failed", "遺失型別修復已停止，工作副本保留："
                + exception.Message);
            return false;
        }
    }

    /// <summary>Writes one document reference. A setter must not mutate the previous document or unrelated Owner data.</summary>
    public bool TryWrite(IGraphDocument document, out GraphDiagnostic diagnostic)
    {
        diagnostic = Check();
        if (diagnostic != null) return false;
        try
        {
            if (!binding.TryWrite(owner, document)) throw new InvalidOperationException("Binding rejected the document.");
            binding.TryRead(owner, out var stored);
            if (!ReferenceEquals(stored, document)) throw new InvalidOperationException("Binding did not retain the submitted document reference.");
            string storedRevision = binding.ReadRevision(owner);
            baseline = stored;
            revision = storedRevision;
            return true;
        }
        catch (Exception exception)
        {
            if (TryRestore())
                diagnostic = Failure("write-failed", "寫入失敗，指定文件已回復；工作副本與歷程保留：" + exception.Message);
            else
            {
                recoveryRequired = true;
                diagnostic = Failure("recovery-required", "寫入失敗且無法確認指定文件已回復，已停止後續提交。請先檢查 Owner，再重新載入：" + exception.Message);
            }
            return false;
        }
    }

    private bool TryRestore()
    {
        try
        {
            if (owner == null) return false;
            binding.TryRead(owner, out var current);
            if (!ReferenceEquals(current, baseline))
            {
                // A setter may throw after assigning. Read back even when the recovery setter throws.
                try { binding.TryRestore(owner, baseline); }
                catch (Exception) { }
                binding.TryRead(owner, out current);
            }
            if (!ReferenceEquals(current, baseline)) return false;
            revision = binding.ReadRevision(owner);
            return true;
        }
        catch (Exception) { return false; }
    }

    private GraphDiagnostic Failure(string operation, string message)
        => new GraphDiagnostic("graphkit.commit." + operation, GraphDiagnosticSeverity.Error, message,
            new GraphDiagnosticLocation(documentId: binding.DocumentId));

    internal static HGSessionCommandResult ResultOf(GraphDiagnostic diagnostic)
        => diagnostic?.Code switch
        {
            "graphkit.commit.owner-changed" => HGSessionCommandResult.Conflict,
            "graphkit.serialize-reference.missing-type" => HGSessionCommandResult.ValidationFailed,
            "graphkit.commit.validation-failed" => HGSessionCommandResult.ValidationFailed,
            _ => HGSessionCommandResult.WriteFailed,
        };
}

/// <summary>處理 missing reference 與來源標記尚在但內容已遺失的載體；不清除正常 Empty 節點。</summary>
internal sealed class HGMissingTypeCleanup
{
    private readonly HashSet<GraphNode> broken = new(HGRefComparer.Instance);
    private readonly HashSet<string> brokenIds = new();
    private readonly HashSet<string> brokenPaths = new();
    private readonly HashSet<GraphNode> affectedCells = new(HGRefComparer.Instance);
    private readonly HashSet<string> affectedCellIds = new();
    private readonly HashSet<string> affectedCellPaths = new();
    private readonly HashSet<GraphSlotBase> brokenSlots = new(HGRefComparer.Instance);
    private readonly HashSet<string> brokenSlotPaths = new();
    private readonly HashSet<GraphToken> brokenTokens = new(HGRefComparer.Instance);
    private readonly HashSet<string> brokenTokenIds = new();
    private readonly HashSet<string> brokenTokenPaths = new();
    private readonly Dictionary<string, List<int>> missingItems = new();

    // 公開 SetBody/SetToken 在傳入 null 時皆切成 Empty；以下是序列化遺失殘留，並非編輯中的空節點。
    private static bool HasLostContent(GraphNode node)
        => (node.Kind == NodeKind.Inline && node.BodyObject == null)
            || (node.Kind == NodeKind.Token && node.Token == null);

    internal static bool HasLostContent(IGraphDocument document)
    {
        var objects = new List<object>();
        Collect(document, objects, new HashSet<object>(HGRefComparer.Instance));
        foreach (var value in objects)
            if (value is GraphNode node && HasLostContent(node)) return true;
        return false;
    }

    internal static HGMissingTypeCleanup Capture(Object owner, IGraphDocument live)
    {
        var cleanup = new HGMissingTypeCleanup();
        var missing = new HashSet<long>();
        foreach (var item in SerializationUtility.GetManagedReferencesWithMissingTypes(owner)) missing.Add(item.referenceId);
        var located = new HashSet<long>();
        var nodePaths = new Dictionary<GraphNode, string>(HGRefComparer.Instance);
        using (var serialized = new SerializedObject(owner))
        {
            var property = serialized.GetIterator();
            var seen = new HashSet<long>();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = true;
                if (property.propertyType != SerializedPropertyType.ManagedReference) continue;
                long id = property.managedReferenceId;
                enter = seen.Add(id);
                if (!missing.Contains(id))
                {
                    if (property.managedReferenceValue is GraphNode carrier) nodePaths[carrier] = property.propertyPath;
                    continue;
                }
                enter = false;
                located.Add(id);
                cleanup.CaptureLocation(owner, property.propertyPath);
            }
        }

        // 遺失引用的 SerializedProperty ID 可能退成 RefIdNull (-2)。
        // 只能用原序列化 rid 證明哪些 null 來自遺失型別，不能把普通空槽一併刪除。
        missing.ExceptWith(located);
        if (missing.Count > 0)
            foreach (string path in HGSerializedReferenceLocations.Read(owner, missing))
                if (TryResolvePath(owner, path, out object value, requireManagedReference: true) && value == null)
                    cleanup.CaptureLocation(owner, path);

        var objects = new List<object>();
        Collect(live, objects, new HashSet<object>(HGRefComparer.Instance));
        foreach (var value in objects)
            if (value is GraphNode node && HasLostContent(node))
            {
                cleanup.broken.Add(node);
                if (!string.IsNullOrEmpty(node.Id)) cleanup.brokenIds.Add(node.Id);
                if (nodePaths.TryGetValue(node, out string path)) cleanup.brokenPaths.Add(path);
            }
        foreach (var value in objects)
            if (value is GraphNode node && node.Token != null && cleanup.brokenTokens.Contains(node.Token))
            {
                cleanup.broken.Add(node);
                if (!string.IsNullOrEmpty(node.Id)) cleanup.brokenIds.Add(node.Id);
                if (nodePaths.TryGetValue(node, out string path)) cleanup.brokenPaths.Add(path);
            }
        return cleanup;
    }

    private void CaptureLocation(Object owner, string path)
    {
        int itemStart = path.LastIndexOf(".Array.data[", StringComparison.Ordinal);
        if (itemStart >= 0 && path.EndsWith("]", StringComparison.Ordinal)
            && int.TryParse(path.Substring(itemStart + 12, path.Length - itemStart - 13), out int index))
        {
            string listPath = path.Substring(0, itemStart);
            if (!missingItems.TryGetValue(listPath, out var indices)) missingItems[listPath] = indices = new List<int>();
            if (!indices.Contains(index)) indices.Add(index);
        }
        for (int dot = path.LastIndexOf('.'); dot >= 0; dot = path.LastIndexOf('.'))
        {
            path = path.Substring(0, dot);
            object parent = ResolvePath(owner, path);
            if (parent is GraphToken token)
            {
                brokenTokens.Add(token);
                brokenTokenPaths.Add(path);
                if (!string.IsNullOrEmpty(token.Id)) brokenTokenIds.Add(token.Id);
                break;
            }
            if (parent is GraphSlotBase slot)
            {
                brokenSlots.Add(slot);
                brokenSlotPaths.Add(path);
                break;
            }
            if (parent is not GraphNode node) continue;
            broken.Add(node);
            brokenPaths.Add(path);
            if (!string.IsNullOrEmpty(node.Id)) brokenIds.Add(node.Id);
            break;
        }
    }

    internal void Apply(Object owner, IGraphDocument live, IGraphDocument working)
    {
        // 清除 API 可能重建 managed objects；依清除前的序列化位置重新取得同一載體。
        foreach (string path in brokenPaths)
            if (ResolvePath(owner, path) is GraphNode node) broken.Add(node);
        foreach (string path in affectedCellPaths)
            if (ResolvePath(owner, path) is GraphNode node) affectedCells.Add(node);
        foreach (string path in brokenSlotPaths)
            if (ResolvePath(owner, path) is GraphSlotBase slot) brokenSlots.Add(slot);
        foreach (string path in brokenTokenPaths)
            if (ResolvePath(owner, path) is GraphToken token) brokenTokens.Add(token);
        foreach (var entry in missingItems)
        {
            if (ResolvePath(owner, entry.Key) is not IList list || list.IsReadOnly || list.IsFixedSize) continue;
            entry.Value.Sort();
            for (int i = entry.Value.Count - 1; i >= 0; i--)
            {
                int index = entry.Value[i];
                if (index < list.Count && list[index] == null) list.RemoveAt(index);
            }
        }
        var objects = new List<object>();
        Collect(live, objects, new HashSet<object>(HGRefComparer.Instance));
        if (working != null && !ReferenceEquals(live, working))
            Collect(working, objects, new HashSet<object>(HGRefComparer.Instance));

        bool IsBroken(GraphNode node) => node != null
            && (broken.Contains(node) || (!string.IsNullOrEmpty(node.Id) && brokenIds.Contains(node.Id)))
            && (broken.Contains(node) || (node.Kind == NodeKind.Inline && node.BodyObject == null)
                || (node.Kind == NodeKind.Token && node.Token?.Slot == null));

        // 先移除壞動作的清單項目，再斷線；否則清空 Slot 後會遺失它來自 missing class 的依據。
        foreach (var value in objects)
        {
            if (value is not IList list || list.IsReadOnly) continue;
            for (int i = list.Count - 1; i >= 0; i--)
                if ((list[i] is GraphNode node && IsBroken(node))
                    || (list[i] is ActionSlotBase action && (IsBroken(action.Node) || brokenSlots.Contains(action)))
                    || (list[i] is GraphToken token && (brokenTokens.Contains(token)
                        || (!string.IsNullOrEmpty(token.Id) && brokenTokenIds.Contains(token.Id) && token.Slot == null))))
                {
                    if (list.IsFixedSize) list[i] = null;
                    else list.RemoveAt(i);
                }
        }
        foreach (var value in objects)
        {
            if (value is IGraphNodeOwner nodeOwner)
                foreach (var node in broken) nodeOwner.RemoveChild(node);
            if (value is GraphSlotBase slot && (IsBroken(slot.Node) || brokenSlots.Contains(slot))) slot.SetNode(null);
        }
    }

    private static object ResolvePath(object root, string path)
        => TryResolvePath(root, path, out object value) ? value : null;

    private static bool TryResolvePath(object root, string path, out object value, bool requireManagedReference = false)
    {
        value = null;
        object current = root;
        bool managedField = false;
        const string registry = "managedReferences[";
        if (path.StartsWith(registry, StringComparison.Ordinal))
        {
            int end = path.IndexOf(']');
            if (root is not Object owner || end < registry.Length
                || !long.TryParse(path.Substring(registry.Length, end - registry.Length), out long id)) return false;
#if UNITY_2022_3_OR_NEWER
            current = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReference(owner, id);
#else
            current = SerializationUtility.GetManagedReference(owner, id);
#endif
            if (current == null) return false;
            path = path.Substring(end + 1).TrimStart('.');
            if (path.Length == 0) { value = current; return true; }
        }
        foreach (string part in path.Replace(".Array.data[", "[").Split('.'))
        {
            if (current == null) return false;
            int bracket = part.IndexOf('[');
            string name = bracket < 0 ? part : part.Substring(0, bracket);
            var field = HGReflect.Find(current.GetType(), name);
            if (field == null) return false;
            managedField = field.IsDefined(typeof(UnityEngine.SerializeReference), false);
            current = field.GetValue(current);
            if (bracket < 0) continue;
            if (!part.EndsWith("]", StringComparison.Ordinal)
                || !int.TryParse(part.Substring(bracket + 1, part.Length - bracket - 2), out int index)
                || current is not IList list || index < 0 || index >= list.Count) return false;
            current = list[index];
        }
        value = current;
        return !requireManagedReference || managedField;
    }

    private static void Collect(object value, List<object> objects, HashSet<object> visited)
    {
        if (value == null || value is Object || value is string || value is Type || value is Delegate) return;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || !visited.Add(value)) return;
        objects.Add(value);
        if (value is IList list)
        {
            foreach (var item in list) Collect(item, objects, visited);
            return;
        }
        foreach (var field in HGReflect.Fields(type))
        {
            if (field.IsNotSerialized || (!field.IsPublic
                && !field.IsDefined(typeof(UnityEngine.SerializeField), false)
                && !field.IsDefined(typeof(UnityEngine.SerializeReference), false))) continue;
            Collect(field.GetValue(value), objects, visited);
        }
    }
}
}
