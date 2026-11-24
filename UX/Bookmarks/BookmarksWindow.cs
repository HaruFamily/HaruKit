#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // 長駐 / 可 dock 的書籤視窗。與 BookmarksPopup 共用 BookmarksGUI，差別僅在點 row 後不關閉（onItemPicked = null）。
    internal class BookmarksWindow : EditorWindow
    {
        private BookmarksGUI gui;
        private int storageVersion = -1;
        private int referenceVersion = -1;
        private bool wasEnabled, couldUndo;
        private int timeTick = -1;
        private string storageError;

        [MenuItem("HaruFamily/Bookmarks/Open")]
        public static void Open()
        {
            if (!BookmarkService.IsEnabled) return;
            var w = GetWindow<BookmarksWindow>(utility: false, title: BookmarkConstants.LabelBookmarks, focus: true);
            w.minSize = new Vector2(320, 300);
        }

        [MenuItem("HaruFamily/Bookmarks/Open", true)]
        private static bool ValidateOpen() => BookmarkService.IsEnabled;

        private void OnEnable()
        {
            titleContent = new GUIContent(BookmarkConstants.LabelBookmarks);
            minSize = new Vector2(320, 300);
            wantsMouseMove = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshSelectionRepaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            RefreshSelectionRepaint();
            Repaint();
        }

        private void RefreshSelectionRepaint()
        {
            // EditorWindow 在 Selection 變動時不會自動 repaint；Play Mode 停用時解除訂閱。
            Selection.selectionChanged -= Repaint;
            if (BookmarkService.IsEnabled)
                Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnInspectorUpdate()
        {
            bool enabled = BookmarkService.IsEnabled;
            bool canUndo = BookmarkService.CanUndo;
            // 每 30 秒刷新一次，讓紀錄的「幾分鐘前」跟著更新。
            int tick = (int)(EditorApplication.timeSinceStartup / 30);
            if (storageVersion == BookmarkStorage.Version && referenceVersion == BookmarkService.ReferenceVersion
                && wasEnabled == enabled && storageError == BookmarkStorage.LastError && couldUndo == canUndo && timeTick == tick) return;
            couldUndo = canUndo;
            timeTick = tick;
            storageVersion = BookmarkStorage.Version;
            referenceVersion = BookmarkService.ReferenceVersion;
            storageError = BookmarkStorage.LastError;
            if (wasEnabled != enabled) RefreshSelectionRepaint();
            wasEnabled = enabled;
            Repaint();
        }

        private void OnGUI()
        {
            gui ??= new BookmarksGUI();
            gui.DrawBody(onItemPicked: null, repaint: Repaint);
        }
    }
}
#endif
