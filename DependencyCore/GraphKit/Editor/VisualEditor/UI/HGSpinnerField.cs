namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>候選數量編輯器。拖曳只修改預覽，放開才回傳一份集合供視窗交易寫回。</summary>
internal sealed class HGSpinnerField
{
    internal const float Height = 106f;
    private const float ColumnGap = 8f;
    private const float CountWidth = 40f;
    private const string ColumnsPreference = "HaruGraph.Spinner.Columns.";
    private object owner;
    private int hotControl;
    private int previousIndex;
    private bool removing;
    private List<object> preview;

    internal bool IsPainting => owner != null;

    internal static int PreferredColumns(string library)
        => string.IsNullOrWhiteSpace(library) ? 5 : Mathf.Max(1, EditorPrefs.GetInt(ColumnsPreference + library, 5));

    internal static void SavePreferredColumns(string library, int columns)
        => EditorPrefs.SetInt(ColumnsPreference + library, Mathf.Max(1, columns));

    internal static List<HGOptionsCatalog.Entry> Entries(List<HGOptionsCatalog.Entry> catalog, IList current)
    {
        var entries = new List<HGOptionsCatalog.Entry>(catalog);
        if (current != null)
            foreach (var value in current)
                if (!entries.Exists(entry => Equals(entry.Value, value)))
                    entries.Add(new HGOptionsCatalog.Entry(value, "", "未在候選目錄", "⚠ " + HGOptionsCatalog.Display(value)));
        return entries;
    }

    private static float LabelHeight(float width, int columns, List<HGOptionsCatalog.Entry> entries)
    {
        float pitch = Mathf.Max(8f, width - 12f) / Mathf.Max(1, columns);
        float labelWidth = Mathf.Max(1f, pitch - Mathf.Min(ColumnGap, pitch * 0.15f));
        float height = 16f;
        foreach (var entry in entries)
            height = Mathf.Max(height, HGStyles.SpinnerLabel.CalcHeight(new GUIContent(entry.Label), labelWidth));
        return Mathf.Min(28f, height);
    }

    internal static float MeasureHeight(float width, int columns, List<HGOptionsCatalog.Entry> catalog, IList current)
    {
        var entries = Entries(catalog, current);
        columns = Mathf.Clamp(columns, 1, Mathf.Max(1, entries.Count));
        return 90f + LabelHeight(width, columns, entries);
    }

    internal void Cancel()
    {
        if (hotControl != 0 && GUIUtility.hotControl == hotControl) GUIUtility.hotControl = 0;
        owner = null;
        preview = null;
        hotControl = 0;
    }

    internal static bool TryElementType(Type type, out Type element)
    {
        element = type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : null;
        return element != null && HGOptionsCatalog.TryValueType(element, out _, out bool multiple) && !multiple;
    }

    internal static List<object> Copy(IList source)
    {
        var result = new List<object>();
        if (source != null) foreach (var item in source) result.Add(item);
        return result;
    }

    internal static int Count(IList source, object value)
    {
        int count = 0;
        if (source != null) foreach (var item in source) if (Equals(item, value)) count++;
        return count;
    }

    internal static void SetCount(List<object> values, object value, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        int existing = Count(values, value);
        for (int i = values.Count - 1; i >= 0 && existing > count; i--)
        {
            if (!Equals(values[i], value)) continue;
            values.RemoveAt(i);
            existing--;
        }
        while (existing < count) { values.Add(value); existing++; }
    }

    internal static IList Materialize(Type type, List<object> values)
    {
        if (!TryElementType(type, out var element)) throw new ArgumentException("HGSpinner 僅支援 List<T> 或一維 T[]。");
        if (type.IsArray)
        {
            var array = Array.CreateInstance(element, values.Count);
            for (int i = 0; i < values.Count; i++) array.SetValue(values[i], i);
            return array;
        }
        var list = (IList)Activator.CreateInstance(type);
        foreach (var value in values) list.Add(value);
        return list;
    }

