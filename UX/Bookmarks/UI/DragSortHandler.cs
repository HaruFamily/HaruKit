#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    internal sealed class DragSortHandler
    {
        private const string BookmarkDragKey = "HaruFamily.Bookmarks.Item";
        private const float DragThresholdSquared = 36f;
        private int pressedControl;
        private Vector2 pressedPosition;
        private UnityEngine.Object pressedObject;
        private ObjectRef pressedBookmark;

        // 呼叫端先畫 Open / 星號，再把剩餘區域交給這個 control，避免 GUI.Button 搶走拖曳事件。
        public bool HandleItem(Rect rect, UnityEngine.Object payload, string title, ObjectRef bookmark)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !rect.Contains(e.mousePosition) || payload == null) return false;
                    pressedControl = control;
                    pressedPosition = e.mousePosition;
                    pressedObject = payload;
                    pressedBookmark = bookmark;
                    GUIUtility.hotControl = control;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control || pressedControl != control) return false;
                    if (pressedObject != payload || pressedBookmark != bookmark)
                    {
                        GUIUtility.hotControl = 0;
                        pressedControl = 0;
                        e.Use();
                        return false;
                    }
                    if ((e.mousePosition - pressedPosition).sqrMagnitude >= DragThresholdSquared && payload != null)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new[] { payload };
                        DragAndDrop.SetGenericData(BookmarkDragKey, bookmark);
                        DragAndDrop.StartDrag(string.IsNullOrEmpty(title) ? "Bookmark" : title);
                        GUIUtility.hotControl = 0;
                        pressedControl = 0;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (e.button != 0 || GUIUtility.hotControl != control || pressedControl != control) return false;
                    GUIUtility.hotControl = 0;
                    pressedControl = 0;
                    e.Use();
                    return pressedObject == payload && pressedBookmark == bookmark && rect.Contains(e.mousePosition);
                case EventType.Ignore:
                    if (pressedControl != control) return false;
                    if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
                    pressedControl = 0;
                    break;
            }
            return false;
        }

        internal static ObjectRef GetDraggedBookmark()
        {
            var references = DragAndDrop.objectReferences;
            if (references == null || references.Length != 1 || references[0] == null) return null;
            if (DragAndDrop.GetGenericData(BookmarkDragKey) is ObjectRef item
                && JSONStorage.Data.bookmarks.Contains(item))
                return item;
            return Inspector.FindBookmark(references[0]);
        }
    }
}
#endif
