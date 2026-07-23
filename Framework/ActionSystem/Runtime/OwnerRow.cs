namespace PinPlugin.ActionSystem
{
#if UNITY_EDITOR
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

// ActionAsset / FormulaAsset 共用的「已註冊 Owner」一列：顯示驗證狀態 + 驗證/選取按鈕。
// editor-only 型別，但住 runtime asm：被 ActionAssetBase / FormulaAsset 的 #if UNITY_EDITOR 區塊引用，
// 故不可移進 Editor/ 資料夾（跨 asm 引用會編譯失敗）。
[Serializable, HideLabel, InlineProperty]
public struct OwnerRow
{
    [HorizontalGroup("Row", Width = 0.45f)]
    [HideLabel, ReadOnly]
    public ScriptableObject Owner;

    [HorizontalGroup("Row", Width = 0.2f)]
    [HideLabel, ReadOnly, GUIColor("$StatusColor")]
    public string Status;

    public OwnerRow(ScriptableObject owner)
    {
        Owner = owner;
        Status = (owner is IActionSystemOwner aso && aso.IsActionSystemValidated())
            ? "✓ 已驗證"
            : "✗ 未驗證";
    }

    private Color StatusColor => Status != null && Status.StartsWith("✓")
        ? new Color(0.6f, 1f, 0.6f)
        : new Color(1f, 0.7f, 0.7f);

    [HorizontalGroup("Row")]
    [Button("驗證", ButtonSizes.Small)]
    private void VerifyOwner()
    {
        if (Owner is IActionSystemOwner aso)
        {
            aso.VerifyActionSystem();
            UnityEditor.EditorUtility.SetDirty(Owner);
        }
        else
        {
            UnityEngine.Debug.LogError($"[OwnerRow] '{(Owner != null ? Owner.name : "null")}' 未實作 IActionSystemOwner。");
        }
    }

    [HorizontalGroup("Row")]
    [Button("選取", ButtonSizes.Small)]
    private void SelectOwner()
    {
        if (Owner == null) return;
        UnityEditor.Selection.activeObject = Owner;
        UnityEditor.EditorGUIUtility.PingObject(Owner);
    }

    public static List<OwnerRow> Build(List<ScriptableObject> subscribers)
    {
        var rows = new List<OwnerRow>();
        if (subscribers == null) return rows;
        foreach (var s in subscribers)
            if (s != null) rows.Add(new OwnerRow(s));
        return rows;
    }

    public static void VerifyAll(List<ScriptableObject> subscribers)
    {
        if (subscribers == null) return;
        foreach (var s in subscribers)
        {
            if (s is IActionSystemOwner aso)
            {
                aso.VerifyActionSystem();
                UnityEditor.EditorUtility.SetDirty(s);
            }
        }
    }
}
#endif

}