    internal bool Draw(Rect rect, object key, Type type, IList current, List<HGOptionsCatalog.Entry> catalog,
        HGSpinnerAttribute settings, int columns, ref int start, out IList result)
    {
        result = null;
        int control = GUIUtility.GetControlID(FocusType.Passive, rect);
        var e = Event.current;
        if (e.type == EventType.Repaint)
        {
            HGStyles.RoundedFill(rect, HGStyles.SpinnerBorder, 6f);
            HGStyles.RoundedFill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), HGStyles.SpinnerBackground, 5f);
        }
        rect = new Rect(rect.x + 6f, rect.y + 4f, Mathf.Max(8f, rect.width - 12f), rect.height - 8f);
        bool active = ReferenceEquals(owner, key);
        if (active && (GUIUtility.hotControl != hotControl || !GUI.enabled)) { Cancel(); active = false; }
        if (active && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Cancel();
            e.Use();
            return false;
        }

        // 未在目錄內的既有值仍可見、可減少；不自動清除，也不允許增加未知值。
        var entries = Entries(catalog, current);
        if (entries.Count == 0)
        {
            GUI.Label(rect, "（候選目錄為空）", EditorStyles.miniLabel);
            return false;
        }

        columns = Mathf.Clamp(columns, 1, entries.Count);
        float labelHeight = LabelHeight(rect.width + 12f, columns, entries);
        if (start < 0)
        {
            int first = entries.FindIndex(entry => Count(current, entry.Value) > 0);
            start = first < 0 ? (entries.Count - columns) / 2 : Mathf.Max(0, first - columns / 2);
        }
        int maxStart = Mathf.Max(0, entries.Count - columns);
        start = Mathf.Clamp(start, 0, maxStart);
        var sliderRect = new Rect(rect.x, rect.y + 66f + labelHeight, rect.width, 16f);
        using (new EditorGUI.DisabledScope(active || maxStart == 0))
        {
            float position = GUI.HorizontalSlider(sliderRect, start, 0f, Mathf.Max(1, maxStart));
            start = Mathf.Clamp(Mathf.RoundToInt(position), 0, maxStart);
        }

        IList shown = active ? preview : current;
        float pitch = rect.width / columns;
        float gap = Mathf.Min(ColumnGap, pitch * 0.15f);
        float width = pitch - gap;
        float stripLeft = rect.x + gap * 0.5f;
        float countWidth = Mathf.Min(CountWidth, width);
        float countInset = (width - countWidth) * 0.5f;
        int hit = -1;
        if (e.mousePosition.y >= rect.y + 20f && e.mousePosition.y < rect.y + 44f)
        {
            int column = Mathf.FloorToInt((e.mousePosition.x - stripLeft) / pitch);
            float localX = e.mousePosition.x - stripLeft - column * pitch;
            if (column >= 0 && column < columns && localX >= countInset && localX < countInset + countWidth)
                hit = start + column;
        }
        if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && hit >= 0)
        {
            Cancel();
            owner = key;
            hotControl = control;
            GUIUtility.hotControl = control;
            GUIUtility.keyboardControl = 0;
            preview = Copy(current);
            int lower = hit < catalog.Count ? settings.CountMin : 0;
            removing = Count(preview, entries[hit].Value) > lower;
            previousIndex = hit;
            Paint(hit);
            active = true;
            shown = preview;
            e.Use();
        }
        else if (active && e.type == EventType.MouseDrag)
        {
            if (hit >= 0)
            {
                for (int i = Math.Min(previousIndex, hit); i <= Math.Max(previousIndex, hit); i++) Paint(i);
                previousIndex = hit;
            }
            e.Use();
        }

        for (int i = start; i < entries.Count && i < start + columns; i++)
        {
            var entry = entries[i];
            int count = Count(shown, entry.Value);
            bool known = i < catalog.Count;
            int min = known ? settings.CountMin : 0;
            int max = known ? settings.CountMax : count;
            float x = stripLeft + (i - start) * pitch;
            string tooltip = entry.Label + "\n" + HGOptionsCatalog.Display(entry.Value) + "\n" + entry.Tooltip
                + "\n" + entry.Source + "\n點選／拖曳切換數量；上下箭頭逐次調整。";
            using (new EditorGUI.DisabledScope(active))
            {
                using (new EditorGUI.DisabledScope(!known || count >= max))
                    if (GUI.Button(new Rect(x + countInset, rect.y + 2f, countWidth, 16f), new GUIContent("▴", tooltip), HGStyles.SpinnerArrow))
                    { result = Change(entry.Value, Math.Max(min, count + 1)); return true; }
                using (new EditorGUI.DisabledScope(count <= min))
                    if (GUI.Button(new Rect(x + countInset, rect.y + 46f, countWidth, 14f), new GUIContent("▾", tooltip), HGStyles.SpinnerArrow))
                    { result = Change(entry.Value, Math.Max(min, Math.Min(max, count - 1))); return true; }
            }
            var cell = new Rect(x + countInset, rect.y + 20f, countWidth, 24f);
            if (e.type == EventType.Repaint)
                HGStyles.RoundedFill(cell, Color.Lerp(HGStyles.NodeBody,
                    count > 0 ? HGStyles.NodeBorderSelected : HGStyles.Muted, count > 0 ? 0.3f : 0.1f), 4f);
            bool outOfRange = known && (count < min || count > max);
            if (outOfRange) tooltip += $"\n數量超出設定：{min}～{max}";
            GUI.Label(cell, new GUIContent(count.ToString(), tooltip), outOfRange ? HGStyles.SpinnerNumberError : HGStyles.SpinnerNumber);
            GUI.Label(new Rect(x, rect.y + 62f, width, labelHeight), new GUIContent(entry.Label, tooltip), HGStyles.SpinnerLabel);
        }
        var summary = new List<string>();
        bool invalid = false;
        for (int i = 0; i < entries.Count; i++)
        {
            int count = Count(shown, entries[i].Value);
            if (count > 0) summary.Add(entries[i].Label + " ×" + count);
            if (i < catalog.Count && (count < settings.CountMin || count > settings.CountMax)) invalid = true;
        }
        string full = string.Join(", ", summary);
        string text = $"顯示 {start + 1}–{start + columns} / {entries.Count}\n"
            + (invalid ? "⚠ 數量超出設定\n" : "") + (summary.Count == 0 ? "（未選取）" : full);
        GUI.Label(sliderRect, new GUIContent("", text), GUIStyle.none);
        if (active && e.type == EventType.MouseUp && e.button == 0)
        {
            result = Materialize(type, preview);
            Cancel();
            e.Use();
            return true;
        }
        return false;

        void Paint(int index)
        {
            object value = entries[index].Value;
            if (index >= catalog.Count) { if (removing) SetCount(preview, value, 0); return; }
            int count = Count(preview, value);
            if (removing) SetCount(preview, value, settings.CountMin);
            else if (count <= settings.CountMin)
                SetCount(preview, value, settings.CountMin < settings.CountMax ? settings.CountMin + 1 : settings.CountMin);
        }

        IList Change(object value, int count)
        {
            var values = Copy(current);
            SetCount(values, value, count);
            return Materialize(type, values);
        }
    }
}
}
