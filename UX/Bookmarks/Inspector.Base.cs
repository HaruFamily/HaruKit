#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    internal static partial class Inspector
    {
        // ====================== Inspector Header ======================
        private static void DrawInspectorHeader(Editor editor)
        {
            if (editor.target == null) return;
            // ProjectSettings 開關：整體關 / 僅關 toolbar 都跳過繪製
            if (!IsEnabled || !Data.enableInspectorHeader) return;

            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            //Previous
            GUI.enabled = CanNavigateBack();
            if (GUILayout.Button(InspectorConstants.LabelPrevious, EditorStyles.miniButtonLeft, GUILayout.Width(70)))
                NavigateBack();
            GUI.enabled = true;

            //Next
            GUI.enabled = CanNavigateForward();
            if (GUILayout.Button(InspectorConstants.LabelNext, EditorStyles.miniButtonMid, GUILayout.Width(70)))
                NavigateForward();
            GUI.enabled = true;

            //PopupMenu — 用 GetRect 預先 reserve explicit rect，避免 GetLastRect 在 callback context 取錯 rect
            var popupContent = new GUIContent(InspectorConstants.LabelPopuptMenu);
            Rect popupBtnRect = GUILayoutUtility.GetRect(popupContent, EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true));
            if (GUI.Button(popupBtnRect, popupContent, EditorStyles.miniButtonMid))
            {
                PopupWindow.Show(popupBtnRect, new InspectorPopup());
            }

            bool isBookmarked = IsBookmarked(editor.target);

            //BookMarks
            if (GUILayout.Button(isBookmarked ? InspectorConstants.PrefixOnBookMarks : InspectorConstants.PrefixOffBookMarks,
                EditorStyles.miniButtonRight, GUILayout.Width(25)))
            {
                ToggleBookmark(editor.target);
            }

            GUILayout.EndHorizontal();

            Color line = EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.76f, 0.76f, 0.76f);
            Rect r = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), line);
        }
    }
}
#endif
