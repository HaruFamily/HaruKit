using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public enum PipelineStepStatus { Success, Skipped, Partial, Failed, NotRun }
    public enum PipelineItemStatus { Created, Modified, Collected, Skipped, Failed }
    public enum PipelineTransactionStatus { NotStarted, Committed, RolledBack, RecoveryRequired }

    /// <summary>記錄當時的名稱與路徑，回復刪除新資產後仍能閱讀結果。</summary>
    public sealed class PipelineAssetRecord
    {
        private readonly GlobalObjectId identity;
        private readonly bool hasIdentity;
        public Object Asset { get; }
        public string Path { get; }
        public string Name { get; }
        public string TypeName { get; }
        public PipelineItemStatus Status { get; }
        public string Message { get; }

        public PipelineAssetRecord(Object asset, string path, PipelineItemStatus status, string message)
        {
            Asset = asset;
            hasIdentity = asset != null && AssetDatabase.Contains(asset);
            if (hasIdentity) identity = GlobalObjectId.GetGlobalObjectIdSlow(asset);
            Path = path ?? (asset != null ? AssetDatabase.GetAssetPath(asset) : "");
            Name = asset != null ? asset.name : System.IO.Path.GetFileName(Path);
            TypeName = asset != null ? asset.GetType().Name : "";
            Status = status;
            Message = message ?? "";
        }

        public Object Resolve() => Asset != null ? Asset
            : hasIdentity ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(identity) : null;
    }

    public sealed class PipelineActionResult
    {
        private readonly List<PipelineAssetRecord> items = new();
        private readonly List<string> messages = new();
        public string NodeId { get; }
        public string Name { get; }
        public int Index { get; }
        public double Seconds { get; internal set; }
        public bool WasSkipped { get; internal set; }
        public bool WasNotRun { get; internal set; }
        public int ErrorCount { get; private set; }
        public IReadOnlyList<PipelineAssetRecord> Items => items;
        public IReadOnlyList<string> Messages => messages;
        public int Count(PipelineItemStatus status) => items.FindAll(item => item.Status == status).Count;
        public PipelineStepStatus Status => WasNotRun ? PipelineStepStatus.NotRun
            : ErrorCount > 0 ? HasCompletedItems ? PipelineStepStatus.Partial : PipelineStepStatus.Failed
            : WasSkipped && !HasCompletedItems ? PipelineStepStatus.Skipped : PipelineStepStatus.Success;
        private bool HasCompletedItems => items.Exists(item => item.Status is PipelineItemStatus.Created
            or PipelineItemStatus.Modified or PipelineItemStatus.Collected);
        public bool HasFailure => ErrorCount > 0;

        public PipelineActionResult(int index, string nodeId, string name)
        {
            Index = index;
            NodeId = nodeId;
            Name = name;
        }

        public void Record(PipelineItemStatus status, Object asset = null, string path = null, string message = null)
        {
            items.Add(new PipelineAssetRecord(asset, path, status, message));
            if (status == PipelineItemStatus.Failed) ErrorCount++;
        }

        public void Message(string message) { if (!string.IsNullOrEmpty(message)) messages.Add(message); }
        public void Fail(string message, string path = null)
        {
            if (!string.IsNullOrEmpty(path)) Record(PipelineItemStatus.Failed, path: path, message: message);
            else { ErrorCount++; Message(message); }
        }
    }

    public sealed class PipelineRunResult
    {
        public DateTime StartedAt { get; } = DateTime.Now;
        public List<PipelineActionResult> Steps { get; } = new();
        public List<string> Errors { get; } = new();
        public List<PipelineCatalogSnapshot> Catalogs { get; } = new();
        public PipelineTransactionStatus Transaction { get; internal set; }
        public string RecoveryDirectory { get; internal set; }
        internal Func<List<string>> RestoreCatalogs;
        public string Summary => $"交易：{Transaction}；動作成功 {Count(PipelineStepStatus.Success)}，跳過 {Count(PipelineStepStatus.Skipped)}，"
            + $"部分完成 {Count(PipelineStepStatus.Partial)}，失敗 {Count(PipelineStepStatus.Failed)}，未執行 {Count(PipelineStepStatus.NotRun)}。"
            + $"\n資產操作：新增 {ItemCount(PipelineItemStatus.Created)}，修改 {ItemCount(PipelineItemStatus.Modified)}，"
            + $"收集 {ItemCount(PipelineItemStatus.Collected)}，跳過 {ItemCount(PipelineItemStatus.Skipped)}，失敗 {ItemCount(PipelineItemStatus.Failed)}。"
            + (Transaction == PipelineTransactionStatus.RolledBack ? "\n以上為嘗試執行的結果；本次資產寫入已回復。" : "");
        private int Count(PipelineStepStatus status) => Steps.FindAll(step => step.Status == status).Count;
        private int ItemCount(PipelineItemStatus status)
        {
            int count = 0;
            foreach (var step in Steps) count += step.Count(status);
            return count;
        }
    }

    /// <summary>Action 只寫正向操作；資產寫入與回復由共用交易承接。</summary>
    public sealed class PipelineActionContext
    {
        public PipelineAssetWriter Assets { get; }
        public PipelineActionResult Result { get; }
        private readonly Dictionary<CatalogCell, PipelineValueSnapshot> observations;

        internal PipelineActionContext(PipelineAssetTransaction transaction, PipelineActionResult result,
            Dictionary<CatalogCell, PipelineValueSnapshot> observations)
        {
            Result = result;
            Assets = new PipelineAssetWriter(transaction, result);
            this.observations = observations;
        }

        public void Fail(string message, string path = null) => Result.Fail(message, path);
        public void Skip(string message) { Result.WasSkipped = true; Result.Message(message); }
        internal void Observe(CatalogCell cell, object value) => observations[cell] = PipelineValueSnapshot.Capture(value);
    }
}
