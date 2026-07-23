namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable, HideReferenceObjectPicker]
[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ActionSlot")]
public class ActionSlot<TPack>
{
    [SerializeField]
    [HorizontalGroup("MainRow", Width = 0.25f)]
    [HideLabel]
    [EnumToggleButtons]
    private UseType _useType = UseType.UnActive;

    [ShowInInspector, SerializeReference]
    [HorizontalGroup("SubRow")]
    [ShowIf("_isFormula")]
    [HideLabel]
    [TypeSelectorSettings(ShowCategories = true)]
    [FormerlySerializedAs("_target")]
    private ActionBase<TPack> _formula;

    [SerializeField, InlineEditor]
    [HorizontalGroup("SubRow")]
    [ShowIf("_isAsset")]
    [HideLabel]
#if UNITY_EDITOR
    [OnValueChanged("OnAssetChanged")]
#endif
    private ActionAssetBase<TPack> _asset;

#if UNITY_EDITOR
    [SerializeField, HideInInspector] private ActionAssetBase<TPack> _previousAsset;

    internal int EditorUseTypeRaw => (int)_useType;
    internal bool EditorHasFormula => _formula != null;
    internal bool EditorHasAsset => _asset != null;

    private void OnAssetChanged()
    {
        var owner = FindOwnerSO();
        if (owner == null) { _previousAsset = _asset; return; }

        if (_previousAsset != null) _previousAsset.UnregisterSubscriber(owner);
        if (_asset != null) _asset.RegisterSubscriber(owner);
        _previousAsset = _asset;
    }

    private static ScriptableObject FindOwnerSO()
    {
        var sel = Selection.activeObject as ScriptableObject;
        if (sel is IActionSystemOwner) return sel;
        return null;
    }
#endif

    private bool _isFormula => _useType == UseType.Formula;
    private bool _isAsset => _useType == UseType.Asset;

    public ActionSlot() { }   // 序列化 / Inspector 路徑

    /// <summary>程式建構（mod 匯入等執行期路徑）：以「公式」模式直接包一個 action。</summary>
    public ActionSlot(ActionBase<TPack> formula)
    {
        _formula = formula;
        _useType = UseType.Formula;
    }

    public enum UseType
    {
        [LabelText("無效")] UnActive,
        [LabelText("公式")] Formula,
        [LabelText("資產")] Asset,
    }

    public async UniTask Execute(TPack pack, TokenCache<TPack> tokens)
    {
        switch (_useType)
        {
            case UseType.Formula: if (_formula != null) await _formula.Execute(pack, tokens); break;
            case UseType.Asset: if (_asset != null) await _asset.Execute(pack, tokens); break;
            default: return;
        }
    }

#if UNITY_EDITOR
    [Button("轉成 Formula", ButtonSizes.Small, ButtonStyle.Box)]
    [ShowIf("@_isAsset")]
    [EnableIf("@_asset != null")]
    [HorizontalGroup("MainRow")]
    private void ConvertToFormula()
    {
        if (_asset == null) return;
        var src = _asset.EditorGetAction();
        if (src == null)
        {
            Debug.LogError("[ConvertToFormula] 來源 ActionAsset._action 為 null。");
            return;
        }

        var clone = Sirenix.Serialization.SerializationUtility.CreateCopy(src) as ActionBase<TPack>;
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

        // Project 端通常有唯一 concrete subclass（綁定 TPack 的 ActionAsset），用 TypeCache 找。
        var baseType = typeof(ActionAssetBase<TPack>);
        var concreteType = TypeCache.GetTypesDerivedFrom(baseType).FirstOrDefault(t => !t.IsAbstract);
        if (concreteType == null)
        {
            Debug.LogError($"[ConvertToAsset] 找不到 {baseType.Name} 的非 abstract 子類。");
            return;
        }

        string defaultName = $"{_formula.GetType().Name}";
        string dir = ActionSystemSavePathPrefs.GetInitialDir();
        string path = EditorUtility.SaveFilePanelInProject(
            "儲存 Action Asset",
            defaultName,
            "asset",
            "請選擇要儲存 ActionAsset 的位置",
            dir);

        if (string.IsNullOrEmpty(path)) return;

        var newAsset = ScriptableObject.CreateInstance(concreteType) as ActionAssetBase<TPack>;
        if (newAsset == null)
        {
            Debug.LogError($"[ConvertToAsset] CreateInstance({concreteType.Name}) 失敗。");
            return;
        }
        newAsset.SetTarget(_formula);

        AssetDatabase.CreateAsset(newAsset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var loadedAsset = AssetDatabase.LoadAssetAtPath(path, concreteType) as ActionAssetBase<TPack>;
        if (loadedAsset != null)
        {
            var monoScript = MonoScript.FromScriptableObject(loadedAsset);
            if (monoScript != null)
            {
                EditorUtility.SetDirty(loadedAsset);
                AssetDatabase.SaveAssets();
            }

            _asset = loadedAsset;
            _useType = UseType.Asset;
            _formula = null;

            var owner = FindOwnerSO();
            if (owner != null)
            {
                loadedAsset.RegisterSubscriber(owner);
                _previousAsset = loadedAsset;
            }

            ActionSystemSavePathPrefs.RememberDir(path);
            EditorGUIUtility.PingObject(loadedAsset);
        }
        else
        {
            Debug.LogError("建立 ActionAsset 失敗！");
        }
    }
#endif
}

}
