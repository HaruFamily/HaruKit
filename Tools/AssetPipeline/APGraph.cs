using HaruFamily.Framework.LogicGraph;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 一條管線的步驟清單，在節點圖上是一顆節點。
    /// </summary>
    // 形狀對應 LogicGraph 的 ActionTimingGroup：它是 root，本體是一串頭端。
    // AssetPipeline 目前只有一條管線，所以整張圖只會有一顆 root。
    [Serializable]
    public class APStepGroup : IGraphHead
    {
        [SerializeReference]
        public List<APActionSlot> Steps = new List<APActionSlot>();

        [SerializeField, HideInInspector]
        private Vector2 _pos;

        [SerializeField, HideInInspector]
        private bool _hasPos;

        public Vector2 Pos
        {
            get => _pos;
            set { _pos = value; _hasPos = true; }
        }

        public bool HasPos => _hasPos;

        public void ClearPos() { _hasPos = false; _pos = Vector2.zero; }
    }

    /// <summary>
    /// AssetPipeline 的節點圖。掛在 <see cref="AssetPipeline"/> 上當序列化欄位，
    /// 編輯器靠 <see cref="IGraphDocument"/> 找到它，不必認識 AssetPipeline 任何型別。
    /// </summary>
    // 為什麼是欄位而不是讓 SO 自己實作 IGraphDocument：GraphKit 的 LGModel.FindSystemField
    // 找的是「型別實作 IGraphDocument 的欄位」，並對那個欄位 DeepCopy 出工作副本。
    // SO 本身是 UnityEngine.Object，深複製會原樣沿用，取消就救不回來了。
    [Serializable]
    public class APGraph : IGraphDocument
    {
        /// <summary>整張圖唯一的 root 識別值。編輯器只拿它做 Equals 比較與 ToString 顯示。</summary>
        public const string PipelineKey = "Pipeline";

        [SerializeReference]
        private List<APStepGroup> _roots = new List<APStepGroup>();

        [SerializeReference, HideInInspector]
        private List<GraphNode> _orphans = new List<GraphNode>();

        [SerializeReference]
        private List<GraphEndpoint> _endpoints = new List<GraphEndpoint>();

        [SerializeField, HideInInspector]
        private bool _validated;

        /// <summary>強型別存取，給 AssetPipeline 自己的執行與驗證用。</summary>
        public List<APStepGroup> Groups
        {
            get { _roots ??= new List<APStepGroup>(); return _roots; }
        }

        /// <summary>整張圖的步驟，依 root 順序展開。沒有 root 時回空清單。</summary>
        public List<APActionSlot> Steps
        {
            get
            {
                var result = new List<APActionSlot>();
                foreach (APStepGroup group in Groups)
                {
                    if (group?.Steps == null) continue;
                    foreach (APActionSlot slot in group.Steps)
                        if (slot != null) result.Add(slot);
                }
                return result;
            }
        }

        public List<GraphNode> Orphans
        {
            get { _orphans ??= new List<GraphNode>(); return _orphans; }
        }

        public List<GraphEndpoint> Endpoints
        {
            get { _endpoints ??= new List<GraphEndpoint>(); return _endpoints; }
        }

        public bool IsValidated => _validated;

        /// <summary>內容變動，撤銷已驗證狀態。改圖後一定要呼叫，否則 Run 會用過期的驗證結果。</summary>
        public void MarkDirty() => _validated = false;

        /// <summary>只可用於程式建立且已自行保證正確的圖。</summary>
        public void MarkValidated() => _validated = true;

        IList IGraphDocument.Roots => Groups;

        Type IGraphDocument.PackType => typeof(APPack);

        Type IGraphDocument.ItemSlotType => typeof(APActionSlot);

        // 只有一條管線，所以不看 owner，永遠回同一個識別值。
        IReadOnlyList<object> IGraphDocument.RootKeys(UnityEngine.Object owner) => new object[] { PipelineKey };

        object IGraphDocument.KeyOf(object root) => PipelineKey;

        string IGraphDocument.TitleOf(object root) => "管線";

        IList IGraphDocument.ItemsOf(object root) => (root as APStepGroup)?.Steps;

        object IGraphDocument.AddRoot(object key)
        {
            if (!PipelineKey.Equals(key)) return null;
            if (Groups.Count > 0) return Groups[0];

            var group = new APStepGroup();
            Groups.Add(group);
            return group;
        }

        void IGraphDocument.Verify() => Verify();

        object IGraphDocument.DeepCopy() => LogicGraphDeepCopy.Copy(this);

        /// <summary>
        /// 驗證整張圖並更新 <see cref="IsValidated"/>。錯誤記進 Console，
        /// 通過才把圖標成可執行。
        /// </summary>
        public void Verify()
        {
            List<string> errors = APGraphVerifier.Collect(this);
            _validated = errors.Count == 0;

            if (_validated) return;

            var report = new System.Text.StringBuilder("[AssetPipeline] 驗證未通過：");
            foreach (string error in errors) report.Append('\n').Append("  • ").Append(error);
            Debug.LogError(report.ToString());
        }
    }
}
