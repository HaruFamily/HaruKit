namespace HaruFamily.Framework.LogicGraph.Editor
{
#if UNITY_EDITOR
using HaruFamily.Framework.LogicGraph;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;

public static class LogicGraphAutoVerifySweep
{
    [MenuItem("HaruFamily/LogicGraph/Verify All Owners")]
    private static void SweepFromMenu() => SweepAndReport(true);

    /// <summary>
    /// 全專案 Owner 重驗。**不看已存的「已驗證」旗標**：它的正確性取決於引用到的共用資產，
    /// 而資產可能在編輯器外被改（git pull、Inspector 直接動）。跳過已驗證的就是信任一份可能過期的快取。
    /// 驗證本身不碰檔案；只有結果真的翻轉的 Owner 才寫檔。
    /// 由選單明確觸發；未完成的圖可以保存，不在進入 Play 時掃描全專案。
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
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !LogicGraphEditor.HasGraph(type) && !typeof(IGraphOwner).IsAssignableFrom(type)) continue;
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (!HGOwnerValidation.CanVerify(so)) continue;

            bool now = HGOwnerValidation.Verify(so, out bool changed);
            verified++;

            if (!now) failures.Add($"  - {so.name} ({path})");
            if (!changed) continue;

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
