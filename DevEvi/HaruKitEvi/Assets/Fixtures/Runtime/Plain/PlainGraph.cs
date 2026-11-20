using System;
using System.Collections;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Plain
{
    // ================= 文件 =================

    /// <summary>單一 root 的最小文件，形狀比照 AssetPipeline 的 Graph，但沒有任何執行或驗證規則。</summary>
    [Serializable]
    public sealed class PlainDocument : IGraphDocument, IGraphViewStateOwner
    {
        public const string RootKey = "Main";

        [SerializeReference] private List<PlainRoot> roots = new List<PlainRoot>();
        [SerializeReference, HideInInspector] private List<GraphNode> orphans = new List<GraphNode>();
        [SerializeField, HideInInspector] private bool validated;
        [SerializeField, HideInInspector] private GraphViewState viewState = new GraphViewState();

        public List<PlainRoot> RootList
        {
            get { roots ??= new List<PlainRoot>(); return roots; }
        }

        /// <summary>唯一的 root；沒有就建立。</summary>
        public PlainRoot Main
        {
            get
            {
                if (RootList.Count == 0) RootList.Add(new PlainRoot());
                return RootList[0];
            }
        }

        public List<GraphNode> Orphans
        {
            get { orphans ??= new List<GraphNode>(); return orphans; }
        }

        public GraphViewState ViewState => viewState ??= new GraphViewState();

        public IList Roots => RootList;
        public bool IsValidated => validated;
        public void InvalidateValidation() => validated = false;

        // 沒有領域規則：驗證一律通過，方便單獨觀察編輯器的保存與驗證旗標流程。
        public void Verify() => validated = true;

        public object DeepCopy() => GraphDeepCopy.Copy(this);
        public Type PackType => typeof(NullPack);
        public Type ItemSlotType => typeof(PlainActionSlot);
        public IReadOnlyList<object> RootKeys(UnityEngine.Object owner) => new object[] { RootKey };
        public object KeyOf(object root) => RootKey;
        public string TitleOf(object root) => "主流程";
        public string RootChip => "Plain";
        public string RootNoun => "流程";
        public HGCapabilities Capabilities => HGCapabilities.None;
        public string WindowTitle => "Plain GraphKit";
        public IList ItemsOf(object root) => (root as PlainRoot)?.Actions;

        public object AddRoot(object key)
        {
            if (!RootKey.Equals(key)) return null;
            return Main;
        }
    }

    [Serializable]
    public sealed class PlainRoot : IGraphHead, ISequentialActionContainer
    {
        public List<PlainActionSlot> Actions = new List<PlainActionSlot>();

        [SerializeField, HideInInspector] private Vector2 pos;
        [SerializeField, HideInInspector] private bool hasPos;

        IReadOnlyList<ActionSlotBase> ISequentialActionContainer.SequentialActions => Actions;

        public Vector2 Pos
        {
            get => pos;
            set { pos = value; hasPos = true; }
        }

        public bool HasPos => hasPos;
        public void ClearPos() { hasPos = false; pos = Vector2.zero; }
    }

    // ================= Slot =================

    [Serializable]
    public sealed class PlainActionSlot : ActionSlotBase
    {
        [SerializeReference] private GraphNode node;
        [SerializeField] private string id;
        [SerializeField] private bool disabled;
        [SerializeField] private string label;
        [SerializeField, HideInInspector] private Vector2 pos;
        [SerializeField, HideInInspector] private bool hasPos;
        [SerializeReference, HideInInspector] private List<GraphNode> orphans = new List<GraphNode>();

        public PlainActionSlot() { }

        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override bool AcceptsBody(GraphNodeContent body) => body is PlainAction;

        public override bool Disabled { get => disabled; set => disabled = value; }
        public override string Label { get => label; set => label = value; }
        public override string Id => id;
        public override string EnsureId() => string.IsNullOrEmpty(id) ? id = Guid.NewGuid().ToString("N") : id;
        public override void ResetId() => id = null;

        public override Vector2 Pos
        {
            get => pos;
            set { pos = value; hasPos = true; }
        }

        public override bool HasPos => hasPos;
        public override void ClearPos() { hasPos = false; pos = Vector2.zero; }

        public override List<GraphNode> Orphans
        {
            get { orphans ??= new List<GraphNode>(); return orphans; }
        }

        public override Type PackType => typeof(NullPack);
        public override Type BodyBaseType => typeof(PlainAction);
        public override Type AssetBaseType => null;
    }

    /// <summary>整數值欄位：未接來源時是常數，接上時只收 <see cref="PlainValue"/>。</summary>
    [Serializable]
    [HGKind("Plain Int", "Plain")]
    public sealed class PlainValueSlot : FormulaSlotBase
    {
        [SerializeReference] private GraphNode node;
        [SerializeField] private int constant;

        public PlainValueSlot() { }
        public PlainValueSlot(int value) { constant = value; }

        public override GraphNode Node => node;
        public override void SetNode(GraphNode value) => node = value;
        public override Type ResultType => typeof(int);
        public override Type PackType => typeof(NullPack);
        public override Type BodyBaseType => typeof(PlainValue);
        public override Type AssetBaseType => null;
        public override object DefaultObject
        {
            get => constant;
            set => constant = value is int number ? number : 0;
        }
        public override bool AcceptsBody(GraphNodeContent body) => body is PlainValue;
    }

    // ================= 節點 =================

    /// <summary>Plain 文件的動作節點基底。只有資料，沒有執行。</summary>
    [Serializable]
    public abstract class PlainAction : ActionNodeShape { }

    /// <summary>Plain 文件的整數值節點基底。</summary>
    [Serializable]
    public abstract class PlainValue : FormulaNodeShape<int, NullPack>, ITypedFormulaNode
    {
        public Type ResultType => typeof(int);
        public Type PackType => typeof(NullPack);
    }

    [Serializable]
    [HGNode("備註", "只帶一段文字的動作", "Plain")]
    public sealed class PlainNote : PlainAction
    {
        [HGLabel("文字")] public string Text = "note";
    }

    [Serializable]
    [HGNode("帶數值的步驟", "一個整數欄位加一個子動作欄位", "Plain")]
    public sealed class PlainStep : PlainAction
    {
        [HGLabel("數值")] public PlainValueSlot Amount = new PlainValueSlot(1);
        [HGLabel("子動作")] public PlainActionSlot Child = new PlainActionSlot();
    }

    [Serializable]
    [HGNode("循序", "依序排列的一串動作", "Plain 流程")]
    public sealed class PlainSequence : PlainAction, ISequentialActionContainer
    {
        [HGLabel("動作")] public List<PlainActionSlot> Actions = new List<PlainActionSlot>();

        IReadOnlyList<ActionSlotBase> ISequentialActionContainer.SequentialActions => Actions;
    }

    [Serializable]
    [HGNode("迴圈", "宣告成 Loop 的流程容器（只有結構，不執行）", "Plain 流程")]
    public sealed class PlainLoop : PlainAction, IGraphFlowContainer
    {
        [HGLabel("次數")] public PlainValueSlot Count = new PlainValueSlot(3);
        [HGLabel("內容")] public List<PlainActionSlot> Body = new List<PlainActionSlot>();

        public GraphFlowDescription DescribeFlow() => new GraphFlowDescription(GraphFlowKind.Loop,
            new[] { new GraphFlowSection("body", "內容", GraphFlowSectionRole.Body, Body) },
            new[] { new GraphFlowInput("count", "次數", Count) });
    }

    [Serializable]
    [HGNode("條件", "宣告成 Conditional 的流程容器（只有結構，不執行）", "Plain 流程")]
    public sealed class PlainCondition : PlainAction, IGraphFlowContainer
    {
        [HGLabel("條件值")] public PlainValueSlot Condition = new PlainValueSlot(1);
        [HGLabel("成立")] public List<PlainActionSlot> WhenTrue = new List<PlainActionSlot>();
        [HGLabel("不成立")] public List<PlainActionSlot> WhenFalse = new List<PlainActionSlot>();

        public GraphFlowDescription DescribeFlow() => new GraphFlowDescription(GraphFlowKind.Conditional,
            new[]
            {
                new GraphFlowSection("true", "成立", GraphFlowSectionRole.WhenTrue, WhenTrue),
                new GraphFlowSection("false", "不成立", GraphFlowSectionRole.WhenFalse, WhenFalse),
            },
            new[] { new GraphFlowInput("condition", "條件值", Condition) });
    }

    [Serializable]
    [HGNode("相加", "A + B（只有結構，不求值）", "Plain 數值")]
    public sealed class PlainAdd : PlainValue
    {
        [HGLabel("A")] public PlainValueSlot A = new PlainValueSlot(1);
        [HGLabel("B")] public PlainValueSlot B = new PlainValueSlot(1);
    }

    [Serializable]
    [HGNode("加總", "一串整數欄位（只有結構，不求值）", "Plain 數值")]
    public sealed class PlainSum : PlainValue
    {
        [HGLabel("項目")] public List<PlainValueSlot> Values = new List<PlainValueSlot>();
    }
}
