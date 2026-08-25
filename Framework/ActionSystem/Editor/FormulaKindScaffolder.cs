namespace HaruFamily.Framework.ActionSystem.Editor
{
#if UNITY_EDITOR
using HaruFamily.Framework.ActionSystem;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 新增一種 result 型別的 token 族（Formula base / FormulaAsset / Slot / Entry）腳手架。
/// 顯式吐 .cs，無 SourceGenerator / 反射魔法；產完手動把 ForEachKind 接線（視窗會印出片段）。
/// 入口：Tools/ActionSystem/新增 Formula 型別。
/// </summary>
public class FormulaKindScaffolder : EditorWindow
{
    private string kind = "Int";          // 識別前綴（型別名用）
    private string resultType = "int";    // TResult，C# 型別字面（int / float / Vector3…）
    private string packType = "TPack";    // 目標 TPack 型別名
    private string outputFolder = "Assets";
    private string outputNamespace = "";

    [MenuItem("PinTools/ActionSystem/Add Formula Type")]
    private static void Open() => GetWindow<FormulaKindScaffolder>("新增 Formula 型別");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("產生一種 result 型別的 token 族", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Kind=識別前綴（如 Int / Damage）\nResultType=求值回傳型別（如 int / Vector3）\nPackType=目標 TPack 型別名",
            MessageType.Info);

        kind = EditorGUILayout.TextField("Kind 前綴", kind);
        resultType = EditorGUILayout.TextField("Result 型別", resultType);
        packType = EditorGUILayout.TextField("Pack 型別", packType);
        outputNamespace = EditorGUILayout.TextField("Namespace（可空）", outputNamespace);

        using (new EditorGUILayout.HorizontalScope())
        {
            outputFolder = EditorGUILayout.TextField("輸出資料夾", outputFolder);
            if (GUILayout.Button("選…", GUILayout.Width(50)))
            {
                var abs = EditorUtility.OpenFolderPanel("選擇輸出資料夾", outputFolder, "");
                if (!string.IsNullOrEmpty(abs)) outputFolder = ToAssetsRelative(abs);
            }
        }

        EditorGUILayout.Space();
        GUI.enabled = IsValid(out var why);
        if (GUILayout.Button("產生", GUILayout.Height(32))) Generate();
        GUI.enabled = true;
        if (!string.IsNullOrEmpty(why)) EditorGUILayout.HelpBox(why, MessageType.Warning);
    }

    private bool IsValid(out string why)
    {
        why = null;
        if (!IsIdent(kind)) { why = "Kind 前綴須為合法識別字（字母開頭）。"; return false; }
        if (string.IsNullOrWhiteSpace(resultType)) { why = "Result 型別不可空。"; return false; }
        if (!IsIdent(packType)) { why = "Pack 型別須為合法識別字。"; return false; }
        if (!outputFolder.Replace('\\', '/').StartsWith("Assets")) { why = "輸出資料夾須在 Assets 下。"; return false; }
        return true;
    }

    private void Generate()
    {
        var dir = outputFolder.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(dir)) Directory.CreateDirectory(dir);

        var path = $"{dir}/{kind}Asset.cs";
        if (File.Exists(path) &&
            !EditorUtility.DisplayDialog("覆寫確認", $"{path} 已存在，覆寫？", "覆寫", "取消"))
            return;

        File.WriteAllText(path, Template());
        AssetDatabase.ImportAsset(path);
        AssetDatabase.Refresh();

        // 標註化之後不需要任何接線：Token 是節點上的一個名字，不是每個 kind 各一份宣告清單。
        Debug.Log($"[FormulaKindScaffolder] 已產生 {path}（無須接線）");

        var obj = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        if (obj != null) EditorGUIUtility.PingObject(obj);
    }

    private string Template() =>
$@"using System;
using System.Collections.Generic;
using HaruFamily.Framework.ActionSystem;
using UnityEngine;

{NamespaceOpen()}

// ===== {kind} 公式族（result 型別：{resultType}）— FormulaKindScaffolder 產生 =====
// 具體算式：class XxxFormula : {kind}Formula {{ 覆寫 OnEvaluate }}。

/// <summary>{kind} 算式分類 base：節點選單只列此類下的 {resultType} 公式。</summary>
public abstract class {kind}Formula : FormulaBase<{resultType}, {packType}> {{ }}

/// <summary>{kind} 公式經 Graph 抽出成共用 SO 時的資產型別。</summary>
public class {kind}Asset : FormulaAsset<{resultType}, {packType}> {{ }}

/// <summary>{kind} 求值槽：沒接節點＝常數，接了節點＝公式或資產。</summary>
[Serializable]
public class {kind}Slot : FormulaSlot<{resultType}, {kind}Asset, {kind}Formula, {packType}>
{{
    public {kind}Slot() {{ }}
    public {kind}Slot({resultType} defaultValue) : base(defaultValue) {{ }}
}}
{NamespaceClose()}
";

    private string NamespaceOpen() => string.IsNullOrWhiteSpace(outputNamespace)
        ? ""
        : $"namespace {outputNamespace}\n{{";

    private string NamespaceClose() => string.IsNullOrWhiteSpace(outputNamespace) ? "" : "}";

    private static bool IsIdent(string s) => !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"^[A-Za-z_]\w*$");

    private static string ToAssetsRelative(string abs)
    {
        abs = abs.Replace('\\', '/');
        var data = Application.dataPath.Replace('\\', '/');
        return abs.StartsWith(data) ? "Assets" + abs.Substring(data.Length) : abs;
    }
}
#endif

}
