#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks
{
    internal enum RowClick { None, Click, DoubleClick }

    internal struct DragPayload
    {
        public Object[] objects;
        public string key;
        public object data;
        public string title;
    }

    // 列的點選與拖曳起點。呼叫端先畫列內按鈕，再把剩餘區域交給這個 control，避免按鈕搶走拖曳事件。
    internal sealed class DragSortHandler
    {
        public const string BookmarksKey = "HaruFamily.Bookmarks.Items";
        public const string FolderKey = "HaruFamily.Bookmarks.Folder";
        private const float DragThresholdSquared = 36f;
        private int pressedControl;
        private Vector2 pressedPosition;
        private object pressedIdentity;
        private bool pressedDouble;

        // identity：這一列的身分（書籤引用或資料夾名稱），用來確認按下與放開是同一列。
        // payload：開始拖曳時才呼叫，回傳要拖的內容。
        public RowClick HandleItem(Rect rect, object identity, Func<DragPayload> payload)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || identity == null || !rect.Contains(e.mousePosition)) return RowClick.None;
                    pressedControl = control;
                    pressedPosition = e.mousePosition;
                    pressedIdentity = identity;
                    pressedDouble = e.clickCount >= 2;
                    GUIUtility.hotControl = control;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control || pressedControl != control) return RowClick.None;
                    if (!Equals(pressedIdentity, identity))
                    {
                        Reset();
                        e.Use();
                        return RowClick.None;
                    }
                    if ((e.mousePosition - pressedPosition).sqrMagnitude >= DragThresholdSquared && payload != null)
                    {
                        var p = payload();
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = p.objects ?? Array.Empty<Object>();
                        if (p.key != null) DragAndDrop.SetGenericData(p.key, p.data);
                        DragAndDrop.StartDrag(string.IsNullOrEmpty(p.title) ? "Bookmark" : p.title);
                        Reset();
                    }
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (e.button != 0 || GUIUtility.hotControl != control || pressedControl != control) return RowClick.None;
                    Reset();
                    e.Use();
                    if (!Equals(pressedIdentity, identity) || !rect.Contains(e.mousePosition)) return RowClick.None;
                    return pressedDouble ? RowClick.DoubleClick : RowClick.Click;
                case EventType.Ignore:
                    if (pressedControl != control) return RowClick.None;
                    Reset();
                    break;
            }
            return RowClick.None;
        }

        private void Reset()
        {
            if (GUIUtility.hotControl == pressedControl) GUIUtility.hotControl = 0;
            pressedControl = 0;
        }

        // 從本視窗拖出的書籤（仍存在於資料中的才算）；不是書籤拖曳時回傳 null。
        internal static List<ObjectRef> GetDraggedBookmarks()
        {
            if (DragAndDrop.GetGenericData(BookmarksKey) is not List<ObjectRef> items) return null;
            var result = items.FindAll(item => BookmarkStorage.Data.bookmarks.Contains(item));
            return result.Count > 0 ? result : null;
        }

        internal static string GetDraggedFolder() => DragAndDrop.GetGenericData(FolderKey) as string;
    }
}
#endif
