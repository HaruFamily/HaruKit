#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // Popup 與 dockable Window 共用繪製；host 決定選取後是否關閉。
    internal sealed class BookmarksGUI
    {
        private const float RowHeight = 28f;
        private GUIStyle rowStyle, favoriteStyle, folderHeaderStyle;
        private readonly SearchBar searchBookmarkBar = new();
        private readonly SearchBar searchHistoryBar = new();
        private readonly DragSortHandler drag = new();
        private Vector2 scrollAll;
        private string newFolderInput = string.Empty;
        private string renamingFolder;
        private string renameInput = string.Empty;

        private struct RowData
        {
            public ObjectRef item;
            public UnityEngine.Object obj;
            public string label;
        }

        private sealed class FolderRows
        {
            public string name;
            public int total;
            public bool folded;
            public readonly List<RowData> rows = new();
        }

        private readonly Dictionary<string, FolderRows> folders = new(StringComparer.Ordinal);
        private readonly List<FolderRows> folderOrder = new();
        private readonly List<string> removedFolders = new();
        private readonly HashSet<string> activeFolders = new(StringComparer.Ordinal);
        private readonly List<RowData> historyRows = new();
        private readonly Dictionary<ObjectRef, UnityEngine.Object> resolveCache = new();
        private readonly HashSet<ObjectRef> activeReferences = new();
        private readonly List<ObjectRef> removedReferences = new();
        private int resolveVersion = -1;
        private bool hasSnapshot;
        private string bookmarkKeyword, historyKeyword;
        private int bookmarkTotal, bookmarkMatched, historyTotal;

        private UnityEngine.Object Resolve(ObjectRef item)
        {
            if (item == null) return null;
            activeReferences.Add(item);
            if (resolveCache.TryGetValue(item, out var cached))
            {
                // 真正的 null 是已查證的 missing；Unity destroyed wrapper 則重新解析一次。
                if (cached != null || ReferenceEquals(cached, null)) return cached;
            }
            var obj = Inspector.RefToObject(item);
            resolveCache[item] = obj;
            return obj;
        }

        private void InitStyles()
        {
            if (rowStyle != null) return;
            rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            favoriteStyle = new GUIStyle(EditorStyles.miniButtonRight) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            folderHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        }

        private static string GetLabel(UnityEngine.Object obj)
        {
            bool isAsset = EditorUtility.IsPersistent(obj);
            return $"{(isAsset ? InspectorConstants.PrefixAsset : InspectorConstants.PrefixScene)} {obj.name} ({obj.GetType().Name})";
        }

        internal static bool MatchesSearch(string label, string folder, string keyword)
        {
            if (label == null) return false;
            return string.IsNullOrEmpty(keyword)
                || label.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                || (folder != null && folder == keyword);
        }

        private void AddFolderSnapshot(string name, bool folded)
        {
            if (!activeFolders.Add(name)) return;
            if (!folders.TryGetValue(name, out var group))
            {
                group = new FolderRows { name = name };
                folders.Add(name, group);
            }
            group.total = 0;
            group.folded = folded;
            group.rows.Clear();
            folderOrder.Add(group);
        }

        // 每個 Layout 建一次 O(書籤 + 歷史 + 資料夾) 快照；其餘事件沿用，保持 IMGUI control 順序一致。
        private void BuildSnapshot()
        {
            if (resolveVersion != Inspector.ReferenceVersion)
            {
                resolveCache.Clear();
                resolveVersion = Inspector.ReferenceVersion;
            }
            var data = JSONStorage.Data;
            bookmarkKeyword = searchBookmarkBar.Keyword;
            historyKeyword = searchHistoryBar.Keyword;
            bookmarkTotal = data.bookmarks.Count;
            historyTotal = data.history.Count;
            bookmarkMatched = 0;
            folderOrder.Clear();
            activeFolders.Clear();
            activeReferences.Clear();
            historyRows.Clear();

            foreach (var folder in data.folders)
                if (folder != null && !string.IsNullOrEmpty(folder.name)) AddFolderSnapshot(folder.name, folder.fold);
            AddFolderSnapshot(string.Empty, data.foldUncategorized);

            removedFolders.Clear();
            foreach (var name in folders.Keys)
                if (!activeFolders.Contains(name)) removedFolders.Add(name);
            foreach (var name in removedFolders) folders.Remove(name);
            if (renamingFolder != null && !activeFolders.Contains(renamingFolder)) renamingFolder = null;

            foreach (var item in data.bookmarks)
            {
                if (item == null) continue;
                if (!folders.TryGetValue(item.folder ?? string.Empty, out var group)) group = folders[string.Empty];
                group.total++;
                activeReferences.Add(item);
                if (string.IsNullOrEmpty(bookmarkKeyword) && (!data.foldBookmarks || group.folded)) continue;
                var obj = Resolve(item);
                if (obj == null) continue;
                string label = GetLabel(obj);
                if (!MatchesSearch(label, group.name, bookmarkKeyword)) continue;
                group.rows.Add(new RowData { item = item, obj = obj, label = label });
                bookmarkMatched++;
            }
            foreach (var item in data.history)
            {
                if (item == null) continue;
                activeReferences.Add(item);
                if (string.IsNullOrEmpty(historyKeyword) && !data.foldHistory) continue;
                var obj = Resolve(item);
                if (obj == null) continue;
                string label = GetLabel(obj);
                if (MatchesSearch(label, null, historyKeyword))
                    historyRows.Add(new RowData { item = item, obj = obj, label = label });
            }

            // 移除被清空或被歷史容量淘汰的引用，長駐視窗不累積物件快取。
            removedReferences.Clear();
            foreach (var item in resolveCache.Keys)
                if (!activeReferences.Contains(item)) removedReferences.Add(item);
            foreach (var item in removedReferences) resolveCache.Remove(item);
            hasSnapshot = true;
        }

        private static string CountLabel(int matched, int total, string keyword)
        {
            return string.IsNullOrEmpty(keyword) ? total.ToString() : $"{matched}/{total}";
        }

        public void DrawBody(Action onItemPicked, Action repaint)
        {
            InitStyles();
            if (Event.current.type == EventType.MouseMove) repaint?.Invoke();
            if (!string.IsNullOrEmpty(JSONStorage.LastError))
                EditorGUILayout.HelpBox(JSONStorage.LastError, MessageType.Error);
            if (!Inspector.IsEnabled)
            {
                if (JSONStorage.IsAvailable) EditorGUILayout.HelpBox("Pin Inspector 目前已停用。", MessageType.Info);
                return;
            }
            if (!hasSnapshot || Event.current.type == EventType.Layout) BuildSnapshot();

            scrollAll = EditorGUILayout.BeginScrollView(scrollAll);
            DrawBookmarksSection(onItemPicked, repaint);
            GUILayout.Space(6);
            DrawHistorySection(onItemPicked, repaint);
            EditorGUILayout.EndScrollView();
        }

        private static void EndChangedGUI(Action repaint)
        {
            repaint?.Invoke();
            GUIUtility.ExitGUI();
        }

        private void DrawBookmarksSection(Action onItemPicked, Action repaint)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var data = JSONStorage.Data;
            string arrow = data.foldBookmarks ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} {InspectorConstants.LabelBookmarks} ({CountLabel(bookmarkMatched, bookmarkTotal, bookmarkKeyword)})",
                    EditorStyles.boldLabel, GUILayout.ExpandWidth(true)))
            {
                data.foldBookmarks = !data.foldBookmarks;
                Inspector.Save();
                EndChangedGUI(repaint);
            }
            if (GUILayout.Button(InspectorConstants.LabelClear, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                if (bookmarkTotal > 0 && ConfirmClear(InspectorConstants.LabelBookmarks)) Inspector.ClearAllBookmarks();
                EndChangedGUI(repaint);
            }
            EditorGUILayout.EndHorizontal();
            if (!data.foldBookmarks)
            {
                GUILayout.EndVertical();
                return;
            }

            searchBookmarkBar.Draw();
            if (searchBookmarkBar.Keyword != bookmarkKeyword) EndChangedGUI(repaint);

            EditorGUILayout.BeginHorizontal();
            newFolderInput = EditorGUILayout.TextField(newFolderInput, GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newFolderInput)))
            {
                if (GUILayout.Button(InspectorConstants.LabelAddFolder, EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    if (Inspector.AddFolder(newFolderInput))
                    {
                        newFolderInput = string.Empty;
                        GUI.FocusControl(null);
                    }
                    else ShowInvalidFolder(InspectorConstants.LabelAddFolder);
                    EndChangedGUI(repaint);
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
            foreach (var folder in folderOrder) DrawFolder(folder, onItemPicked, repaint);
            GUILayout.EndVertical();
        }

        private void DrawHistorySection(Action onItemPicked, Action repaint)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var data = JSONStorage.Data;
            string arrow = data.foldHistory ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} {InspectorConstants.LabelHistory} ({CountLabel(historyRows.Count, historyTotal, historyKeyword)})",
                    EditorStyles.boldLabel, GUILayout.ExpandWidth(true)))
            {
                data.foldHistory = !data.foldHistory;
                Inspector.Save();
                EndChangedGUI(repaint);
            }
            if (GUILayout.Button(InspectorConstants.LabelClear, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                if (historyTotal == 0 || ConfirmClear(InspectorConstants.LabelHistory)) Inspector.ClearAllHistory();
                EndChangedGUI(repaint);
            }
            EditorGUILayout.EndHorizontal();
            if (data.foldHistory)
            {
                searchHistoryBar.Draw();
                if (searchHistoryBar.Keyword != historyKeyword) EndChangedGUI(repaint);
                for (int i = 0; i < historyRows.Count; i++) DrawRow(historyRows[i], i, false, onItemPicked, repaint);
            }
            GUILayout.EndVertical();
        }

        private static bool ConfirmClear(string label) => EditorUtility.DisplayDialog(
            InspectorConstants.LabelClearConfirmTitle, string.Format(InspectorConstants.LabelClearConfirmMsg, label),
            InspectorConstants.LabelYes, InspectorConstants.LabelNo);

        private static void ShowInvalidFolder(string title) => EditorUtility.DisplayDialog(
            title, InspectorConstants.LabelInvalidFolderName, InspectorConstants.LabelYes);

        private void DrawFolder(FolderRows folder, Action onItemPicked, Action repaint)
        {
            string name = folder.name;
            bool uncategorized = name.Length == 0;
            EditorGUILayout.BeginHorizontal();
            if (!uncategorized && renamingFolder == name)
            {
                renameInput = EditorGUILayout.TextField(renameInput, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(InspectorConstants.LabelYes, EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    if (renameInput.Trim() == name || Inspector.RenameFolder(name, renameInput))
                    {
                        renamingFolder = null;
                        GUI.FocusControl(null);
                    }
                    else ShowInvalidFolder(InspectorConstants.LabelRenameFolderTitle);
                    EndChangedGUI(repaint);
                }
                if (GUILayout.Button(InspectorConstants.LabelNo, EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    renamingFolder = null;
                    GUI.FocusControl(null);
                    EndChangedGUI(repaint);
                }
            }
            else
            {
                string display = uncategorized ? InspectorConstants.LabelUncategorized : name;
                string arrow = folder.folded ? "▶" : "▼";
                if (GUILayout.Button($"{arrow} {display} ({CountLabel(folder.rows.Count, folder.total, bookmarkKeyword)})",
                        folderHeaderStyle, GUILayout.ExpandWidth(true)))
                {
                    Inspector.ToggleFolderFold(name);
                    EndChangedGUI(repaint);
                }
                if (!uncategorized)
                {
                    if (GUILayout.Button("✎", EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    {
                        renamingFolder = name;
                        renameInput = name;
                        GUI.FocusControl(null);
                        EndChangedGUI(repaint);
                    }
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    {
                        if (EditorUtility.DisplayDialog(InspectorConstants.LabelDeleteFolderConfirmTitle,
                                string.Format(InspectorConstants.LabelDeleteFolderConfirmMsg, name, folder.total),
                                InspectorConstants.LabelYes, InspectorConstants.LabelNo)) Inspector.DeleteFolder(name);
                        EndChangedGUI(repaint);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            HandleDrop(GUILayoutUtility.GetLastRect(), name, null, repaint);
            if (folder.folded) return;
            for (int i = 0; i < folder.rows.Count; i++) DrawRow(folder.rows[i], i, true, onItemPicked, repaint);
        }

        private void DrawRow(RowData data, int index, bool bookmarkRow, Action onItemPicked, Action repaint)
        {
            Rect row = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            bool alive = data.obj != null;
            if (Event.current.type == EventType.Repaint)
            {
                bool current = alive && Selection.activeObject == data.obj;
                bool hover = row.Contains(Event.current.mousePosition);
                Color background = current ? new Color(0.24f, 0.45f, 0.85f, 0.35f)
                    : hover ? new Color(0.4f, 0.4f, 0.4f, 0.25f)
                    : index % 2 == 0 ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.93f, 0.93f, 0.93f))
                    : Color.clear;
                EditorGUI.DrawRect(row, background);
            }

            Rect openRect = new(row.x + 6, row.y + 5, 36, 18);
            using (new EditorGUI.DisabledScope(!Inspector.CanOpen(data.obj)))
            {
                if (GUI.Button(openRect, InspectorConstants.LabelOpen, EditorStyles.miniButtonLeft))
                {
                    AssetDatabase.OpenAsset(data.obj);
                    EndChangedGUI(repaint);
                }
            }
            Rect favoriteRect = new(openRect.xMax, row.y + 5, 21, 18);
            bool bookmarked = bookmarkRow || Inspector.IsBookmarked(data.item);
            using (new EditorGUI.DisabledScope(!alive))
            {
                if (GUI.Button(favoriteRect, bookmarked ? InspectorConstants.PrefixOnBookMarks : InspectorConstants.PrefixOffBookMarks, favoriteStyle))
                {
                    Inspector.ToggleBookmark(data.obj);
                    EndChangedGUI(repaint);
                }
            }
            var e = Event.current;
            if (alive && e.type == EventType.MouseDown && e.button == 1 && row.Contains(e.mousePosition))
            {
                ShowBookmarkMenu(data.obj, repaint);
                e.Use();
            }

            Rect iconRect = new(favoriteRect.xMax + 6, row.y + (RowHeight - 22f) * 0.5f, 22f, 22f);
            if (alive && e.type == EventType.Repaint)
            {
                var icon = EditorGUIUtility.ObjectContent(data.obj, data.obj.GetType()).image;
                if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }
            Rect labelRect = new(iconRect.xMax + 8, row.y + 5, Mathf.Max(0, row.xMax - iconRect.xMax - 14), 18);
            GUI.Box(labelRect, data.label, rowStyle);
            Rect selectionRect = new(iconRect.x, row.y, Mathf.Max(0, row.xMax - iconRect.x), row.height);
            if (drag.HandleItem(selectionRect, data.obj, data.label, bookmarkRow ? data.item : null))
            {
                if (data.obj == null) return;
                Selection.activeObject = data.obj;
                onItemPicked?.Invoke();
                EndChangedGUI(repaint);
            }
            if (bookmarkRow) HandleDrop(row, data.item.folder ?? string.Empty, data.item, repaint);
        }

        private static void HandleDrop(Rect rect, string folder, ObjectRef target, Action repaint)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
            var source = DragSortHandler.GetDraggedBookmark();
            if (source == null || source == target || (target == null && (source.folder ?? string.Empty) == folder)
                || (target != null && !JSONStorage.Data.bookmarks.Contains(target)))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                e.Use();
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragPerform)
            {
                if (target == null) Inspector.MoveBookmarkToFolder(source, folder);
                else Inspector.ReorderBookmark(source, target);
                DragAndDrop.AcceptDrag();
                e.Use();
                EndChangedGUI(repaint);
            }
            e.Use();
        }

        private static void ShowBookmarkMenu(UnityEngine.Object obj, Action repaint)
        {
            var existing = Inspector.FindBookmark(obj);
            bool bookmarked = existing != null;
            string currentFolder = existing?.folder ?? string.Empty;
            string prefix = (bookmarked ? InspectorConstants.LabelMoveToFolder : InspectorConstants.LabelAddBookmark) + "/";
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(bookmarked ? InspectorConstants.LabelRemoveBookmark : InspectorConstants.LabelAddBookmark), false, () =>
            {
                Inspector.ToggleBookmark(obj);
                repaint?.Invoke();
            });
            menu.AddSeparator(string.Empty);
            AddFolderMenuItem(menu, prefix, InspectorConstants.LabelUncategorized, string.Empty,
                bookmarked && currentFolder.Length == 0, obj, repaint);
            foreach (var folder in JSONStorage.Data.folders)
            {
                if (folder == null || string.IsNullOrEmpty(folder.name)) continue;
                AddFolderMenuItem(menu, prefix, folder.name, folder.name, bookmarked && currentFolder == folder.name, obj, repaint);
            }
            menu.ShowAsContext();
        }

        private static void AddFolderMenuItem(GenericMenu menu, string prefix, string label, string folder,
            bool selected, UnityEngine.Object obj, Action repaint)
        {
            menu.AddItem(new GUIContent(prefix + label), selected, () =>
            {
                Inspector.AddBookmarkToFolder(obj, folder);
                repaint?.Invoke();
            });
        }
    }
}
#endif
