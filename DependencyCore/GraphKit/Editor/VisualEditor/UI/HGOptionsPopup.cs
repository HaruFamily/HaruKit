namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>單選立即套用；多選在面板內勾選後一次套用，取消不修改圖。</summary>
internal sealed class HGOptionsPopup : PopupWindowContent
{
    private readonly List<HGOptionsCatalog.Entry> entries;
    private readonly List<object> selected = new();
    private readonly List<object> unknown = new();
    private readonly bool multiple;
    private readonly bool allowEmpty;
    private readonly Type fieldType;
    private readonly Func<object, string> apply;
    private readonly Func<bool> isCurrent;
    private string error;
    private string search = "";
    private Vector2 scroll;
    private bool focusSearch = true;

    internal HGOptionsPopup(string library, Type fieldType, object current, bool allowEmpty,
        Func<bool> isCurrent, Func<object, string> apply)
    {
        this.fieldType = fieldType;
        this.allowEmpty = allowEmpty;
        this.apply = apply;
        this.isCurrent = isCurrent;
        HGOptionsCatalog.TryValueType(fieldType, out var valueType, out multiple);
        HGOptionsCatalog.TryGet(library, valueType, out entries, out error);
        if (multiple)
        {
            if (current is IList list) foreach (var value in list) selected.Add(value);
        }
        else selected.Add(current);
        foreach (var value in selected)
        {
            if (!multiple && allowEmpty && (value == null || value is string text && text.Length == 0)) continue;
            if (!entries.Exists(entry => Equals(entry.Value, value)) && !unknown.Contains(value)) unknown.Add(value);
        }
    }

    public override Vector2 GetWindowSize() => new(420f, 380f);

    public override void OnGUI(Rect rect)
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Event.current.Use();
            editorWindow.Close();
            return;
        }
        if (!isCurrent())
        {
            EditorGUILayout.HelpBox("圖已變更，請關閉並重新開啟選單。", MessageType.Info);
            if (GUILayout.Button("關閉")) editorWindow.Close();
            return;
        }
        if (!string.IsNullOrEmpty(error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("關閉")) editorWindow.Close();
            return;
        }

        GUI.SetNextControlName("HGOptionsSearch");
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (focusSearch) { EditorGUI.FocusTextInControl("HGOptionsSearch"); focusSearch = false; }
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        if (!multiple && allowEmpty && !fieldType.IsValueType && GUILayout.Button("（清除）", EditorStyles.miniButton))
            Commit(fieldType == typeof(string) ? "" : null);
        foreach (var value in unknown)
        {
            if (!Matches(HGOptionsCatalog.Display(value))) continue;
            var content = new GUIContent("⚠ 未在候選庫：" + HGOptionsCatalog.Display(value), "保留既有值；多選可取消勾選，單選請選取替代值。");
            if (multiple) DrawToggle(value, content);
            else EditorGUILayout.LabelField(content);
        }
        int visible = 0;
        foreach (var entry in entries)
        {
            if (!Matches(entry.Label + " " + entry.Group + " " + entry.Source)) continue;
            visible++;
            string label = string.IsNullOrEmpty(entry.Group) ? entry.Label : entry.Group + " / " + entry.Label;
            string path = entry.Value is UnityEngine.Object asset ? AssetDatabase.GetAssetPath(asset) : "";
            var content = new GUIContent(label, entry.Source + (string.IsNullOrEmpty(path) ? "" : "\n" + path));
            if (multiple) DrawToggle(entry.Value, content);
            else if (GUILayout.Button(new GUIContent((selected.Contains(entry.Value) ? "✓ " : "") + label, content.tooltip), EditorStyles.miniButton))
                Commit(entry.Value);
        }
        if (visible == 0) EditorGUILayout.LabelField("沒有符合的候選");
        EditorGUILayout.EndScrollView();
        if (multiple)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"已選 {selected.Count}");
            if (GUILayout.Button("清空")) selected.Clear();
            if (GUILayout.Button("取消")) editorWindow.Close();
            if (GUILayout.Button("套用"))
            {
                var result = (IList)Activator.CreateInstance(fieldType);
                foreach (var value in selected) result.Add(value);
                Commit(result);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private bool Matches(string text) => string.IsNullOrEmpty(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

    private void DrawToggle(object value, GUIContent content)
    {
        bool before = selected.Contains(value);
        bool after = GUILayout.Toggle(before, content);
        if (before == after) return;
        if (after) selected.Add(value);
        else selected.RemoveAll(item => Equals(item, value));
    }

    private void Commit(object value)
    {
        error = apply(value);
        if (string.IsNullOrEmpty(error)) editorWindow.Close();
    }
}
}
