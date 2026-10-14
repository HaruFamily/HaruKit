namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>選取即寫回；多選保留面板供連續勾選，關閉不撤回修改。</summary>
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
    private const float RowHeight = 24f;
    private const float Padding = 6f;

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

    public override Vector2 GetWindowSize()
    {
        float width = 260f;
        foreach (var entry in entries)
        {
            string label = string.IsNullOrEmpty(entry.Group) ? entry.Label : entry.Group + " / " + entry.Label;
            string raw = HGOptionsCatalog.Display(entry.Value);
            float detailWidth = entry.Label == raw ? 0f : EditorStyles.miniLabel.CalcSize(new GUIContent(raw)).x + 16f;
            width = Mathf.Max(width, EditorStyles.label.CalcSize(new GUIContent(label)).x + detailWidth + 56f);
        }
        foreach (var value in unknown)
            width = Mathf.Max(width, EditorStyles.label.CalcSize(new GUIContent("⚠ 未在候選庫：" + HGOptionsCatalog.Display(value))).x + 56f);
        int rows = entries.Count + unknown.Count + (!multiple && allowEmpty && !fieldType.IsValueType ? 1 : 0);
        float height = 36f + Mathf.Clamp(rows, 1, 12) * RowHeight + (multiple ? 34f : Padding);
        if (!string.IsNullOrEmpty(error)) height = Mathf.Max(height, 140f);
        return new Vector2(Mathf.Min(width, 420f), height);
    }

    public override void OnOpen() => editorWindow.wantsMouseMove = true;

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

        if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();
        GUILayout.BeginArea(new Rect(Padding, Padding, rect.width - Padding * 2f, rect.height - Padding * 2f));
        Rect searchRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
        GUI.SetNextControlName("HGOptionsSearch");
        string nextSearch = EditorGUI.TextField(searchRect, search, EditorStyles.toolbarSearchField);
        if (nextSearch != search) { search = nextSearch; scroll = Vector2.zero; }
        if (string.IsNullOrEmpty(search) && Event.current.type == EventType.Repaint)
            GUI.Label(new Rect(searchRect.x + 18f, searchRect.y, searchRect.width - 24f, searchRect.height), "搜尋…", EditorStyles.miniLabel);
        if (focusSearch) { EditorGUI.FocusTextInControl("HGOptionsSearch"); focusSearch = false; }
        GUILayout.Space(6f);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        if (!multiple && allowEmpty && !fieldType.IsValueType && DrawOption(new GUIContent("（清除）"), false))
            Commit(fieldType == typeof(string) ? "" : null);
        int visible = 0;
        foreach (var value in unknown)
        {
            if (!Matches(HGOptionsCatalog.Display(value))) continue;
            visible++;
            var content = new GUIContent("⚠ 未在候選庫：" + HGOptionsCatalog.Display(value), "保留既有值；多選可取消勾選，單選請選取替代值。");
            if (multiple) DrawToggle(value, content);
            else EditorGUILayout.LabelField(content);
        }
        foreach (var entry in entries)
        {
            if (!Matches(entry.Label + " " + HGOptionsCatalog.Display(entry.Value) + " " + entry.Group + " " + entry.Source)) continue;
            visible++;
            string label = string.IsNullOrEmpty(entry.Group) ? entry.Label : entry.Group + " / " + entry.Label;
            string path = entry.Value is UnityEngine.Object asset ? AssetDatabase.GetAssetPath(asset) : "";
            string tooltip = (string.IsNullOrEmpty(entry.Tooltip) ? "" : entry.Tooltip + "\n")
                + HGOptionsCatalog.Display(entry.Value) + "\n" + entry.Source + (string.IsNullOrEmpty(path) ? "" : "\n" + path);
            var content = new GUIContent(label, tooltip);
            string detail = entry.Label == HGOptionsCatalog.Display(entry.Value) ? "" : HGOptionsCatalog.Display(entry.Value);
            if (multiple) DrawToggle(entry.Value, content, detail);
            else if (DrawOption(content, selected.Contains(entry.Value), detail))
                Commit(entry.Value);
        }
        if (visible == 0) EditorGUILayout.LabelField("沒有符合的候選");
        EditorGUILayout.EndScrollView();
        if (multiple)
        {
            GUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"已選 {selected.Count}", EditorStyles.miniLabel, GUILayout.Height(22f));
            using (new EditorGUI.DisabledScope(selected.Count == 0))
                if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(40f), GUILayout.Height(22f)))
                {
                    selected.Clear();
                    CommitSelection();
                }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.EndArea();
    }

    private bool Matches(string text) => string.IsNullOrEmpty(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

    private void DrawToggle(object value, GUIContent content, string detail = "")
    {
        bool before = selected.Contains(value);
        bool after = DrawOption(content, before, detail) ? !before : before;
        if (before == after) return;
        if (after) selected.Add(value);
        else selected.RemoveAll(item => Equals(item, value));
        CommitSelection();
    }

    private void CommitSelection()
    {
        var result = (IList)Activator.CreateInstance(fieldType);
        foreach (var value in selected) result.Add(value);
        Commit(result);
    }

    private static bool DrawOption(GUIContent content, bool selected, string detail = "")
    {
        Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint && row.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f));
        if (Event.current.type == EventType.Repaint)
            EditorStyles.toggle.Draw(new Rect(row.x + 4f, row.y + (RowHeight - 16f) * 0.5f, 16f, 16f), GUIContent.none, false, false, selected, false);
        float detailWidth = string.IsNullOrEmpty(detail) ? 0f : Mathf.Min(row.width * 0.45f, EditorStyles.miniLabel.CalcSize(new GUIContent(detail)).x + 8f);
        GUI.Label(new Rect(row.x + 24f, row.y, row.width - 24f - detailWidth, row.height), content, EditorStyles.label);
        if (detailWidth > 0f)
        {
            Color previous = GUI.color;
            GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * 0.6f);
            GUI.Label(new Rect(row.xMax - detailWidth, row.y, detailWidth, row.height), new GUIContent(detail, content.tooltip), EditorStyles.miniLabel);
            GUI.color = previous;
        }
        return GUI.Button(row, GUIContent.none, GUIStyle.none);
    }

    private void Commit(object value)
    {
        error = apply(value);
        if (string.IsNullOrEmpty(error) && !multiple) editorWindow.Close();
    }
}
}
