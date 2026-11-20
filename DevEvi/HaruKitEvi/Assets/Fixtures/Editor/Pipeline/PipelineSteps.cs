using System;
using System.Collections.Generic;
using System.Text;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Tools.AssetPipeline;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruKitEvi.PipelineSteps
{
    // ================= 公式 =================

    [Serializable]
    [HGNode("串接文字", "把每一段依序接起來", "Evi")]
    public sealed class PipeJoinText : Formula_String<NullPack>
    {
        [HGLabel("片段")] public List<StringSlot> parts = new List<StringSlot>();

        protected override string OnEvaluate(NullPack pack)
        {
            var builder = new StringBuilder();
            foreach (StringSlot part in parts)
                if (part != null) builder.Append(part.Evaluate());
            return builder.ToString();
        }
    }

    [Serializable]
    [HGNode("整數轉文字", "把整數轉成文字", "Evi")]
    public sealed class PipeIntToText : Formula_String<NullPack>
    {
        [HGLabel("數值")] public IntSlot value = new IntSlot(0);

        protected override string OnEvaluate(NullPack pack) => value.Evaluate().ToString();
    }

    [Serializable]
    [HGNode("相加", "A + B", "Evi")]
    public sealed class PipeAddInt : Formula_Int<NullPack>
    {
        [HGLabel("A")] public IntSlot a = new IntSlot(1);
        [HGLabel("B")] public IntSlot b = new IntSlot(1);

        protected override int OnEvaluate(NullPack pack) => a.Evaluate() + b.Evaluate();
    }

    [Serializable]
    [HGNode("計數器的值", "讀取 PipelineCounter 目前的數字", "Evi")]
    public sealed class PipeCounterValue : Formula_Int<NullPack>
    {
        [HGLabel("計數器")] public PipelineCounter counter;

        protected override int OnEvaluate(NullPack pack) => counter != null ? counter.value : 0;
    }

    [Serializable]
    [HGNode("大於", "A 是否大於 B", "Evi")]
    public sealed class PipeIsGreater : Formula_Bool<NullPack>
    {
        [HGLabel("A")] public IntSlot a = new IntSlot(0);
        [HGLabel("B")] public IntSlot b = new IntSlot(0);

        protected override bool OnEvaluate(NullPack pack) => a.Evaluate() > b.Evaluate();
    }

    [Serializable]
    [HGNode("資料夾內的資產", "列出資料夾（含子資料夾）裡的主資產", "Evi")]
    public sealed class PipeAssetsInFolder : Formula_ObjectList<NullPack>
    {
        [HGLabel("資料夾")] public FolderSlot folder = new FolderSlot();

        protected override List<Object> OnEvaluate(NullPack pack)
        {
            var result = new List<Object>();
            string path = folder.EvaluatePath();
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return result;

            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(assetPath)) continue;
                Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset != null) result.Add(asset);
            }
            return result;
        }
    }

    // ================= 動作 =================

    [Serializable]
    [HGNode("回報訊息", "把訊息寫進這一步的結果", "Evi")]
    public sealed class PipeReport : ActionBase
    {
        [HGLabel("訊息")] public StringSlot message = new StringSlot("Hello");

        protected override void OnExecute(PipelineActionContext context)
        {
            string text = message.Evaluate() ?? string.Empty;
            context.Result.Message(text);
            Debug.Log("[Pipeline] " + text);
        }
    }

    [Serializable]
    [HGNode("跳過", "回報這一步無事可做", "Evi")]
    public sealed class PipeSkip : ActionBase
    {
        [HGLabel("原因")] public string reason = "沒有要處理的東西。";

        protected override void OnExecute(PipelineActionContext context) => context.Skip(reason);
    }

    [Serializable]
    [HGNode("故意失敗", "讓這一步失敗，用來測整次交易的回復", "Evi")]
    public sealed class PipeFailStep : ActionBase
    {
        [HGLabel("訊息")] public string message = "PipeFailStep：故意失敗";

        protected override void OnExecute(PipelineActionContext context) => context.Fail(message);
    }

    [Serializable]
    [HGNode("條件執行", "條件成立執行上面的動作，否則執行下面的", "Evi")]
    public sealed class PipeIf : ActionBase
    {
        [HGLabel("條件")] public BoolSlot condition = new BoolSlot(true);
        [HGLabel("成立")] public ActionSlot whenTrue = new ActionSlot();
        [HGLabel("不成立")] public ActionSlot whenFalse = new ActionSlot();

        protected override void OnExecute(PipelineActionContext context)
        {
            ActionSlot branch = condition.Evaluate() ? whenTrue : whenFalse;
            if (!branch.Execute(context)) context.Skip("這個分支沒有動作。");
        }
    }

    [Serializable]
    [HGNode("累加計數器", "把 PipelineCounter 的數字加上指定值（經由交易寫入）", "Evi")]
    public sealed class PipeBumpCounter : ActionBase
    {
        [HGLabel("計數器")] public PipelineCounter counter;
        [HGLabel("增量")] public IntSlot amount = new IntSlot(1);

        protected override void OnExecute(PipelineActionContext context)
        {
            if (counter == null) { context.Fail("沒有指定計數器。"); return; }

            int delta = amount.Evaluate();
            int before = counter.value;
            context.Assets.EditAssetSet(new Object[] { counter }, counter, () =>
            {
                counter.value = before + delta;
                EditorUtility.SetDirty(counter);
            }, $"{before} → {before + delta}");
        }
    }

    [Serializable]
    [HGNode("寫入整數 Property", "把數值寫進選定的 Property", "Evi")]
    public sealed class PipeStoreInt : ActionBase
    {
        [HGLabel("數值")] public IntSlot value = new IntSlot(0);
        [HGLabel("輸出")] public PropertySlot<int, IntSlot> output = new PropertySlot<int, IntSlot>();

        protected override void OnExecute(PipelineActionContext context)
        {
            int result = value.Evaluate();
            output.Write(result);
            context.Result.Message("寫入 " + result);
        }
    }

    [Serializable]
    [HGNode("收集物件", "把物件清單寫進 Property，並逐一記錄到結果", "Evi")]
    public sealed class PipeCollectObjects : ActionBase
    {
        [HGLabel("目標")] public ObjectListSlot targets = new ObjectListSlot();
        [HGLabel("輸出")] public PropertySlot<List<Object>, ObjectListSlot> output = new PropertySlot<List<Object>, ObjectListSlot>();

        protected override void OnExecute(PipelineActionContext context)
        {
            List<Object> items = targets.Evaluate();
            if (items == null || items.Count == 0) { context.Skip("沒有目標。"); return; }

            output.Write(items);
            foreach (Object item in items) context.Result.Record(PipelineItemStatus.Collected, item);
        }
    }
}
