#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("HaruFamily.UX.Bookmarks.Editor.Tests")]

namespace HaruFamily.UX.Bookmarks
{
    // ====================== InspectorItem ======================================
    [System.Serializable]
    internal class ObjectRef
    {
        public string guid;
        public int instanceId;
        // "" 代表「未分類」（顯示時轉為 InspectorConstants.LabelUncategorized）。
        // 舊資料無此欄位時 JsonUtility 預設填 "" 而自動歸到「未分類」分組。
        public string folder = "";
    }

    // ====================== Folder ======================================
    [System.Serializable]
    internal class FolderInfo
    {
        public string name;
        public bool fold = true;
    }

    // ====================== JSON Data ======================================
    [System.Serializable]
    internal class InspectorJData
    {
        public bool foldBookmarks = true;
        public bool foldHistory = true;

        public List<ObjectRef> history = new();
        public List<ObjectRef> bookmarks = new();
        public int index = -1;

        // 未分類分組（folder == ""）的折疊狀態，獨立於 folders list 之外
        public bool foldUncategorized = true;
        public List<FolderInfo> folders = new();

        public int maxCount = 20;

        // 整個 Pin Inspector 系統開關（false 時 selection 不記錄、shortcut 失效、Inspector toolbar 不顯示）
        public bool enableSystem = true;
        // Inspector header 上的 toolbar（Previous / Next / 最近使用 ▼ / 書籤切換）顯示與否。enableSystem=false 時此值無效
        public bool enableInspectorHeader = true;
        // true 時 Play Mode 不記錄、顯示或操作 Pin Inspector；預設維持既有行為。
        public bool disableInPlayMode;
    }

    // ====================== JSON Storage ======================================
    internal static class JSONStorage
    {
        // 純個人偏好檔，放 UserSettings/（Unity 預設 gitignore）—— 不污染 ProjectSettings/ 也不必動 .gitignore。
        public const string PathData = "../UserSettings/PinInspectorData.json";
        // 一次性 migration 來源：舊版資料原本放在 git tracked 的 ProjectSettings/
        private const string LegacyPathData = "../ProjectSettings/PinInspectorData.json";

        private static readonly string SavePath =
            Path.Combine(Application.dataPath, PathData);
        private static readonly string LegacySavePath =
            Path.Combine(Application.dataPath, LegacyPathData);

        private static readonly BookmarkFileStorage storage = new(SavePath, LegacySavePath);
        private static bool isLoaded;

        // 儲存版本僅用於畫面刷新，不作為物件解析或書籤成員快取的版本。
        public static int Version { get; private set; }
        public static bool IsAvailable => isLoaded && !storage.IsWriteBlocked;
        public static string LastError => storage.LastError;

        private static bool _flushScheduled;

        public static InspectorJData Data
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
        // domain reload / Editor quitting 透過 Flush() 強制即時落盤（Inspector.Init 內註冊）。
        public static void Save()
        {
            if (!IsAvailable) return;
            Version++;
            storage.MarkDirty();
            if (_flushScheduled) return;
            _flushScheduled = true;
            EditorApplication.delayCall += Flush;
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

        public InspectorJData Data { get; private set; } = new();
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
                    ? JsonUtility.FromJson<InspectorJData>(File.ReadAllText(source))
                    : new InspectorJData();
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
                LastError = $"[PinTools.Bookmarks] 載入失敗：{source} → {path}\n{e.Message}\n已停止書籤操作與寫入，請修復檔案後重新載入腳本或重開 Editor。";
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
                LastError = $"[PinTools.Bookmarks] 儲存失敗：{path}\n{e.Message}\n保留原檔與待存狀態；下次操作或結束 Editor 時會重試，暫存位置：{temporaryPath}";
                return false;
            }
        }
    }
}
#endif
