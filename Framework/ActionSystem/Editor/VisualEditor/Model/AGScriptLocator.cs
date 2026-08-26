namespace HaruFamily.Framework.ActionSystem.Editor
{
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 由節點的程式型別跳到宣告它的那一行原始碼。
/// </summary>
// 不能用 MonoScript 的檔名對應：一個 .cs 裝十幾個節點類別（ActionAsset.cs、EntityIdListAsset.cs 都是），
// 而且它們是純 [Serializable] 類別不是 ScriptableObject，Unity 根本沒建立型別→腳本的索引。
// 行號一律點下去當場用宣告文字算，不存進任何檔案：程式一改行號就跟著對，沒有會過期的座標。
public static class AGScriptLocator
{
    // 型別 → 宣告位置。查一次就記著，domain reload 會清空重算。
    private static readonly Dictionary<Type, Located> cache = new();

    // 組件名 → 該組件 asmdef 所在資料夾。全專案只有數十個 .asmdef，掃一次就夠。
    private static Dictionary<string, string> asmFolders;

    private struct Located
    {
        public MonoScript Script;
        public int Line;
    }

    /// <summary>用預設腳本編輯器打開宣告該型別的那一行。</summary>
    public static void Open(Type type)
    {
        var found = Locate(type);
        if (found.Script == null)
        {
            Debug.LogWarning($"[ActionGraph] 找不到 {type?.FullName ?? "null"} 的原始碼，可能只存在於編譯好的 DLL。");
            return;
        }
        AssetDatabase.OpenAsset(found.Script, found.Line);
    }

    /// <summary>先照檔名猜（絕大多數節點所在的檔名就是族名），沒中再掃該型別所屬組件的資料夾。</summary>
    private static Located Locate(Type type)
    {
        if (type == null) return default;
        if (cache.TryGetValue(type, out var hit)) return hit;

        // 泛型型別的 Name 帶 `1 這種後綴，宣告文字裡沒有，要先切掉。
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);

        var declaration = new Regex($@"\b(?:class|struct|record|interface|enum)\s+{Regex.Escape(name)}\b");

        var found = Search(AssetDatabase.FindAssets($"{name} t:MonoScript"), name, declaration);
        // 沒中就退回逐檔比對，但只掃這個型別所屬組件的資料夾——全專案有近萬個 .cs，
        // 每個都要 LoadAssetAtPath + 讀全文，掃完是秒級停頓。
        if (found.Script == null)
            found = Search(AssetDatabase.FindAssets("t:MonoScript", SearchFolders(type)), name, declaration);

        cache[type] = found;
        return found;
    }

    /// <summary>型別所屬組件的資料夾；沒有 asmdef（Assembly-CSharp 之類）就退回整個 Assets。</summary>
    private static string[] SearchFolders(Type type)
    {
        asmFolders ??= BuildAsmFolders();
        string assembly = type.Assembly.GetName().Name;
        if (asmFolders.TryGetValue(assembly, out string folder)) return new[] { folder };
        return new[] { "Assets" };
    }

    /// <summary>掃全專案的 .asmdef，建立組件名 → 資料夾對照。</summary>
    private static Dictionary<string, string> BuildAsmFolders()
    {
        var map = new Dictionary<string, string>();
        // asmdef 是 JSON，第一個 "name" 就是組件名；為了不依賴 UnityEditorInternal，直接當文字讀。
        var nameField = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"");

        foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || string.IsNullOrEmpty(asset.text)) continue;

            var match = nameField.Match(asset.text);
            if (!match.Success) continue;

            int slash = path.LastIndexOf('/');
            if (slash <= 0) continue;

            map[match.Groups[1].Value] = path.Substring(0, slash);
        }
        return map;
    }

    /// <summary>在一批腳本裡找宣告；先用 Contains 篩掉大多數檔案，正則只跑在真的提到這個名字的檔上。</summary>
    private static Located Search(string[] guids, string name, Regex declaration)
    {
        if (guids == null) return default;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs")) continue;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script == null) continue;

            string text = script.text;
            if (string.IsNullOrEmpty(text) || text.IndexOf(name, StringComparison.Ordinal) < 0) continue;

            var match = declaration.Match(text);
            if (!match.Success) continue;

            return new Located { Script = script, Line = LineOf(text, match.Index) };
        }
        return default;
    }

    /// <summary>字元位置換算成 1-based 行號（AssetDatabase.OpenAsset 吃的就是 1-based）。</summary>
    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }
}

}
