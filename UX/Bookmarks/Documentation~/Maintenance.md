# Bookmarks 維護手冊

給 AI Agent 與維護者。修改 Bookmarks 之前先讀完本檔，並遵守其中的規則。

## 0. 使用本手冊的規則

- 本檔只寫**現在成立**的事實與規則。程式碼是行為的證據；本檔與程式不一致時，先確認實際行為，再在同一個 commit 修正本檔或程式。
- 改到本檔描述的行為、契約、檔案位置或限制時，**同一個 commit 更新本檔**。
- 歷史與改動理由寫進 commit 訊息，不寫在這裡。

## 1. 定位與邊界

Editor-only 的書籤與選取紀錄工具，只有一個組件 `HaruFamily.UX.Bookmarks.Editor`（`includePlatforms: Editor`），沒有外部依賴。測試組件 `HaruFamily.UX.Bookmarks.Editor.Tests` 只引用本組件與 Test Runner。

所有型別都是 `internal`，沒有給使用端的公開 API；測試透過 `InternalsVisibleTo` 存取。

## 2. 目錄地圖

| 檔案 | 內容 |
|---|---|
| `BookmarkService.cs` | 核心邏輯：初始化、物件識別、選取紀錄、導覽、書籤與資料夾操作 |
| `BookmarkService.Operations.cs` | 多物件加入／移除、可復原的移除、多筆移動、資料夾排序、通知、Project／Hierarchy 標記查詢、相對時間 |
| `BookmarkService.Header.cs` | Inspector 標題下方的工具列（`Editor.finishedDefaultHeaderGUI`） |
| `BookmarksIntegration.cs` | Project／Hierarchy 的 ★ 標記與右鍵選單 |
| `BookmarkIcons.cs` | Unity 內建圖示（找不到時回傳 null，由呼叫端改用文字） |
| `BookmarkStorage.cs` | 資料型別 `ObjectRef`／`FolderInfo`／`BookmarksData`、`BookmarkStorage`（延遲寫檔）、`BookmarkFileStorage`（讀寫與舊資料搬移） |
| `BookmarksGUI.cs` | 彈出視窗與書籤視窗共用的列表繪製 |
| `BookmarksPopup.cs` | 工具列「最近使用 ▼」開啟的彈出視窗；點選後關閉 |
| `BookmarksWindow.cs` | 可停靠的書籤視窗；點選後不關閉 |
| `BookmarksSettingsProvider.cs` | `Preferences/HaruFamily/Bookmarks` 設定頁 |
| `BookmarksShortcuts.cs` | 快捷鍵（id 見 §7） |
| `BookmarkConstants.cs` | 介面文字 |
| `UI/DragSortHandler.cs` | 列的單擊／雙擊判斷與拖曳起點；拖曳資料的 key |
| `UI/SearchBar.cs` | 包裝 Unity `SearchField` 的共用搜尋框 |
| `Tests/Editor/` | 見 §8 |

## 3. 物件識別（`ObjectRef`）

| 欄位 | 意義 |
|---|---|
| `globalId` | `GlobalObjectId` 字串，主要識別。只存重開 Editor 後仍可解析的 id（`identifierType != 0`、`assetGUID` 非空、`targetObjectId != 0`）。 |
| `instanceId` | 本次 Editor 執行期間的快速路徑。 |
| `guid` | 所屬資產檔的 guid（場景物件為 null），用來偵測資產被刪，以及解析沒有 `globalId` 的舊資料。 |
| `label` | 最後一次看到的「名稱 (型別)」，用於搜尋、失效列與 Inspector 工具列的 tooltip。 |
| `time` | 加入的時間（Unix 毫秒，UTC），紀錄列表顯示「幾分鐘前」；舊資料為 0，不顯示。 |
| `folder` | 書籤所屬資料夾；`""` 是「未分類」。 |

規則：

