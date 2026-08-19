namespace PinPlugin.ActionSystem.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

/// <summary>
/// 就地確認框：開在按下的那顆按鈕旁邊，不是 OS 的 modal dialog。
/// `EditorUtility.DisplayDialog` 一律開在 Unity 主視窗的正中央，編輯器被拖到第二螢幕時，
/// 每按一次刪除都要把滑鼠跨螢幕拉過去再拉回來。
/// Enter＝確認、Esc＝取消，點到別處也是取消（PopupWindow 失焦自動關）。
/// </summary>
public class AGConfirmPopup : PopupWindowContent
{
    private const float Width = 268f;
    private const float Padding = 8f;
    private const float ButtonRow = 24f;

    private readonly string message;
    private readonly string confirmLabel;
    private readonly Action onConfirm;

    public AGConfirmPopup(string message, string confirmLabel, Action onConfirm)
    {
        this.message = message ?? "";
        this.confirmLabel = string.IsNullOrEmpty(confirmLabel) ? "確定" : confirmLabel;
        this.onConfirm = onConfirm;
    }

    public override Vector2 GetWindowSize()
    {
        float textHeight = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(message), Width - Padding * 2f);
        return new Vector2(Width, textHeight + ButtonRow + Padding * 3f);
    }

    public override void OnGUI(Rect rect)
    {
        var e = Event.current;
        // 鍵盤要在畫按鈕之前判：按鈕會把事件吃掉，畫完再問就問不到。
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) { editorWindow.Close(); e.Use(); return; }
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Confirm(); e.Use(); return; }
        }

        float textHeight = rect.height - ButtonRow - Padding * 3f;
        GUI.Label(new Rect(Padding, Padding, rect.width - Padding * 2f, textHeight),
            message, EditorStyles.wordWrappedLabel);

        float y = rect.height - ButtonRow - Padding;
        if (GUI.Button(new Rect(rect.width - Padding - 78f, y, 78f, 20f), confirmLabel)) Confirm();
        if (GUI.Button(new Rect(rect.width - Padding - 78f - 66f, y, 62f, 20f), "取消")) editorWindow.Close();
    }

    private void Confirm()
    {
        // 先關再做：回呼會重建圖、可能還會切焦點，讓它跑在一個已經收掉的視窗上比較乾淨。
        editorWindow.Close();
        onConfirm?.Invoke();
    }
}

}
