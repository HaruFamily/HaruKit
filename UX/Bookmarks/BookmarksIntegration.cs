#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // Project / Hierarchy 整合：已加入書籤的項目右側顯示 ★，右鍵選單可加入或移除書籤。
    [InitializeOnLoad]
    internal static class BookmarksIntegration
    {
        private const float BadgeSize = 12f;

        static BookmarksIntegration()
        {
#if UNITY_2022_1_OR_NEWER
            // 依物件判斷，子資產（Sprite、FBX 裡的 Mesh）各自標記。
            EditorApplication.projectWindowItemInstanceOnGUI -= OnProjectItem;
            EditorApplication.projectWindowItemInstanceOnGUI += OnProjectItem;
#else
            // 舊版只有 guid：檔案內任一物件是書籤就標記。
            EditorApplication.projectWindowItemOnGUI -= OnProjectItemByGuid;
            EditorApplication.projectWindowItemOnGUI += OnProjectItemByGuid;
#endif
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItem;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
        }

        private static bool BadgesEnabled => Event.current.type == EventType.Repaint
            && BookmarkStorage.IsAvailable && BookmarkStorage.Data.showItemBadges;

#if UNITY_2022_1_OR_NEWER
        private static void OnProjectItem(int instanceId, Rect rect)
        {
            if (BadgesEnabled && BookmarkService.IsBookmarkedInstance(instanceId)) DrawBadge(rect, grid: rect.height > 20f);
        }
#else
        private static void OnProjectItemByGuid(string guid, Rect rect)
        {
            if (BadgesEnabled && BookmarkService.IsBookmarkedFile(guid)) DrawBadge(rect, grid: rect.height > 20f);
        }
#endif

        private static void OnHierarchyItem(int instanceId, Rect rect)
        {
            if (BadgesEnabled && BookmarkService.IsBookmarkedInstance(instanceId)) DrawBadge(rect, grid: false);
        }

        // 列表模式畫在列尾；格狀（大圖示）模式畫在縮圖右上角。
        private static void DrawBadge(Rect rect, bool grid)
        {
            Rect badge = grid
                ? new Rect(rect.xMax - BadgeSize - 2, rect.y + 2, BadgeSize, BadgeSize)
                : new Rect(rect.xMax - BadgeSize - 2, rect.y + (rect.height - BadgeSize) * 0.5f, BadgeSize, BadgeSize);
            BookmarkIcons.DrawStar(badge, on: true);
        }

        // ====================== 右鍵選單 ======================
        private const string AssetsMenu = "Assets/HaruFamily/Toggle Bookmark in Bookmarks";
        private const string GameObjectMenu = "GameObject/HaruFamily/Toggle Bookmark in Bookmarks";

        [MenuItem(AssetsMenu, false, 2000)]
        private static void ToggleAssets() => BookmarkService.ToggleBookmarks(Selection.objects, notify: true);

        [MenuItem(AssetsMenu, true)]
        private static bool ValidateAssets() => BookmarkService.IsEnabled && Selection.objects.Length > 0;

        // GameObject 選單在多選時會對每個物件各呼叫一次：只在 context 是 activeObject 時執行一次。
        [MenuItem(GameObjectMenu, false, 49)]
        private static void ToggleGameObjects(MenuCommand command)
        {
            if (command.context != null && command.context != Selection.activeObject) return;
            BookmarkService.ToggleBookmarks(Selection.objects, notify: true);
        }

        [MenuItem(GameObjectMenu, true)]
        private static bool ValidateGameObjects() => BookmarkService.IsEnabled && Selection.objects.Length > 0;
    }
}
#endif
