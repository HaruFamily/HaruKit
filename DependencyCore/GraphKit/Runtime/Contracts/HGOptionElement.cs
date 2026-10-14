namespace HaruFamily.DependencyCore.GraphKit
{
/// <summary>候選顯示資料的非泛型讀取介面；不作為圖內保存的值。</summary>
public interface IHGOptionElement
{
    object Value { get; }
    string Label { get; }
    string Tooltip { get; }
}

/// <summary>一般候選的實際值與顯示資料。圖欄位只保存 Value。</summary>
public readonly struct HGOptionElement<T> : IHGOptionElement
{
    public T Value { get; }
    public string Label { get; }
    public string Tooltip { get; }
    object IHGOptionElement.Value => Value;

    public HGOptionElement(T value, string label, string tooltip = "")
    {
        Value = value;
        Label = label;
        Tooltip = tooltip;
    }
}
}
