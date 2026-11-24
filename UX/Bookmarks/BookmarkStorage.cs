#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("HaruFamily.UX.Bookmarks.Editor.Tests")]

namespace HaruFamily.UX.Bookmarks
{
    // ====================== ObjectRef ======================================
    [System.Serializable]
    // 識別規則見 BookmarkService.cs 開頭說明。
    internal class ObjectRef
    {
        // 所屬資產檔的 guid（場景物件為 null）。
        public string guid;
        // GlobalObjectId 字串；重開 Editor 後仍可解析。舊版資料沒有此欄位，啟動時自動補上。
        public string globalId;
        // 本次 Editor 執行期間的 instanceId；Editor 重開時歸零。
        public int instanceId;
        // 最後一次看到的顯示名稱，物件找不到時用來顯示失效列。
        public string label;
        // 加入的時間（Unix 毫秒，UTC）；紀錄列表顯示「幾分鐘前」。舊資料為 0，不顯示。
        public long time;
        // "" 代表「未分類」（顯示時轉為 BookmarkConstants.LabelUncategorized）。
        // 舊資料無此欄位時 JsonUtility 預設填 "" 而自動歸到「未分類」分組。
        public string folder = "";
    }

    // ====================== Folder ======================================
    [System.Serializable]
    internal class FolderInfo
    {
        public string name;
        // true = 收合（注意：與 BookmarksData.foldBookmarks / foldHistory 的意思相反，欄位名稱保留以相容舊資料）。
        public bool fold;
    }

    // ====================== JSON Data ======================================
    [System.Serializable]
    internal class BookmarksData
    {
        // true = 展開（欄位名稱保留以相容舊資料）。
        public bool foldBookmarks = true;
        public bool foldHistory = true;

        public List<ObjectRef> history = new();
        public List<ObjectRef> bookmarks = new();
        public int index = -1;

        // 未分類分組（folder == ""）的折疊狀態，獨立於 folders list 之外。true = 收合，同 FolderInfo.fold。
        public bool foldUncategorized;
        public List<FolderInfo> folders = new();

        public int maxCount = 20;
        // 紀錄列表中同一物件只顯示最近一次；不影響上一頁 / 下一頁的導覽順序。
        public bool mergeDuplicateHistory = true;
        // 在 Project / Hierarchy 已加入書籤的項目旁顯示 ★。
        public bool showItemBadges = true;
        // 用快捷鍵、Inspector ★ 或右鍵選單加入／移除書籤時，在視窗上短暫顯示通知。
        public bool showNotifications = true;

        // 整個書籤功能的開關（false 時 selection 不記錄、shortcut 失效、Inspector toolbar 不顯示）
        public bool enableSystem = true;
        // Inspector header 上的 toolbar（Previous / Next / 最近使用 ▼ / 書籤切換）顯示與否。enableSystem=false 時此值無效
        public bool enableInspectorHeader = true;
        // true 時 Play Mode 不記錄、顯示或操作書籤功能；預設維持既有行為。
        public bool disableInPlayMode;
    }

    // ====================== JSON Storage ======================================
    internal static class BookmarkStorage
    {
        // 純個人偏好檔，放 UserSettings/（Unity 預設 gitignore）—— 不污染 ProjectSettings/ 也不必動 .gitignore。
        // 檔名沿用舊名 PinInspectorData.json，避免既有資料與降版使用者分岔成兩份。
        public const string PathData = "../UserSettings/PinInspectorData.json";
        // 一次性 migration 來源：舊版資料原本放在 git tracked 的 ProjectSettings/
        private const string LegacyPathData = "../ProjectSettings/PinInspectorData.json";

        private static readonly string SavePath =
            Path.Combine(Application.dataPath, PathData);
        private static readonly string LegacySavePath =
            Path.Combine(Application.dataPath, LegacyPathData);

        private static BookmarkFileStorage storage = new(SavePath, LegacySavePath);
        private static bool isLoaded;

        // 儲存版本僅用於畫面刷新，不作為物件解析或書籤成員快取的版本。
        public static int Version { get; private set; }
        // 不含「只動到選取紀錄」的存檔；書籤區的畫面只看這個，選取物件時不必重建。
        public static int NonHistoryVersion { get; private set; }
        public static bool IsAvailable => isLoaded && !storage.IsWriteBlocked;
        public static string LastError => storage.LastError;

        private static bool _flushScheduled;

        public static BookmarksData Data
        {
            get
            {
                if (!isLoaded)
                    Load();
                return storage.Data;
            }
        }

        public static void Load()
        {
            if (isLoaded) return;
            isLoaded = true;
            if (!storage.Load()) Debug.LogError(storage.LastError);
        }

