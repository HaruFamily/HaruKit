# PinTools — AI 上架規範 (SSOT)

任何 AI 助手在任何專案 / 電腦要把新 Unity Editor 工具丟上本 repo 時，**先讀本檔並完全照辦**。
Raw 讀取：`https://raw.githubusercontent.com/HaruFamily/PinTools/main/CONVENTIONS.md`

---

## 1. Repo 定位

- **Monorepo**：一個 GitHub repo 收多個獨立 UPM 工具。
- **單一寫入者**：owner (HaruFamily) 獨寫；他人 public 唯讀取用，無 write 權不能 push。
- 每個工具 = repo root 底下**一個資料夾**，資料夾內含自己的 `package.json`，即成一個可獨立 UPM 安裝的套件。

```
PinTools/                    ← repo root
  Bookmarks/                 → 套件，install ?path=/Bookmarks
    package.json
    <Tool>.Editor.asmdef
    *.cs + *.cs.meta
  AssetPipeline/             → 下一個套件，install ?path=/AssetPipeline
    package.json
    ...
  README.md
  CONVENTIONS.md             ← 本檔
```

---

## 2. 新增工具 — 硬性規則

### 2.1 資料夾
- 工具放 **root 一層**，夾名 = PascalCase 工具名（`Bookmarks`、`AssetPipeline`）。
- **不要**再包一層 `PinTools/` 前綴（repo 本身已是 PinTools）。

### 2.2 package.json（每工具必備）
```json
{
  "name": "com.harufamily.pintools.<tool-lowercase>",
  "version": "1.0.0",
  "displayName": "PinTools <Tool>",
  "description": "<一句話說明>",
  "unity": "2021.3",
  "author": { "name": "HaruFamily" },
  "keywords": ["editor", "<tool>", "pintools", "tool"]
}
```
- `name` 反向網域，全小寫，命名空間固定 `com.harufamily.pintools.*`。
- `unity` 填工具實際支援的最低版本。
- Editor-only 工具**不需**填 runtime 依賴。

### 2.3 asmdef
- Editor 工具 asmdef 命名 `HaruFamily.PinTools.<Tool>.Editor`。
- `includePlatforms` 設 `["Editor"]`，確保不進 runtime build。

### 2.4 .meta 鐵律（AI 最常漏 — 必查）
- **每個 `.cs`、`.asmdef`、資料夾都要有對應 `.meta`，且一起 commit。**
- 缺 .meta → 使用端 Unity 會重生 GUID，破壞引用、每次 clone 產生 diff。
- commit 前執行檢查（見 §4）確認無漏 meta。

---

## 3. 安裝與版本

### 3.1 安裝 URL
```
https://github.com/HaruFamily/PinTools.git?path=/<Tool>
```
寫進使用端 `Packages/manifest.json` 的 `dependencies`，key = package `name`。

### 3.2 版本 tag 慣例
- tag 綁「整 repo 的 commit」，非單一資料夾 → 用 **per-tool 命名空間**避免混淆：
  ```
  <tool-lowercase>/vX.Y.Z      例：bookmarks/v1.0.0
  ```
- 釘版安裝：`...?path=/<Tool>#bookmarks/v1.0.0`
- 不加 tag = 吃 default branch 最新（浮動）。個人用初期可不釘；要給外部/求穩就釘。
- 發版時：更新該工具 `package.json` 的 `version` → commit → `git tag <tool>/vX.Y.Z` → `git push --tags`。

---

## 4. Push 流程（AI 照做）

1. Clone（public 免認證）：
   ```
   git clone https://github.com/HaruFamily/PinTools.git
   ```
2. 新工具放 `root/<Tool>/`，備妥 §2 的 package.json + asmdef + 所有 .cs + **所有 .meta**。
3. 移動既有檔案用 `git mv` 保留歷史，勿刪後重加。
4. commit 前自檢：
   ```bash
   # 有無 .cs / .asmdef 缺對應 .meta
   for f in $(git ls-files '<Tool>/*.cs' '<Tool>/*.asmdef'); do
     [ -f "$f.meta" ] || echo "MISSING META: $f"
   done
   # 確認沒混入 Library/ .vs/ Temp/ 等非套件檔
   git status --short
   ```
5. commit 訊息用繁中、結論先行、說明「動了什麼 + 為何」。
6. push：
   ```
   git push origin main
   ```
   owner push 需 GCM 認證；使用端只讀免認證。

---

## 5. 禁止 / 注意

- ❌ 不 commit `Library/`、`Temp/`、`obj/`、`.vs/`、`*.csproj`、`*.sln`（IDE / build 產物）。
- ❌ 不把 PAT / token 寫進 URL 或任何檔案。
- ❌ 不在 root 再套 `PinTools/` 冗餘層。
- ⚠️ 改 repo 結構（移夾 / 改路徑）後，同步更新 `README.md` 安裝表與各使用端 manifest.json。
- ⚠️ runtime 套件（非 Editor-only）才需 `Runtime/` 夾 + runtime asmdef；純 Editor 工具全放同層即可。

---

## 6. 現有套件清單

| 套件 | 資料夾 | package name | 安裝 path |
|------|--------|--------------|-----------|
| PinTools Bookmarks | `Bookmarks/` | `com.harufamily.pintools.bookmarks` | `?path=/Bookmarks` |
