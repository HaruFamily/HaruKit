#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{

    // ====================== Main Logic ======================================
    internal static partial class Inspector
    {
        private static InspectorJData Data => JSONStorage.Data;
        internal static bool IsEnabled => Data.enableSystem && JSONStorage.IsAvailable && (!Data.disableInPlayMode || !EditorApplication.isPlaying);
        internal static int BookmarkVersion { get; private set; }
        internal static int ReferenceVersion { get; private set; }

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

        private static void SaveBookmarkMembership()
        {
            BookmarkVersion++;
            Save();
        }

        [InitializeOnLoadMethod]
        private static void Init()
        {
            JSONStorage.Load();

            AssemblyReloadEvents.beforeAssemblyReload -= JSONStorage.Flush;
            AssemblyReloadEvents.beforeAssemblyReload += JSONStorage.Flush;
            EditorApplication.quitting -= JSONStorage.Flush;
            EditorApplication.quitting += JSONStorage.Flush;
            EditorApplication.delayCall += DelayedInit;
            if (!JSONStorage.IsAvailable) return;

            // 場景 instance 只保留於目前 domain，重新載入腳本時清除。
            Data.history.RemoveAll(x => string.IsNullOrEmpty(x.guid));
            Data.bookmarks.RemoveAll(x => string.IsNullOrEmpty(x.guid));

            // 第二輪：guid 解析不到 Asset（被刪 / 重命名打斷）— 啟動時延遲 prune
            // AssetDatabase 在 InitializeOnLoadMethod 階段可能尚未 ready，放進 delayCall 與 DrawHeader 註冊一併處理
            EditorApplication.delayCall += PruneInvalidRefs;

            Data.index = Data.history.Count == 0 ? -1 : Mathf.Clamp(Data.index, 0, Data.history.Count - 1);

            SaveBookmarkMembership();
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
                SaveBookmarkMembership();
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

            EditorApplication.projectChanged -= InvalidateReferences;
            EditorApplication.projectChanged += InvalidateReferences;
            EditorApplication.hierarchyChanged -= InvalidateReferences;
            EditorApplication.hierarchyChanged += InvalidateReferences;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void InvalidateReferences()
        {
            ReferenceVersion++;
            bookmarkObjectCache.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _expectedNavTarget = null;
            InvalidateReferences();
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
            if (bookmarkCacheVersion == BookmarkVersion) return;

            bookmarkCacheVersion = BookmarkVersion;
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
            if (item == null) return null;
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
            if (!IsEnabled) return;
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
            if (!IsEnabled || obj == null) return;
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

            SaveBookmarkMembership();
        }

        public static void ClearAllBookmarks()
        {
            if (!IsEnabled || Data.bookmarks.Count == 0) return;
            Data.bookmarks.Clear();
            SaveBookmarkMembership();
        }

        internal static ObjectRef FindBookmark(Object obj)
        {
            if (obj == null) return null;
            var reference = ObjectToRef(obj);
            return Data.bookmarks.Find(item => EqualsItem(item, reference));
        }

        internal static void AddBookmarkToFolder(Object obj, string folder)
        {
            if (!IsEnabled || obj == null) return;
            var existing = FindBookmark(obj);
            if (existing != null)
            {
                MoveBookmarkToFolder(existing, folder);
                return;
            }

            var item = ObjectToRef(obj);
            item.folder = FolderExists(folder) ? folder ?? string.Empty : string.Empty;
            Data.bookmarks.Add(item);
            SaveBookmarkMembership();
        }

        internal static void ReorderBookmark(ObjectRef source, ObjectRef target)
        {
            if (!IsEnabled) return;
            if (ReorderBookmarks(Data.bookmarks, source, target)) Save();
        }

        // 同夾保留其他資料夾的 slot；跨夾插在目標之前。以引用識別避免搜尋後的可見 index 與原清單混用。
        internal static bool ReorderBookmarks(List<ObjectRef> bookmarks, ObjectRef source, ObjectRef target)
        {
            if (source == null || target == null || source == target) return false;
            int from = bookmarks.IndexOf(source);
            int to = bookmarks.IndexOf(target);
            if (from < 0 || to < 0) return false;

            string folder = target.folder ?? string.Empty;
            if ((source.folder ?? string.Empty) != folder)
            {
                bookmarks.RemoveAt(from);
                bookmarks.Insert(bookmarks.IndexOf(target), source);
                source.folder = folder;
                return true;
            }

            int direction = from < to ? 1 : -1;
            int slot = from;
            for (int i = from + direction; direction > 0 ? i <= to : i >= to; i += direction)
            {
                if ((bookmarks[i].folder ?? string.Empty) != folder) continue;
                bookmarks[slot] = bookmarks[i];
                slot = i;
            }
            bookmarks[slot] = source;
            return true;
        }

        // ====================== Folder API ======================
        public static bool FolderExists(string name)
        {
            if (string.IsNullOrEmpty(name)) return true; // 未分類視為一定存在
            return Data.folders.Exists(f => f != null && f.name == name);
        }

        // 回傳 true 表示新增成功；名稱空白、重複、或為保留值（"未分類" 字面）則拒絕。
        public static bool AddFolder(string name)
        {
            if (!IsEnabled) return false;
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
            if (!IsEnabled) return false;
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
            if (!IsEnabled) return;
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
            if (!IsEnabled || item == null || !Data.bookmarks.Contains(item)) return;
            folder ??= "";
            // 非未分類但 folder list 沒這個夾 → 視為無效，落到未分類
            if (!string.IsNullOrEmpty(folder) && !FolderExists(folder)) folder = "";
            if ((item.folder ?? string.Empty) == folder) return;
            item.folder = folder;
            Save();
        }

        public static void ToggleFolderFold(string name)
        {
            if (!IsEnabled) return;
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

        public static void ClearAllHistory()
        {
            if (!IsEnabled) return;
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
            if (a == null || b == null) return false;
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
