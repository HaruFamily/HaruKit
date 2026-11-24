# Bookmarks

目前套件版本：`1.1.0`（Unity 2021.3 以上）。

> **使用 AI Agent 修改本套件前，請要求它先閱讀並遵守 [`Documentation~/Maintenance.md`](Documentation~/Maintenance.md)（維護手冊）。**
> 可以直接對 Agent 說：「先讀 Bookmarks 的維護手冊，照裡面的規則做。」
> 透過 UPM 安裝時，手冊位於 `Library/PackageCache/com.harufamily.ux.bookmarks@<hash>/Documentation~/Maintenance.md`。Agent 的搜尋工具可能略過 `Library/`，請把路徑直接告訴它，或使用 [GraphKit README 的路由範本](../../DependencyCore/GraphKit/README.md#給-agent-的路由範本)。

Editor-only 的書籤與選取紀錄工具。可以把資產、子資產與場景物件加入書籤並分資料夾整理，也能像瀏覽器一樣在選取紀錄中上一頁／下一頁。沒有 Runtime 組件，不會進入 player build。

## 功能

- **書籤**：資產、子資產（例如 Texture 裡的 Sprite、FBX 裡的 Mesh）與已存檔場景內的物件都能加入，重開 Editor 後仍找得回來。
- **資料夾**：新增、重新命名（雙擊或右鍵）、刪除（內含書籤搬回「未分類」），書籤與資料夾都能拖曳排序。
- **拖放加入**：把 Project、Hierarchy 或紀錄列中的物件拖到資料夾標題或某筆書籤上即可加入，可一次拖多個。
- **選取紀錄**：新的在上並顯示「幾分鐘前」，可上一頁／下一頁；列表預設合併重複項目。
- **復原**：移除書籤、清除全部、刪除資料夾後，視窗上方會出現「復原」列（約 20 秒，期間沒有其他書籤變動才有效）。
- **失效列**：找不到的物件顯示為灰色斜體並註明原因，可單獨或一次清除；所在場景未開啟時可直接切過去。
- **搜尋**：書籤與紀錄共用一個搜尋框；以空白分隔的每個字都要出現在名稱裡，或輸入完整的資料夾名稱。
- **Project／Hierarchy 標記**：已加入書籤的項目右側顯示 ★。

## 入口

| 入口 | 位置 |
|---|---|
| 書籤視窗 | `HaruFamily/Bookmarks/Open`，或 `Alt+B` |
| Inspector 工具列 | 每個 Inspector 標題下方：◀ ▶（右鍵列出紀錄）、書籤與紀錄 ▼、★ |
| 上一頁／下一頁 | `Alt+A`／`Alt+D` |
| 加入或移除書籤 | `Alt+S`（目前所有選取的物件）；Project 右鍵 `HaruFamily > Toggle Bookmark in Bookmarks`；Hierarchy 右鍵同名項目 |
| 設定 | `Preferences > HaruFamily > Bookmarks` |

快捷鍵可在 `Edit > Shortcuts` 的 `HaruFamily/Bookmarks` 底下重新綁定。

## 列表操作

| 操作 | 結果 |
|---|---|
| 單擊 | 選取並在 Project／Hierarchy 中閃一下位置；從「書籤與紀錄 ▼」開啟時，選取後關閉 |
| 雙擊、Enter | 可開啟的（場景、Prefab、腳本、Material…）就開啟，否則同單擊；彈出視窗中 Enter 只選取並關閉 |
| Ctrl／Cmd+單擊、Shift+單擊 | 多選（Shift 為範圍，限同一區塊）；可一起拖曳、移除或移動到資料夾 |
| Alt+單擊、中鍵 | 只在 Project／Hierarchy 中閃一下位置，不改變選取 |
| ↑↓ | 移動游標；彈出視窗打開時游標就在搜尋框，可直接打字篩選 |
| Delete | 移除選取的書籤（可復原） |
| Esc | 清除搜尋；彈出視窗中搜尋為空時關閉 |
| 右鍵 | 開啟、顯示位置、加入／移除書籤、移動到資料夾 |

## 安裝

```json
{
  "dependencies": {
    "com.harufamily.ux.bookmarks": "https://github.com/HaruFamily/HaruKit.git?path=/UX/Bookmarks"
  }
}
```

不需要其他套件。

## 限制

- 資料是個人偏好，存在專案的 `UserSettings/PinInspectorData.json`，不進版控，不與團隊共用。
- 未存檔場景、Prefab Mode 與 Play Mode 中產生的物件只在本次 Editor 執行期間有效；場景存檔後，在其中加入的書籤會自動轉為可永久保存。
- 上一頁／下一頁會跳過找不到的項目；全部都找不到時按鈕不會有反應。
- Unity 2022.1 以前的 Project 視窗只能以檔案判斷 ★：檔案內任一物件是書籤，該檔所有列都會顯示 ★。
