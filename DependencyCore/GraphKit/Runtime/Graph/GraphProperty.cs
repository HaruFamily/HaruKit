namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using UnityEngine;

/// <summary>
/// 圖內的具名型別化變數：Action 透過 <see cref="PropertySlotBase"/> 寫入，一般輸入欄位直接讀它的目前值。
///
/// LocalProperty 住在單一 GraphNode，ProtoProperty 定義住圖層清單（<see cref="IPropertyOwner"/>）。
/// 讀寫節點都只存同一份定義引用，因此改 ProtoProperty Key 不會斷線。
/// </summary>
// 型別宣告借用 FormulaSlotBase：族身分、結果型別、常數編輯型別與 Project asset 相容判定全部已經住在那裡，
// ProtoProperty 的初始內容就是它的常數值。**這顆 Slot 只宣告型別與初始值，不接節點**——
// 接了節點就變成「有來源公式的具名取值」，那是 GraphToken 的職責，由驗證擋下。
//
// 目前值是執行期資料，所以不序列化。它存 object 而不是泛型 T：載體非泛型才能讓候選池、
// 複製貼上與編輯器走訪走同一條路，型別安全收斂在寫入端的相容判定與編輯期選單。
[Serializable]
public class GraphProperty
{
    [SerializeField]
    private string _name;

    [SerializeField, HideInInspector]
    private string _id;

    [SerializeReference]
    private FormulaSlotBase _slot;

    // true＝ProtoProperty：初始內容由 _slot 的常數提供，可直接讀取，不必先 Set。
    [SerializeField]
    private bool _proto;

    [NonSerialized]
    private object _current;

    [NonSerialized]
    private bool _written;

    [NonSerialized]
    private bool _resolvingInput;

    public GraphProperty() { }

    public GraphProperty(string name, FormulaSlotBase slot, bool proto = false)
    {
        _name = name;
        _slot = slot;
        _proto = proto;
    }

    /// <summary>顯示名稱。ProtoProperty 在同一變數庫內跨族唯一；LocalProperty 不要求名稱。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? null : _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>型別宣告用的欄位，同時保存 ProtoProperty 的初始內容。</summary>
    public FormulaSlotBase Slot => _slot;

    /// <summary>換值型別＝換整個 Slot。Id、名稱與目前值狀態不動。</summary>
    public void SetSlot(FormulaSlotBase slot) => _slot = slot;

    /// <summary>有沒有可編輯的初始內容。false＝一般 Property，初始化後為值型別預設值或 null。</summary>
    public bool Proto { get => _proto; set => _proto = value; }

    /// <summary>值型別。Slot 未指定時為 null。</summary>
    public Type ResultType => _slot?.ResultType;

    /// <summary>族身分（＝Slot 型別）。讀寫相容看族，不看結果型別。</summary>
    public Type FamilyType => _slot?.FamilyType;

    /// <summary>穩定識別碼，改名不影響。</summary>
    public string Id => _id;

    /// <summary>沒有識別碼時補一個，已存在則沿用；回傳最終識別碼。</summary>
    public string EnsureId()
    {
        if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        return _id;
    }

    /// <summary>複製定義後必須換新識別碼，否則兩份定義共用同一筆焦點與引用。</summary>
    public void ResetId() => _id = null;

    /// <summary>初始內容：ProtoProperty 取 Slot 的常數，一般 Property 取值型別預設值。</summary>
    // 一般 Property 不讀 Slot 常數：那一格在編輯器上沒有入口，但舊資料或族切換仍可能留著值，
    // 讀了就會變成「沒有初始內容的 Property 卻拿得到清單」。
    public object InitialValue => _proto ? _slot?.DefaultObject : DefaultOf(ResultType);

    /// <summary>目前值。尚未寫入時回初始內容；參考型別取得的是同一份引用，不做複製隔離。</summary>
    public object CurrentValue => _written ? _current : InitialValue;

    /// <summary>是否已被寫入過。未寫入不是錯誤，讀取一律回初始內容。</summary>
    public bool HasValue => _written;

    /// <summary>替換目前值。只替換，不追加、不合併、不去重、不複製。</summary>
    public void SetValue(object value)
    {
        _current = value;
        _written = true;
    }

    /// <summary>明確初始化：回到當時的初始內容。已被原地修改的共用清單不保證還原成修改前的內容。</summary>
    public void Initialize()
    {
        _current = null;
        _written = false;
    }

    /// <summary>取回目前值的備份，交給呼叫端保管。</summary>
    // 只記「有沒有寫過」與寫過的引用；交易回復的清單內容備份由使用端負責，這裡不做深層複製。
    public (object value, bool written) CaptureValue() => (_current, _written);

    /// <summary>用 <see cref="CaptureValue"/> 的備份還原目前值。</summary>
    public void RestoreValue((object value, bool written) snapshot)
    {
        _current = snapshot.value;
        _written = snapshot.written;
    }

    /// <summary>避免 Property 輸入間接讀回自己時遞迴求值。</summary>
    public bool TryEnterInputEvaluation()
    {
        if (_resolvingInput) return false;
        _resolvingInput = true;
        return true;
    }

    public void ExitInputEvaluation() => _resolvingInput = false;

    private static object DefaultOf(Type type)
    {
        if (type == null) return null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

}
