namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
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

    public GraphDiagnostic Check()
    {
        try
        {
            if (owner == null) return Failure("owner-missing", "Owner 已不存在，提交已停止。");
            if (recoveryRequired) return Failure("recovery-required", "上次寫入無法確認已回復，提交已停止；請先檢查 Owner，再重新載入文件。");
            if (SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                return new GraphDiagnostic("graphkit.serialize-reference.missing-type", GraphDiagnosticSeverity.Error,
                    "Owner 含有遺失型別的 SerializeReference，提交已停止以保留原資料。",
                    new GraphDiagnosticLocation(documentId: binding.DocumentId),
                    "恢復遺失的程式型別後重新開啟；不要直接覆寫資產。");
            binding.TryRead(owner, out var current);
            if (!ReferenceEquals(current, baseline) || !string.Equals(binding.ReadRevision(owner), revision, StringComparison.Ordinal))
                return Failure("owner-changed", "Owner 的文件已被其他入口修改，提交已停止；工作副本保留。請保留所需修改後取消並重新載入。");
            return null;
        }
        catch (Exception exception)
        {
            return Failure("check-failed", "無法確認 Owner 狀態，提交已停止：" + exception.Message);
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
