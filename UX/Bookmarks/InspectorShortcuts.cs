#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // ====================== Shortcuts ======================================
    // Inspector 歷史/書籤的快捷鍵入口；可在 Edit > Shortcuts 中重新繫結。
    internal static class InspectorShortcuts
    {
        // ProjectSettings 整體關閉或 Play Mode 停用時所有 shortcut 失效
        private static bool Enabled => Inspector.IsEnabled;

        [Shortcut("PinTools/Inspector/Previous", KeyCode.A, ShortcutModifiers.Alt)]
        private static void Previous()
        {
            if (!Enabled) return;
            Inspector.NavigateBack();
        }

        [Shortcut("PinTools/Inspector/Next", KeyCode.D, ShortcutModifiers.Alt)]
        private static void Next()
        {
            if (!Enabled) return;
            Inspector.NavigateForward();
        }

        [Shortcut("PinTools/Inspector/Toggle Bookmark", KeyCode.S, ShortcutModifiers.Alt)]
        private static void ToggleBookmark()
        {
            if (!Enabled) return;
            if (Selection.activeObject == null) return;
            Inspector.ToggleBookmark(Selection.activeObject);
        }

        [Shortcut("PinTools/Inspector/Open Bookmarks Window", KeyCode.B, ShortcutModifiers.Alt)]
        private static void OpenBookmarksWindow()
        {
            if (!Enabled) return;
            BookmarksWindow.Open();
        }
    }
}
#endif
