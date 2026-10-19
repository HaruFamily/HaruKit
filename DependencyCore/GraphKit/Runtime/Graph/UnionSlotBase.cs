using System;
using UnityEngine;

namespace HaruFamily.DependencyCore.GraphKit
{
    /// <summary>單一來源、多族接收的輸入欄位；不是可宣告為 Token／Property 的結果族。</summary>
    [Serializable]
    public abstract class UnionSlotBase : FormulaSlotBase
    {
        [SerializeReference] private FormulaSlotBase selectedSlot;
        [NonSerialized] private FormulaSlotBase[] candidates;

        public abstract int BranchCount { get; }
        protected abstract FormulaSlotBase CreateBranch(int index);
        protected virtual bool IsBranchAllowed(FormulaSlotBase slot) => slot.PackType == PackType;

        private FormulaSlotBase[] Candidates
        {
            get
            {
                if (candidates != null) return candidates;
                var result = new FormulaSlotBase[BranchCount];
                if (result.Length < 2 || result.Length > 5)
                    throw new InvalidOperationException("Union requires 2–5 branches.");
                for (int i = 0; i < result.Length; i++)
                {
                    var branch = CreateBranch(i);
                    if (branch == null || branch is UnionSlotBase || !IsBranchAllowed(branch))
                        throw new InvalidOperationException("Union branches must be ordinary formula slots with the same Pack.");
                    for (int j = 0; j < i; j++)
                        if (result[j].FamilyType == branch.FamilyType)
                            throw new InvalidOperationException("Union branches must have distinct families.");
                    result[i] = branch;
                }
                return candidates = result;
            }
        }

        public Type BranchType(int index) => Candidates[index].FamilyType;

        /// <summary>目前保存的分支。讀取不建立或改寫序列化內容。</summary>
        public FormulaSlotBase SelectedSlot => selectedSlot;
        public override GraphNode Node => selectedSlot?.Node;
        public override Type BodyBaseType => typeof(GraphNodeContent);
        public override Type CandidateBodyBaseType => typeof(GraphNodeContent);
        public override Type CandidatePackType => PackType;
        public override Type AssetBaseType => typeof(ScriptableObject);
        public override Type DefaultEditType => ResultType;

        /// <summary>程式組圖使用；保存來源分支，不改變 Union 自己的 TResult 預設值。</summary>
        public void SetSelectedSlot(FormulaSlotBase slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            for (int i = 0; i < BranchCount; i++)
                if (slot.FamilyType == BranchType(i))
                {
                    selectedSlot = slot;
                    return;
                }
            throw new ArgumentException("The slot is not a declared Union branch.", nameof(slot));
        }

        public override void SetNode(GraphNode node)
        {
            if (node == null || node.Kind == NodeKind.Empty)
            {
                selectedSlot?.SetNode(node);
                return;
            }
            int branch = FindBranch(node.Kind, node.BodyObject, node.AssetObject, node.Token, node.Property);
            if (branch < 0) throw new ArgumentException("Union source is incompatible or ambiguous.", nameof(node));
            if (selectedSlot?.FamilyType != BranchType(branch)) selectedSlot = CreateBranch(branch);
            selectedSlot.SetNode(node);
        }

        public override bool AcceptsBody(GraphNodeContent body)
            => FindBranch(NodeKind.Inline, body, null, null, null) >= 0;
        public override bool AcceptsAsset(ScriptableObject asset)
            => FindBranch(NodeKind.Asset, null, asset, null, null) >= 0;
        public override bool AcceptsToken(GraphToken token)
            => FindBranch(NodeKind.Token, null, null, token, null) >= 0;
        public override bool AcceptsProperty(GraphProperty property)
            => FindBranch(NodeKind.Property, null, null, null, property) >= 0;

        private int FindBranch(NodeKind kind, GraphNodeContent body, ScriptableObject asset,
            GraphToken token, GraphProperty property)
        {
            int match = -1;
            int exact = -1;
            bool ambiguous = false;
            bool exactAmbiguous = false;
            var slots = Candidates;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                bool accepts = kind switch
                {
                    NodeKind.Inline => body != null && slot.AcceptsBody(body),
                    NodeKind.Asset => asset != null && slot.AcceptsAsset(asset),
                    NodeKind.Token => token != null && slot.AcceptsToken(token),
                    NodeKind.Property => property != null && slot.AcceptsProperty(property),
                    _ => false,
                };
                if (!accepts) continue;
                if (match >= 0) ambiguous = true;
                match = i;
                bool native = kind switch
                {
                    NodeKind.Inline => slot.BodyBaseType?.IsInstanceOfType(body) == true,
                    NodeKind.Asset => slot.AssetBaseType?.IsInstanceOfType(asset) == true,
                    NodeKind.Token => token.FamilyType == slot.FamilyType,
                    NodeKind.Property => property.FamilyType == slot.FamilyType,
                    _ => false,
                };
                if (!native) continue;
                if (exact >= 0) exactAmbiguous = true;
                exact = i;
            }
            if (exact >= 0) return exactAmbiguous ? -1 : exact;
            return ambiguous ? -1 : match;
        }

        /// <summary>只讀診斷；保存的分支必須能處理目前來源，含共用載體原地換型。</summary>
        public string ConfigurationError
        {
            get
            {
                var slots = Candidates;
                if (selectedSlot == null) return "Union 尚未選擇輸入族。";
                bool declared = false;
                foreach (var slot in slots)
                    if (slot.FamilyType == selectedSlot.FamilyType) declared = true;
                if (!declared) return "Union 的輸入族不在宣告範圍內。";
                var node = Node;
                if (node == null || node.Kind == NodeKind.Empty) return null;
                int branch = FindBranch(node.Kind, node.BodyObject, node.AssetObject, node.Token, node.Property);
                return branch >= 0 && selectedSlot.FamilyType == BranchType(branch)
                    ? null : "Union 來源與保存的輸入族不一致或有歧義，請重新接線。";
            }
        }
    }
}
