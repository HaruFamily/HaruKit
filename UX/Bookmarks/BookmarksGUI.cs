#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks
{
    // Popup 與可停靠 Window 共用的書籤／紀錄列表。host 透過 onItemPicked 決定選取後是否關閉（popup 會關閉）。
    //
    // 操作：
    // - 單擊：選取並在 Project / Hierarchy 中 Ping；Popup 選取後關閉。
    // - 雙擊或 Enter：可開啟的（場景、Prefab、腳本…）就開啟，否則同單擊；Popup 中 Enter 只選取並關閉。
    // - Ctrl／Cmd+單擊：加入或取消多選；Shift+單擊：範圍多選（同一區塊內）。
    // - Alt+單擊或中鍵：只 Ping，不改變選取。
    // - ↑↓ 移動游標、Delete 移除選取的書籤、Esc 清除搜尋（Popup 中搜尋為空時關閉）。
    internal sealed class BookmarksGUI
    {
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float ActionWidth = 20f;
        private const float BookmarkIndent = 16f;
        private const string NewFolderControl = "HaruFamily.Bookmarks.NewFolder";
        private const string RenameControl = "HaruFamily.Bookmarks.RenameFolder";

        private struct RowData
        {
            public ObjectRef item;
            public Object obj;
            public string label;      // 搜尋比對用
            public GUIContent name;   // 名稱 + tooltip（路徑）
            public string detail;     // 名稱後方的灰字：型別或失效原因
            public Texture icon;
            public bool sceneUnloaded;
            public bool canOpen;
            public bool bookmarkRow;
            public int historyIndex;  // 紀錄列在 history 中的位置；書籤列為 -1
        }

        private sealed class FolderRows
        {
            public string name;
            public int total;
            public bool folded;
            public readonly List<RowData> rows = new();
        }

        // ---------- 樣式 ----------
        private GUIStyle nameStyle, missingStyle, detailStyle, timeStyle, headerStyle, sectionStyle, emptyStyle, placeholderStyle, iconButtonStyle;

        // ---------- 狀態 ----------
        private readonly SearchBar search = new();
        private readonly DragSortHandler drag = new();
        private Vector2 scroll;
        private bool creatingFolder, focusNewFolder;
        private string newFolderInput = string.Empty;
        private string renamingFolder;
        private string renameInput = string.Empty;
        private bool focusRename;

        // 列表內的選取（與 Unity Selection 同步）；cursor 是鍵盤游標，anchor 是 Shift 範圍選取的起點。
        private readonly HashSet<ObjectRef> picked = new();
        private ObjectRef cursor, anchor;
        private Object appliedSelection;
        private bool scrollToCursor;
        private Rect cursorRect;

        // hover 只在指到的列改變時重畫。
        private int rowCounter, hoverCandidate = -1, hoveredRow = -1;
        // 拖放落點提示：DragUpdated 時記下，Repaint 時畫。
        private Rect dropRect, pendingDropRect;
        private bool dropAsLine, pendingDropAsLine;

        // ---------- 快照 ----------
        // 只在資料、引用或搜尋字變動時於 Layout 事件重建；其他事件沿用，維持 IMGUI control 順序一致。
        private readonly Dictionary<string, FolderRows> folders = new(StringComparer.Ordinal);
        private readonly List<FolderRows> folderOrder = new();
        private readonly List<string> removedFolders = new();
        private readonly HashSet<string> activeFolders = new(StringComparer.Ordinal);
        private readonly List<RowData> historyRows = new();
        private readonly List<int> historyIndices = new();
        private readonly List<RowData> navRows = new();
        private readonly List<ObjectRef> missingBookmarks = new();
        private readonly Dictionary<ObjectRef, Object> resolveCache = new();
        private readonly Dictionary<ObjectRef, RowData> missingRowCache = new();
        private readonly HashSet<ObjectRef> activeReferences = new();
        private readonly List<ObjectRef> removedReferences = new();
        private int resolveVersion = -1;
        private bool hasSnapshot;
        private int snapshotStorageVersion = -1, snapshotReferenceVersion = -1, snapshotBookmarkVersion = -1;
        private string snapshotKeyword;
        private string keyword;
        private int bookmarkTotal, bookmarkMatched, historyTotal;

        public void FocusSearch() => search.RequestFocus();

        // ====================== Styles ======================
        private void InitStyles()
        {
            if (nameStyle != null) return;
            nameStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
            missingStyle = new GUIStyle(nameStyle) { fontStyle = FontStyle.Italic };
            missingStyle.normal.textColor = Gray(0.5f);
            detailStyle = new GUIStyle(EditorStyles.miniLabel) { padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
            detailStyle.normal.textColor = Gray(0.45f);
            timeStyle = new GUIStyle(detailStyle) { alignment = TextAnchor.MiddleRight };
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(0, 0, 0, 0) };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, padding = new RectOffset(0, 0, 0, 0) };
            emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 11, padding = new RectOffset(8, 8, 10, 10) };
            placeholderStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(4, 0, 0, 0) };
            placeholderStyle.normal.textColor = Gray(0.4f);
            iconButtonStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(1, 1, 1, 1) };
        }

        private static Color Gray(float alpha) => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, alpha + 0.1f) : new Color(0f, 0f, 0f, alpha);
        private static Color SelectionColor => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.75f, 0.55f);
        private static Color SoftSelectionColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.68f, 0.68f, 0.68f, 0.6f);
        private static Color HoverColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.06f);
        private static Color AccentColor => new(0.27f, 0.6f, 1f);

        // ====================== Search ======================
        // 每個以空白分隔的字都要出現在名稱裡（不分大小寫）；或關鍵字剛好等於資料夾名稱。
        internal static bool MatchesSearch(string label, string folder, string keyword)
        {
            if (label == null) return false;
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            if (folder != null && folder == keyword) return true;
            foreach (var token in keyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (label.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        // ====================== Snapshot ======================
        private Object Resolve(ObjectRef item)
        {
            if (item == null) return null;
            activeReferences.Add(item);
            if (resolveCache.TryGetValue(item, out var cached))
            {
                // 真正的 null 是已查證的 missing；Unity destroyed wrapper 則重新解析一次。
                if (cached != null || ReferenceEquals(cached, null)) return cached;
            }
            var obj = BookmarkService.RefToObject(item);
            resolveCache[item] = obj;
            return obj;
        }

        private bool IsSnapshotStale()
        {
            return snapshotStorageVersion != BookmarkStorage.Version
                || snapshotReferenceVersion != BookmarkService.ReferenceVersion
                || snapshotBookmarkVersion != BookmarkService.BookmarkVersion
                || snapshotKeyword != search.Keyword;
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

        private bool Searching => !string.IsNullOrWhiteSpace(keyword);

        private void BuildSnapshot()
        {
            snapshotStorageVersion = BookmarkStorage.Version;
            snapshotReferenceVersion = BookmarkService.ReferenceVersion;
            snapshotBookmarkVersion = BookmarkService.BookmarkVersion;
            snapshotKeyword = search.Keyword;
            if (resolveVersion != BookmarkService.ReferenceVersion)
            {
                resolveCache.Clear();
                missingRowCache.Clear();
                resolveVersion = BookmarkService.ReferenceVersion;
            }

            var data = BookmarkStorage.Data;
            keyword = search.Keyword?.Trim() ?? string.Empty;
            bookmarkTotal = data.bookmarks.Count;
            bookmarkMatched = 0;
            folderOrder.Clear();
            activeFolders.Clear();
            activeReferences.Clear();
            historyRows.Clear();
            navRows.Clear();
            missingBookmarks.Clear();

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
                var row = MakeRow(item, -1);
                if (row.obj == null) missingBookmarks.Add(item);
                if (!MatchesSearch(row.label, group.name, keyword)) continue;
                group.rows.Add(row);
                bookmarkMatched++;
            }
            // 只有「未分類」時不畫資料夾標題，列一律顯示（與 DrawBookmarksSection 一致）。
            bool onlyUncategorized = folderOrder.Count == 1;
            if (data.foldBookmarks)
                foreach (var group in folderOrder)
                    if (!group.folded || Searching || onlyUncategorized) navRows.AddRange(group.rows);

            historyIndices.Clear();
            if (data.mergeDuplicateHistory) BookmarkService.CollectLatestDistinct(data.history, historyIndices);
            else for (int i = 0; i < data.history.Count; i++) historyIndices.Add(i);
            historyTotal = historyIndices.Count;
            // 紀錄新的在上。
            for (int k = historyIndices.Count - 1; k >= 0; k--)
            {
                int i = historyIndices[k];
                var item = data.history[i];
                if (item == null) continue;
                activeReferences.Add(item);
                var row = MakeRow(item, i);
                if (MatchesSearch(row.label, null, keyword)) historyRows.Add(row);
            }
            if (data.foldHistory) navRows.AddRange(historyRows);

            // 移除已不存在的引用，長駐視窗不累積快取與選取。
            removedReferences.Clear();
            foreach (var item in resolveCache.Keys)
                if (!activeReferences.Contains(item)) removedReferences.Add(item);
            foreach (var item in removedReferences) resolveCache.Remove(item);
            removedReferences.Clear();
            foreach (var item in missingRowCache.Keys)
                if (!activeReferences.Contains(item)) removedReferences.Add(item);
            foreach (var item in removedReferences) missingRowCache.Remove(item);
            picked.RemoveWhere(item => !activeReferences.Contains(item));
            if (cursor != null && !activeReferences.Contains(cursor)) cursor = null;
            if (anchor != null && !activeReferences.Contains(anchor)) anchor = null;
            hasSnapshot = true;
        }

        private RowData MakeRow(ObjectRef item, int historyIndex)
        {
            var obj = Resolve(item);
            if (obj != null)
            {
                string label = BookmarkService.GetLabel(obj);
                item.label = label; // 記住最新名稱，之後失效時顯示；隨下次存檔寫入
                return new RowData
                {
                    item = item,
                    obj = obj,
                    label = label,
                    name = new GUIContent(obj.name, BookmarkService.GetLocationTooltip(obj)),
                    detail = obj.GetType().Name,
                    icon = EditorGUIUtility.ObjectContent(obj, obj.GetType()).image,
                    canOpen = BookmarkService.CanOpen(obj),
                    bookmarkRow = historyIndex < 0,
                    historyIndex = historyIndex,
                };
            }
            if (!missingRowCache.TryGetValue(item, out var missing))
            {
                string label = BookmarkService.GetMissingLabel(item);
                missing = new RowData
                {
                    item = item,
                    label = label,
                    name = new GUIContent(label),
                    sceneUnloaded = BookmarkService.TryGetUnloadedScene(item, out _),
                };
                missingRowCache[item] = missing;
            }
            missing.bookmarkRow = historyIndex < 0;
            missing.historyIndex = historyIndex;
            return missing;
        }

        // ====================== Body ======================
        // drawToolbarExtras：host 在搜尋框右側加的按鈕（Popup 的「釘選成視窗」）。
        public void DrawBody(Action onItemPicked, Action repaint, Action drawToolbarExtras = null)
        {
            InitStyles();
            var e = Event.current;
            bool mouseMove = e.type == EventType.MouseMove;
            bool dragUpdated = e.type == EventType.DragUpdated;
            if (mouseMove) hoverCandidate = -1;
            if (dragUpdated)
            {
                pendingDropRect = default;
                pendingDropAsLine = false;
            }
            if (e.type == EventType.DragExited || e.type == EventType.DragPerform) dropRect = default;
            rowCounter = 0;

            if (!string.IsNullOrEmpty(BookmarkStorage.LastError))
                EditorGUILayout.HelpBox(BookmarkStorage.LastError, MessageType.Error);
            if (!BookmarkService.IsEnabled)
            {
                if (BookmarkStorage.IsAvailable) EditorGUILayout.HelpBox(BookmarkConstants.LabelSystemDisabled, MessageType.Info);
                return;
            }
            if (!hasSnapshot || (e.type == EventType.Layout && IsSnapshotStale())) BuildSnapshot();
            SyncPickedWithSelection();

            HandleKeyboard(onItemPicked, repaint);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            search.DrawToolbar();
            drawToolbarExtras?.Invoke();
            EditorGUILayout.EndHorizontal();

            DrawUndoBar(repaint);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawBookmarksSection(onItemPicked, repaint);
            GUILayout.Space(4);
            DrawHistorySection(onItemPicked, repaint);
            if (Searching && bookmarkMatched == 0 && historyRows.Count == 0)
                GUILayout.Label(string.Format(BookmarkConstants.LabelNoMatches, keyword), emptyStyle);
            GUILayout.Space(6);
            EditorGUILayout.EndScrollView();

            if (scrollToCursor && e.type == EventType.Repaint)
            {
                scrollToCursor = false;
                Rect view = GUILayoutUtility.GetLastRect();
                if (cursorRect.height > 0)
                {
                    if (cursorRect.yMin < scroll.y) scroll.y = cursorRect.yMin;
                    else if (cursorRect.yMax > scroll.y + view.height) scroll.y = cursorRect.yMax - view.height;
                    repaint?.Invoke();
                }
            }
            if (mouseMove && hoverCandidate != hoveredRow)
            {
                hoveredRow = hoverCandidate;
                repaint?.Invoke();
            }
            if (dragUpdated && (pendingDropRect != dropRect || pendingDropAsLine != dropAsLine))
            {
                dropRect = pendingDropRect;
                dropAsLine = pendingDropAsLine;
                repaint?.Invoke();
            }
        }

        private static void EndChangedGUI(Action repaint)
        {
            repaint?.Invoke();
            GUIUtility.ExitGUI();
        }

        // ====================== Selection ======================
        // Unity 的選取被外部改掉（點了 Hierarchy 等）時，清掉列表內的多選，避免殘留高亮。
        private void SyncPickedWithSelection()
        {
            if (picked.Count == 0 || Selection.activeObject == appliedSelection) return;
            picked.Clear();
        }

        private void ApplyPickedSelection()
        {
            var objects = new List<Object>();
            foreach (var row in navRows)
                if (row.obj != null && picked.Contains(row.item) && !objects.Contains(row.obj)) objects.Add(row.obj);
            if (objects.Count > 0) Selection.objects = objects.ToArray();
            appliedSelection = Selection.activeObject;
        }

        private void SelectSingle(RowData row, bool ping)
        {
            picked.Clear();
            picked.Add(row.item);
            cursor = anchor = row.item;
            if (row.obj != null) Selection.activeObject = row.obj;
            appliedSelection = Selection.activeObject;
            if (ping && row.obj != null) EditorGUIUtility.PingObject(row.obj);
        }

        // 雙擊／Enter：可開啟的就開啟，場景未開啟的就切過去，其餘選取並 Ping。
        private void Activate(RowData row, Action onItemPicked, bool openInPopup)
        {
            if (row.obj == null)
            {
                if (row.sceneUnloaded && !EditorApplication.isPlaying) BookmarkService.OpenSceneOf(row.item);
                return;
            }
            SelectSingle(row, ping: true);
            if (onItemPicked != null && !openInPopup)
            {
                onItemPicked();
                return;
            }
            if (row.canOpen) AssetDatabase.OpenAsset(row.obj);
            onItemPicked?.Invoke();
        }

        private void OnRowClicked(RowData row, RowClick click, EventModifiers modifiers, Action onItemPicked, Action repaint)
        {
            if (click == RowClick.DoubleClick)
            {
                Activate(row, onItemPicked, openInPopup: true);
                EndChangedGUI(repaint);
            }
            if ((modifiers & EventModifiers.Alt) != 0)
            {
                if (row.obj != null) EditorGUIUtility.PingObject(row.obj);
                cursor = row.item;
                EndChangedGUI(repaint);
            }
            if ((modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0)
            {
                if (!picked.Remove(row.item)) picked.Add(row.item);
                cursor = anchor = row.item;
                ApplyPickedSelection();
                EndChangedGUI(repaint);
            }
            if ((modifiers & EventModifiers.Shift) != 0 && anchor != null)
            {
                int from = navRows.FindIndex(r => r.item == anchor);
                int to = navRows.FindIndex(r => r.item == row.item);
                if (from >= 0 && to >= 0)
                {
                    picked.Clear();
                    int step = from <= to ? 1 : -1;
                    for (int i = from; ; i += step)
                    {
                        if (navRows[i].bookmarkRow == row.bookmarkRow) picked.Add(navRows[i].item);
                        if (i == to) break;
                    }
                    cursor = row.item;
                    ApplyPickedSelection();
                    EndChangedGUI(repaint);
                }
            }

            SelectSingle(row, ping: true);
            if (row.obj != null) onItemPicked?.Invoke();
            EndChangedGUI(repaint);
        }

        // ====================== Keyboard ======================
        private void HandleKeyboard(Action onItemPicked, Action repaint)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            // 正在輸入資料夾名稱時不搶按鍵。
            if (!search.HasFocus && GUIUtility.keyboardControl != 0) return;

            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                case KeyCode.UpArrow:
                    MoveCursor(e.keyCode == KeyCode.DownArrow ? 1 : -1);
                    e.Use();
                    repaint?.Invoke();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    int index = navRows.FindIndex(r => r.item == cursor);
                    if (index < 0 && Searching && navRows.Count > 0) index = 0; // 搜尋後直接 Enter 選第一筆
                    if (index < 0) return;
                    e.Use();
                    Activate(navRows[index], onItemPicked, openInPopup: false);
                    EndChangedGUI(repaint);
                    break;
                }
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (search.HasFocus && !string.IsNullOrEmpty(search.Keyword)) return;
                    if (e.keyCode == KeyCode.Backspace && search.HasFocus) return;
                    e.Use();
                    RemovePickedBookmarks();
                    EndChangedGUI(repaint);
                    break;
                case KeyCode.Escape:
                    e.Use();
                    if (!string.IsNullOrEmpty(search.Keyword)) search.Clear();
                    else if (picked.Count > 0) picked.Clear();
                    else onItemPicked?.Invoke();
                    EndChangedGUI(repaint);
                    break;
            }
        }

        private void MoveCursor(int delta)
        {
            if (navRows.Count == 0) return;
            int index = navRows.FindIndex(r => r.item == cursor);
            index = index < 0 ? (delta > 0 ? 0 : navRows.Count - 1) : Mathf.Clamp(index + delta, 0, navRows.Count - 1);
            cursor = anchor = navRows[index].item;
            picked.Clear();
            picked.Add(cursor);
            appliedSelection = Selection.activeObject;
            scrollToCursor = true;
        }

        private List<ObjectRef> PickedBookmarks()
        {
            var result = new List<ObjectRef>();
            foreach (var item in BookmarkStorage.Data.bookmarks)
                if (picked.Contains(item)) result.Add(item);
            return result;
        }

        private void RemovePickedBookmarks()
        {
            var items = PickedBookmarks();
            if (items.Count == 0 && cursor != null && BookmarkStorage.Data.bookmarks.Contains(cursor)) items.Add(cursor);
            if (items.Count == 0) return;
            BookmarkService.RemoveBookmarks(items);
            picked.Clear();
        }

        // ====================== Undo bar ======================
        private void DrawUndoBar(Action repaint)
        {
            if (!BookmarkService.CanUndo) return;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(BookmarkService.UndoMessage, EditorStyles.label, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(BookmarkConstants.LabelUndo, EditorStyles.miniButton, GUILayout.Width(56)))
            {
                BookmarkService.UndoLastRemoval();
                EndChangedGUI(repaint);
            }
            if (GUILayout.Button(new GUIContent("✕", BookmarkConstants.LabelDismissTooltip), EditorStyles.miniButton, GUILayout.Width(22)))
            {
                BookmarkService.DismissUndo();
                EndChangedGUI(repaint);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ====================== Sections ======================
        private static string CountLabel(int matched, int total, bool searching) => searching ? $"{matched}/{total}" : total.ToString();

        private bool DrawSectionHeader(string title, bool expanded, int matched, int total, out Rect rect)
        {
            rect = GUILayoutUtility.GetRect(0, HeaderHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Gray(0.12f));
                var arrow = new Rect(rect.x + 4, rect.y + 3, 14, 16);
                EditorStyles.foldout.Draw(arrow, false, false, expanded, false);
                sectionStyle.Draw(new Rect(rect.x + 20, rect.y + 3, rect.width, 16),
                    $"{title}  ({CountLabel(matched, total, Searching)})", false, false, false, false);
            }
            return e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition);
        }

        private void DrawBookmarksSection(Action onItemPicked, Action repaint)
        {
            var data = BookmarkStorage.Data;
            // 標題右側按鈕先處理，標題點擊要扣掉按鈕區。
            bool toggle = DrawSectionHeader(BookmarkConstants.LabelBookmarks, data.foldBookmarks, bookmarkMatched, bookmarkTotal, out Rect header);
            Rect menuRect = new(header.xMax - ActionWidth - 2, header.y + 1, ActionWidth, ActionWidth);
            Rect plusRect = new(menuRect.x - ActionWidth, header.y + 1, ActionWidth, ActionWidth);
            if (GUI.Button(plusRect, BookmarkIcons.Content(BookmarkIcons.Plus, "+", BookmarkConstants.LabelNewFolder), iconButtonStyle))
            {
                creatingFolder = true;
                focusNewFolder = true;
                newFolderInput = string.Empty;
                if (!data.foldBookmarks)
                {
                    data.foldBookmarks = true;
                    BookmarkService.Save();
                }
                EndChangedGUI(repaint);
            }
            if (GUI.Button(menuRect, BookmarkIcons.Content(BookmarkIcons.Menu, "⋯"), iconButtonStyle))
            {
                ShowBookmarksMenu(menuRect, repaint);
                GUIUtility.ExitGUI();
            }
            if (toggle && !plusRect.Contains(Event.current.mousePosition) && !menuRect.Contains(Event.current.mousePosition))
            {
                data.foldBookmarks = !data.foldBookmarks;
                BookmarkService.Save();
                EndChangedGUI(repaint);
            }
            if (!data.foldBookmarks) return;

            if (creatingFolder) DrawNewFolderRow(repaint);

            if (bookmarkTotal == 0 && !Searching)
            {
                GUILayout.Label(BookmarkConstants.LabelEmptyBookmarks, emptyStyle);
                HandleDrop(GUILayoutUtility.GetLastRect(), string.Empty, null, repaint);
                return;
            }

            foreach (var folder in folderOrder)
            {
                // 搜尋時只顯示有符合項目的資料夾，且一律展開。
                if (Searching && folder.rows.Count == 0) continue;
                // 沒有自訂資料夾時不必顯示「未分類」標題。
                bool onlyUncategorized = folderOrder.Count == 1 && folder.name.Length == 0;
                if (!onlyUncategorized) DrawFolderHeader(folder, repaint);
                if (folder.folded && !Searching && !onlyUncategorized) continue;
                foreach (var row in folder.rows) DrawRow(row, onlyUncategorized ? 0 : BookmarkIndent, onItemPicked, repaint);
            }

            if (missingBookmarks.Count > 0 && !Searching)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(string.Format(BookmarkConstants.LabelClearMissing, missingBookmarks.Count), EditorStyles.linkLabel))
                {
                    BookmarkService.RemoveBookmarks(new List<ObjectRef>(missingBookmarks));
                    EndChangedGUI(repaint);
                }
                GUILayout.Space(6);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ShowBookmarksMenu(Rect rect, Action repaint)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(BookmarkConstants.LabelNewFolder), false, () =>
            {
                creatingFolder = true;
                focusNewFolder = true;
                newFolderInput = string.Empty;
                repaint?.Invoke();
            });
            menu.AddSeparator(string.Empty);
            var missing = new List<ObjectRef>(missingBookmarks);
            if (missing.Count > 0)
                menu.AddItem(new GUIContent(string.Format(BookmarkConstants.LabelClearMissing, missing.Count)), false, () =>
                {
                    BookmarkService.RemoveBookmarks(missing);
                    repaint?.Invoke();
                });
            else menu.AddDisabledItem(new GUIContent(string.Format(BookmarkConstants.LabelClearMissing, 0)));
            if (bookmarkTotal > 0)
                menu.AddItem(new GUIContent(BookmarkConstants.LabelClearAllBookmarks), false, () =>
                {
                    if (ConfirmClear(BookmarkConstants.LabelBookmarks)) BookmarkService.ClearAllBookmarks();
                    repaint?.Invoke();
                });
            else menu.AddDisabledItem(new GUIContent(BookmarkConstants.LabelClearAllBookmarks));
            menu.DropDown(rect);
        }

        private void DrawHistorySection(Action onItemPicked, Action repaint)
        {
            var data = BookmarkStorage.Data;
            bool toggle = DrawSectionHeader(BookmarkConstants.LabelHistory, data.foldHistory, historyRows.Count, historyTotal, out Rect header);
            Rect clearRect = new(header.xMax - 44, header.y + 3, 40, 16);
            if (GUI.Button(clearRect, BookmarkConstants.LabelClear, EditorStyles.miniButton))
            {
                if (historyTotal == 0 || ConfirmClear(BookmarkConstants.LabelHistory)) BookmarkService.ClearAllHistory();
                EndChangedGUI(repaint);
            }
            if (toggle && !clearRect.Contains(Event.current.mousePosition))
            {
                data.foldHistory = !data.foldHistory;
                BookmarkService.Save();
                EndChangedGUI(repaint);
            }
            if (!data.foldHistory) return;
            foreach (var row in historyRows) DrawRow(row, 0, onItemPicked, repaint);
        }

        private static bool ConfirmClear(string label) => EditorUtility.DisplayDialog(
            BookmarkConstants.LabelClearConfirmTitle, string.Format(BookmarkConstants.LabelClearConfirmMsg, label),
            BookmarkConstants.LabelYes, BookmarkConstants.LabelNo);

        private static void ShowInvalidFolder(string title) => EditorUtility.DisplayDialog(
            title, BookmarkConstants.LabelInvalidFolderName, BookmarkConstants.LabelYes);

        // ====================== Folder rows ======================
        // TextField 會吃掉 Enter / Esc，必須在畫 TextField 之前檢查。
        private static bool ConsumeKey(string controlName, KeyCode key)
        {
            var e = Event.current;
            bool match = e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == controlName
                && (e.keyCode == key || (key == KeyCode.Return && e.keyCode == KeyCode.KeypadEnter));
            if (match) e.Use();
            return match;
        }

        private bool DrawInlineTextField(string control, ref string value, ref bool focus, string placeholder, Action repaint, out bool cancel)
        {
            bool submit = ConsumeKey(control, KeyCode.Return);
            cancel = ConsumeKey(control, KeyCode.Escape);
            GUI.SetNextControlName(control);
            value = EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(value) && placeholder != null && Event.current.type == EventType.Repaint)
                GUI.Label(GUILayoutUtility.GetLastRect(), placeholder, placeholderStyle);
            if (focus && Event.current.type == EventType.Repaint)
            {
                focus = false;
                EditorGUI.FocusTextInControl(control);
                repaint?.Invoke();
            }
            return submit;
        }

        private void DrawNewFolderRow(Action repaint)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            bool submit = DrawInlineTextField(NewFolderControl, ref newFolderInput, ref focusNewFolder,
                BookmarkConstants.LabelNewFolderPlaceholder, repaint, out bool cancel);
            if (GUILayout.Button(BookmarkConstants.LabelYes, EditorStyles.miniButton, GUILayout.Width(44)) || submit)
            {
                if (BookmarkService.AddFolder(newFolderInput))
                {
                    creatingFolder = false;
                    GUI.FocusControl(null);
                }
                else ShowInvalidFolder(BookmarkConstants.LabelNewFolder);
                EndChangedGUI(repaint);
            }
            if (GUILayout.Button(BookmarkConstants.LabelNo, EditorStyles.miniButton, GUILayout.Width(44)) || cancel)
            {
                creatingFolder = false;
                GUI.FocusControl(null);
                EndChangedGUI(repaint);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void StartRename(string name)
        {
            renamingFolder = name;
            renameInput = name;
            focusRename = true;
            GUI.FocusControl(null);
        }

        private void DrawFolderHeader(FolderRows folder, Action repaint)
        {
            string name = folder.name;
            bool uncategorized = name.Length == 0;
            if (!uncategorized && renamingFolder == name)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                bool submit = DrawInlineTextField(RenameControl, ref renameInput, ref focusRename, null, repaint, out bool cancel);
                if (GUILayout.Button(BookmarkConstants.LabelYes, EditorStyles.miniButton, GUILayout.Width(44)) || submit)
                {
                    if (renameInput.Trim() == name || BookmarkService.RenameFolder(name, renameInput))
                    {
                        renamingFolder = null;
                        GUI.FocusControl(null);
                    }
                    else ShowInvalidFolder(BookmarkConstants.LabelRenameFolderTitle);
                    EndChangedGUI(repaint);
                }
                if (GUILayout.Button(BookmarkConstants.LabelNo, EditorStyles.miniButton, GUILayout.Width(44)) || cancel)
                {
                    renamingFolder = null;
                    GUI.FocusControl(null);
                    EndChangedGUI(repaint);
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            int rowId = rowCounter++;
            if (e.type == EventType.MouseMove && rect.Contains(e.mousePosition)) hoverCandidate = rowId;
            bool hover = rect.Contains(e.mousePosition);
            bool expanded = !folder.folded || Searching;

            // 動作按鈕：只在 hover 時顯示；沒顯示時仍保留 control id，避免後續列的 id 位移導致點擊失效。
            Rect deleteRect = new(rect.xMax - ActionWidth - 2, rect.y, ActionWidth, RowHeight);
            Rect renameRect = new(deleteRect.x - ActionWidth, rect.y, ActionWidth, RowHeight);
            bool showActions = hover && !uncategorized;
            if (showActions)
            {
                if (GUI.Button(renameRect, new GUIContent("✎", BookmarkConstants.LabelRenameFolder), iconButtonStyle))
                {
                    StartRename(name);
                    EndChangedGUI(repaint);
                }
                if (GUI.Button(deleteRect, BookmarkIcons.Content(BookmarkIcons.Trash, "✕", BookmarkConstants.LabelDeleteFolder), iconButtonStyle))
                {
                    BookmarkService.DeleteFolder(name);
                    EndChangedGUI(repaint);
                }
            }
            else
            {
                GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.GetControlID(FocusType.Passive);
            }

            if (e.type == EventType.Repaint)
            {
                if (hover) EditorGUI.DrawRect(rect, HoverColor);
                if (dropRect == rect && !dropAsLine) EditorGUI.DrawRect(rect, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f));
                if (dropRect == rect && dropAsLine) EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), AccentColor);
                EditorStyles.foldout.Draw(new Rect(rect.x + 4, rect.y + 2, 14, 16), false, false, expanded, false);
                var icon = expanded ? BookmarkIcons.FolderOpened : BookmarkIcons.Folder;
                if (icon != null) GUI.DrawTexture(new Rect(rect.x + 18, rect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);
                string display = uncategorized ? BookmarkConstants.LabelUncategorized : name;
                var labelRect = new Rect(rect.x + 38, rect.y + 2, rect.width - 38 - (showActions ? ActionWidth * 2 + 4 : 4), 16);
                headerStyle.Draw(labelRect, display, false, false, false, false);
                float width = headerStyle.CalcSize(new GUIContent(display)).x;
                detailStyle.Draw(new Rect(labelRect.x + width + 6, rect.y + 3, 60, 14),
                    CountLabel(folder.rows.Count, folder.total, Searching), false, false, false, false);
            }

            if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
            {
                ShowFolderMenu(name, repaint);
                e.Use();
            }

            Rect clickRect = showActions ? new Rect(rect.x, rect.y, renameRect.x - rect.x, rect.height) : rect;
            string identity = "folder:" + name;
            var click = drag.HandleItem(clickRect, identity, uncategorized ? null : () => new DragPayload
            {
                key = DragSortHandler.FolderKey,
                data = name,
                title = name,
            });
            if (click == RowClick.DoubleClick && !uncategorized)
            {
                StartRename(name);
                EndChangedGUI(repaint);
            }
            else if (click != RowClick.None)
            {
                BookmarkService.ToggleFolderFold(name);
                EndChangedGUI(repaint);
            }
            HandleDrop(rect, name, null, repaint);
        }

        private void ShowFolderMenu(string name, Action repaint)
        {
            var menu = new GenericMenu();
            if (name.Length > 0)
            {
                menu.AddItem(new GUIContent(BookmarkConstants.LabelRenameFolder), false, () =>
                {
                    StartRename(name);
                    repaint?.Invoke();
                });
                menu.AddItem(new GUIContent(BookmarkConstants.LabelDeleteFolder), false, () =>
                {
                    BookmarkService.DeleteFolder(name);
                    repaint?.Invoke();
                });
                menu.AddSeparator(string.Empty);
            }
            menu.AddItem(new GUIContent(BookmarkConstants.LabelNewFolder), false, () =>
            {
                creatingFolder = true;
                focusNewFolder = true;
                newFolderInput = string.Empty;
                repaint?.Invoke();
            });
            menu.ShowAsContext();
        }

        // ====================== Item rows ======================
        private void DrawRow(RowData data, float indent, Action onItemPicked, Action repaint)
        {
            Rect row = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            int rowId = rowCounter++;
            if (e.type == EventType.MouseMove && row.Contains(e.mousePosition)) hoverCandidate = rowId;
            bool alive = data.obj != null;
            bool hover = row.Contains(e.mousePosition);
            bool isPicked = picked.Contains(data.item);
            bool isCursor = data.item == cursor;
            if (isCursor && e.type == EventType.Repaint) cursorRect = row;
            bool bookmarked = data.bookmarkRow || BookmarkService.IsBookmarked(data.item);

            // ---- 右側動作（hover 或游標所在時顯示）----
            bool showActions = hover || isCursor;
            bool canOpen = data.canOpen || (data.sceneUnloaded && !EditorApplication.isPlaying);
            Rect starRect = new(row.xMax - ActionWidth - 2, row.y, ActionWidth, RowHeight);
            Rect openRect = new(starRect.x - ActionWidth, row.y, ActionWidth, RowHeight);
            // 失效的紀錄列不能加入書籤；失效的書籤列可以移除。
            bool starEnabled = alive || data.bookmarkRow;
            if (showActions && starEnabled)
            {
                var starTip = bookmarked ? BookmarkConstants.LabelStarRemoveTooltip : BookmarkConstants.LabelStarAddTooltip;
                if (GUI.Button(starRect, new GUIContent(string.Empty, starTip), iconButtonStyle))
                {
                    if (!alive) BookmarkService.RemoveBookmark(data.item);
                    else if (isPicked && picked.Count > 1 && data.bookmarkRow) RemovePickedBookmarks();
                    else BookmarkService.ToggleBookmark(data.obj);
                    EndChangedGUI(repaint);
                }
            }
            else GUIUtility.GetControlID(FocusType.Passive);
            if (showActions && canOpen)
            {
                var tip = alive ? BookmarkConstants.LabelOpen : BookmarkConstants.LabelOpenSceneTooltip;
                if (GUI.Button(openRect, new GUIContent(string.Empty, tip), iconButtonStyle))
                {
                    if (alive) AssetDatabase.OpenAsset(data.obj);
                    else BookmarkService.OpenSceneOf(data.item);
                    EndChangedGUI(repaint);
                }
            }
            else GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Repaint)
            {
                Color background = isPicked ? SelectionColor
                    : alive && Selection.activeObject == data.obj ? SoftSelectionColor
                    : hover ? HoverColor : Color.clear;
                if (background.a > 0) EditorGUI.DrawRect(row, background);
                if (isCursor && !isPicked) DrawOutline(row, AccentColor);
                if (data.historyIndex >= 0 && data.historyIndex == BookmarkStorage.Data.index)
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3, 2, row.height - 6), AccentColor); // 目前在紀錄中的位置
                if (dropRect == row) EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, 2), AccentColor);

                float x = row.x + 6 + indent;
                if (data.icon != null) GUI.DrawTexture(new Rect(x, row.y + 2, 16, 16), data.icon, ScaleMode.ScaleToFit);
                x += 20;

                float right = row.xMax - 4;
                if (showActions)
                {
                    if (starEnabled) BookmarkIcons.DrawStar(new Rect(starRect.x + 3, starRect.y + 3, 14, 14), bookmarked);
                    if (canOpen) iconButtonStyle.Draw(openRect, new GUIContent("↗"), false, false, false, false);
                    right = (canOpen ? openRect.x : starRect.x) - 2;
                }
                else if (data.historyIndex >= 0)
                {
                    string time = BookmarkService.FormatRelativeTime(data.item.time, BookmarkService.NowMs());
                    if (bookmarked)
                    {
                        BookmarkIcons.DrawStar(new Rect(row.xMax - 18, row.y + 4, 12, 12), true);
                        right = row.xMax - 20;
                    }
                    if (time.Length > 0)
                    {
                        float w = timeStyle.CalcSize(new GUIContent(time)).x;
                        timeStyle.Draw(new Rect(right - w, row.y + 3, w, 14), time, false, false, false, false);
                        right -= w + 6;
                    }
                }

                var style = alive ? nameStyle : missingStyle;
                var nameRect = new Rect(x, row.y + 2, Mathf.Max(0, right - x), 16);
                style.Draw(nameRect, data.name, false, false, false, false);
                if (alive && !string.IsNullOrEmpty(data.detail))
                {
                    float nameWidth = style.CalcSize(data.name).x;
                    float detailX = x + nameWidth + 6;
                    if (detailX < right - 20)
                        detailStyle.Draw(new Rect(detailX, row.y + 3, right - detailX, 14), data.detail, false, false, false, false);
                }
            }
            // tooltip（路徑）：透明 label 疊在名稱上。
            if (data.name.tooltip != null && hover)
                GUI.Label(new Rect(row.x + 6 + indent, row.y, Mathf.Max(0, (showActions ? openRect.x : row.xMax) - row.x - 6 - indent), row.height),
                    new GUIContent(string.Empty, data.name.tooltip));

            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 2 && alive)
                {
                    EditorGUIUtility.PingObject(data.obj);
                    e.Use();
                }
                else if (e.button == 1)
                {
                    if (!isPicked) SelectSingle(data, ping: false);
                    ShowRowMenu(data, repaint);
                    e.Use();
                }
            }

            Rect clickRect = showActions ? new Rect(row.x, row.y, openRect.x - row.x, row.height) : row;
            var modifiers = e.modifiers;
            var click = drag.HandleItem(clickRect, data.item, () => MakeRowPayload(data));
            if (click != RowClick.None) OnRowClicked(data, click, modifiers, onItemPicked, repaint);
            if (data.bookmarkRow) HandleDrop(row, data.item.folder ?? string.Empty, data.item, repaint);
        }

        private static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        // 拖曳選取中的列時一起拖；書籤列帶上引用（移動／排序），紀錄列只帶物件（拖進書籤＝加入）。
        private DragPayload MakeRowPayload(RowData data)
        {
            var objects = new List<Object>();
            var items = new List<ObjectRef>();
            bool multi = picked.Contains(data.item) && picked.Count > 1;
            foreach (var row in navRows)
            {
                if (row.bookmarkRow != data.bookmarkRow) continue;
                if (multi ? !picked.Contains(row.item) : row.item != data.item) continue;
                if (row.obj != null && !objects.Contains(row.obj)) objects.Add(row.obj);
                if (row.bookmarkRow) items.Add(row.item);
            }
            if (!multi && items.Count == 0 && data.bookmarkRow) items.Add(data.item);
            return new DragPayload
            {
                objects = objects.ToArray(),
                key = data.bookmarkRow ? DragSortHandler.BookmarksKey : null,
                data = data.bookmarkRow ? items : null,
                title = multi ? string.Format(BookmarkConstants.LabelDragMany, picked.Count) : data.name.text,
            };
        }

        private void ShowRowMenu(RowData data, Action repaint)
        {
            var menu = new GenericMenu();
            var pickedBookmarks = PickedBookmarks();
            bool multi = data.bookmarkRow && pickedBookmarks.Count > 1 && picked.Contains(data.item);

            if (data.obj != null)
            {
                if (data.canOpen)
                    menu.AddItem(new GUIContent(BookmarkConstants.LabelOpen), false, () => AssetDatabase.OpenAsset(data.obj));
                menu.AddItem(new GUIContent(BookmarkConstants.LabelShowInProject), false, () => EditorGUIUtility.PingObject(data.obj));
                menu.AddSeparator(string.Empty);
            }
            else if (data.sceneUnloaded && !EditorApplication.isPlaying)
            {
                menu.AddItem(new GUIContent(BookmarkConstants.LabelOpenSceneTooltip), false, () => BookmarkService.OpenSceneOf(data.item));
                menu.AddSeparator(string.Empty);
            }

            if (multi)
            {
                menu.AddItem(new GUIContent(string.Format(BookmarkConstants.LabelRemoveBookmarks, pickedBookmarks.Count)), false, () =>
                {
                    BookmarkService.RemoveBookmarks(pickedBookmarks);
                    picked.Clear();
                    repaint?.Invoke();
                });
                AddFolderItems(menu, string.Format(BookmarkConstants.LabelMoveManyToFolder, pickedBookmarks.Count) + "/", null,
                    folder => BookmarkService.MoveBookmarks(pickedBookmarks, folder, null), repaint);
                menu.ShowAsContext();
                return;
            }

            var existing = data.bookmarkRow ? data.item : (data.obj != null ? BookmarkService.FindBookmark(data.obj) : null);
            if (existing != null)
            {
                menu.AddItem(new GUIContent(BookmarkConstants.LabelRemoveBookmark), false, () =>
                {
                    BookmarkService.RemoveBookmark(existing);
                    repaint?.Invoke();
                });
                AddFolderItems(menu, BookmarkConstants.LabelMoveToFolder + "/", existing.folder ?? string.Empty,
                    folder => BookmarkService.MoveBookmarkToFolder(existing, folder), repaint);
            }
            else if (data.obj != null)
            {
                var obj = data.obj;
                menu.AddItem(new GUIContent(BookmarkConstants.LabelAddBookmark), false, () =>
                {
                    BookmarkService.ToggleBookmark(obj);
                    repaint?.Invoke();
                });
                AddFolderItems(menu, BookmarkConstants.LabelAddBookmarkTo + "/", null,
                    folder => BookmarkService.AddBookmarkToFolder(obj, folder), repaint);
            }
            menu.ShowAsContext();
        }

        private static void AddFolderItems(GenericMenu menu, string prefix, string current, Action<string> onPick, Action repaint)
        {
            void Add(string label, string folder) => menu.AddItem(new GUIContent(prefix + label), current == folder, () =>
            {
                onPick(folder);
                repaint?.Invoke();
            });
            Add(BookmarkConstants.LabelUncategorized, string.Empty);
            foreach (var folder in BookmarkStorage.Data.folders)
                if (folder != null && !string.IsNullOrEmpty(folder.name)) Add(folder.name, folder.name);
        }

        // ====================== Drop ======================
        // target 為 null：放在資料夾標題（或空狀態區）上；否則放在某筆書籤上（排到它前面）。
        private void HandleDrop(Rect rect, string folder, ObjectRef target, Action repaint)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
            if (target != null && !BookmarkStorage.Data.bookmarks.Contains(target))
            {
                Reject(e);
                return;
            }

            // 拖資料夾：只能放在資料夾標題上，排到該資料夾前面（放在「未分類」上＝移到最後）。
            string draggedFolder = DragSortHandler.GetDraggedFolder();
            if (draggedFolder != null)
            {
                if (target != null || draggedFolder == folder)
                {
                    Reject(e);
                    return;
                }
                Accept(e, rect, asLine: true, DragAndDropVisualMode.Move);
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    e.Use();
                    BookmarkService.ReorderFolder(draggedFolder, folder);
                    EndChangedGUI(repaint);
                }
                return;
            }

            var items = DragSortHandler.GetDraggedBookmarks();
            if (items != null)
            {
                bool sameFolderOnly = target == null && items.TrueForAll(i => (i.folder ?? string.Empty) == folder);
                if (sameFolderOnly || (items.Count == 1 && items[0] == target))
                {
                    Reject(e);
                    return;
                }
                Accept(e, rect, asLine: target != null, DragAndDropVisualMode.Move);
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    e.Use();
                    if (target == null) BookmarkService.MoveBookmarks(items, folder, null);
                    else if (items.Count == 1) BookmarkService.ReorderBookmark(items[0], target);
                    else BookmarkService.MoveBookmarks(items, target.folder, target);
                    EndChangedGUI(repaint);
                }
                return;
            }

            // 從 Project / Hierarchy / 紀錄列拖進來的物件：加入書籤（已是書籤的移到這裡）。
            var objects = DragAndDrop.objectReferences;
            if (objects == null || objects.Length == 0)
            {
                Reject(e);
                return;
            }
            Accept(e, rect, asLine: target != null, DragAndDropVisualMode.Copy);
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                e.Use();
                BookmarkService.AddBookmarks(objects, folder, target);
                EndChangedGUI(repaint);
            }
        }

        private static void Reject(Event e)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            e.Use();
        }

        private void Accept(Event e, Rect rect, bool asLine, DragAndDropVisualMode mode)
        {
            DragAndDrop.visualMode = mode;
            if (e.type == EventType.DragUpdated)
            {
                pendingDropRect = rect;
                pendingDropAsLine = asLine;
                e.Use();
            }
        }
    }
}
#endif
