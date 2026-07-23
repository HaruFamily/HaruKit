namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

// 非泛型 base：給 Editor walker 不必泛型參數就能取 DebugTokenKey/IsSelfReferencing/Use-Type 等。
public abstract class FormulaSlotBase
{
    internal abstract string DebugTokenKey { get; }
    public abstract bool IsSelfReferencing { get; }
    internal abstract void SetDictKey(string key);
#if UNITY_EDITOR
    internal abstract int EditorUseTypeRaw { get; }
    internal abstract bool EditorHasFormula { get; }
    internal abstract bool EditorHasAsset { get; }
    internal abstract bool EditorHasTokenKey { get; }
#endif
}

[Serializable]
public abstract class FormulaSlot<TResult, TAsset, TFormula, TPack> : FormulaSlotBase, IFormulaSlot<TResult, TPack>
    where TAsset : FormulaAsset<TResult, TPack>
    where TFormula : FormulaBase<TResult, TPack>
{
    [SerializeField]
    [HorizontalGroup("MainRow", Width = 0.2f)]
    [HideLabel]
    [EnumToggleButtons]
    protected UseType _useType = UseType.Default;

    [SerializeField]
    [HorizontalGroup("MainRow")]
    [LabelText("預設"), LabelWidth(40)]
    protected TResult _default = default;

    [ShowInInspector, SerializeReference]
    [HorizontalGroup("SubRow")]
    [ShowIf("_isFormula")]
    [HideLabel]
    [TypeSelectorSettings(ShowCategories = true)]
    [FormerlySerializedAs("_target")]
    private TFormula _formula;

    [SerializeField, InlineEditor]
    [HorizontalGroup("SubRow")]
    [ShowIf("_isAsset")]
    [HideLabel]
#if UNITY_EDITOR
    [OnValueChanged("OnAssetChanged")]
#endif
    private TAsset _asset;

    [SerializeField]
    [HorizontalGroup("SubRow")]
    [ShowIf("_isToken")]
    [HideLabel]
    private string _tokenKey;

    [NonSerialized] internal string _dictKey;

    private bool _isFormula => _useType == UseType.Formula;
    private bool _isAsset => _useType == UseType.Asset;
    private bool _isToken => _useType == UseType.Token;

    internal override string DebugTokenKey => _useType == UseType.Token ? _tokenKey : null;

    public override bool IsSelfReferencing => _useType == UseType.Token && _dictKey != null && _tokenKey == _dictKey;

    internal override void SetDictKey(string key) => _dictKey = key;

#if UNITY_EDITOR
    internal override int EditorUseTypeRaw => (int)_useType;
    internal override bool EditorHasFormula => _formula != null;
    internal override bool EditorHasAsset => _asset != null;
    internal override bool EditorHasTokenKey => !string.IsNullOrEmpty(_tokenKey);
#endif

    public enum UseType
    {
        [LabelText("常數")] Default,
        [LabelText("公式")] Formula,
        [LabelText("資產")] Asset,
        [LabelText("變數")] Token,
    }

    public FormulaSlot(bool active)
    {
        if (active)
            _useType = UseType.Formula;
        else
            _useType = UseType.Default;
    }

    // 帶初始常數值：active 決定模式（true=公式、false=常數），defaultValue 寫入 _default（常數模式即此值、公式模式為 fallback）。
    public FormulaSlot(bool active, TResult defaultValue) : this(active)
    {
        _default = defaultValue;
    }

    public async UniTask<TResult> Evaluate(TPack pack, TokenCache<TPack> tokens)
    {
        switch (_useType)
        {
            case UseType.Formula:
                return _formula != null ? await _formula.Evaluate(pack, tokens) : _default;
            case UseType.Asset:
                return _asset != null ? await _asset.Evaluate(pack, tokens) : _default;
            case UseType.Token:
                {
                    if (IsSelfReferencing) return _default;
                    if (tokens == null || !tokens.Has<TResult>(_tokenKey)) return _default;
                    if (tokens.IsResolving<TResult>(_tokenKey)) return _default;
                    return await tokens.Resolve<TResult>(_tokenKey, pack);
                }
            default:
                return _default;
        }
    }

#if UNITY_EDITOR
    [SerializeField, HideInInspector] private TAsset _previousAsset;

    private void OnAssetChanged()
    {
        var owner = FindOwnerSO();
        if (owner == null) { _previousAsset = _asset; return; }

        if (_previousAsset != null) _previousAsset.UnregisterSubscriber(owner);
        if (_asset != null) _asset.RegisterSubscriber(owner);
        _previousAsset = _asset;
    }

    internal TAsset EditorGetAsset() => _asset;
    internal void EditorSetAsset(TAsset a) => _asset = a;
    internal TFormula EditorGetFormula() => _formula;
    internal void EditorSetFormula(TFormula f) => _formula = f;
    internal UseType EditorGetUseType() => _useType;
    internal void EditorSetUseType(UseType t) => _useType = t;
    internal TResult EditorGetDefault() => _default;
    internal void EditorSetDefault(TResult v) => _default = v;

    // Default / Formula(非空) / Asset(非空) 三模式都允許轉 Token；Token → Token 跳過
    private bool CanShowTokenKey()
    {
        return _useType == UseType.Default
            || (_useType == UseType.Formula)
            || (_useType == UseType.Asset);
    }

    [Button("轉成 Token", ButtonSizes.Small, ButtonStyle.Box)]
    [HorizontalGroup("MainRow")]
    [EnableIf("@FindOwnerSO() != null")]
    private void ConvertToToken()
    {
        if (!CanShowTokenKey()) return;
        switch (_useType)
        {
            case UseType.Formula:
                if (_formula == null) return;
                break;
            case UseType.Asset:
                if (_asset == null) return;
                break;
        }
        // Key 改由 modal popup 輸入，與「預設值」資料欄分離（避免被當成資料設定）。確認後才執行轉換。
        TokenKeyPopup.Open(DoConvertToToken);
    }

    private void DoConvertToToken(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        var owner = FindOwnerSO();
        if (owner == null)
        {
            Debug.LogError("[ConvertToToken] 找不到 owning ActionSystem。請從 Card / Effect SO inspector 開啟編輯（不要直接雙擊 FormulaAsset .asset）。");
            return;
        }

        if (!(owner is IActionSystemOwner aso))
        {
            Debug.LogError($"[ConvertToToken] owner '{owner.name}' 未實作 IActionSystemOwner。");
            return;
        }

        bool added = TryAddTokenEntry(aso, key, _useType, _formula, _asset, _default);
        if (!added)
        {
            Debug.LogError($"[ConvertToToken] 無法在 owner '{owner.name}' 加入 {typeof(TResult).Name} token entry。");
            return;
        }

        _useType = UseType.Token;
        _tokenKey = key;
        _formula = null;
        _asset = null;
        _previousAsset = null;

        aso.MarkActionSystemDirty();
        EditorUtility.SetDirty(owner);
    }

    // 6 型子類覆寫：依 mode 把對應 source 塞進新 token entry 的 Slot
    protected abstract bool TryAddTokenEntry(IActionSystemOwner owner, string key, UseType mode, TFormula formula, TAsset asset, TResult constant);

    // 共用 populate：子類傳入新 entry + entry.Slot + source 三模式資料
    protected static bool EditorPopulateAndAdd<TEntry>(
        List<TEntry> list, TEntry entry,
        FormulaSlot<TResult, TAsset, TFormula, TPack> slot,
        UseType mode, TFormula formula, TAsset asset, TResult constant)
    {
        if (list == null || entry == null || slot == null) return false;
        switch (mode)
        {
            case UseType.Default:
                slot.EditorSetUseType(UseType.Default);
                slot.EditorSetDefault(constant);
                break;
            case UseType.Formula:
                if (formula == null) return false;
                slot.EditorSetUseType(UseType.Formula);
                slot.EditorSetFormula(formula);
                break;
            case UseType.Asset:
                if (asset == null) return false;
                slot.EditorSetUseType(UseType.Asset);
                slot.EditorSetAsset(asset);
                break;
            default:
                return false;
        }
        list.Add(entry);
        return true;
    }

    private ScriptableObject FindOwnerSO()
    {
        var sel = Selection.activeObject as ScriptableObject;
        if (sel is IActionSystemOwner) return sel;
        return null;
    }

    [Button("轉成 Formula", ButtonSizes.Small, ButtonStyle.Box)]
    [ShowIf("@_isAsset")]
    [EnableIf("@_asset != null")]
    [HorizontalGroup("MainRow")]
    private void ConvertToFormula()
    {
        if (_asset == null) return;
        var src = _asset.EditorGetTarget();
        if (src == null)
        {
            Debug.LogError("[ConvertToFormula] 來源 FormulaAsset._target 為 null。");
            return;
        }

        var clone = Sirenix.Serialization.SerializationUtility.CreateCopy(src) as TFormula;
        if (clone == null)
        {
            Debug.LogError($"[ConvertToFormula] Clone 失敗（type={src.GetType().Name}）。");
            return;
        }

        var owner = FindOwnerSO();
        if (owner != null) _asset.UnregisterSubscriber(owner);

        _formula = clone;
        _asset = null;
        _previousAsset = null;
        _useType = UseType.Formula;

        if (owner != null) EditorUtility.SetDirty(owner);
    }

    [Button("轉成 Asset", ButtonSizes.Small, ButtonStyle.Box)]
    [ShowIf("@_isFormula")]
    [EnableIf("@_formula != null")]
    [HorizontalGroup("MainRow")]
    private void ConvertToAsset()
    {
        if (_formula == null) return;

        string defaultName = $"{_formula.GetType().Name}";
        string dir = ActionSystemSavePathPrefs.GetInitialDir();
        string path = EditorUtility.SaveFilePanelInProject(
            "儲存 Formula Asset",
            defaultName,
            "asset",
            "請選擇要儲存 FormulaAsset 的位置",
            dir);

        if (string.IsNullOrEmpty(path)) return;

        var newAsset = ScriptableObject.CreateInstance<TAsset>();
        newAsset.SetTarget(_formula);

        AssetDatabase.CreateAsset(newAsset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var loadedAsset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
        if (loadedAsset != null)
        {
            var monoScript = MonoScript.FromScriptableObject(loadedAsset);
            if (monoScript != null)
            {
                EditorUtility.SetDirty(loadedAsset);
                AssetDatabase.SaveAssets();
            }

            // 自動訂閱
            var owner = FindOwnerSO();
            if (owner != null) loadedAsset.RegisterSubscriber(owner);

            _asset = loadedAsset;
            _previousAsset = loadedAsset;
            _useType = UseType.Asset;
            _formula = null;
            ActionSystemSavePathPrefs.RememberDir(path);
            EditorGUIUtility.PingObject(loadedAsset);
        }
        else
        {
            Debug.LogError("建立 FormulaAsset 失敗！");
        }
    }
#endif
}

#if UNITY_EDITOR
/// <summary>
/// 「轉成 Token」用的 Key 輸入 modal 小窗（非泛型，全 Slot 共用）。與「預設值」資料欄分離 → 明確是工具操作非資料設定。
/// 確認回呼帶回 trim 後的 Key；取消/Esc 不回呼。留 runtime asm（被 FormulaSlot #if 引用，不可進 Editor/）。
/// </summary>
internal class TokenKeyPopup : EditorWindow
{
    private string key = "";
    private System.Action<string> onConfirm;
    private bool focused;

    public static void Open(System.Action<string> onConfirm)
    {
        var w = CreateInstance<TokenKeyPopup>();
        w.titleContent = new GUIContent("轉成 Token");
        var size = new Vector2(320f, 96f);
        var res = Screen.currentResolution;
        w.position = new Rect((res.width - size.x) * 0.5f, (res.height - size.y) * 0.5f, size.x, size.y);
        w.minSize = w.maxSize = size;
        w.onConfirm = onConfirm;
        w.ShowModalUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("輸入 Token Key", EditorStyles.boldLabel);

        GUI.SetNextControlName("keyField");
        key = EditorGUILayout.TextField(key);
        if (!focused) { EditorGUI.FocusTextInControl("keyField"); focused = true; }

        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool valid = !string.IsNullOrWhiteSpace(key);
            GUI.enabled = valid;
            if (GUILayout.Button("確認") || (enter && valid))
            {
                onConfirm?.Invoke(key.Trim());
                Close();
            }
            GUI.enabled = true;
            if (GUILayout.Button("取消") || esc) Close();
        }
    }
}
#endif

}
