using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    /// <summary>[HGOptions]／[HGSpinner] 共用的候選目錄。</summary>
    public static class EviCatalogs
    {
        public const string Levels = "Evi.Levels";
        public const string Tags = "Evi.Tags";

        [HGCatalog(Levels)]
        private static IEnumerable<HGCatalogEntry<int>> LevelEntries => new[]
        {
            new HGCatalogEntry<int>(1, "簡單", "適合初次遊玩"),
            new HGCatalogEntry<int>(2, "普通"),
            new HGCatalogEntry<int>(3, "困難"),
            new HGCatalogEntry<int>(4, "惡夢", "只有標籤與數值不同名的項目才會顯示原始值"),
        };

        [HGCatalog(Tags, Group = "元素")]
        private static IEnumerable<string> ElementTags => new[] { "fire", "ice", "wind", "earth" };

        [HGCatalog(Tags, Group = "狀態")]
        private static IEnumerable<string> StatusTags => new[] { "stun", "poison", "shield" };
    }

    /// <summary>
    /// 把 GraphKit 的欄位屬性集中在一顆節點上：分頁、摺疊群組、條件顯示、enum／bool 按鈕、
    /// 候選選單、數量格位、Label 排列與 Slot 清單。執行時只輸出一行摘要。
    /// </summary>
    [Serializable]
    [HGNode("屬性展示", "集中展示 GraphKit 欄位屬性的測試節點", "展示", Width = 16)]
    public sealed class EviShowcase : ActionBase<EviPack>
    {
        // ---- 基本 ----
        [HGTab("基本"), HGLabel("數量")] public int Amount = 1;
        [HGTab("基本"), HGLabel("標題"), HGDescription("一般字串欄位")] public string Title = "Showcase";
        [HGTab("基本"), HGLabel("啟用強度"), HGBool("開", "關")] public bool UseStrength = true;
        [HGTab("基本"), HGLabel("強度"), HGShowIf(nameof(UseStrength))] public float Strength = 0.5f;
        [HGTab("基本"), HGLabel("比較"), HGEnum] public EviCompareOp Op = EviCompareOp.Greater;
        [HGTab("基本"), HGLabel("比較（下拉）")] public EviCompareOp OpDropdown = EviCompareOp.Equal;

        // ---- 候選 ----
        [HGTab("候選"), HGLabel("難度"), HGOptions(EviCatalogs.Levels)] public int Level = 2;
        [HGTab("候選"), HGLabel("主標籤"), HGOptions(EviCatalogs.Tags, AllowEmpty = true)] public string MainTag = "fire";
        [HGTab("候選"), HGLabel("標籤"), HGOptions(EviCatalogs.Tags)] public List<string> Tags = new List<string> { "fire", "stun" };
        [HGTab("候選"), HGLabel("難度數量"), HGSpinner(EviCatalogs.Levels, 0, 3)] public List<int> LevelCounts = new List<int> { 1, 1, 3 };

        // ---- 進階：Tab 包 Foldout ----
        [HGTab("進階"), HGFoldout("進階/外觀"), HGLabel("顏色")] public Color Tint = Color.white;
        [HGTab("進階"), HGFoldout("進階/外觀"), HGLabel("偏移")] public Vector2 Offset;
        [HGTab("進階"), HGFoldout("進階/引用"), HGLabel("資產")] public ScriptableObject Reference;
        [HGTab("進階"), HGFoldout("進階/引用"), HGLabel("純值清單")] public List<int> Numbers = new List<int> { 1, 2, 3 };

        // ---- Slot：常駐（不在任何分頁）----
        [HGLabel("條件", Layout = HGLabelLayout.Vertical)] public EviBoolSlot Condition = new EviBoolSlot(true);
        [HGLabel(Layout = HGLabelLayout.Hidden)] public EviIntSlot HiddenLabel = new EviIntSlot(3);
        [HGLabel("數值清單")] public List<EviIntSlot> Values = new List<EviIntSlot>();
        [HGLabel("子動作")] public List<ActionSlot<EviPack>> Children = new List<ActionSlot<EviPack>>();

        [HGHide] public int HiddenFromGraph = 42;

        protected override async UniTask OnExecute(EviPack pack, TokenTable<EviPack> tokens)
        {
            bool condition = await Condition.Evaluate(pack, tokens);
            int hidden = await HiddenLabel.Evaluate(pack, tokens);
            int sum = 0;
            foreach (EviIntSlot value in Values)
                if (value != null) sum += await value.Evaluate(pack, tokens);

            string text = $"Showcase「{Title}」amount={Amount} level={Level} tags=[{string.Join(",", Tags)}] "
                + $"condition={condition} hidden={hidden} sum={sum}";
            pack?.Log.Add(text);
            Debug.Log("[Evi] " + text);

            foreach (ActionSlot<EviPack> child in Children)
                if (child != null) await child.Execute(pack, tokens);
        }
    }
}