1. **比對**（`EqualsItem`）：兩邊都有 `globalId` 時只比 `globalId`（同一資產檔的子資產 guid 相同，必須以此區分）；否則非零且相同的 `instanceId` 視為同一物件；最後才以 `guid` 比對舊資料。`IdentityKey` 與此順序一致，改其中一個必須同步改另一個。
2. **解析**（`RefToObject`）：先試 `instanceId`；失敗再用 `globalId`；場景物件所在場景沒開時直接回傳 null，不呼叫 `GlobalObjectIdentifierToObjectSlow`（它很慢，且失效列每次重建都會呼叫）。解析成功會回寫 `instanceId`。
3. **只在本次執行期間有效的引用**：沒有 `globalId` 也沒有 `guid`（未存檔場景、Prefab Mode、Play Mode 產生的物件）。
4. **Editor 重開**：以 `SessionState` 鍵 `HaruFamily.Bookmarks.SessionAlive` 分辨重開 Editor 與 domain reload。重開時把所有 `instanceId` 歸零（可能指到別的物件），並移除第 3 點的引用。domain reload（重新編譯、進出 Play Mode）不做這件事。
5. **補上 `globalId`**（`PromoteRefs`）：啟動後的 `delayCall` 與 `EditorSceneManager.sceneSaved` 時，替缺少 `globalId` 的引用補上：舊版只有 `guid` 的資料（解析成主資產），以及之後已存檔的場景物件。
6. **清除失效引用**（`PruneInvalidRefs`，啟動後 `delayCall`）：只移除「所屬資產檔已不存在」的引用。場景沒開或子物件暫時找不到的引用保留，列表顯示為失效列。

## 4. 資料檔與寫入

- 路徑：`UserSettings/PinInspectorData.json`（個人偏好，Unity 預設不進版控）。檔名沿用舊名，避免已安裝的使用者資料分成兩份；改檔名需要另寫搬移。
- 舊位置 `ProjectSettings/PinInspectorData.json`：新位置不存在時，驗證通過才複製過來；舊檔不刪。
- 載入失敗（JSON 壞掉、結構不完整）時封鎖所有寫入與操作，並顯示錯誤；不可用預設資料覆蓋使用者的檔案。
- `BookmarkStorage.Save()` 是延遲寫檔：遞增 `Version`、標記待寫，於下一個 `delayCall` 寫一次。domain reload 前與 Editor 結束時強制寫入。寫檔用暫存檔 + `File.Replace`。
- `BookmarkService.BookmarkVersion` 在書籤或資料夾的任何變動（加入、移除、移動、排序、資料夾新增／改名／刪除／排序）時遞增，一律經由 `SaveBookmarkMembership()`；它同時是 `IsBookmarked` 快取、Project／Hierarchy 標記快取與復原有效性的依據。資料夾展開／收合只呼叫 `Save()`，不遞增；`ReferenceVersion` 在 `projectChanged`／`hierarchyChanged`／Play Mode 切換時遞增，用於解析快取。
- 欄位相容：`JsonUtility` 會略過不認得的欄位、缺少的欄位保留初始值。**不可改既有欄位名稱**。`FolderInfo.fold` 與 `foldUncategorized` 的 true 是「收合」，`foldBookmarks`／`foldHistory` 的 true 是「展開」，名稱保留以相容舊資料。

## 5. 選取紀錄

- 瀏覽器模型：新的選取會砍掉游標之後的紀錄再加在最後；`NavigateTo` 造成的選取以哨兵 `_expectedNavTarget` 跳過記錄。
- 上一頁／下一頁往該方向找第一筆能解析的項目，跳過失效項目。`CanNavigateBack`／`CanNavigateForward` 只看位置，不解析物件。
- 移除項目一律用 `RemoveAllKeepCursor`／`TrimToLimit`，游標要留在原本那筆；那筆被移除時退到前一筆仍存在的項目。
- `mergeDuplicateHistory` 只影響紀錄列表的顯示（`CollectLatestDistinct`：每個物件只留最近一次），不改 `history` 本身與導覽順序。

## 6. 列表繪製（`BookmarksGUI`）

彈出視窗（`BookmarksPopup`）與書籤視窗（`BookmarksWindow`）共用；host 傳入 `onItemPicked`（彈出視窗傳 Close，視窗傳 null）。操作對照表見 README「列表操作」。

