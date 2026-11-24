#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // ====================== Shortcuts ======================================
    // Inspector 歷史/書籤的快捷鍵入口；可在 Edit > Shortcuts 中重新繫結。
    internal static class BookmarksShortcuts
    {
        // ProjectSettings 整體關閉或 Play Mode 停用時所有 shortcut 失效
        private static bool Enabled => BookmarkService.IsEnabled;

        [Shortcut("HaruFamily/Bookmarks/Previous", KeyCode.A, ShortcutModifiers.Alt)]
        private static void Previous()
        {
            if (!Enabled) return;
            BookmarkService.NavigateBack();
        }

        [Shortcut("HaruFamily/Bookmarks/Next", KeyCode.D, ShortcutModifiers.Alt)]
        private static void Next()
        {
            if (!Enabled) return;
            BookmarkService.NavigateForward();
        }

        [Shortcut("HaruFamily/Bookmarks/Toggle Bookmark", KeyCode.S, ShortcutModifiers.Alt)]
        private static void ToggleBookmark()
        {
            if (!Enabled) return;
            if (Selection.objects.Length == 0) return;
            BookmarkService.ToggleBookmarks(Selection.objects, notify: true);
        }

        [Shortcut("HaruFamily/Bookmarks/Open Bookmarks Window", KeyCode.B, ShortcutModifiers.Alt)]
        private static void OpenBookmarksWindow()
        {
            if (!Enabled) return;
            BookmarksWindow.Open();
        }
    }
}
#endif
