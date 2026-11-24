#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // Unity 內建圖示。名稱在不同 Unity 版本可能不存在：找不到時回傳 null，呼叫端改用文字。
    // 用 FindTexture 而不是 IconContent：後者找不到會在 Console 印錯誤。
    internal static class BookmarkIcons
    {
        private static readonly Dictionary<string, Texture> cache = new();

        public static Texture Get(string name)
        {
            string key = (EditorGUIUtility.isProSkin ? "d:" : "l:") + name;
            if (cache.TryGetValue(key, out var texture)) return texture;
            texture = EditorGUIUtility.isProSkin ? EditorGUIUtility.FindTexture("d_" + name) : null;
            if (texture == null) texture = EditorGUIUtility.FindTexture(name);
            cache[key] = texture;
            return texture;
        }

        public static Texture Star
        {
            get
            {
                var texture = Get("Favorite On Icon");
                return texture != null ? texture : Get("Favorite Icon");
            }
        }

        public static Texture Back => Get("back");
        public static Texture Forward => Get("forward");
        public static Texture Folder => Get("Folder Icon");
        public static Texture FolderOpened => Get("FolderOpened Icon");
        public static Texture Plus => Get("Toolbar Plus");
        public static Texture Menu => Get("_Menu");
        public static Texture Trash => Get("TreeEditor.Trash");

        public static GUIContent Content(Texture texture, string fallback, string tooltip = null)
            => texture != null ? new GUIContent(texture, tooltip) : new GUIContent(fallback, tooltip);

        // 已加入：亮星；未加入：同一顆星變淡。沒有圖示時用 ★ / ☆。
        public static void DrawStar(Rect rect, bool on)
        {
            var texture = Star;
            if (texture == null)
            {
                GUI.Label(rect, on ? BookmarkConstants.FallbackStarOn : BookmarkConstants.FallbackStarOff, EditorStyles.centeredGreyMiniLabel);
                return;
            }
            var color = GUI.color;
            if (!on) GUI.color = new Color(color.r, color.g, color.b, color.a * 0.3f);
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            GUI.color = color;
        }
    }
}
#endif
