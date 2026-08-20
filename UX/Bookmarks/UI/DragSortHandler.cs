#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    internal class DragSortHandler
    {
        private readonly string dragKey;

        public DragSortHandler(string key)
        {
            dragKey = key;
        }

        // drag-out only：純拖曳出去（如塞給 Inspector ObjectField），不接收 drop、不排序。
        // 適用「歷史紀錄」等順序不可變但需 drag-to-assign 的清單。
        public static void BeginDragOut(Rect row, UnityEngine.Object payload, string title = null)
        {
            var e = Event.current;
            // 限定左鍵：button=1（右鍵）不該被當 drag 啟動，否則右鍵 context menu 會被吃掉
            if (e.type != EventType.MouseDown || e.button != 0 || !row.Contains(e.mousePosition)) return;
            if (payload == null) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new[] { payload };
            DragAndDrop.StartDrag(string.IsNullOrEmpty(title) ? "Drag" : title);
            e.Use();
        }

        // payload 非 null 時同時把該物件塞進 DragAndDrop.objectReferences，使 Inspector ObjectField 能接收為賦值來源。
        // title 用於 StartDrag 顯示名稱（hover 時 Unity 會帶出該字串）。
        public void HandleDrag(Rect row, int index, System.Action<int, int> onSorted,
            UnityEngine.Object payload = null, string title = null)
        {
            var e = Event.current;

            // 限定左鍵啟動 drag；右鍵預留給 context menu
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(dragKey, index);
                if (payload != null)
                    DragAndDrop.objectReferences = new[] { payload };
                DragAndDrop.StartDrag(string.IsNullOrEmpty(title) ? "DragSort" : title);
                e.Use();
            }
            else if (e.type == EventType.DragUpdated && row.Contains(e.mousePosition))
            {
                // 只在 row 內顯示 Move visual + e.Use()；不 contains 時讓事件流過去，給上層（例如 folder header）有機會接收 drop。
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                e.Use();
            }
            else if (e.type == EventType.DragPerform && row.Contains(e.mousePosition))
            {
                // 跨 handler（例如跨 folder）拖曳時，本 handler 的 dragKey 取不到 source index → 不接 drop。
                // 防 NRE 並避免錯誤排序；上層（如 folder header drop）會處理跨域 case。
                if (DragAndDrop.GetGenericData(dragKey) is int from)
                {
                    int to = index;
                    if (from != to)
                        onSorted?.Invoke(from, to);

                    DragAndDrop.AcceptDrag();
                    e.Use();
                }
            }
        }
    }

}
#endif
