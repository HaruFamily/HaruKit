#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks
{
    internal static partial class BookmarkService
    {
        private const int HistoryMenuLimit = 30;

        // ====================== Inspector Header ======================
        // 每個 Inspector 標題下方的精簡工具列：◀ ▶（右鍵看紀錄）｜書籤與紀錄 ▼｜★。
        private static void DrawInspectorHeader(Editor editor)
        {
            if (editor.target == null) return;
            // Preferences 開關：整體關 / 僅關工具列都跳過繪製
            if (!IsEnabled || !Data.enableInspectorHeader) return;
            // 內嵌的 Material 編輯器（例如 MeshRenderer 底下）也會觸發標題事件：只在它本身就是選取物件時畫，避免重複出現。
            if (editor.target is Material && Array.IndexOf(Selection.objects, editor.target) < 0) return;

            Object target = ResolveInspectedTarget(editor.target);
            var e = Event.current;
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawNavigateButton(back: true, e);
            DrawNavigateButton(back: false, e);

            // 用 GetRect 預先保留 rect，讓 PopupWindow 對齊按鈕位置。
            var popupContent = new GUIContent(BookmarkConstants.LabelPopupMenu, BookmarkConstants.LabelPopupMenuTooltip);
            Rect popupRect = GUILayoutUtility.GetRect(popupContent, EditorStyles.toolbarDropDown, GUILayout.ExpandWidth(true));
            if (GUI.Button(popupRect, popupContent, EditorStyles.toolbarDropDown))
                PopupWindow.Show(popupRect, new BookmarksPopup());

            bool isBookmarked = IsBookmarked(target);
            var starContent = new GUIContent(string.Empty, isBookmarked ? BookmarkConstants.LabelStarRemoveTooltip : BookmarkConstants.LabelStarAddTooltip);
            Rect starRect = GUILayoutUtility.GetRect(starContent, EditorStyles.toolbarButton, GUILayout.Width(28));
            if (GUI.Button(starRect, starContent, EditorStyles.toolbarButton))
                ToggleBookmarks(editor.targets.Length > 1 ? Array.ConvertAll(editor.targets, ResolveInspectedTarget) : new[] { target }, notify: true);
            if (e.type == EventType.Repaint)
                BookmarkIcons.DrawStar(new Rect(starRect.center.x - 7, starRect.center.y - 7, 14, 14), isBookmarked);

            GUILayout.EndHorizontal();
        }

        private static void DrawNavigateButton(bool back, Event e)
        {
            int target = back ? Data.index - 1 : Data.index + 1;
            bool can = back ? CanNavigateBack() : CanNavigateForward();
            string name = can && target >= 0 && target < Data.history.Count ? Data.history[target]?.label : null;
            string tooltip = string.Format(back ? BookmarkConstants.LabelPreviousTooltip : BookmarkConstants.LabelNextTooltip,
                string.IsNullOrEmpty(name) ? "—" : name);
            var content = BookmarkIcons.Content(back ? BookmarkIcons.Back : BookmarkIcons.Forward, back ? "◀" : "▶", tooltip);
            Rect rect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarButton, GUILayout.Width(26));

            // 右鍵：列出紀錄直接跳（像瀏覽器在上一頁按右鍵）。
            if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
            {
                ShowHistoryMenu();
                e.Use();
            }
            using (new EditorGUI.DisabledScope(!can))
            {
                if (GUI.Button(rect, content, EditorStyles.toolbarButton))
                {
                    if (back) NavigateBack();
                    else NavigateForward();
                }
            }
        }

        private static void ShowHistoryMenu()
        {
            var menu = new GenericMenu();
            int shown = 0;
            for (int i = Data.history.Count - 1; i >= 0 && shown < HistoryMenuLimit; i--, shown++)
            {
                var item = Data.history[i];
                string label = string.IsNullOrEmpty(item?.label) ? BookmarkConstants.LabelUnknownObject : item.label;
                // GenericMenu 會把 '/' 當子選單：換成全形斜線。
                label = $"{shown + 1}. {label.Replace('/', '／')}";
                int index = i;
                menu.AddItem(new GUIContent(label), i == Data.index, () => NavigateTo(index));
            }
            if (shown == 0) menu.AddDisabledItem(new GUIContent(BookmarkConstants.LabelHistory));
            menu.ShowAsContext();
        }
    }
}
#endif
