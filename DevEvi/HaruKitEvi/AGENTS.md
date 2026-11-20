# HaruKitEvi — HaruKit 開發宿主專案

這個 Unity 專案只用來開發、測試 HaruKit 的套件，本身不是套件，也不會發佈。

## 開始前
1. 讀 repo 根目錄的 `CONVENTIONS.md`（本專案往上三層，`../../../CONVENTIONS.md`），先做 **§0 開工檢查**：確認 remote、目前分支、工作區狀態。
2. 要改的套件先讀它的維護手冊：
   - GraphKit：`DependencyCore/GraphKit/Documentation~/Maintenance.md`
   - LogicGraph：`Framework/LogicGraph/Documentation~/Maintenance.md`
   - AssetPipeline：`Tools/AssetPipeline/Documentation~/Maintenance.md`

## 這個專案怎麼運作
- 套件原始碼在 repo 根目錄的 `UX/`、`Framework/`、`DependencyCore/`、`Tools/`；`Packages/manifest.json` 以 `file:` 參照它們。**修改套件一律改 repo 裡的套件資料夾**，不要複製進 `Assets/` 或 `Packages/`。
- `Assets/` 只放測試用 Fixtures、範例與專案設定。
- 測試：Test Runner 的 EditMode（`testables` 已列出 GraphKit、LogicGraph、AssetPipeline、Bookmarks）。
- MCP：CoplayDev 的 MCP for Unity。不要另外安裝 Unity 官方 AI Assistant／MCP。
- 開發環境細節見 CONVENTIONS §4.1；發佈與 tag 見 §3.3；禁止事項見 §5（push、刪 tag、改寫歷史都要先問 owner）。
- git 操作在 repo 根目錄進行。
