#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

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

        private static InspectorJData _data;

        // 每次 Save() 遞增；BookmarksGUI 用來判斷 RefToObject / Count 等 cache 是否失效。
        public static int Version { get; private set; }

        private static bool _isDirty;
        private static bool _flushScheduled;

        public static InspectorJData Data
        {
            get
            {
                if (_data == null)
                    Load();
                return _data;
            }
        }

        public static void Load()
        {
            // 一次性 migration：新路徑無檔 + 舊路徑有檔 → 搬到新路徑。
            // 舊檔保留不刪（user 需自行 `git rm --cached ProjectSettings/PinInspectorData.json` 並 commit）
            if (!File.Exists(SavePath) && File.Exists(LegacySavePath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                    File.Copy(LegacySavePath, SavePath, overwrite: false);
                    Debug.Log($"[PinTools.Bookmarks] 已將舊書籤資料從 ProjectSettings/ 搬到 UserSettings/。" +
                              $"請執行 `git rm --cached ProjectSettings/PinInspectorData.json` 並 commit 一次以解 track。");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[PinTools.Bookmarks] Migration 失敗：{e.Message}。將以空資料起始。");
                }
            }

            if (!File.Exists(SavePath))
            {
                _data = new InspectorJData();
                SaveImmediate();
            }
            else
            {
                string json = File.ReadAllText(SavePath);
                _data = JsonUtility.FromJson<InspectorJData>(json) ?? new InspectorJData();
            }
        }

        // Save 為 debounced：呼叫即 bump Version + 排程一次 delayCall 落盤。
        // 連續 N 次 mutate（例：方向鍵狂選 Project）只實際寫盤 1 次。
        // domain reload / Editor quitting 透過 Flush() 強制即時落盤（Inspector.Init 內註冊）。
        public static void Save()
        {
            Version++;
            _isDirty = true;
            if (_flushScheduled) return;
            _flushScheduled = true;
            EditorApplication.delayCall += Flush;
        }

        public static void Flush()
        {
            _flushScheduled = false;
            if (!_isDirty) return;
            _isDirty = false;
            SaveImmediate();
        }

        private static void SaveImmediate()
        {
            if (_data == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            string json = JsonUtility.ToJson(_data, true);
            File.WriteAllText(SavePath, json);
        }
    }
}
#endif
