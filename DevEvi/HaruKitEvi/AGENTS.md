# HaruKitEvi — HaruKit 開發宿主專案

這個 Unity 專案只用來開發、測試 HaruKit 的套件，本身不是套件，也不會發佈。

## 原始碼在哪裡
- 套件原始碼在 repo 根目錄（本專案往上三層，`../../../`）：`UX/`、`Framework/`、`DependencyCore/`、`Tools/`。
- `Packages/manifest.json` 以 `file:` 參照這些套件。**修改套件一律改 repo 裡的套件資料夾**，不要把套件複製進 `Assets/` 或 `Packages/`。
- 本專案的 `Assets/` 只放測試用 Fixtures、範例與專案設定。

## 修改前必讀
1. repo 根目錄的 `CONVENTIONS.md`（上架規範：.meta、asmdef、版本、選單路徑等）。
2. 要改的套件的維護手冊：
   - GraphKit：`DependencyCore/GraphKit/Documentation~/Maintenance.md`
   - LogicGraph：`Framework/LogicGraph/Documentation~/Maintenance.md`
   - AssetPipeline：`Tools/AssetPipeline/Documentation~/Maintenance.md`

## 工作規則
- 在套件內新增或移動檔案時，`.meta` 必須跟著建立或一起 `git mv`；最好讓 Unity 開著時產生 .meta，不要手寫 GUID。
- 改到手冊描述的行為、契約或限制時，同一個 commit 更新手冊與 README。
- 改 `package.json` 版本時，同步 repo `README.md` 套件表與該套件 README 開頭的版本。
- 測試：Unity Test Runner 的 EditMode（`testables` 已列出 GraphKit、LogicGraph、AssetPipeline、Bookmarks）。
- git 操作在 repo 根目錄進行；不要 commit `Library/`、`Temp/`、`Logs/`、`UserSettings/` 與 IDE 產物（已由 `.gitignore` 排除）。
