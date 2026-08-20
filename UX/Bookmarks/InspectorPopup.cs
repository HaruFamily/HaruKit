#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    internal static partial class Inspector
    {
        // 薄殼：將 PopupWindowContent 的 host 行為（失焦關閉、固定尺寸）映射到共用 BookmarksGUI。
        // 點 row label 透過 onItemPicked callback 主動 Close；toolbar「⧉」按鈕將 popup 升級為長駐 BookmarksWindow。
        internal class InspectorPopup : PopupWindowContent
        {
            private readonly BookmarksGUI gui = new();

            public override Vector2 GetWindowSize() => new(500, 700);

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(InspectorConstants.LabelOpenWindow, EditorStyles.toolbarButton, GUILayout.Width(28)))
                {
                    BookmarksWindow.Open();
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                gui.DrawBody(rect, () => editorWindow.Close());
            }
        }
    }
}
#endif
