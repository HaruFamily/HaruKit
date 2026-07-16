#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PinTools.Inspector
{

    // ====================== Main Logic ======================================
    internal static partial class Inspector
    {
        private static InspectorJData Data => JSONStorage.Data;
        internal static bool IsEnabled => Data.enableSystem && (!Data.disableInPlayMode || !EditorApplication.isPlaying);

        private const int BookmarkObjectCacheLimit = 128;
        private static int bookmarkCacheVersion = -1;
        private static readonly HashSet<string> bookmarkGuids = new();
        private static readonly HashSet<int> bookmarkInstanceIds = new();
        private static readonly Dictionary<int, BookmarkCacheItem> bookmarkObjectCache = new();

        private struct BookmarkCacheItem
        {
            public Object obj;
            public bool isBookmarked;
        }

        // 哨兵：NavigateTo 設下要切換的目標，OnSelectionChanged 收到相符選擇時消費掉並跳過記錄。
        // 比 _isNavigating 旗標可靠 —— 不依賴 Selection.selectionChanged 的同步/非同步時序。
        private static ObjectRef _expectedNavTarget;

        public static void Save() => JSONStorage.Save();

        [InitializeOnLoadMethod]
        private static void Init()
        {
            JSONStorage.Load();

            // 第一輪：空 guid（純 scene instance 跨重啟必失效）
            Data.history.RemoveAll(x => string.IsNullOrEmpty(x.guid));
            Data.bookmarks.RemoveAll(x => string.IsNullOrEmpty(x.guid));

            // 第二輪：guid 解析不到 Asset（被刪 / 重命名打斷）— 啟動時延遲 prune
            // AssetDatabase 在 InitializeOnLoadMethod 階段可能尚未 ready，放進 delayCall 與 DrawHeader 註冊一併處理
            EditorApplication.delayCall += PruneInvalidRefs;

            Data.index = Data.history.Count == 0 ? -1 : Mathf.Clamp(Data.index, 0, Data.history.Count - 1);

            Save();

            // 確保 debounced Save 的髒資料在 domain reload / Editor 結束前真的落盤
            AssemblyReloadEvents.beforeAssemblyReload -= JSONStorage.Flush;
            AssemblyReloadEvents.beforeAssemblyReload += JSONStorage.Flush;
            EditorApplication.quitting -= JSONStorage.Flush;
            EditorApplication.quitting += JSONStorage.Flush;

            EditorApplication.delayCall += DelayedInit;
        }

        private static void PruneInvalidRefs()
        {
            int beforeBM = Data.bookmarks.Count;
            int beforeHS = Data.history.Count;
            Data.bookmarks.RemoveAll(IsRefDangling);
            Data.history.RemoveAll(IsRefDangling);

            // folder 指向已不存在的資料夾 → 落到未分類（不刪書籤）
            int reassigned = 0;
            foreach (var b in Data.bookmarks)
            {
                if (b == null) continue;
                if (!string.IsNullOrEmpty(b.folder) && !FolderExists(b.folder))
                {
                    b.folder = "";
                    reassigned++;
                }
            }

            if (Data.history.Count == 0) Data.index = -1;
            else Data.index = Mathf.Clamp(Data.index, 0, Data.history.Count - 1);

            int removedBM = beforeBM - Data.bookmarks.Count;
            int removedHS = beforeHS - Data.history.Count;
            if (removedBM > 0 || removedHS > 0 || reassigned > 0)
            {
                Debug.Log($"[PinTools.Bookmarks] Prune 失效引用：書籤 -{removedBM} / 歷史 -{removedHS} / 失效資料夾重置 {reassigned}");
                Save();
            }
        }

        private static bool IsRefDangling(ObjectRef r)
        {
            if (r == null || string.IsNullOrEmpty(r.guid)) return true;
            string path = AssetDatabase.GUIDToAssetPath(r.guid);
            return string.IsNullOrEmpty(path);
        }

        private static void DelayedInit()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            Editor.finishedDefaultHeaderGUI -= DrawInspectorHeader;
            Editor.finishedDefaultHeaderGUI += DrawInspectorHeader;
        }

        private static void OnSelectionChanged()
        {
            if (!IsEnabled) return; // 系統整體關閉或 Play Mode 停用時不記錄 selection
            if (Selection.activeObject == null) return;

            ObjectRef item = ObjectToRef(Selection.activeObject);

            // NavigateTo 觸發的選擇 —— 消費掉哨兵並跳過記錄
            if (_expectedNavTarget != null && EqualsItem(_expectedNavTarget, item))
            {
                _expectedNavTarget = null;
                return;
            }
            _expectedNavTarget = null;

            // Browser 模型：若已是當前項目就不重複記錄
            if (Data.index >= 0 && Data.index < Data.history.Count
                && EqualsItem(Data.history[Data.index], item))
                return;

            // Browser 模型：fresh selection 時砍掉游標之後的所有 forward 記錄
            if (Data.index >= 0 && Data.index + 1 < Data.history.Count)
                Data.history.RemoveRange(Data.index + 1, Data.history.Count - Data.index - 1);

            Data.history.Add(item);
            Data.index = Data.history.Count - 1;

            TrimHistoryToLimit();
            Save();
        }

        private static void TrimHistoryToLimit()
        {
            if (Data.history.Count > Data.maxCount)
            {
                int removeCount = Data.history.Count - Data.maxCount;
                Data.history.RemoveRange(0, removeCount);
                Data.index = Data.history.Count - 1;
            }
        }

        // ====================== Object <-> Ref ======================
        private static ObjectRef ObjectToRef(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            string guid = AssetDatabase.AssetPathToGUID(path);

            return new ObjectRef
            {
                guid = string.IsNullOrEmpty(path) ? null : guid,
                instanceId = obj.GetInstanceID()
            };
        }

        internal static bool IsBookmarked(Object obj)
        {
            if (obj == null) return false;

            RefreshBookmarkCache();
            int instanceId = obj.GetInstanceID();
            if (bookmarkObjectCache.TryGetValue(instanceId, out var cached) && cached.obj == obj)
                return cached.isBookmarked;

            bool isBookmarked = IsBookmarked(ObjectToRef(obj));
            if (bookmarkObjectCache.Count >= BookmarkObjectCacheLimit)
                bookmarkObjectCache.Clear();
            bookmarkObjectCache[instanceId] = new BookmarkCacheItem { obj = obj, isBookmarked = isBookmarked };
            return isBookmarked;
        }

        internal static bool IsBookmarked(ObjectRef item)
        {
            if (item == null) return false;
            RefreshBookmarkCache();
            if (!string.IsNullOrEmpty(item.guid)) return bookmarkGuids.Contains(item.guid);
            return bookmarkInstanceIds.Contains(item.instanceId);
        }

        private static void RefreshBookmarkCache()
        {
            if (bookmarkCacheVersion == JSONStorage.Version) return;

            bookmarkCacheVersion = JSONStorage.Version;
            bookmarkGuids.Clear();
            bookmarkInstanceIds.Clear();
            bookmarkObjectCache.Clear();
            foreach (var bookmark in Data.bookmarks)
            {
                if (bookmark == null) continue;
                if (!string.IsNullOrEmpty(bookmark.guid)) bookmarkGuids.Add(bookmark.guid);
                bookmarkInstanceIds.Add(bookmark.instanceId);
            }
        }

        public static Object RefToObject(ObjectRef item)
        {
            if (!string.IsNullOrEmpty(item.guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(item.guid);
                if (!string.IsNullOrEmpty(path))
                {
                    Object loaded = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (loaded != null) return loaded;
                }
            }

#if UNITY_6000_0_OR_NEWER
            return EditorUtility.EntityIdToObject(item.instanceId);
#else
    return EditorUtility.InstanceIDToObject(item.instanceId);
#endif
        }

        // ====================== Commands ======================
        public static void NavigateTo(int newIndex)
        {
            if (newIndex < 0 || newIndex >= Data.history.Count) return;

            Object obj = RefToObject(Data.history[newIndex]);
            if (obj == null) return;

            Data.index = newIndex;

            // 已經是當前選擇就不重設（避免無謂的 selectionChanged）
            if (Selection.activeObject != obj)
            {
                _expectedNavTarget = Data.history[newIndex];
                Selection.activeObject = obj;
            }

            Save();
        }

        public static void NavigateBack() => NavigateTo(Data.index - 1);
        public static void NavigateForward() => NavigateTo(Data.index + 1);

        public static bool CanNavigateBack() => Data.index > 0;
        public static bool CanNavigateForward() => Data.index >= 0 && Data.index < Data.history.Count - 1;

        public static void ToggleBookmark(Object obj)
        {
            ObjectRef item = ObjectToRef(obj);
            int exist = Data.bookmarks.FindIndex(x => EqualsItem(x, item));

            if (exist >= 0)
            {
                Data.bookmarks.RemoveAt(exist);
            }
            else
            {
                // 新書籤預設歸到「未分類」（folder = ""）。使用者可在 popup 內改 folder。
                item.folder = "";
                Data.bookmarks.Add(item);
            }

            Save();
        }

        public static void ClearAllBookmarks()
        {
            Data.bookmarks.Clear();
            Save();
        }

        // ====================== Folder API ======================
        // 回傳所有「分組顯示順序」：使用者自訂 folders 依序 + 未分類放最後（用 "" 表示）。
        public static System.Collections.Generic.List<string> GetFolderDisplayOrder()
        {
            var list = new System.Collections.Generic.List<string>(Data.folders.Count + 1);
            foreach (var f in Data.folders)
            {
                if (f == null || string.IsNullOrEmpty(f.name)) continue;
                list.Add(f.name);
            }
            list.Add(""); // 未分類永遠存在，固定排最後
            return list;
        }

        public static bool FolderExists(string name)
        {
            if (string.IsNullOrEmpty(name)) return true; // 未分類視為一定存在
            return Data.folders.Exists(f => f != null && f.name == name);
        }

        // 回傳 true 表示新增成功；名稱空白、重複、或為保留值（"未分類" 字面）則拒絕。
        public static bool AddFolder(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return false;
            if (name == InspectorConstants.LabelUncategorized) return false;
            if (FolderExists(name)) return false;

            Data.folders.Add(new FolderInfo { name = name, fold = true });
            Save();
            return true;
        }

        public static bool RenameFolder(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName)) return false;
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName)) return false;
            if (newName == InspectorConstants.LabelUncategorized) return false;
            if (oldName == newName) return false;
            if (FolderExists(newName)) return false;

            var folder = Data.folders.Find(f => f != null && f.name == oldName);
            if (folder == null) return false;

            folder.name = newName;
            foreach (var b in Data.bookmarks)
                if (b.folder == oldName) b.folder = newName;

            Save();
            return true;
        }

        // 刪除資料夾，原本歸屬此夾的書籤搬回「未分類」（folder = ""）。
        public static void DeleteFolder(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            int idx = Data.folders.FindIndex(f => f != null && f.name == name);
            if (idx < 0) return;

            Data.folders.RemoveAt(idx);
            foreach (var b in Data.bookmarks)
                if (b.folder == name) b.folder = "";

            Save();
        }

        public static void MoveBookmarkToFolder(ObjectRef item, string folder)
        {
            if (item == null) return;
            folder ??= "";
            // 非未分類但 folder list 沒這個夾 → 視為無效，落到未分類
            if (!string.IsNullOrEmpty(folder) && !FolderExists(folder)) folder = "";
            item.folder = folder;
            Save();
        }

        public static void ToggleFolderFold(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Data.foldUncategorized = !Data.foldUncategorized;
            }
            else
            {
                var f = Data.folders.Find(x => x != null && x.name == name);
                if (f == null) return;
                f.fold = !f.fold;
            }
            Save();
        }

        public static bool IsFolderFolded(string name)
        {
            if (string.IsNullOrEmpty(name)) return Data.foldUncategorized;
            var f = Data.folders.Find(x => x != null && x.name == name);
            return f == null || f.fold;
        }

        public static void ClearAllHistory()
        {
            Data.history.Clear();

            if (Selection.activeObject != null)
            {
                Data.history.Add(ObjectToRef(Selection.activeObject));
                Data.index = 0;
            }
            else
            {
                Data.index = -1;
            }

            Save();
        }

        // ====================== Variables ======================
        internal static bool EqualsItem(ObjectRef a, ObjectRef b)
        {
            if (!string.IsNullOrEmpty(a.guid) && !string.IsNullOrEmpty(b.guid))
                return a.guid == b.guid;

            return a.instanceId == b.instanceId; // scene instance only
        }
        public static bool CanOpen(Object obj)
        {
            if (obj == null) return false;
            if (!EditorUtility.IsPersistent(obj)) return false;
            if (obj is SceneAsset) return true;
            if (PrefabUtility.IsPartOfPrefabAsset(obj)) return true;
            if (obj is Material || obj is Shader ||
                obj is UnityEditor.Animations.AnimatorController ||
                obj is MonoScript)
                return true;
            return false;
        }
    }
}
#endif
