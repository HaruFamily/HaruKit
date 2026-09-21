namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.IO;
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
                    "可恢復原型別；或修正空節點後在圖視窗按存檔，確認備份並放棄遺失內容。");
            return null;
        }
        catch (Exception exception)
        {
            return Failure("check-failed", "無法確認 Owner 狀態，提交已停止：" + exception.Message);
        }
    }

    /// <summary>使用者明確同意後，先備份再清除 Owner 遺失型別；不重載編輯中的工作副本。</summary>
    internal bool TryRecoverMissingTypes(out string backupDirectory, out GraphDiagnostic diagnostic)
    {
        backupDirectory = null;
        diagnostic = Check(true);
        if (diagnostic != null) return false;
        bool clearing = false;
        try
        {
            if (!SerializationUtility.HasManagedReferencesWithMissingTypes(owner)) return true;
            string path = AssetDatabase.GetAssetPath(owner);
            if (owner is not UnityEngine.ScriptableObject || !AssetDatabase.IsMainAsset(owner)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("圖內備份修復只支援 Assets 內已儲存的主 .asset。");
            string source = Path.GetFullPath(path);
            if (!File.Exists(source) || !File.Exists(source + ".meta"))
                throw new IOException("原 .asset 或 .meta 不存在，無法完整備份；原資料未清除。");
            backupDirectory = Path.GetFullPath(Path.Combine("Library", "GraphKitMissingTypes",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, Path.GetFileName(source));
            File.Copy(source, backup);
            File.Copy(source + ".meta", backup + ".meta");

            diagnostic = Check(true);
            if (diagnostic != null) return false;
            clearing = true;
            if (!SerializationUtility.ClearAllManagedReferencesWithMissingTypes(owner)
                || SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                throw new InvalidOperationException("Unity 未完成清除。");
            // Unity 清除 managed reference 時可能重建 Owner 的資料；只採納這次明確修復造成的基準。
            if (!binding.TryRead(owner, out var current))
                throw new InvalidOperationException("清除後無法重新取得 Owner 文件。");
            baseline = current;
            revision = binding.ReadRevision(owner);
            return true;
        }
        catch (Exception exception)
        {
            if (clearing) recoveryRequired = true;
            diagnostic = Failure("missing-type-recovery-failed", "遺失型別修復已停止，工作副本保留："
                + exception.Message + " 備份：" + (backupDirectory ?? "尚未建立"));
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
            _ => HGSessionCommandResult.WriteFailed,
        };
}
}
