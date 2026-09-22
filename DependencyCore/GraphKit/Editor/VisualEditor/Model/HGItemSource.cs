namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>一段項目列的資料歸屬：這一項從哪裡來、怎麼安全地改。</summary>
// 這一層只放「兩種來源都成立」的能力。增刪、重排與複製目前各自只有一個來源支援，
// 所以留在具體子類上，由該來源專屬的繪製路徑直接呼叫；出現第二個支援同一命令的來源時才上移。
// 形狀（HGRowKind）、端點（HGPort）與資料歸屬（本型別）是三個獨立軸，不可互相推導。
public abstract class HGItemSource
{
    /// <summary>目前有幾項。每次重建圖現問，不快取。</summary>
    public abstract int Count { get; }

    /// <summary>第 <paramref name="index"/> 項的內容物；越界回 null，呼叫端不必自己夾。</summary>
    public abstract object Get(int index);

    protected bool InRange(int index) => index >= 0 && index < Count;
}

/// <summary><c>IList</c> 支撐的項目來源。<c>List&lt;T&gt;</c> 與 <c>T[]</c> 是同一種來源的兩種組態。</summary>
// 陣列與清單的差別只有 CanEditStructure 一個開關：資料歸屬同樣是 owner + index，
// 走訪、斑馬紋、縮排與命中完全一樣，分成兩個來源只會讓每個呼叫點都要判一次。
public sealed class HGListItemSource : HGItemSource
{
    public IList List { get; }
    public Type ElementType { get; }

    public HGListItemSource(IList list, Type elementType)
    {
        List = list ?? throw new ArgumentNullException(nameof(list));
        ElementType = elementType;
    }

    public override int Count => List.Count;

    public override object Get(int index) => InRange(index) ? List[index] : null;

    /// <summary>陣列長度由程式或 Inspector 決定，編輯器不得增刪或重排。</summary>
    public bool CanEditStructure => !List.IsFixedSize;

    /// <summary>就地改寫一項。固定長度的陣列仍可改內容，只是不能改結構。</summary>
    public bool Set(int index, object value)
    {
        if (!InRange(index)) return false;
        List[index] = value;
        return true;
    }

    public bool Insert(int index, object item)
    {
        if (!CanEditStructure || index < 0 || index > Count) return false;
        List.Insert(index, item);
        return true;
    }

    public bool Add(object item)
    {
        if (!CanEditStructure) return false;
        List.Add(item);
        return true;
    }

    public bool RemoveAt(int index)
    {
        if (!CanEditStructure || !InRange(index)) return false;
        List.RemoveAt(index);
        return true;
    }

    /// <summary>把第 <paramref name="from"/> 項搬到 <paramref name="to"/>。索引是搬移後的位置。</summary>
    public bool Move(int from, int to)
    {
        if (!CanEditStructure || !InRange(from) || !InRange(to) || from == to) return false;
        var item = List[from];
        List.RemoveAt(from);
        List.Insert(to, item);
        return true;
    }

    /// <summary>建一個可以放進這個清單的新元素；建不出來回 null。</summary>
    // 基本型別與 string 沒有「空實例」的問題，用 default 值；其餘走無參數建構。
    // string 的 default 是 null，會被當成建構失敗，所以它要單獨認一次。
    public object CreateElement()
    {
        if (ElementType == null) return null;
        if (ElementType == typeof(string)) return "";
        if (ElementType.IsValueType) return Activator.CreateInstance(ElementType);
        return HGReflect.CreateInstance(ElementType);
    }
}

}
