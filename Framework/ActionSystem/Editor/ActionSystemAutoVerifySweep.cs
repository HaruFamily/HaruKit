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

    private static void SweepAndReport()
    {
        var failures = new List<string>();
        var verified = 0;
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so is not IActionSystemOwner owner) continue;
            if (!owner.IsAutoVerifyOnPlay()) continue;
            if (owner.IsActionSystemValidated()) continue;

            owner.VerifyActionSystem();
            verified++;
            if (!owner.IsActionSystemValidated())
                failures.Add($"  - {so.name} ({path})");
        }

        if (failures.Count > 0)
        {
            Debug.LogError(
                $"[AutoVerify] Play 前掃描：{failures.Count} 個 owner 驗證未通過：\n"
                + string.Join("\n", failures));
        }
    }
}
#endif

}
