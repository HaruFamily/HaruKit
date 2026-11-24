#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // Inspector 工具列「書籤與紀錄 ▼」開啟的快速切換器：打開即可輸入搜尋，↑↓ 選擇、Enter 跳過去並關閉。
    // 與 BookmarksWindow 共用 BookmarksGUI；差別只在選取後會關閉（onItemPicked = Close）。
    internal class BookmarksPopup : PopupWindowContent
    {
        private readonly BookmarksGUI gui = new();
        private int storageVersion = -1;
        private int referenceVersion = -1;
        private string storageError;
        private bool wasEnabled, couldUndo;

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
            gui.FocusSearch();
            EditorApplication.update += Refresh;
        }

        public override void OnClose() => EditorApplication.update -= Refresh;

        private void Refresh()
        {
            if (editorWindow == null) return;
            bool enabled = BookmarkService.IsEnabled;
            bool canUndo = BookmarkService.CanUndo;
            if (storageVersion == BookmarkStorage.Version && referenceVersion == BookmarkService.ReferenceVersion
                && storageError == BookmarkStorage.LastError && wasEnabled == enabled && couldUndo == canUndo) return;
            storageVersion = BookmarkStorage.Version;
            referenceVersion = BookmarkService.ReferenceVersion;
            storageError = BookmarkStorage.LastError;
            wasEnabled = enabled;
            couldUndo = canUndo;
            editorWindow.Repaint();
        }

        private void Close() => editorWindow.Close();
        private void Repaint()
        {
            if (editorWindow != null) editorWindow.Repaint();
        }

        public override Vector2 GetWindowSize() => new(440, 560);

        public override void OnGUI(Rect rect) => gui.DrawBody(Close, Repaint, DrawPinButton);

        private void DrawPinButton()
        {
            if (GUILayout.Button(new GUIContent(BookmarkConstants.LabelPinWindow, BookmarkConstants.LabelPinWindowTooltip),
                    EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                BookmarksWindow.Open();
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }
        }
    }
}
#endif
