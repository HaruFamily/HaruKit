namespace HaruFamily.DependencyCore.GraphKit
{
using System;

/// <summary>
/// 目錄的共同基底：持有一包 T，由外部寫入、由底下的格子讀取。
/// </summary>
// 形狀與行為分層，同 ActionBase／FormulaBase：CatalogNodeShape 只答型別關係，這一層才有資料。
// Write 是非虛的入口，合併語意一律走 OnWrite——abstract 而非 virtual 是刻意的：
// new／Add／先 hash 再替換三種語意差太多，留下可以被不小心繼承的預設遲早會錯。
//
// 內容是執行期產物，因此 Index 與 Initialized 都不序列化；節點的序列化欄位留在使用端的子類上。
[Serializable]
public abstract class CatalogBase<T> : CatalogNodeShape<T>
{
    [NonSerialized]
    private T _index;

    [NonSerialized]
    private bool _init;

    /// <summary>這一包的內容。<see cref="OnInit"/> 建立它，<see cref="OnWrite"/> 併進它。</summary>
    // 可寫：合併語意允許「先 hash 再整個替換」，只給 getter 就只剩就地改內容那一種。
    protected T Index { get => _index; set => _index = value; }

    /// <summary>有沒有建立過內容。未建立時 <see cref="Index"/> 是 default(T)。</summary>
    protected bool Initialized => _init;

    /// <summary>建立空的內容，例如 Index = new()。任意 T 不保證有無參建構子，所以交給實作。</summary>
    protected abstract void OnInit();

    /// <summary>把 value 併進 <see cref="Index"/>。new／Add／先 hash 再替換由實作決定。</summary>
    protected abstract void OnWrite(T value);

    /// <summary>
    /// 寫入一份內容。<paramref name="reset"/> 為 true 代表這一次是重來，先清空再併入。
    /// </summary>
    // reset 由寫入端給（CatalogSlot 上的欄位），不由這一層推：OnWrite 只看得到「Index 已經有東西」，
    // 而那既可能是同一批的前一次寫入，也可能是上一輪留下的，兩者要做的事相反。
    // !_init 是另一回事，只擋「從未建立」，不承擔任何重置語意。
    public void Write(T value, bool reset)
    {
        if (reset || !_init)
        {
            OnInit();
            _init = true;
        }
        OnWrite(value);
    }

    /// <summary>取這一包的內容。未建立過就先建立，讀的人不必先寫過才拿得到空集合。</summary>
    // virtual：內容不一定來自 Write。來源是外部資料（例如目錄庫）的子類覆寫它直接讀來源，
    // 才不會把「編輯期改了來源要即時反映」變成只在第一次讀取時決定。
    public virtual T Read()
    {
        if (!_init)
        {
            OnInit();
            _init = true;
        }
        return _index;
    }
}

}
