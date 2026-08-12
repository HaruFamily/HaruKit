namespace PinPlugin.ActionSystem.Editor
{
using System;
using UnityEditor;
using UnityEngine;

/// <summary>單行文字輸入小窗（命名 Token、輸入標籤等）。確認才回呼，取消不動任何資料。</summary>
public class AGPrompt : EditorWindow
{
    private string text = "";
    private string message = "";
    private Action<string> onConfirm;
    private bool focused;

    public static void Show(string title, string message, string initial, Action<string> onConfirm)
    {
        var w = CreateInstance<AGPrompt>();
        w.titleContent = new GUIContent(title);
        w.message = message;
        w.text = initial ?? "";
        w.onConfirm = onConfirm;

        var size = new Vector2(340f, 104f);
        var res = Screen.currentResolution;
        w.position = new Rect((res.width - size.x) * 0.5f, (res.height - size.y) * 0.5f, size.x, size.y);
        w.minSize = w.maxSize = size;
        w.ShowModalUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(message, EditorStyles.boldLabel);

        GUI.SetNextControlName("agPromptField");
        text = EditorGUILayout.TextField(text);
        if (!focused) { EditorGUI.FocusTextInControl("agPromptField"); focused = true; }

        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool valid = !string.IsNullOrWhiteSpace(text);
            GUI.enabled = valid;
            if (GUILayout.Button("確認") || (enter && valid))
            {
                onConfirm?.Invoke(text.Trim());
                Close();
            }
            GUI.enabled = true;
            if (GUILayout.Button("取消") || esc) Close();
        }
    }
}

}
