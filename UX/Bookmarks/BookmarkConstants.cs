#if UNITY_EDITOR

namespace HaruFamily.UX.Bookmarks
{
    // ====================== Constants ======================================
    internal static class BookmarkConstants
    {
        public const string DefaultMissing = "(Missing)";
        public const string LabelUnknownObject = "(未知物件)";
        public const string LabelSceneNotLoaded = "(場景未開啟：{0})";
        public const string LabelOpenSceneTooltip = "開啟所在場景並選取此物件";
        public const string LabelSystemDisabled = "書籤功能目前已停用，可在 Preferences > HaruFamily > Bookmarks 開啟。";

        public const string LabelBookmarks = "書籤";
        public const string LabelHistory = "紀錄";
        public const string LabelClear = "清除";
        public const string LabelOpen = "開啟";
        public const string LabelPrevious = "上一頁";
        public const string LabelNext = "下一頁";
        public const string LabelPreviousTooltip = "上一頁：{0}（Alt+A，右鍵看紀錄）";
        public const string LabelNextTooltip = "下一頁：{0}（Alt+D，右鍵看紀錄）";
        public const string LabelPopupMenu = "書籤與紀錄";
        public const string LabelPopupMenuTooltip = "快速切換書籤與紀錄（Alt+B 開啟視窗）";
        public const string LabelPinWindow = "釘選成視窗";
        public const string LabelPinWindowTooltip = "改成可停靠的書籤視窗";
        public const string LabelStarAddTooltip = "加入書籤（Alt+S）";
        public const string LabelStarRemoveTooltip = "移除書籤（Alt+S）";
        public const string FallbackStarOn = "★";
        public const string FallbackStarOff = "☆";

        public const string LabelYes = "確定";
        public const string LabelNo = "取消";

        public const string LabelClearConfirmTitle = "確認清除";
        public const string LabelClearConfirmMsg = "確定要清除「{0}」全部項目嗎？";

        public const string LabelUncategorized = "未分類";
        public const string LabelNewFolder = "新增資料夾";
        public const string LabelNewFolderPlaceholder = "新資料夾名稱（Enter 建立，Esc 取消）";
        public const string LabelMoveToFolder = "移動到資料夾";
        public const string LabelMoveManyToFolder = "移動 {0} 筆到資料夾";
        public const string LabelAddBookmark = "加入書籤";
        public const string LabelAddBookmarkTo = "加入書籤到資料夾";
        public const string LabelRemoveBookmark = "移除書籤";
        public const string LabelRemoveBookmarks = "移除 {0} 筆書籤";
        public const string LabelShowInProject = "在 Project／Hierarchy 中顯示";
        public const string LabelRenameFolder = "重新命名";
        public const string LabelDeleteFolder = "刪除資料夾";
        public const string LabelRenameFolderTitle = "重新命名資料夾";
        public const string LabelInvalidFolderName = "資料夾名稱無效（空白、重複，或為保留名稱）。";
        public const string LabelClearMissing = "清除 {0} 筆失效書籤";
        public const string LabelClearAllBookmarks = "清除全部書籤";

        public const string LabelEmptyBookmarks = "還沒有書籤。\n按 Inspector 上的 ★、Alt+S，或把物件拖到這裡。";
        public const string LabelNoMatches = "沒有符合「{0}」的項目";

        public const string LabelUndo = "復原";
        public const string LabelDismissTooltip = "關閉";
        public const string LabelNotifyAddedOne = "已加入書籤：{0}";
        public const string LabelNotifyAddedMany = "已加入 {0} 筆書籤";
        public const string LabelNotifyRemovedOne = "已移除書籤：{0}";
        public const string LabelNotifyRemovedMany = "已移除 {0} 筆書籤";
        public const string LabelDragMany = "{0} 筆";
        public const string LabelNotifyFolderDeleted = "已刪除資料夾「{0}」";

        public const string LabelJustNow = "剛剛";
        public const string LabelMinutesAgo = "{0} 分鐘前";
        public const string LabelHoursAgo = "{0} 小時前";
        public const string LabelDaysAgo = "{0} 天前";
    }
}
#endif
