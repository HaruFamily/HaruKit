namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using UnityEngine;

/// <summary>圖層的具名型別化儲存定義；節點只引用，執行期值由各執行層管理。</summary>
[Serializable]
public class GraphProperty
{
    [SerializeField]
    private string _name;

    [SerializeField, HideInInspector]
    private string _id;

    [SerializeReference]
    private FormulaSlotBase _slot;

    // null 是參考型別合法的初始值，與庫歸屬或保留時間無關。
    [SerializeField]
    private bool _nullInitialValue;

    public bool InitialValueIsNull
    {
        get => _nullInitialValue;
        set => _nullInitialValue = value && ResultType != null && !ResultType.IsValueType;
    }

    public void SetInitialValue(object value)
    {
        if (_slot == null) return;
        InitialValueIsNull = value == null;
        _slot.DefaultObject = value;
    }

    // 執行生命週期；新定義預設只在本次執行有效。
    [SerializeField]
    private bool _executionLocal = true;

    /// <summary>保留到圖實例結束；關閉時只在本次執行鏈內有效。</summary>
    public bool RetainValue { get => !_executionLocal; set => _executionLocal = !value; }

    [NonSerialized]
    private object _current;

    [NonSerialized]
    private bool _written;

    [NonSerialized]
    private bool _resolvingInput;

    [NonSerialized]
    private object _executionInitial;

    [NonSerialized]
    private bool _hasExecutionInitial;

    public GraphProperty() { }

    public GraphProperty(string name, FormulaSlotBase slot)
    {
        _name = name;
        _slot = slot;
    }

    /// <summary>顯示名稱，在同一變數庫內跨族唯一。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? null : _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>型別宣告用的欄位，同時保存 Property 的初始內容。</summary>
    public FormulaSlotBase Slot => _slot;

    /// <summary>換值型別＝換整個 Slot。Id、名稱與目前值狀態不動。</summary>
    public void SetSlot(FormulaSlotBase slot)
    {
        _slot = slot;
        InitialValueIsNull = _nullInitialValue;
    }

    /// <summary>值型別。Slot 未指定時為 null。</summary>
    public Type ResultType => _slot?.ResultType;

    /// <summary>族身分（＝Slot 型別）。寫入須同族；讀取依接收 Slot 的相容政策。</summary>
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

    /// <summary>初始內容，可明確指定 null；定義的型別 Slot 不參與求值。</summary>
    public object InitialValue => _nullInitialValue ? null : _slot?.DefaultObject;

    /// <summary>目前值。尚未寫入時回初始內容；參考型別取得的是同一份引用，不做複製隔離。</summary>
    public object CurrentValue
    {
        get
        {
            if (_written) return _current;
            if (RetainValue) return InitialValue;
            if (!_hasExecutionInitial)
            {
                _executionInitial = GraphDeepCopy.Copy(InitialValue);
                _hasExecutionInitial = true;
            }
            return _executionInitial;
        }
    }

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
        _executionInitial = null;
        _hasExecutionInitial = false;
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


}

}
