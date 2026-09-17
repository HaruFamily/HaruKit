using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>
    /// 一條管線的動作清單，在節點圖上是一顆節點。
    /// </summary>
    // 形狀對應 LogicGraph 的 ActionTimingGroup：它是 root，本體是一串頭端。
    // AssetPipeline 目前只有一條管線，所以整張圖只會有一顆 root。
    [Serializable]
    public class ActionGroup : IGraphHead
    {
        [SerializeReference]
        public List<ActionSlot> Actions = new List<ActionSlot>();

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
    // 為什麼是欄位而不是讓 SO 自己實作 IGraphDocument：GraphKit 的 HGModel.FindSystemField
    // 找的是「型別實作 IGraphDocument 的欄位」，並對那個欄位 DeepCopy 出工作副本。
    // SO 本身是 UnityEngine.Object，深複製會原樣沿用，取消就救不回來了。
    [Serializable]
    public class Graph : IGraphDocument
    {
        /// <summary>整張圖唯一的 root 識別值。編輯器只拿它做 Equals 比較與 ToString 顯示。</summary>
        public const string PipelineKey = "Pipeline";

        [SerializeReference]
        private List<ActionGroup> _roots = new List<ActionGroup>();

        [SerializeReference, HideInInspector]
        private List<GraphNode> _orphans = new List<GraphNode>();

        [SerializeReference]
        private List<GraphToken> _endpoints = new List<GraphToken>();

        [SerializeField, HideInInspector]
        private bool _validated;

        /// <summary>強型別存取，給 AssetPipeline 自己的執行與驗證用。</summary>
        public List<ActionGroup> Groups
        {
            get { _roots ??= new List<ActionGroup>(); return _roots; }
        }

        /// <summary>整張圖的動作，依 root 順序展開。沒有 root 時回空清單。</summary>
        public List<ActionSlot> Actions
        {
            get
            {
                var result = new List<ActionSlot>();
                foreach (ActionGroup group in Groups)
                {
                    if (group?.Actions == null) continue;
                    foreach (ActionSlot slot in group.Actions)
                        if (slot != null) result.Add(slot);
                }
                return result;
            }
        }

        public List<GraphNode> Orphans
        {
            get { _orphans ??= new List<GraphNode>(); return _orphans; }
        }

        public List<GraphToken> Tokens
        {
            get { _endpoints ??= new List<GraphToken>(); return _endpoints; }
        }

        public bool IsValidated => _validated;

        /// <summary>內容變動，撤銷已驗證狀態。改圖後一定要呼叫，否則 Run 會用過期的驗證結果。</summary>
        public void MarkDirty() => _validated = false;

        /// <summary>只可用於程式建立且已自行保證正確的圖。</summary>
        public void MarkValidated() => _validated = true;

        IList IGraphDocument.Roots => Groups;

        Type IGraphDocument.PackType => typeof(NullPack);

        Type IGraphDocument.ItemSlotType => typeof(ActionSlot);

        // 只有一條管線，所以不看 owner，永遠回同一個識別值。
        IReadOnlyList<object> IGraphDocument.RootKeys(UnityEngine.Object owner) => new object[] { PipelineKey };

        object IGraphDocument.KeyOf(object root) => PipelineKey;

        string IGraphDocument.TitleOf(object root) => "管線";

        string IGraphDocument.RootChip => "動作清單";

        string IGraphDocument.RootNoun => "管線";

        string IGraphDocument.WindowTitle => "AssetPipelineGraph";

        // 只宣告目錄：Slot 的 AssetBaseType 是 null、AcceptsAsset 永遠 false，管線的欄位接不到共用資產；
        // Token 則是管線用不到——動作欄位不收 Token（ActionSlot.AcceptsToken 永遠 false），
        // 公式欄位要的是「哪一批資產」而不是具名常數。目錄的內容由 Owner（AssetPipeline）提供，見 ICatalogOwner。
        HGCapabilities IGraphDocument.Capabilities => HGCapabilities.Catalogs;

        IList IGraphDocument.ItemsOf(object root) => (root as ActionGroup)?.Actions;

        object IGraphDocument.AddRoot(object key)
        {
            if (!PipelineKey.Equals(key)) return null;
            if (Groups.Count > 0) return Groups[0];

            var group = new ActionGroup();
            Groups.Add(group);
            return group;
        }

        void IGraphDocument.Verify() => Verify();

        object IGraphDocument.DeepCopy() => GraphDeepCopy.Copy(this);

        /// <summary>
        /// 驗證整張圖並更新 <see cref="IsValidated"/>。錯誤記進 Console，
        /// 通過才把圖標成可執行。
        /// </summary>
        public void Verify()
        {
            List<GraphDiagnostic> diagnostics = GraphVerifier.CollectDiagnostics(this);
            _validated = diagnostics.Count == 0;

            if (_validated) return;

            var report = new System.Text.StringBuilder("[AssetPipeline] 驗證未通過：");
            foreach (GraphDiagnostic diagnostic in diagnostics) report.Append('\n').Append("  • ").Append(diagnostic.Message);
            Debug.LogError(report.ToString());
        }
    }
}
