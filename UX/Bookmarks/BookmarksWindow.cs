#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // 長駐 / 可 dock 的書籤視窗。與 InspectorPopup 共用 BookmarksGUI，差別僅在點 row 後不關閉（onItemPicked = null）。
    internal class BookmarksWindow : EditorWindow
    {
        private BookmarksGUI gui;

        [MenuItem("PinTools/Bookmarks Window")]
        public static void Open()
        {
            if (!Inspector.IsEnabled) return;
            var w = GetWindow<BookmarksWindow>(utility: false, title: InspectorConstants.LabelBookmarks, focus: true);
            w.minSize = new Vector2(430, 400);
        }

        [MenuItem("PinTools/Bookmarks Window", true)]
        private static bool ValidateOpen() => Inspector.IsEnabled;

        private void OnEnable()
        {
            titleContent = new GUIContent(InspectorConstants.LabelBookmarks);
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
            if (Inspector.IsEnabled)
                Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnGUI()
        {
            gui ??= new BookmarksGUI();
            gui.DrawBody(new Rect(0, 0, position.width, position.height), onItemPicked: null);
        }
    }
}
#endif
