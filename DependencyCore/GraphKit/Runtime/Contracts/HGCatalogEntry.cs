namespace HaruFamily.DependencyCore.GraphKit
{
/// <summary>候選顯示資料的非泛型讀取介面；不作為圖內保存的值。</summary>
public interface IHGCatalogEntry
{
    object Value { get; }
    string Label { get; }
    string Tooltip { get; }
}

/// <summary>一般候選的實際值與顯示資料。圖欄位只保存 Value。</summary>
public readonly struct HGCatalogEntry<T> : IHGCatalogEntry
{
    public T Value { get; }
    public string Label { get; }
    public string Tooltip { get; }
    object IHGCatalogEntry.Value => Value;

    public HGCatalogEntry(T value, string label, string tooltip = "")
    {
        Value = value;
        Label = label;
        Tooltip = tooltip;
    }
}
}
