namespace HaruFamily.Tools.AssetPipeline.Tests
{
    /// <summary>資產庫排序測試使用的可掃描圖資產。</summary>
    public sealed class LibraryOrderTestAsset : HaruFamily.Framework.LogicGraph.FormulaAsset<int, LibraryOrderSlot, object> { }
    public abstract class LibraryOrderFormula : HaruFamily.Framework.LogicGraph.FormulaBase<int, object> { }
    [System.Serializable]
    public sealed class LibraryOrderToken : HaruFamily.Framework.LogicGraph.FormulaToken<int, LibraryOrderSlot, object> { }
    [System.Serializable]
    public sealed class LibraryOrderSlot : HaruFamily.Framework.LogicGraph.FormulaSlot<int, LibraryOrderFormula, LibraryOrderToken, LibraryOrderTestAsset, object> { }
}
