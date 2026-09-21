namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>從 Unity 文字序列化保留的 rid 連結定位遺失引用；不使用已退成 -2 的 SerializedProperty 值推測。</summary>
internal static class HGSerializedReferenceLocations
{
    private sealed class Frame
    {
        internal int Indent;
        internal string Path;
        internal bool Item;
        internal int NextIndex;
    }

    internal static List<string> Read(Object owner, HashSet<long> missing)
    {
        string assetPath = AssetDatabase.GetAssetPath(owner);
        string path = string.IsNullOrEmpty(assetPath) ? null
            : Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), assetPath);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)
            || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(owner, out string _, out long fileId))
            throw new InvalidOperationException($"無法取得遺失引用的原始序列化位置，未清除資料：{owner.name} ({assetPath})");
        return Parse(File.ReadLines(path), fileId, missing);
    }

    // 僅讀取 Unity YAML 的區塊、集合及 rid 連結，不解析型別資料、不反序列化未知 class。
    internal static List<string> Parse(IEnumerable<string> lines, long fileId, HashSet<long> missing)
    {
        var result = new List<string>();
        var frames = new List<Frame>();
        bool first = true, active = false, found = false, registry = false, foundRegistry = false;
        int recordIndent = -1, dataIndent = -1, scalarIndent = -1;
        long recordId = 0;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (first)
            {
                first = false;
                if (!line.StartsWith("%YAML", StringComparison.Ordinal) && !line.StartsWith("--- !u!", StringComparison.Ordinal))
                    throw new InvalidDataException("遺失引用定位需要 Unity 文字序列化資料，原資料未清除。");
            }
            if (line.StartsWith("--- !u!", StringComparison.Ordinal))
            {
                if (active) break;
                int ampersand = line.IndexOf('&');
                string id = ampersand < 0 ? "" : line.Substring(ampersand + 1).Split(' ')[0];
                active = TryId(id, out long currentId) && currentId == fileId;
                found |= active;
                continue;
            }
            if (!active) continue;
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            string text = line.Substring(indent).TrimEnd();
            if (text.StartsWith("#", StringComparison.Ordinal)) continue;
            if (indent == 0) continue; // MonoBehaviour 等 Unity 文件根，不是 C# 欄位。

            if (indent == 2 && text == "references:")
            {
                registry = true;
                frames.Clear();
                scalarIndent = -1;
                continue;
            }
            if (registry)
            {
                if (recordIndent < 0 && text.StartsWith("version:", StringComparison.Ordinal))
                {
                    if (text.Substring(8).Trim() != "2")
                        throw new InvalidDataException("不支援此 managed-reference 序列化版本，原資料未清除。");
                    continue;
                }
                if (text == "RefIds:" && dataIndent < 0) { foundRegistry = true; continue; }
                if (text.StartsWith("- rid:", StringComparison.Ordinal) && (recordIndent < 0 || indent == recordIndent))
                {
                    if (!TryId(text.Substring(6).Trim(), out recordId))
                        throw new InvalidDataException("無法解析 managed-reference 記錄 ID，原資料未清除。");
                    recordIndent = indent;
                    dataIndent = -1;
                    scalarIndent = -1;
                    frames.Clear();
                    continue; // 宣告 rid 不是指向該 rid 的引用。
                }
                if (recordIndent >= 0 && indent == recordIndent + 2 && text == "data:")
                {
                    dataIndent = indent;
                    continue;
                }
                if (dataIndent < 0 || indent <= dataIndent) continue;
            }

            // 多行字串／scalar 的延伸行不當成物件欄位，避免 note 裡的 rid 文字被誤認。
            if (scalarIndent >= 0 && indent > scalarIndent) continue;
            scalarIndent = -1;
            bool item = text.StartsWith("- ", StringComparison.Ordinal);
            while (frames.Count > 0)
            {
                var last = frames[frames.Count - 1];
                if (last.Indent < indent || (item && last.Indent == indent && !last.Item)) break;
                frames.RemoveAt(frames.Count - 1);
            }
            string parent = frames.Count == 0 ? "" : frames[frames.Count - 1].Path;
            if (item)
            {
                if (frames.Count == 0) throw new InvalidDataException("無法定位序列化集合，原資料未清除。");
                var collection = frames[frames.Count - 1];
                parent = Join(parent, $"Array.data[{collection.NextIndex++}]");
                frames.Add(new Frame { Indent = indent, Path = parent, Item = true });
                indent += 2;
                text = text.Substring(2).Trim();
            }
            bool flow = text.StartsWith("{rid:", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal);
            if (flow) text = text.Substring(1, text.Length - 2).Trim();
            int colon = text.IndexOf(':');
            if (colon < 0) { scalarIndent = indent; continue; }
            string key = text.Substring(0, colon).Trim();
            string value = text.Substring(colon + 1).Trim();
            string path = Join(parent, key);
            if (key == "rid" && TryId(value, out long target) && missing.Contains(target))
                result.Add(registry ? Join($"managedReferences[{recordId}]", parent) : parent);
            else if (value.StartsWith("{rid:", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal)
                && TryId(value.Substring(5, value.Length - 6).Trim(), out target) && missing.Contains(target))
                result.Add(registry ? Join($"managedReferences[{recordId}]", path) : path);

            if (value.Length == 0) frames.Add(new Frame { Indent = indent, Path = path });
            else scalarIndent = indent;
        }
        if (!found || !foundRegistry) throw new InvalidDataException("找不到此 Owner 的 managed-reference 序列化記錄，原資料未清除。");
        return result;
    }

    private static string Join(string parent, string child)
        => string.IsNullOrEmpty(parent) ? child : string.IsNullOrEmpty(child) ? parent : parent + "." + child;

    private static bool TryId(string value, out long id)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}
}