        // Save 為 debounced：呼叫即 bump Version + 排程一次 delayCall 落盤。
        // 連續 N 次 mutate（例：方向鍵狂選 Project）只實際寫盤 1 次。
        // domain reload / Editor quitting 透過 Flush() 強制即時落盤（BookmarkService.Init 內註冊）。
        public static void Save(bool historyOnly = false)
        {
            if (!IsAvailable) return;
            Version++;
            if (!historyOnly) NonHistoryVersion++;
            storage.MarkDirty();
            if (_flushScheduled) return;
            _flushScheduled = true;
            EditorApplication.delayCall += Flush;
        }

        // 測試用：暫時改用獨立的資料檔，Dispose 時換回；不會讀寫使用者的書籤資料。
        internal static System.IDisposable UseStorageForTests(BookmarkFileStorage replacement)
        {
            Flush(); // 先把使用者尚未寫入的變更落盤
            var previous = storage;
            bool previousLoaded = isLoaded;
            SwapStorage(replacement, loaded: true);
            return new RestoreScope(() => SwapStorage(previous, previousLoaded));
        }

        private static void SwapStorage(BookmarkFileStorage next, bool loaded)
        {
            storage = next;
            isLoaded = loaded;
            Version++;
            NonHistoryVersion++;
            BookmarkService.OnStorageSwapped();
        }

        private sealed class RestoreScope : System.IDisposable
        {
            private System.Action restore;
            public RestoreScope(System.Action restore) => this.restore = restore;
            public void Dispose()
            {
                restore?.Invoke();
                restore = null;
            }
        }

        public static void Flush()
        {
            EditorApplication.delayCall -= Flush;
            _flushScheduled = false;
            string previousError = storage.LastError;
            if (!storage.Flush() && storage.LastError != previousError)
                Debug.LogError(storage.LastError);
        }
    }

    // 路徑由呼叫端提供，測試可使用獨立目錄，不碰使用者的書籤資料。
    internal sealed class BookmarkFileStorage
    {
        private readonly string path;
        private readonly string legacyPath;

        public BookmarksData Data { get; private set; } = new();
        public bool IsDirty { get; private set; }
        public bool IsWriteBlocked { get; private set; }
        public string LastError { get; private set; }

        public BookmarkFileStorage(string path, string legacyPath = null)
        {
            this.path = path;
            this.legacyPath = legacyPath;
        }

        public bool Load()
        {
            string source = path;
            try
            {
                if (!File.Exists(path) && legacyPath != null && File.Exists(legacyPath))
                    source = legacyPath;

                var loaded = File.Exists(source)
                    ? JsonUtility.FromJson<BookmarksData>(File.ReadAllText(source))
                    : new BookmarksData();
                if (loaded == null || loaded.bookmarks == null || loaded.history == null)
                    throw new InvalidDataException("書籤或歷史資料結構不完整。");
                if (loaded.bookmarks.Exists(x => x == null) || loaded.history.Exists(x => x == null))
                    throw new InvalidDataException("書籤或歷史含有空白項目。");

                loaded.folders ??= new List<FolderInfo>();
                foreach (var item in loaded.bookmarks) item.folder ??= string.Empty;
                loaded.maxCount = Mathf.Clamp(loaded.maxCount, 1, 999);
                loaded.index = loaded.history.Count == 0 ? -1 : Mathf.Clamp(loaded.index, 0, loaded.history.Count - 1);

                // 先驗證舊檔才複製；失敗時不以預設資料蓋過任何來源。
                if (source != path)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(source, path + ".tmp", overwrite: true);
                    File.Move(path + ".tmp", path);
                }

                Data = loaded;
                IsWriteBlocked = false;
                IsDirty = false;
                LastError = null;
                return true;
            }
            catch (System.Exception e)
            {
                IsWriteBlocked = true;
                LastError = $"[HaruFamily.Bookmarks] 載入失敗：{source} → {path}\n{e.Message}\n已停止書籤操作與寫入，請修復檔案後重新載入腳本或重開 Editor。";
                return false;
            }
        }

        public void MarkDirty()
        {
            if (!IsWriteBlocked) IsDirty = true;
        }

        public bool Flush()
        {
            if (IsWriteBlocked) return false;
            if (!IsDirty) return true;
            string temporaryPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(Data, true));
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);

                IsDirty = false;
                LastError = null;
                return true;
            }
            catch (System.Exception e)
            {
                LastError = $"[HaruFamily.Bookmarks] 儲存失敗：{path}\n{e.Message}\n保留原檔與待存狀態；下次操作或結束 Editor 時會重試，暫存位置：{temporaryPath}";
                return false;
            }
        }
    }
}
#endif
