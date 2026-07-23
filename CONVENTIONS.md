# HaruKit — AI 上架規範 (SSOT)

任何 AI 助手在任何專案 / 電腦要把新 Unity 工具丟上本 repo 時，**先讀本檔並完全照辦**。
Raw 讀取：`https://raw.githubusercontent.com/HaruFamily/HaruKit/main/CONVENTIONS.md`

---

## 1. Repo 定位

- **Monorepo，依類別分組**：一個 GitHub repo 收多個獨立 UPM 工具，工具依類別放 `<Category>/<Tool>/`。
- **單一寫入者**：owner (HaruFamily) 獨寫；他人 public 唯讀取用，無 write 權不能 push。
- 每個工具 = `<Category>/<Tool>/` 資料夾內含自己的 `package.json`，即一個可獨立 UPM 安裝的套件。

```
HaruKit/                       ← repo root
  UX/                          ← 類別（純組織用，非安裝單位）
    Bookmarks/                 → 套件，install ?path=/UX/Bookmarks
      package.json
      HaruFamily.UX.Bookmarks.Editor.asmdef
      *.cs + *.cs.meta
  Framework/                   ← 之後的類別
    <Tool>/ ...                → ?path=/Framework/<Tool>
  Tools/
    <Tool>/ ...                → ?path=/Tools/<Tool>
  README.md
  CONVENTIONS.md               ← 本檔
```

### 1.1 巢狀與「一次裝整組」的限制（重要）
- `?path=` 可指任意深度 → 巢狀資料夾做**組織**沒問題，各葉節點 `?path=/<Category>/<Tool>` 單裝正常。
- **UPM git URL 無法用單一 path 拉整組子套件**：package 不能巢狀（Unity 只認第一個 package.json），且 package.json 的 `dependencies` **不會自動解析 git 子依賴**。
- 所以「類別」只是資料夾組織，**不是安裝單位**。要裝一整組 → 在使用端 `manifest.json` 一次列該類別下每個 `?path=` 條目（唯一輕量解；真要一鍵裝整組須改用 scoped registry，個人用過重、不採）。

---

## 2. 新增工具 — 硬性規則

### 2.1 資料夾
- 工具放 `<Category>/<Tool>/`。Category 與 Tool 皆 PascalCase（`UX/Bookmarks`、`Framework/EventBus`）。
- 沒有合適既有類別再開新類別；別為單一工具亂開類別（YAGNI）。

### 2.2 package.json（每工具必備）
```json
{
  "name": "com.harufamily.<category-lowercase>.<tool-lowercase>",
  "version": "1.0.0",
  "displayName": "HaruKit <Category> <Tool>",
  "description": "<一句話說明>",
  "unity": "2021.3",
  "author": { "name": "HaruFamily" },
  "keywords": ["editor", "<tool>", "<category>", "harukit", "tool"]
}
```
- `name` 反向網域全小寫，命名空間 `com.harufamily.<category>.<tool>`（category 進 namespace）。
- `unity` 填實際支援最低版本。

### 2.3 asmdef
- 命名 `HaruFamily.<Category>.<Tool>.Editor`（Editor 工具）或 `HaruFamily.<Category>.<Tool>`（runtime）。
- **asmdef 檔名 = assembly name**（Unity 慣例，改 name 時檔名一起 `git mv`）。
- Editor 工具 `includePlatforms` 設 `["Editor"]`，確保不進 runtime build。

### 2.4 .meta 鐵律（AI 最常漏 — 必查）
- **每個 `.cs`、`.asmdef`、資料夾都要有對應 `.meta`，且一起 commit。**
- 缺 .meta → 使用端 Unity 重生 GUID，破壞引用、每次 clone 產生 diff。
- 重命名檔案時 `.meta` 一起 `git mv`（保留 GUID）。

---

## 3. 安裝與版本

### 3.1 安裝 URL
```
https://github.com/HaruFamily/HaruKit.git?path=/<Category>/<Tool>
```
寫進使用端 `Packages/manifest.json` 的 `dependencies`，key = package `name`。

### 3.2 版本 tag 慣例
- tag 綁「整 repo 的 commit」→ 用 **per-tool 命名空間**：`<tool-lowercase>/vX.Y.Z`（例 `bookmarks/v1.0.0`）。
- 釘版安裝：`...?path=/UX/Bookmarks#bookmarks/v1.0.0`
- 不加 tag = 吃 default branch 最新（浮動）。個人用初期可不釘；給外部 / 求穩就釘。
- 發版：更新該工具 `package.json` 的 `version` → commit → `git tag <tool>/vX.Y.Z` → `git push --tags`。

---

## 4. Push 流程（AI 照做）

1. Clone（public 免認證）：
   ```
   git clone https://github.com/HaruFamily/HaruKit.git
   ```
2. 新工具放 `<Category>/<Tool>/`，備妥 §2 的 package.json + asmdef + 所有 .cs + **所有 .meta**。
3. 移動 / 重命名既有檔案一律 `git mv` 保留歷史，勿刪後重加；檔案與其 `.meta` 成對移動。
4. commit 前自檢：
   ```bash
   # .cs / .asmdef 缺對應 .meta
   for f in $(git ls-files '<Category>/<Tool>/*.cs' '<Category>/<Tool>/*.asmdef'); do
     [ -f "$f.meta" ] || echo "MISSING META: $f"
   done
   # 有無混入 Library/ .vs/ Temp/ 等非套件檔
   git status --short
   ```
5. commit 訊息繁中、結論先行、說明「動了什麼 + 為何」。
6. push：`git push origin main`（owner push 需 GCM 認證；使用端只讀免認證）。

---

## 5. 禁止 / 注意

- ❌ 不 commit `Library/`、`Temp/`、`obj/`、`.vs/`、`*.csproj`、`*.sln`（IDE / build 產物）。
- ❌ 不把 PAT / token 寫進 URL 或任何檔案。
- ⚠️ 改 repo 結構（移夾 / 改 namespace）後，同步更新 `README.md` 套件表、`CONVENTIONS.md` §6、及各使用端 manifest.json。
- ⚠️ runtime 套件才需 `Runtime/` 夾 + runtime asmdef；純 Editor 工具全放 `<Category>/<Tool>/` 同層即可。
- ⚠️ 「類別」非安裝單位，不要試圖做 `?path=/UX` 拉整組（見 §1.1）。

---

## 6. 現有套件清單

| 類別 | 套件 | package name | 安裝 path |
|------|------|--------------|-----------|
| UX | Bookmarks | `com.harufamily.ux.bookmarks` | `?path=/UX/Bookmarks` |
| Framework | Nexus | `com.harufamily.framework.nexus` | `?path=/Framework/Nexus` |
| Framework | ActionSystem | `com.harufamily.framework.actionsystem` | `?path=/Framework/ActionSystem` |