- **快照**：資料夾分組、每列的名稱／型別／圖示／tooltip、鍵盤可走訪的列（`navRows`）、失效書籤清單，只在 Layout 事件且 `BookmarkStorage.Version`、`ReferenceVersion`、`BookmarkVersion` 或搜尋字有變時重建；其他事件沿用同一份快照，維持 IMGUI control 順序一致。新增會影響列表內容的狀態時，必須讓它反映在上述版本號之一，否則畫面不會更新。`navRows` 的順序必須與實際畫出的順序一致（含「只有未分類時不畫資料夾標題、一律展開」的規則）。
- **hover 才出現的按鈕**：列尾的 ★、↗ 與資料夾的 ✎、刪除只在 hover（或鍵盤游標所在）時畫；**沒畫時必須用 `GUIUtility.GetControlID` 補上相同數量的 control id**，否則後面各列的 id 位移，點擊與拖曳會失效。
- **選取**：列表內的多選（`picked`）與 Unity `Selection` 同步；`Selection.activeObject` 被外部改掉時清空多選。鍵盤游標（`cursor`）移動時不改變 Unity 選取，避免紀錄被灌滿。
- **鍵盤**：只在搜尋框有焦點或沒有任何控制項有焦點時處理，正在輸入資料夾名稱時不搶按鍵。按鍵必須在畫搜尋框之前處理，否則會被 `SearchField` 吃掉。
- **失效列**：照樣出列（灰色斜體），標題數量與列一致；書籤失效列可用 ★、右鍵或「清除 N 筆失效書籤」移除；場景未開啟時 ↗ 會切換到該場景並選取物件（Play Mode 中停用）。
- **搜尋**：書籤與紀錄共用；有關鍵字時只列出有符合項目的資料夾，且一律展開。以空白分隔的每個字都要出現在名稱裡（不分大小寫），或關鍵字與資料夾名稱完全相同。
- **拖放**：拖曳資料以 `DragSortHandler.BookmarksKey`（書籤引用清單）或 `FolderKey`（資料夾名稱）區分。書籤 → 移動或排序；資料夾 → 只能放在資料夾標題上，排到它前面（放在「未分類」上＝最後）；其他物件（Project、Hierarchy、紀錄列）→ 加入書籤，已是書籤的移到放下的位置。加入書籤會展開所在資料夾。落點以藍線（排到前面）或藍底（放進資料夾）提示。

## 6.1 復原

- 只保留「最近一次」移除類操作（移除書籤、清除全部、刪除資料夾）的快照：書籤清單、每筆的資料夾、資料夾清單。
- 復原只在 `BookmarkVersion` 與當時相同、且在 20 秒內有效；之後任何書籤或資料夾變動都會讓它失效，避免蓋掉新的修改。
- 加入書籤不提供復原（再按一次 ★ 即可移除）。

## 6.2 Inspector 工具列與 Project／Hierarchy 整合

- 工具列畫在 `Editor.finishedDefaultHeaderGUI`。內嵌的 Material 編輯器（例如 MeshRenderer 底下）也會觸發：只有 Material 本身是選取物件時才畫，避免重複出現。
- Inspector 對資產顯示的是 Importer 編輯器；★ 透過 `ResolveInspectedTarget` 換成主資產再加入書籤。
- ◀ ▶ 的 tooltip 顯示目標的 `label`（不解析物件）；右鍵列出最近 30 筆紀錄。
- Project／Hierarchy 的 ★：Unity 2022.1 以上用 `projectWindowItemInstanceOnGUI` 依物件判斷，以前用 `projectWindowItemOnGUI` 依檔案判斷。快取（`EnsureBadgeCache`）只在 `BookmarkVersion` 或已開啟場景組合（開關場景、新場景、Play Mode 切換）變動時重建，重建時會解析每筆書籤。書籤變動時主動重畫 Project 與 Hierarchy。
- 通知（`Notify`）只用於快捷鍵、Inspector ★ 與右鍵選單；列表內的操作畫面本身就有變化，不另外通知。

## 7. Editor 入口（契約）

| 入口 | 值 |
|---|---|
| 選單 | `HaruFamily/Bookmarks/Open` |
| Project 右鍵 | `Assets/HaruFamily/Toggle Bookmark in Bookmarks` |
| Hierarchy 右鍵 | `GameObject/HaruFamily/Toggle Bookmark in Bookmarks`（多選時只在 `context == Selection.activeObject` 執行一次） |
| 快捷鍵 id | `HaruFamily/Bookmarks/Previous`、`/Next`、`/Toggle Bookmark`、`/Open Bookmarks Window` |
| 設定頁 | `Preferences/HaruFamily/Bookmarks`（`SettingsScope.User`） |

快捷鍵 id 是使用者自訂按鍵的儲存鍵；改 id 會讓自訂綁定回到預設，必須在 commit 訊息註明。

## 8. 測試

EditMode，`Tests/Editor/`：

- `BookmarkLogicTests`：排序、搜尋（含多字）、識別比對、`RemoveAllKeepCursor`、`TrimToLimit`、`CollectLatestDistinct`、`MoveBookmarks`、`ReorderFolders`、`FormatRelativeTime`。
- `BookmarkStorageTests`：寫檔與讀回、壞檔封鎖寫入、寫入失敗重試、舊位置搬移。測試使用暫存目錄，不碰使用者資料。

涉及 `AssetDatabase`、場景、`GlobalObjectId` 與 IMGUI 互動的行為沒有自動測試，修改後要在 DevEvi 實際驗證：資產、子資產、已存檔場景物件加入書籤後重新編譯仍在；關閉場景後顯示失效列，↗ 可切回；hover 按鈕、多選、拖放、鍵盤、復原列；Project／Hierarchy 的 ★。
