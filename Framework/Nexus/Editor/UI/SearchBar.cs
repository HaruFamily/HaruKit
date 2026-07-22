#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PinPlugin.Nexus.Editor
{
    /// <summary>Editor toolbar search input with case-insensitive matching.</summary>
    internal sealed class SearchBar
    {
        public string Keyword { get; private set; } = string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Keyword);

        public bool Matches(string text)
            => IsEmpty || (text != null && text.IndexOf(Keyword, System.StringComparison.OrdinalIgnoreCase) >= 0);

        public void DrawToolbar(float width = 160f)
        {
            Keyword = GUILayout.TextField(Keyword, EditorStyles.toolbarSearchField, GUILayout.Width(width));
            if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                Keyword = string.Empty;
        }
    }
}
#endif
