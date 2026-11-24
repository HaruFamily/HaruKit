#if UNITY_EDITOR
using UnityEditor.IMGUI.Controls;

namespace HaruFamily.UX.Bookmarks
{
    // 書籤與紀錄共用的搜尋框（Unity 內建 SearchField：附清除鈕，Ctrl+F 自動聚焦）。
    internal sealed class SearchBar
    {
        private SearchField field;
        private bool requestFocus;
        public string Keyword { get; private set; } = string.Empty;
        public bool HasFocus => field != null && field.HasFocus();

        public void RequestFocus() => requestFocus = true;
        public void Clear() => Keyword = string.Empty;

        public void DrawToolbar()
        {
            field ??= new SearchField();
            if (requestFocus)
            {
                requestFocus = false;
                field.SetFocus();
            }
            Keyword = field.OnToolbarGUI(Keyword) ?? string.Empty;
        }
    }
}
#endif
