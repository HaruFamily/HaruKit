#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks
{

    // ====================== Main Logic ======================================
    // 物件識別（ObjectRef）：
    // - globalId：GlobalObjectId 字串，可跨 Editor 重開找回資產、子資產與已存檔場景內的物件。主要識別。
    // - instanceId：本次 Editor 執行期間的快速路徑；Editor 重開時歸零（見 Init）。
    // - guid：所屬資產檔的 guid，只用於偵測資產被刪；沒有 globalId 的舊資料以它找回主資產。
    // 沒有 globalId 也沒有 guid 的引用（未存檔場景、Prefab Mode、Play Mode 產生的物件）只在本次 Editor 執行期間有效。
    internal static partial class BookmarkService
    {
        private static BookmarksData Data => BookmarkStorage.Data;
        internal static bool IsEnabled => Data.enableSystem && BookmarkStorage.IsAvailable && (!Data.disableInPlayMode || !EditorApplication.isPlaying);
        internal static int BookmarkVersion { get; private set; }
        internal static int ReferenceVersion { get; private set; }

        // SessionState 在 domain reload 後保留、Editor 重開後清空，用來分辨「重開 Editor」與「重新編譯 / 進出 Play Mode」。
        private const string SessionAliveKey = "HaruFamily.Bookmarks.SessionAlive";
        private const int SceneObjectIdentifierType = 2;

        private const int BookmarkObjectCacheLimit = 128;
        private static int bookmarkCacheVersion = -1;
        private static readonly HashSet<string> bookmarkGlobalIds = new(StringComparer.Ordinal);
        private static readonly HashSet<string> bookmarkLegacyGuids = new(StringComparer.Ordinal);
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

        public static void Save() => BookmarkStorage.Save();
        // 只改到 history / index 時用：書籤區畫面不重建。
        private static void SaveHistory() => BookmarkStorage.Save(historyOnly: true);

        private static void SaveBookmarkMembership()
        {
            BookmarkVersion++;
            Save();
            // Project / Hierarchy 的 ★ 標記不會自己重畫。
            if (BookmarkStorage.IsAvailable && Data.showItemBadges)
            {
                EditorApplication.RepaintProjectWindow();
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        [InitializeOnLoadMethod]
        private static void Init()
        {
            BookmarkStorage.Load();

            AssemblyReloadEvents.beforeAssemblyReload -= BookmarkStorage.Flush;
            AssemblyReloadEvents.beforeAssemblyReload += BookmarkStorage.Flush;
            EditorApplication.quitting -= BookmarkStorage.Flush;
            EditorApplication.quitting += BookmarkStorage.Flush;
            EditorApplication.delayCall += DelayedInit;
            if (!BookmarkStorage.IsAvailable) return;

            bool freshEditorSession = !SessionState.GetBool(SessionAliveKey, false);
            SessionState.SetBool(SessionAliveKey, true);

            // AssetDatabase 在 InitializeOnLoadMethod 階段可能尚未 ready：失效檢查與舊資料遷移放進 delayCall。
            EditorApplication.delayCall += PruneInvalidRefs;

            if (freshEditorSession && ResetForNewEditorSession() > 0)
                SaveBookmarkMembership();
        }

        // Editor 重開後 instanceId 全部失效（可能指到別的物件）：歸零，並丟掉只能靠 instanceId 找回的引用。
        private static int ResetForNewEditorSession()
        {
            int changed = Data.bookmarks.RemoveAll(IsSessionOnly);
            int index = Data.index;
            changed += RemoveAllKeepCursor(Data.history, ref index, IsSessionOnly);
            Data.index = index;
            changed += ClearInstanceIds(Data.bookmarks) + ClearInstanceIds(Data.history);
            return changed;
        }

        private static int ClearInstanceIds(List<ObjectRef> list)
        {
            int changed = 0;
            foreach (var item in list)
            {
                if (item == null || item.instanceId == 0) continue;
                item.instanceId = 0;
                changed++;
            }
            return changed;
        }

        internal static bool IsSessionOnly(ObjectRef r)
            => r == null || (string.IsNullOrEmpty(r.globalId) && string.IsNullOrEmpty(r.guid));

        // 依條件移除項目並維持游標指向同一筆；目前那筆被移除時退到前一筆仍存在的項目。
        internal static int RemoveAllKeepCursor(List<ObjectRef> list, ref int index, Predicate<ObjectRef> match)
        {
            int kept = 0, keptUpToCursor = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (match(list[i])) continue;
                list[kept++] = list[i];
                if (i <= index) keptUpToCursor++;
            }
            int removed = list.Count - kept;
            list.RemoveRange(kept, removed);
            index = list.Count == 0 ? -1 : Mathf.Clamp(keptUpToCursor - 1, 0, list.Count - 1);
            return removed;
        }

        private static void PruneInvalidRefs()
        {
            if (!BookmarkStorage.IsAvailable) return;
            int removedBM = Data.bookmarks.RemoveAll(IsRefDangling);
            int index = Data.index;
            int removedHS = RemoveAllKeepCursor(Data.history, ref index, IsRefDangling);
            Data.index = index;
            int promoted = PromoteRefs(Data.bookmarks) + PromoteRefs(Data.history);

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

            if (removedBM > 0 || removedHS > 0 || reassigned > 0)
                Debug.Log($"[HaruFamily.Bookmarks] 清除失效引用：書籤 -{removedBM} / 紀錄 -{removedHS} / 失效資料夾重置 {reassigned}");
            if (removedBM > 0 || removedHS > 0 || reassigned > 0 || promoted > 0)
                SaveBookmarkMembership();
        }

        // 只檢查「所屬資產檔還在不在」；場景未開啟或子物件暫時找不到的引用保留，由列表顯示為失效列。
        private static bool IsRefDangling(ObjectRef r)
        {
            if (r == null) return true;
            if (TryGetGlobalId(r, out var id))
                return string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString()));
            if (!string.IsNullOrEmpty(r.guid))
                return string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(r.guid));
            return false;
        }

        // 補上缺少的 globalId：舊版資料（只有 guid）與未存檔時加入、之後已存檔的場景物件。
        private static int PromoteRefs(List<ObjectRef> list)
        {
            int promoted = 0;
            foreach (var r in list)
            {
                if (r == null || !string.IsNullOrEmpty(r.globalId)) continue;
                Object obj = r.instanceId != 0 ? InstanceIdToObject(r.instanceId) : null;
                if (obj == null && !string.IsNullOrEmpty(r.guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(r.guid);
                    if (!string.IsNullOrEmpty(path)) obj = AssetDatabase.LoadMainAssetAtPath(path);
                }
                if (obj == null) continue;

                string globalId = GetPersistentGlobalId(obj);
                if (globalId == null) continue;
                r.globalId = globalId;
                r.instanceId = obj.GetInstanceID();
                if (string.IsNullOrEmpty(r.label)) r.label = GetLabel(obj);
                promoted++;
            }
            return promoted;
        }

        private static void DelayedInit()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            Editor.finishedDefaultHeaderGUI -= DrawInspectorHeader;
            Editor.finishedDefaultHeaderGUI += DrawInspectorHeader;

            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.hierarchyChanged -= InvalidateReferences;
            EditorApplication.hierarchyChanged += InvalidateReferences;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            RegisterSceneSetEvents();
        }

        private static void InvalidateReferences()
        {
            ReferenceVersion++;
            bookmarkObjectCache.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _expectedNavTarget = null;
            OnSceneSetChanged();
            InvalidateReferences();
        }

        // 未存檔場景中加入的物件在存檔後才有 globalId：存檔時補上，之後重開 Editor 也找得回來。
        private static void OnSceneSaved(Scene scene)
        {
            if (!BookmarkStorage.IsAvailable) return;
            if (PromoteRefs(Data.bookmarks) + PromoteRefs(Data.history) > 0)
                SaveBookmarkMembership();
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
            SaveHistory();
        }

        // 改小上限時也會呼叫（設定頁），因此游標不能直接設成最後一筆。
        internal static void TrimHistoryToLimit()
        {
            int index = Data.index;
            if (TrimToLimit(Data.history, ref index, Data.maxCount) > 0) Data.index = index;
        }

        // 從最舊的一端移除超出的項目，游標跟著原本那筆移動；那筆被移除時指向留下來的最舊一筆。
        internal static int TrimToLimit(List<ObjectRef> list, ref int index, int maxCount)
        {
            int removeCount = list.Count - Mathf.Max(1, maxCount);
            if (removeCount <= 0) return 0;
            list.RemoveRange(0, removeCount);
            index = Mathf.Clamp(index - removeCount, 0, list.Count - 1);
            return removeCount;
        }

        // 同一物件的識別鍵（與 EqualsItem 的優先順序一致），用於紀錄列表合併重複項目。
        internal static string IdentityKey(ObjectRef r)
        {
            if (r == null) return null;
            if (!string.IsNullOrEmpty(r.globalId)) return r.globalId;
            if (!string.IsNullOrEmpty(r.guid)) return "guid:" + r.guid;
            return r.instanceId != 0 ? "instance:" + r.instanceId : null;
        }

        private static readonly HashSet<string> distinctKeys = new(StringComparer.Ordinal);

        // 每個物件只留最近一次出現的位置，結果依舊維持由舊到新的順序。
        internal static void CollectLatestDistinct(List<ObjectRef> list, List<int> result)
        {
            result.Clear();
            distinctKeys.Clear();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string key = IdentityKey(list[i]);
                if (key == null || distinctKeys.Add(key)) result.Add(i);
            }
            result.Reverse();
        }

        // ====================== Object <-> Ref ======================
        private static ObjectRef ObjectToRef(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            return new ObjectRef
            {
                guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path),
                globalId = GetPersistentGlobalId(obj),
                instanceId = obj.GetInstanceID(),
                label = GetLabel(obj),
                time = NowMs(),
            };
        }

        // 只回傳重開 Editor 後仍能解析的 id；未存檔場景、Prefab Mode、執行期產生的物件回傳 null。
        private static string GetPersistentGlobalId(Object obj)
        {
            if (obj == null) return null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            if (id.identifierType == 0 || id.assetGUID.Empty() || id.targetObjectId == 0) return null;
            return id.ToString();
        }

        private static bool TryGetGlobalId(ObjectRef r, out GlobalObjectId id)
        {
            id = default;
            return r != null && !string.IsNullOrEmpty(r.globalId) && GlobalObjectId.TryParse(r.globalId, out id);
        }

        // EntityIdToObject 不在 6000.0 的 API 中（6000.0 文件只有 InstanceIDToObject），只在已驗證的 6000.3 以上使用。
        private static Object InstanceIdToObject(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(instanceId);
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        // 搜尋與失效列使用的名稱。列表本身另外分開畫名稱與型別。
        internal static string GetLabel(Object obj) => $"{obj.name} ({obj.GetType().Name})";

        // 列表 tooltip：資產顯示路徑；場景物件顯示「場景路徑 › 階層路徑」。
        internal static string GetLocationTooltip(Object obj)
        {
            if (obj == null) return null;
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath)) return assetPath;

            Transform transform = obj is Component component ? component.transform : (obj as GameObject)?.transform;
            if (transform == null) return null;
            string hierarchy = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                hierarchy = parent.name + "/" + hierarchy;
            string scenePath = transform.gameObject.scene.path;
            return string.IsNullOrEmpty(scenePath) ? hierarchy : $"{scenePath} › {hierarchy}";
        }

        // 找不到物件時顯示的文字：最後一次看到的名稱 + 失效原因。
        internal static string GetMissingLabel(ObjectRef r)
        {
            string label = r?.label;
            if (string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(r?.guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(r.guid);
                if (!string.IsNullOrEmpty(path))
                    label = Path.GetFileNameWithoutExtension(path);
            }
            if (string.IsNullOrEmpty(label)) label = BookmarkConstants.LabelUnknownObject;

            string reason = TryGetUnloadedScene(r, out string scenePath)
                ? string.Format(BookmarkConstants.LabelSceneNotLoaded, Path.GetFileNameWithoutExtension(scenePath))
                : BookmarkConstants.DefaultMissing;
            return $"{label}  {reason}";
        }

        // 引用指向已存檔、但目前沒有開啟的場景時回傳 true，並給出場景路徑。
        internal static bool TryGetUnloadedScene(ObjectRef r, out string scenePath)
        {
            scenePath = null;
            if (!TryGetGlobalId(r, out var id) || id.identifierType != SceneObjectIdentifierType) return false;
            scenePath = AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString());
            return !string.IsNullOrEmpty(scenePath) && !IsSceneLoaded(scenePath);
        }

        private static bool IsSceneLoaded(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.path == scenePath) return true;
            }
            return false;
        }

        // 開啟引用所在的場景並選取該物件（會先詢問是否儲存目前已修改的場景）。
        internal static void OpenSceneOf(ObjectRef r)
        {
            if (EditorApplication.isPlaying || !TryGetUnloadedScene(r, out string scenePath)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            InvalidateReferences();
            var obj = RefToObject(r);
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        internal static bool IsBookmarked(Object obj)
        {
            if (obj == null) return false;

            RefreshBookmarkCache();
            int instanceId = obj.GetInstanceID();
            if (bookmarkInstanceIds.Contains(instanceId)) return true;
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
            if (!string.IsNullOrEmpty(item.globalId) && bookmarkGlobalIds.Contains(item.globalId)) return true;
            if (item.instanceId != 0 && bookmarkInstanceIds.Contains(item.instanceId)) return true;
            return !string.IsNullOrEmpty(item.guid) && bookmarkLegacyGuids.Contains(item.guid);
        }

        private static void RefreshBookmarkCache()
        {
            if (bookmarkCacheVersion == BookmarkVersion) return;

            bookmarkCacheVersion = BookmarkVersion;
            bookmarkGlobalIds.Clear();
            bookmarkLegacyGuids.Clear();
            bookmarkInstanceIds.Clear();
            bookmarkObjectCache.Clear();
            foreach (var bookmark in Data.bookmarks)
            {
                if (bookmark == null) continue;
                if (!string.IsNullOrEmpty(bookmark.globalId)) bookmarkGlobalIds.Add(bookmark.globalId);
                else if (!string.IsNullOrEmpty(bookmark.guid)) bookmarkLegacyGuids.Add(bookmark.guid);
                if (bookmark.instanceId != 0) bookmarkInstanceIds.Add(bookmark.instanceId);
            }
        }

        public static Object RefToObject(ObjectRef item)
        {
            if (item == null) return null;
            if (item.instanceId != 0)
            {
                var live = InstanceIdToObject(item.instanceId);
                if (live != null) return live;
            }

            Object obj = null;
            if (TryGetGlobalId(item, out var id))
            {
                // 場景沒開就不必掃描（GlobalObjectIdentifierToObjectSlow 對每筆失效列都很慢）。
                if (id.identifierType == SceneObjectIdentifierType
                    && !IsSceneLoaded(AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString())))
                    return null;
                obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            }
            else if (!string.IsNullOrEmpty(item.guid))
            {
                // 舊版資料：只有 guid，只能找回主資產（PruneInvalidRefs 會補上 globalId）。
                string path = AssetDatabase.GUIDToAssetPath(item.guid);
                if (!string.IsNullOrEmpty(path)) obj = AssetDatabase.LoadMainAssetAtPath(path);
            }

            if (obj != null) item.instanceId = obj.GetInstanceID();
            return obj;
        }

        // ====================== Commands ======================
        // 回傳是否成功切換；目標物件已不存在（場景未開、已刪除、Play Mode 產生的物件）時回傳 false。
        public static bool NavigateTo(int newIndex)
        {
            if (!IsEnabled) return false;
            if (newIndex < 0 || newIndex >= Data.history.Count) return false;

            Object obj = RefToObject(Data.history[newIndex]);
            if (obj == null) return false;

            Data.index = newIndex;

            // 已經是當前選擇就不重設（避免無謂的 selectionChanged）
            if (Selection.activeObject != obj)
            {
                _expectedNavTarget = Data.history[newIndex];
                Selection.activeObject = obj;
            }

            SaveHistory();
            return true;
        }

        // 上一頁 / 下一頁跳過已失效的項目，避免按鈕亮著卻沒反應。
        public static void NavigateBack()
        {
            for (int i = Data.index - 1; i >= 0; i--)
                if (NavigateTo(i)) return;
        }

        public static void NavigateForward()
        {
            for (int i = Data.index + 1; i < Data.history.Count; i++)
                if (NavigateTo(i)) return;
        }

        public static bool CanNavigateBack() => Data.index > 0;
        public static bool CanNavigateForward() => Data.index >= 0 && Data.index < Data.history.Count - 1;

        // 新書籤預設歸到「未分類」；移除可復原（見 BookmarkService.Operations.cs）。
        public static void ToggleBookmark(Object obj, bool notify = false)
        {
            if (obj == null) return;
            ToggleBookmarks(new[] { obj }, notify);
        }

        // 失效列（找不到物件）只能用引用本身移除。
        internal static void RemoveBookmark(ObjectRef item)
        {
            if (item == null) return;
            RemoveBookmarks(new[] { item });
        }

        public static void ClearAllBookmarks()
        {
            if (!IsEnabled || Data.bookmarks.Count == 0) return;
            var before = CaptureSnapshot();
            int count = Data.bookmarks.Count;
            Data.bookmarks.Clear();
            CommitUndoable(before, string.Format(BookmarkConstants.LabelNotifyRemovedMany, count));
        }

        internal static ObjectRef FindBookmark(Object obj)
        {
            if (obj == null) return null;
            // 快速路徑：拖曳時每個 DragUpdated 都會呼叫，先比 instanceId 避免計算 GlobalObjectId。
            int instanceId = obj.GetInstanceID();
            foreach (var bookmark in Data.bookmarks)
                if (bookmark != null && bookmark.instanceId == instanceId) return bookmark;

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
            ExpandFolder(item.folder);
            SaveBookmarkMembership();
        }

        // 拖放加入：已是書籤的移到該資料夾，其餘新增；有 before 時排到該書籤前面。回傳處理的物件數。
        internal static int AddBookmarks(IList<Object> objects, string folder, ObjectRef before)
        {
            if (!IsEnabled || objects == null) return 0;
            folder ??= string.Empty;
            if (!FolderExists(folder)) folder = string.Empty;
            if (before != null && !Data.bookmarks.Contains(before)) before = null;

            int count = 0;
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                var item = FindBookmark(obj);
                if (item == null)
                {
                    item = ObjectToRef(obj);
                    Data.bookmarks.Add(item);
                }
                if (item == before) continue;
                item.folder = folder;
                if (before != null)
                {
                    Data.bookmarks.Remove(item);
                    Data.bookmarks.Insert(Data.bookmarks.IndexOf(before), item);
                }
                count++;
            }
            if (count == 0) return 0;
            ExpandFolder(folder);
            SaveBookmarkMembership();
            return count;
        }

        // 加入書籤時展開所在資料夾，否則新書籤落在收合的資料夾裡看起來像沒加成功。
        private static void ExpandFolder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Data.foldUncategorized = false;
                return;
            }
            var folder = Data.folders.Find(f => f != null && f.name == name);
            if (folder != null) folder.fold = false;
        }

        internal static void ReorderBookmark(ObjectRef source, ObjectRef target)
        {
            if (!IsEnabled) return;
            if (ReorderBookmarks(Data.bookmarks, source, target)) SaveBookmarkMembership();
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
            if (name == BookmarkConstants.LabelUncategorized) return false;
            if (FolderExists(name)) return false;

            Data.folders.Add(new FolderInfo { name = name, fold = false });
            SaveBookmarkMembership();
            return true;
        }

        public static bool RenameFolder(string oldName, string newName)
        {
            if (!IsEnabled) return false;
            if (string.IsNullOrEmpty(oldName)) return false;
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName)) return false;
            if (newName == BookmarkConstants.LabelUncategorized) return false;
            if (oldName == newName) return false;
            if (FolderExists(newName)) return false;

            var folder = Data.folders.Find(f => f != null && f.name == oldName);
            if (folder == null) return false;

            folder.name = newName;
            foreach (var b in Data.bookmarks)
                if (b.folder == oldName) b.folder = newName;

            SaveBookmarkMembership();
            return true;
        }

        // 刪除資料夾，原本歸屬此夾的書籤搬回「未分類」（folder = ""）。
        public static void DeleteFolder(string name)
        {
            if (!IsEnabled) return;
            if (string.IsNullOrEmpty(name)) return;
            int idx = Data.folders.FindIndex(f => f != null && f.name == name);
            if (idx < 0) return;

            var before = CaptureSnapshot();
            Data.folders.RemoveAt(idx);
            foreach (var b in Data.bookmarks)
                if (b.folder == name) b.folder = "";

            CommitUndoable(before, string.Format(BookmarkConstants.LabelNotifyFolderDeleted, name));
        }

        public static void MoveBookmarkToFolder(ObjectRef item, string folder)
        {
            if (!IsEnabled || item == null || !Data.bookmarks.Contains(item)) return;
            folder ??= "";
            // 非未分類但 folder list 沒這個夾 → 視為無效，落到未分類
            if (!string.IsNullOrEmpty(folder) && !FolderExists(folder)) folder = "";
            if ((item.folder ?? string.Empty) == folder) return;
            item.folder = folder;
            SaveBookmarkMembership();
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

            SaveHistory();
        }

        // ====================== Variables ======================
        // 兩邊都有 globalId 時只比 globalId（同一資產的不同子資產 guid 相同，必須以此區分）；
        // 否則同一個 instanceId 代表本次執行期間的同一物件；最後才以舊版的主資產 guid 比對。
        internal static bool EqualsItem(ObjectRef a, ObjectRef b)
        {
            if (a == null || b == null) return false;
            if (!string.IsNullOrEmpty(a.globalId) && !string.IsNullOrEmpty(b.globalId))
                return a.globalId == b.globalId;
            if (a.instanceId != 0 && a.instanceId == b.instanceId) return true;
            return !string.IsNullOrEmpty(a.guid) && a.guid == b.guid;
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
