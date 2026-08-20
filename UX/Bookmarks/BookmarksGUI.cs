#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // 純繪製狀態 + GUI 邏輯，由 InspectorPopup（PopupWindowContent）與 BookmarksWindow（EditorWindow）共用。
    // 兩 host 唯一行為分歧透過 DrawBody(onItemPicked) callback 注入：
    //   - Popup host 傳 () => editorWindow.Close()，點 row 後關閉彈窗
    //   - Window host 傳 null，點 row 後保持視窗開啟
    internal class BookmarksGUI
    {
        private bool isInitReady;
        private GUIStyle rowStyle, headerStyle, favoriteStyle, folderHeaderStyle;

        float rowHeight = 28f;

        private SearchBar searchBookmarkBar = new SearchBar();
        private SearchBar searchHistoryBar = new SearchBar();

        // DragSort 每個 folder 一個 handler — key 須全域唯一（陷阱 §5）
        private readonly Dictionary<string, DragSortHandler> _folderDrag = new();

        // RefToObject 結果 row-level cache：以 ObjectRef 引用為 key（同 list 元素穩定）。
        // JSONStorage.Version 改變即整批 invalidate；同一 frame N rows 走 OnGUI 各自查到的同一筆只 LoadAssetAtPath 一次。
        // Unity Object overload `==` null：cache hit 後仍需檢查物件是否仍存活，destroy 後 re-resolve（多半再回 null）。
        private int _resolveVersion = -1;
        private readonly Dictionary<ObjectRef, UnityEngine.Object> _resolveCache = new();

        private UnityEngine.Object Resolve(ObjectRef item)
        {
            if (item == null) return null;
            int v = JSONStorage.Version;
            if (v != _resolveVersion)
            {
                _resolveCache.Clear();
                _resolveVersion = v;
            }
            if (_resolveCache.TryGetValue(item, out var cached) && cached != null)
                return cached;

            var obj = Inspector.RefToObject(item);
            _resolveCache[item] = obj;
            return obj;
        }

        private string searchBookmarkKeyword = string.Empty;
        private string searchHistoryKeyword = string.Empty;

        // 整體 scrollview：書籤＋歷史共用一個外層 scroll，內部不再分小 scroll（避免巢狀滾輪）
        private Vector2 scrollAll;

        // 新增資料夾的輸入緩衝
        private string _newFolderInput = string.Empty;
        // inline rename 狀態：正在改名的 folder name，搭配輸入緩衝
        private string _renamingFolder;
        private string _renameInput = string.Empty;

        private void Init()
        {
            if (isInitReady) return;
            isInitReady = true;
            rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            favoriteStyle = new GUIStyle(EditorStyles.miniButtonRight) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            folderHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        }

        public string GetLabel(UnityEngine.Object obj)
        {
            if (obj == null) return InspectorConstants.DefaultMissing;
            bool isAsset = EditorUtility.IsPersistent(obj);

            return $"{(isAsset ? InspectorConstants.PrefixAsset : InspectorConstants.PrefixScene)} {obj.name} ({obj.GetType().Name})";
        }

        public void DrawBody(Rect rect, Action onItemPicked)
        {
            Init();
            if (!Inspector.IsEnabled)
            {
                EditorGUILayout.HelpBox("Pin Inspector 目前已停用。", MessageType.Info);
                return;
            }

            // 外層整體 scroll：host（Popup 固定尺寸 / Window 可 resize）統一處理超出部分
            scrollAll = EditorGUILayout.BeginScrollView(scrollAll);

            DrawBookmarksSection(onItemPicked);
            GUILayout.Space(6);
            DrawHistorySection(onItemPicked);

            EditorGUILayout.EndScrollView();
        }

        // ====================== Bookmarks Section ======================
        private void DrawBookmarksSection(Action onItemPicked)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // 頂層 header：折疊／清除全部／新增資料夾輸入
            EditorGUILayout.BeginHorizontal();
            string topArrow = JSONStorage.Data.foldBookmarks ? "▼" : "▶";
            // 搜尋有 keyword 時顯示 Y/X（Y=符合、X=總數），無 keyword 顯示 X
            int bookmarkTotal = JSONStorage.Data.bookmarks.Count;
            string bookmarkCountLabel = string.IsNullOrEmpty(searchBookmarkKeyword)
                ? bookmarkTotal.ToString()
                : $"{CountBookmarkMatched(searchBookmarkKeyword)}/{bookmarkTotal}";
            if (GUILayout.Button($"{topArrow} {InspectorConstants.LabelBookmarks} ({bookmarkCountLabel})",
                    EditorStyles.boldLabel, GUILayout.ExpandWidth(true)))
            {
                JSONStorage.Data.foldBookmarks = !JSONStorage.Data.foldBookmarks;
                Inspector.Save();
            }
            if (GUILayout.Button(InspectorConstants.LabelClear, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                int total = JSONStorage.Data.bookmarks.Count;
                if (total == 0 || EditorUtility.DisplayDialog(
                        InspectorConstants.LabelClearConfirmTitle,
                        string.Format(InspectorConstants.LabelClearConfirmMsg, InspectorConstants.LabelBookmarks),
                        InspectorConstants.LabelYes, InspectorConstants.LabelNo))
                {
                    Inspector.ClearAllBookmarks();
                }
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (!JSONStorage.Data.foldBookmarks)
            {
                GUILayout.EndVertical();
                return;
            }

            // 搜尋（fold 起時連同隱藏）
            searchBookmarkBar.Draw();
            searchBookmarkKeyword = searchBookmarkBar.Keyword;

            // 新增資料夾 row
            EditorGUILayout.BeginHorizontal();
            _newFolderInput = EditorGUILayout.TextField(_newFolderInput, GUILayout.ExpandWidth(true));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newFolderInput);
            if (GUILayout.Button(InspectorConstants.LabelAddFolder, EditorStyles.miniButton, GUILayout.Width(110)))
            {
                if (Inspector.AddFolder(_newFolderInput))
                {
                    _newFolderInput = string.Empty;
                    GUI.FocusControl(null);
                }
                else
                {
                    EditorUtility.DisplayDialog(InspectorConstants.LabelAddFolder,
                        InspectorConstants.LabelInvalidFolderName,
                        InspectorConstants.LabelYes);
                }
                GUIUtility.ExitGUI();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 內部不再自帶 scroll：所有 folder 自然展開，由 DrawBody 的外層 scrollAll 統一處理
            foreach (var folderName in Inspector.GetFolderDisplayOrder())
                DrawFolder(folderName, onItemPicked);

            GUILayout.EndVertical();
        }

        // ====================== History Section ======================
        // 與書籤區結構對稱：helpBox + header（折疊/清除）+ fold 展開內含搜尋與紀錄 rows
        private void DrawHistorySection(Action onItemPicked)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            string arrow = JSONStorage.Data.foldHistory ? "▼" : "▶";
            int historyTotal = JSONStorage.Data.history.Count;
            string historyCountLabel = string.IsNullOrEmpty(searchHistoryKeyword)
                ? historyTotal.ToString()
                : $"{CountHistoryMatched(searchHistoryKeyword)}/{historyTotal}";
            if (GUILayout.Button($"{arrow} {InspectorConstants.LabelHistory} ({historyCountLabel})",
                    EditorStyles.boldLabel, GUILayout.ExpandWidth(true)))
            {
                JSONStorage.Data.foldHistory = !JSONStorage.Data.foldHistory;
                Inspector.Save();
            }
            if (GUILayout.Button(InspectorConstants.LabelClear, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                int total = JSONStorage.Data.history.Count;
                if (total == 0 || EditorUtility.DisplayDialog(
                        InspectorConstants.LabelClearConfirmTitle,
                        string.Format(InspectorConstants.LabelClearConfirmMsg, InspectorConstants.LabelHistory),
                        InspectorConstants.LabelYes, InspectorConstants.LabelNo))
                {
                    Inspector.ClearAllHistory();
                }
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (JSONStorage.Data.foldHistory)
            {
                searchHistoryBar.Draw();
                searchHistoryKeyword = searchHistoryBar.Keyword;

                var list = JSONStorage.Data.history;
                for (int i = 0; i < list.Count; i++)
                    DrawRow(list[i], i, false, searchHistoryKeyword, onItemPicked);
            }

            GUILayout.EndVertical();
        }

        private void DrawFolder(string folderName, Action onItemPicked)
        {
            bool isUncategorized = string.IsNullOrEmpty(folderName);
            string display = isUncategorized ? InspectorConstants.LabelUncategorized : folderName;
            bool isRenaming = !isUncategorized && _renamingFolder == folderName;

            int totalCount = 0;
            var list = JSONStorage.Data.bookmarks;
            for (int i = 0; i < list.Count; i++)
                if (list[i].folder == folderName) totalCount++;

            // Y/X 顯示：有 keyword 時 Y=符合該 folder 過濾結果，無 keyword 時與 X 同
            string folderCountLabel = string.IsNullOrEmpty(searchBookmarkKeyword)
                ? totalCount.ToString()
                : $"{CountBookmarkMatchedInFolder(folderName, searchBookmarkKeyword)}/{totalCount}";

            EditorGUILayout.BeginHorizontal();
            bool folded = Inspector.IsFolderFolded(folderName);
            string arrow = folded ? "▶" : "▼";

            if (isRenaming)
            {
                // inline rename：TextField + 確認 / 取消
                _renameInput = EditorGUILayout.TextField(_renameInput, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(InspectorConstants.LabelYes, EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    if (Inspector.RenameFolder(folderName, _renameInput))
                    {
                        _renamingFolder = null;
                        _renameInput = string.Empty;
                        GUI.FocusControl(null);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(InspectorConstants.LabelRenameFolderTitle,
                            InspectorConstants.LabelInvalidFolderName,
                            InspectorConstants.LabelYes);
                    }
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(InspectorConstants.LabelNo, EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    _renamingFolder = null;
                    _renameInput = string.Empty;
                    GUI.FocusControl(null);
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                if (GUILayout.Button($"{arrow} {display} ({folderCountLabel})",
                        folderHeaderStyle, GUILayout.ExpandWidth(true)))
                {
                    Inspector.ToggleFolderFold(folderName);
                }

                if (!isUncategorized)
                {
                    if (GUILayout.Button("✎", EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    {
                        _renamingFolder = folderName;
                        _renameInput = folderName;
                        GUI.FocusControl(null);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    {
                        if (EditorUtility.DisplayDialog(
                                InspectorConstants.LabelDeleteFolderConfirmTitle,
                                string.Format(InspectorConstants.LabelDeleteFolderConfirmMsg, folderName, totalCount),
                                InspectorConstants.LabelYes, InspectorConstants.LabelNo))
                        {
                            Inspector.DeleteFolder(folderName);
                        }
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 跨 folder drop：拖既有書籤到此 folder header 即變更歸屬（同 folder no-op，避免空操作）
            HandleFolderDrop(GUILayoutUtility.GetLastRect(), folderName);

            if (folded) return;

            // 該 folder 的 bookmarks 子集合，記錄全 list index 以便寫回排序
            var subIndices = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].folder == folderName) subIndices.Add(i);

            string dragKey = $"BookmarkFolder:{folderName}";
            if (!_folderDrag.TryGetValue(dragKey, out var drag))
            {
                drag = new DragSortHandler(dragKey);
                _folderDrag[dragKey] = drag;
            }

            for (int sub = 0; sub < subIndices.Count; sub++)
            {
                int globalIndex = subIndices[sub];
                DrawBookmarkRow(list[globalIndex], globalIndex, sub, subIndices, drag, searchBookmarkKeyword, onItemPicked);
            }
        }

        // ====================== Bookmark Row（含資料夾選單） ======================
        private void DrawBookmarkRow(ObjectRef item, int globalIndex, int subIndex,
            List<int> subIndices, DragSortHandler drag, string keyword, Action onItemPicked)
        {
            UnityEngine.Object obj = Resolve(item);
            if (obj == null) return;

            string label = GetLabel(obj);
            if (!string.IsNullOrEmpty(keyword))
            {
                // 命中條件：label 子字串 match，或 keyword 完全等於該書籤所屬 folder 名（讓 user 用 folder 名快速列出整夾）
                bool labelHit = label.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool folderHit = (item.folder ?? string.Empty) == keyword;
                if (!labelHit && !folderHit) return;
            }

            bool isBookmarked = true; // 在書籤區，恆 true
            bool isCurrent = Selection.activeObject == obj;

            Rect row = GUILayoutUtility.GetRect(0, rowHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                bool hover = row.Contains(Event.current.mousePosition);
                Color bg =
                    isCurrent ? new Color(0.24f, 0.45f, 0.85f, 0.35f) :
                    hover ? new Color(0.4f, 0.4f, 0.4f, 0.25f) :
                    (subIndex % 2 == 0
                        ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.93f, 0.93f, 0.93f))
                        : Color.clear);

                EditorGUI.DrawRect(row, bg);
            }

            // Open
            bool canOpen = Inspector.CanOpen(obj);
            Rect openRect = new Rect(row.x + 6, row.y + 5, 36, 18);
            GUI.enabled = canOpen;
            if (GUI.Button(openRect, InspectorConstants.LabelOpen, EditorStyles.miniButtonLeft))
            {
                if (canOpen) AssetDatabase.OpenAsset(obj);
                GUIUtility.ExitGUI();
            }
            GUI.enabled = true;

            // Bookmark toggle
            Rect favRect = new Rect(openRect.xMax, row.y + 5, 21, 18);
            if (GUI.Button(favRect, isBookmarked ? InspectorConstants.PrefixOnBookMarks : InspectorConstants.PrefixOffBookMarks,
                favoriteStyle))
            {
                Inspector.ToggleBookmark(obj);
                GUIUtility.ExitGUI();
            }

            // 資料夾選單走右鍵 → 用 MouseDown + button=1（ContextClick 在 IMGUI 易被內部 button 區與 drag state 吞掉，不穩）
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 1
                && row.Contains(Event.current.mousePosition))
            {
                ShowMoveToFolderMenu(item);
                Event.current.Use();
            }

            // Icon
            GUIContent content = EditorGUIUtility.ObjectContent(obj, obj.GetType());
            Texture icon = content.image;
            float iconSize = 22f;
            Rect iconRect = new Rect(favRect.xMax + 6, row.y + (rowHeight - iconSize) * 0.5f, iconSize, iconSize);
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            // Label button
            Rect labelRect = new Rect(iconRect.xMax + 8, row.y + 5, row.width - iconRect.xMax - 14, 18);
            if (GUI.Button(labelRect, label, rowStyle))
            {
                Selection.activeObject = obj;
                onItemPicked?.Invoke();
            }

            // DragSort：先在 sub-list 內 pop&insert，再寫回該 folder 在全 list 佔用的 slot（其他 folder 順序不動）
            // payload = obj 讓 Inspector ObjectField 也能接收為賦值來源（drop 端決定接收 sort 還是 reference）
            drag.HandleDrag(row, subIndex, (from, to) =>
            {
                var bookmarks = JSONStorage.Data.bookmarks;
                var folderItems = new List<ObjectRef>(subIndices.Count);
                foreach (int gi in subIndices) folderItems.Add(bookmarks[gi]);

                if (from < 0 || from >= folderItems.Count) return;
                var moved = folderItems[from];
                folderItems.RemoveAt(from);
                int insertAt = Mathf.Clamp(to, 0, folderItems.Count);
                folderItems.Insert(insertAt, moved);

                // 寫回：保留其他 folder 的全 list 順序，只重排該 folder 的 slot 內容
                int sub = 0;
                string folderKey = moved.folder ?? string.Empty;
                for (int i = 0; i < bookmarks.Count && sub < folderItems.Count; i++)
                {
                    string bf = bookmarks[i].folder ?? string.Empty;
                    if (bf == folderKey)
                        bookmarks[i] = folderItems[sub++];
                }
                Inspector.Save();
            }, obj, label);

            // 跨 folder drag-to-row：source 屬於別 folder 時，drop 到此 row 同時變更歸屬 + 插入到此 sub-index 位置。
            HandleCrossFolderRowDrop(row, item.folder ?? string.Empty, subIndex);
        }

        private void HandleCrossFolderRowDrop(Rect row, string targetFolder, int targetSubIndex)
        {
            var e = Event.current;
            if (e.type != EventType.DragPerform) return;
            if (!row.Contains(e.mousePosition)) return;

            var refs = DragAndDrop.objectReferences;
            if (refs == null || refs.Length == 0) return;

            var bm = FindBookmarkOf(refs[0]);
            if (bm == null) return;

            string current = bm.folder ?? string.Empty;
            string target = targetFolder ?? string.Empty;
            if (current == target) return;  // 同 folder 由 DragSortHandler.HandleDrag 處理 sort

            var bookmarks = JSONStorage.Data.bookmarks;
            int sourceIdx = bookmarks.IndexOf(bm);
            if (sourceIdx < 0) return;

            // 1) 變更 folder（不呼叫 MoveBookmarkToFolder 避免重複 Save / 它會把書籤留在原全域位置）
            bm.folder = target;

            // 2) 從 list 抽出，找 target folder 內第 targetSubIndex 個書籤的全域 index，插在它之前
            bookmarks.RemoveAt(sourceIdx);
            int subCount = 0;
            int insertGlobal = bookmarks.Count; // fallback：target 內已無書籤 → append 末尾
            for (int i = 0; i < bookmarks.Count; i++)
            {
                if ((bookmarks[i].folder ?? string.Empty) != target) continue;
                if (subCount == targetSubIndex)
                {
                    insertGlobal = i;
                    break;
                }
                subCount++;
            }
            bookmarks.Insert(insertGlobal, bm);
            Inspector.Save();

            DragAndDrop.AcceptDrag();
            e.Use();
            // 變動 list 後當幀 layout / sub-list 已失效，中斷避免 IMGUI Layout vs Repaint pass 不一致
            GUIUtility.ExitGUI();
        }

        // ====================== Search Count Helpers ======================
        // 命中規則與 DrawBookmarkRow 一致：label 子字串 OR folder 名完全相同。
        // 三 helper 在 OnGUI 每幀重算，list 小可接受；如未來變大可加 frame cache。

        private int CountBookmarkMatched(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return JSONStorage.Data.bookmarks.Count;
            int n = 0;
            var list = JSONStorage.Data.bookmarks;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].folder ?? string.Empty) == keyword) { n++; continue; }
                var o = Resolve(list[i]);
                if (o == null) continue;
                if (GetLabel(o).IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0) n++;
            }
            return n;
        }

        private int CountBookmarkMatchedInFolder(string folderName, string keyword)
        {
            int n = 0;
            var list = JSONStorage.Data.bookmarks;
            bool hasKw = !string.IsNullOrEmpty(keyword);
            bool folderNameMatch = hasKw && folderName == keyword;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].folder != folderName) continue;
                if (!hasKw || folderNameMatch) { n++; continue; }
                var o = Resolve(list[i]);
                if (o == null) continue;
                if (GetLabel(o).IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0) n++;
            }
            return n;
        }

        private int CountHistoryMatched(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return JSONStorage.Data.history.Count;
            int n = 0;
            var list = JSONStorage.Data.history;
            for (int i = 0; i < list.Count; i++)
            {
                var o = Resolve(list[i]);
                if (o == null) continue;
                if (GetLabel(o).IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0) n++;
            }
            return n;
        }

        // 反查 obj 是否為已知書籤（依 guid 或 instanceId），用於跨 folder drop 識別 internal bookmark drag。
        // external asset（從 Project view 拖）若未被 bookmark 過 → 回傳 null → drop 不接收。
        private static ObjectRef FindBookmarkOf(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            string path = AssetDatabase.GetAssetPath(obj);
            string guid = AssetDatabase.AssetPathToGUID(path);
            int iid = obj.GetInstanceID();
            return JSONStorage.Data.bookmarks.Find(b =>
                (!string.IsNullOrEmpty(b.guid) && b.guid == guid)
                || (string.IsNullOrEmpty(b.guid) && b.instanceId == iid));
        }

        private void HandleFolderDrop(Rect headerRect, string targetFolder)
        {
            var e = Event.current;
            if (!headerRect.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            var refs = DragAndDrop.objectReferences;
            if (refs == null || refs.Length == 0) return;

            var bm = FindBookmarkOf(refs[0]);
            if (bm == null) return;

            string current = bm.folder ?? string.Empty;
            string target = targetFolder ?? string.Empty;
            if (current == target) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragPerform)
            {
                Inspector.MoveBookmarkToFolder(bm, target);
                DragAndDrop.AcceptDrag();
                e.Use();
                // 變更 folder 後當幀 layout 已失效，中斷避免 IMGUI assertion
                GUIUtility.ExitGUI();
            }
            e.Use();
        }

        // 歷史 row 右鍵 → 加入書籤並指定 folder（若已書籤則改 folder）。
        // checkmark 標示當前 folder（已書籤時），點同一項視為 no-op move（MoveBookmarkToFolder 內部會處理）。
        private void ShowAddBookmarkMenu(UnityEngine.Object obj)
        {
            if (obj == null) return;
            var existing = FindBookmarkOf(obj);
            string currentFolder = existing?.folder ?? string.Empty;
            bool alreadyBookmark = existing != null;
            string prefix = alreadyBookmark ? InspectorConstants.LabelMoveToFolder + "/" : InspectorConstants.LabelAddBookmark + "/";

            var menu = new GenericMenu();
            // 對應狀態的 toggle 項目（加入 / 移除）
            menu.AddItem(new GUIContent(alreadyBookmark ? InspectorConstants.LabelRemoveBookmark : InspectorConstants.LabelAddBookmark),
                false, () => Inspector.ToggleBookmark(obj));
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent(prefix + InspectorConstants.LabelUncategorized),
                alreadyBookmark && currentFolder == string.Empty,
                () => AddBookmarkToFolder(obj, string.Empty));

            if (JSONStorage.Data.folders.Count > 0) menu.AddSeparator(string.Empty);

            foreach (var f in JSONStorage.Data.folders)
            {
                if (f == null || string.IsNullOrEmpty(f.name)) continue;
                string captured = f.name;
                bool on = alreadyBookmark && currentFolder == captured;
                menu.AddItem(new GUIContent(prefix + captured), on,
                    () => AddBookmarkToFolder(obj, captured));
            }
            menu.ShowAsContext();
        }

        private void AddBookmarkToFolder(UnityEngine.Object obj, string folder)
        {
            var existing = FindBookmarkOf(obj);
            if (existing == null)
            {
                Inspector.ToggleBookmark(obj); // add (預設 folder = "")
                existing = FindBookmarkOf(obj);
                // ToggleBookmark 可能被上限 dialog 拒絕 → existing 仍為 null
                if (existing == null) return;
            }
            Inspector.MoveBookmarkToFolder(existing, folder ?? string.Empty);
        }

        private void ShowMoveToFolderMenu(ObjectRef item)
        {
            var menu = new GenericMenu();
            // 書籤 row 必定已書籤 → 提供「移除書籤」
            menu.AddItem(new GUIContent(InspectorConstants.LabelRemoveBookmark), false, () =>
            {
                var obj = Inspector.RefToObject(item);
                if (obj != null) Inspector.ToggleBookmark(obj);
            });
            menu.AddSeparator(string.Empty);

            bool isUncategorized = string.IsNullOrEmpty(item.folder);
            menu.AddItem(new GUIContent(InspectorConstants.LabelMoveToUncategorized), isUncategorized,
                () => { Inspector.MoveBookmarkToFolder(item, ""); });

            if (JSONStorage.Data.folders.Count > 0) menu.AddSeparator(string.Empty);

            foreach (var f in JSONStorage.Data.folders)
            {
                if (f == null || string.IsNullOrEmpty(f.name)) continue;
                string captured = f.name;
                bool on = item.folder == captured;
                menu.AddItem(new GUIContent($"{InspectorConstants.LabelMoveToFolder}/{captured}"), on,
                    () => { Inspector.MoveBookmarkToFolder(item, captured); });
            }
            menu.ShowAsContext();
        }

        // 歷史區仍走原本邏輯，整個 list 共用一個 row 畫法
        private void DrawRow(ObjectRef item, int index, bool CanDrag, string keyword, Action onItemPicked)
        {
            UnityEngine.Object obj = Resolve(item);
            if (obj == null) return;

            string label = GetLabel(obj);
            if (!string.IsNullOrEmpty(keyword)
                && label.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0) return;

            bool isBookmarked = Inspector.IsBookmarked(item);
            bool isCurrent = Selection.activeObject == obj;

            Rect row = GUILayoutUtility.GetRect(0, rowHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                bool hover = row.Contains(Event.current.mousePosition);
                Color bg =
                    isCurrent ? new Color(0.24f, 0.45f, 0.85f, 0.35f) :
                    hover ? new Color(0.4f, 0.4f, 0.4f, 0.25f) :
                    (index % 2 == 0
                        ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.93f, 0.93f, 0.93f))
                        : Color.clear);

                EditorGUI.DrawRect(row, bg);
            }

            bool canOpen = Inspector.CanOpen(obj);
            Rect openRect = new Rect(row.x + 6, row.y + 5, 36, 18);
            GUI.enabled = canOpen;
            if (GUI.Button(openRect, InspectorConstants.LabelOpen, EditorStyles.miniButtonLeft))
            {
                if (canOpen) AssetDatabase.OpenAsset(obj);
                GUIUtility.ExitGUI();
            }
            GUI.enabled = true;

            Rect favRect = new Rect(openRect.xMax, row.y + 5, 21, 18);
            if (GUI.Button(favRect, isBookmarked ? InspectorConstants.PrefixOnBookMarks : InspectorConstants.PrefixOffBookMarks,
                favoriteStyle))
            {
                Inspector.ToggleBookmark(obj);
                GUIUtility.ExitGUI();
            }

            GUIContent content = EditorGUIUtility.ObjectContent(obj, obj.GetType());
            Texture icon = content.image;
            float iconSize = 22f;
            Rect iconRect = new Rect(favRect.xMax + 6, row.y + (rowHeight - iconSize) * 0.5f, iconSize, iconSize);
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            Rect labelRect = new Rect(iconRect.xMax + 8, row.y + 5, row.width - iconRect.xMax - 14, 18);
            if (GUI.Button(labelRect, label, rowStyle))
            {
                Selection.activeObject = obj;
                onItemPicked?.Invoke();
            }

            // 右鍵 → 加入書籤並設 folder（已書籤則改 folder）。早於 BeginDragOut 確保 e.Use() 搶先
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 1
                && row.Contains(Event.current.mousePosition))
            {
                ShowAddBookmarkMenu(obj);
                Event.current.Use();
            }

            // drag-out only：歷史 row 可拖曳到 Inspector ObjectField 賦值，但禁止改變順序（不走 HandleDrag）
            DragSortHandler.BeginDragOut(row, obj, label);
        }
    }
}
#endif
