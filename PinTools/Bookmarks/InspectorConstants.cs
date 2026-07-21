#if UNITY_EDITOR

namespace PinTools.Inspector
{
    // ====================== Constants ======================================
    internal static class InspectorConstants
    {
        public const string DefaultMissing = "(Missing)";

        public const string LabelBookmarks = "書籤";
        public const string LabelHistory = "紀錄";
        public const string LabelClear = "清除";
        public const string LabelOpen = "開啟";
        public const string LabelPrevious = "◀ 上一頁";
        public const string LabelNext = "下一頁 ▶";
        public const string LabelPopuptMenu = "最近使用 ▼";
        public const string LabelOpenWindow = "⧉";

        public const string PrefixAsset = "[Asset]";
        public const string PrefixScene = "[Scene]";
        public const string PrefixOnBookMarks = "★";
        public const string PrefixOffBookMarks = "☆";

        public const string LabelYes = "確定";
        public const string LabelNo = "取消";

        public const string LabelClearConfirmTitle = "確認清除";
        public const string LabelClearConfirmMsg = "確定要清除「{0}」全部項目嗎？此動作無法復原。";

        public const string LabelUncategorized = "未分類";
        public const string LabelAddFolder = "+ 新增資料夾";
        public const string LabelNewFolderPlaceholder = "資料夾名稱";
        public const string LabelMoveToFolder = "移動到資料夾";
        public const string LabelMoveToUncategorized = "移到未分類";
        public const string LabelRenameFolder = "重新命名";
        public const string LabelDeleteFolder = "刪除資料夾";
        public const string LabelAddBookmark = "加入書籤";
        public const string LabelRemoveBookmark = "移除書籤";
        public const string LabelDeleteFolderConfirmTitle = "確認刪除資料夾";
        public const string LabelDeleteFolderConfirmMsg = "確定要刪除資料夾「{0}」嗎？\n內含書籤 {1} 筆會搬回「未分類」。";
        public const string LabelRenameFolderTitle = "重新命名資料夾";
        public const string LabelRenameFolderMsg = "請輸入新名稱：";
        public const string LabelInvalidFolderName = "資料夾名稱無效（空白、重複，或為保留名稱）。";
    }
}
#endif
