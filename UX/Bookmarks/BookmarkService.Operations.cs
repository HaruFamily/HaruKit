#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks
{
    // 多物件操作、可復原的移除、通知，以及 Project / Hierarchy 標記查詢。
    internal static partial class BookmarkService
    {
        // ====================== 復原 ======================
        // 只復原「最近一次」移除類操作（移除書籤、清除全部、刪除資料夾）。
        // 之後只要書籤或資料夾再有任何變動（BookmarkVersion 改變）或超過時限，就不能復原，避免蓋掉新的修改。
        private const double UndoLifetimeSeconds = 20;

        private sealed class BookmarkSnapshot
        {
            public List<ObjectRef> bookmarks;
            public List<string> folderOf;
            public List<FolderInfo> folders;
        }

        private static BookmarkSnapshot undoSnapshot;
        private static int undoVersion = -1;
        private static double undoTime;
        internal static string UndoMessage { get; private set; }

        internal static bool CanUndo => undoSnapshot != null && undoVersion == BookmarkVersion
            && EditorApplication.timeSinceStartup - undoTime < UndoLifetimeSeconds;

        private static BookmarkSnapshot CaptureSnapshot()
        {
            var snapshot = new BookmarkSnapshot
            {
                bookmarks = new List<ObjectRef>(Data.bookmarks),
                folderOf = new List<string>(Data.bookmarks.Count),
                folders = new List<FolderInfo>(Data.folders.Count),
            };
            foreach (var b in Data.bookmarks) snapshot.folderOf.Add(b?.folder ?? string.Empty);
            foreach (var f in Data.folders)
                if (f != null) snapshot.folders.Add(new FolderInfo { name = f.name, fold = f.fold });
            return snapshot;
        }

        private static void CommitUndoable(BookmarkSnapshot before, string message)
        {
            SaveBookmarkMembership();
            undoSnapshot = before;
            undoVersion = BookmarkVersion;
            undoTime = EditorApplication.timeSinceStartup;
            UndoMessage = message;
        }

        internal static void UndoLastRemoval()
        {
            if (!IsEnabled || !CanUndo) return;
            var snapshot = undoSnapshot;
            undoSnapshot = null;
            Data.bookmarks.Clear();
            Data.bookmarks.AddRange(snapshot.bookmarks);
            for (int i = 0; i < snapshot.bookmarks.Count; i++) snapshot.bookmarks[i].folder = snapshot.folderOf[i];
            Data.folders.Clear();
            Data.folders.AddRange(snapshot.folders);
            SaveBookmarkMembership();
        }

        internal static void DismissUndo() => undoSnapshot = null;

        // ====================== 多物件操作 ======================
        // 全部都已是書籤 → 全部移除（可復原）；否則把還不是書籤的加入「未分類」。
        internal static void ToggleBookmarks(IList<Object> objects, bool notify)
        {
            if (!IsEnabled || objects == null) return;
            var targets = new List<Object>();
            foreach (var obj in objects)
                if (obj != null && !targets.Contains(obj)) targets.Add(obj);
            if (targets.Count == 0) return;

            var existing = new List<ObjectRef>();
            foreach (var obj in targets)
            {
                var item = FindBookmark(obj);
                if (item != null) existing.Add(item);
            }

            string message;
            if (existing.Count == targets.Count)
            {
                var before = CaptureSnapshot();
                foreach (var item in existing) Data.bookmarks.Remove(item);
                message = targets.Count == 1
                    ? string.Format(BookmarkConstants.LabelNotifyRemovedOne, targets[0].name)
                    : string.Format(BookmarkConstants.LabelNotifyRemovedMany, targets.Count);
                CommitUndoable(before, message);
            }
            else
            {
                int added = 0;
                Object lastAdded = null;
                foreach (var obj in targets)
                {
                    if (FindBookmark(obj) != null) continue;
                    var item = ObjectToRef(obj);
                    item.folder = string.Empty;
                    Data.bookmarks.Add(item);
                    lastAdded = obj;
                    added++;
                }
                ExpandFolder(string.Empty);
                SaveBookmarkMembership();
                message = added == 1
                    ? string.Format(BookmarkConstants.LabelNotifyAddedOne, lastAdded.name)
                    : string.Format(BookmarkConstants.LabelNotifyAddedMany, added);
            }
            if (notify) Notify(message);
        }

        internal static void RemoveBookmarks(IList<ObjectRef> items)
        {
            if (!IsEnabled || items == null) return;
            var before = CaptureSnapshot();
            int removed = 0;
            foreach (var item in items)
                if (item != null && Data.bookmarks.Remove(item)) removed++;
            if (removed == 0) return;
            CommitUndoable(before, removed == 1
                ? string.Format(BookmarkConstants.LabelNotifyRemovedOne, ShortName(items[0]))
                : string.Format(BookmarkConstants.LabelNotifyRemovedMany, removed));
        }

        // 移動多筆書籤：有 before 時依原順序插到它前面並改成它的資料夾；沒有時只改資料夾、保留位置。
        internal static void MoveBookmarks(IList<ObjectRef> items, string folder, ObjectRef before)
        {
            if (!IsEnabled || items == null) return;
            if (before != null && !Data.bookmarks.Contains(before)) before = null;
            folder = before != null ? before.folder ?? string.Empty : folder ?? string.Empty;
            if (!FolderExists(folder)) folder = string.Empty;
            if (MoveBookmarks(Data.bookmarks, items, folder, before) == 0) return;
            ExpandFolder(folder);
            SaveBookmarkMembership();
        }

        internal static int MoveBookmarks(List<ObjectRef> bookmarks, IList<ObjectRef> items, string folder, ObjectRef before)
        {
            var moving = new List<ObjectRef>();
            foreach (var b in bookmarks)
                if (b != before && items.Contains(b)) moving.Add(b); // 依清單原順序，不依選取順序
            if (moving.Count == 0) return 0;

            foreach (var item in moving) item.folder = folder;
            if (before == null) return moving.Count;
            foreach (var item in moving) bookmarks.Remove(item);
            bookmarks.InsertRange(bookmarks.IndexOf(before), moving);
            return moving.Count;
        }

        // 把資料夾 source 移到 before 前面；before 為空字串（未分類）時移到最後。
        internal static bool ReorderFolder(string source, string before)
        {
            if (!IsEnabled) return false;
            if (!ReorderFolders(Data.folders, source, before)) return false;
            SaveBookmarkMembership();
            return true;
        }

        internal static bool ReorderFolders(List<FolderInfo> folders, string source, string before)
        {
            if (string.IsNullOrEmpty(source) || source == before) return false;
            int from = folders.FindIndex(f => f != null && f.name == source);
            if (from < 0) return false;
            var item = folders[from];
            folders.RemoveAt(from);
            int to = string.IsNullOrEmpty(before) ? -1 : folders.FindIndex(f => f != null && f.name == before);
            if (to < 0) folders.Add(item);
            else folders.Insert(to, item);
            return true;
        }

        private static string ShortName(ObjectRef item)
        {
            var obj = RefToObject(item);
            if (obj != null) return obj.name;
            return string.IsNullOrEmpty(item?.label) ? BookmarkConstants.LabelUnknownObject : item.label;
        }

        // ====================== 通知 ======================
        internal static void Notify(string message)
        {
            if (string.IsNullOrEmpty(message) || !Data.showNotifications) return;
            var window = EditorWindow.focusedWindow ?? EditorWindow.mouseOverWindow;
            if (window != null) window.ShowNotification(new GUIContent(message), 1.2);
        }

        // Inspector 對 Asset 顯示的是 Importer 編輯器；書籤要記主資產，不是 Importer。
        internal static Object ResolveInspectedTarget(Object target)
        {
            if (target is AssetImporter importer)
            {
                var main = AssetDatabase.LoadMainAssetAtPath(importer.assetPath);
                if (main != null) return main;
            }
            return target;
        }

        // ====================== Project / Hierarchy 標記 ======================
        private static int sceneSetVersion;
        private static int resolvedSceneSetVersion = -1, resolvedBookmarkVersion = -1;
        private static readonly HashSet<string> bookmarkFileGuids = new(StringComparer.Ordinal);
        private static readonly HashSet<int> bookmarkLiveInstanceIds = new();

        private static void OnSceneSetChanged() => sceneSetVersion++;

        // 補齊每筆書籤目前的 instanceId（場景沒開的略過），只在書籤或已開啟場景變動時做一次。
        private static void EnsureBadgeCache()
        {
            if (resolvedSceneSetVersion == sceneSetVersion && resolvedBookmarkVersion == BookmarkVersion) return;
            resolvedSceneSetVersion = sceneSetVersion;
            resolvedBookmarkVersion = BookmarkVersion;
            bookmarkFileGuids.Clear();
            bookmarkLiveInstanceIds.Clear();
            foreach (var b in Data.bookmarks)
            {
                if (b == null) continue;
                if (!string.IsNullOrEmpty(b.guid)) bookmarkFileGuids.Add(b.guid);
                var obj = RefToObject(b);
                if (obj != null) bookmarkLiveInstanceIds.Add(obj.GetInstanceID());
            }
        }

        internal static bool IsBookmarkedFile(string guid)
        {
            if (!IsEnabled || string.IsNullOrEmpty(guid)) return false;
            EnsureBadgeCache();
            return bookmarkFileGuids.Contains(guid);
        }

        internal static bool IsBookmarkedInstance(int instanceId)
        {
            if (!IsEnabled || instanceId == 0) return false;
            EnsureBadgeCache();
            return bookmarkLiveInstanceIds.Contains(instanceId);
        }

        private static void RegisterSceneSetEvents()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.newSceneCreated -= OnNewScene;
            EditorSceneManager.newSceneCreated += OnNewScene;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode) => OnSceneSetChanged();
        private static void OnSceneClosed(UnityEngine.SceneManagement.Scene scene) => OnSceneSetChanged();
        private static void OnNewScene(UnityEngine.SceneManagement.Scene scene, NewSceneSetup setup, NewSceneMode mode) => OnSceneSetChanged();

        // ====================== 時間 ======================
        internal static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        internal static string FormatRelativeTime(long unixMs, long nowMs)
        {
            if (unixMs <= 0) return string.Empty;
            double seconds = (nowMs - unixMs) / 1000.0;
            if (seconds < 60) return BookmarkConstants.LabelJustNow;
            if (seconds < 3600) return string.Format(BookmarkConstants.LabelMinutesAgo, (int)(seconds / 60));
            if (seconds < 86400) return string.Format(BookmarkConstants.LabelHoursAgo, (int)(seconds / 3600));
            return string.Format(BookmarkConstants.LabelDaysAgo, (int)(seconds / 86400));
        }
    }
}
#endif
