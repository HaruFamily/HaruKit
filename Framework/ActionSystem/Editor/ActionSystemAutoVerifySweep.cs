namespace PinPlugin.ActionSystem.Editor
{
#if UNITY_EDITOR
using PinPlugin.ActionSystem;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ActionSystemAutoVerifySweep
{
    static ActionSystemAutoVerifySweep()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        SweepAndReport();
    }

    [MenuItem("PinTools/ActionSystem/驗證全部 Owner")]
    private static void SweepFromMenu() => SweepAndReport(true);

    /// <summary>
    /// 全專案 Owner 重驗。**不看已存的「已驗證」旗標**：它的正確性取決於引用到的共用資產，
    /// 而資產可能在編輯器外被改（git pull、Inspector 直接動）。跳過已驗證的就是信任一份可能過期的快取。
    /// 驗證本身不碰檔案；只有結果真的翻轉的 Owner 才寫檔。
    /// </summary>
    private static void SweepAndReport(bool notifyWhenClean = false)
    {
        var failures = new List<string>();
        var verified = 0;
        var touched = 0;
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so is not IActionSystemOwner owner) continue;
            if (!owner.IsAutoVerifyOnPlay()) continue;

            bool was = owner.IsActionSystemValidated();
            owner.VerifyActionSystem();
            bool now = owner.IsActionSystemValidated();
            verified++;

            if (!now) failures.Add($"  - {so.name} ({path})");
            if (was == now) continue;

            EditorUtility.SetDirty(so);
            touched++;
        }

        if (touched > 0) AssetDatabase.SaveAssets();

        if (failures.Count > 0)
        {
            Debug.LogError(
                $"[AutoVerify] 掃描 {verified} 個 owner，{failures.Count} 個驗證未通過：\n"
                + string.Join("\n", failures));
            return;
        }

        if (notifyWhenClean) Debug.Log($"[AutoVerify] 掃描 {verified} 個 owner，全部通過。");
    }
}
#endif

}
