namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;

internal struct HGExecutionView
{
    public string Title;
    public string Description;
    public bool CanRelease;
    public bool CanCancel;
}

internal struct HGExecutionCommands
{
    public Action<Rect> Pick;
    public Action Release;
    public Action Cancel;
}

/// <summary>Execution selector and recovery controls; receives only a snapshot and bounded commands.</summary>
internal static class HGExecutionPanel
{
    public const float Height = 42f;
    public static void Draw(Rect rect, in HGExecutionView view, in HGExecutionCommands commands)
    {
        HGStyles.Fill(rect, HGStyles.Panel);
        var picker = new Rect(rect.x + 5, rect.y + 1, Mathf.Max(30, rect.width - 170), 19);
        if (GUI.Button(picker, new GUIContent(view.Title, "選擇觀察的執行鏈，或準備下一次執行的 Hold"), EditorStyles.popup)) commands.Pick(picker);
        using (new EditorGUI.DisabledScope(!view.CanRelease))
            if (GUI.Button(new Rect(rect.xMax - 160, rect.y + 1, 94, 19), "解除全部 Hold")) commands.Release();
        using (new EditorGUI.DisabledScope(!view.CanCancel))
            if (GUI.Button(new Rect(rect.xMax - 62, rect.y + 1, 57, 19), "取消執行")) commands.Cancel();
        GUI.Label(new Rect(rect.x + 5, rect.y + 21, rect.width - 10, 18), new GUIContent(view.Description, view.Description), EditorStyles.miniLabel);
    }
}
}
