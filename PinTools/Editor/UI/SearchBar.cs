#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PinTools
{
    internal class SearchBar
    {
        public const string LabelSearch = "搜尋";
        public const string LabelClearSearch = "清除";
        public string Keyword { get; private set; } = string.Empty;

        // 內嵌在外層 helpBox 內使用：不再自帶 helpBox（避免雙層框），TextField 採 EditorGUILayout 預設樣式（黑底）
        public void Draw()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(LabelSearch, GUILayout.Width(36));
            Keyword = EditorGUILayout.TextField(Keyword);

            if (GUILayout.Button(LabelClearSearch, EditorStyles.miniButton, GUILayout.Width(40)))
                Keyword = string.Empty;

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
