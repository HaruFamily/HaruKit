# HaruKit

Unity Editor / runtime 工具集，以 UPM (Unity Package Manager) 分發。每個工具都是獨立套件，依類別放在 `UX`、`Framework`、`DependencyCore`、`Tools`，透過 Git URL 的 `?path=` 個別安裝。

> 新增工具 / AI 上架規範見 [`CONVENTIONS.md`](./CONVENTIONS.md)。

## 安裝

Unity → `Window > Package Manager` → `+` → `Add package from git URL...`，貼上對應 URL。

或直接編輯 `Packages/manifest.json` 的 `dependencies`：

```json
{
  "dependencies": {
    "com.harufamily.ux.bookmarks": "https://github.com/HaruFamily/HaruKit.git?path=/UX/Bookmarks"
  }
}
```

指定版本時，在 URL 尾端加 package tag 或 commit，例如 `#bookmarks/v1.0.0`。不加則使用 `main` 最新內容；正式專案建議釘選已確認的 tag 或 commit。

> **一次裝整組**（例：整個 UX）：UPM git URL 無法用單一 path 拉整組子套件（package 不能巢狀）。要裝一組就在 `manifest.json` 一次列該類別底下每個 `?path=` 條目。

> Private repo：安裝端須先在系統 git 設好認證（PAT）。public repo 讀取免認證。UPM 走系統 git，URL 本身不帶 token。

## 套件清單

| 類別 | 套件 | 版本 | 用途 | package name | 安裝 path | 需手動加入的 git 依賴 |
|---|---|---:|---|---|---|---|
| UX | Bookmarks | 1.0.3 | Editor 書籤與釘選物件 Inspector | `com.harufamily.ux.bookmarks` | `?path=/UX/Bookmarks` | — |
| Framework | [Nexus](./Framework/Nexus/README.md) | 1.0.1 | 有 scope、生命週期、pool 與 Addressables 支援的非同步 service locator | `com.harufamily.framework.nexus` | `?path=/Framework/Nexus` | UniTask |
| DependencyCore | [GraphKit](./DependencyCore/GraphKit/README.md) | 1.1.0 | 無領域執行語意的序列化節點圖模型與 IMGUI 編輯器 | `com.harufamily.dependencycore.graphkit` | `?path=/DependencyCore/GraphKit` | — |
| Framework | [LogicGraph](./Framework/LogicGraph/README.md) | 2.0.0 | Action、Formula、Token、驗證與描述編譯框架 | `com.harufamily.framework.logicgraph` | `?path=/Framework/LogicGraph` | GraphKit、UniTask |
| Tools | [AssetPipeline](./Tools/AssetPipeline/README.md) | 2.0.0 | 基於 GraphKit 的 Editor-only 資產處理管線框架 | `com.harufamily.tools.assetpipeline` | `?path=/Tools/AssetPipeline` | GraphKit |

表中的版本來自各套件目前的 `package.json`，不是建議安裝 tag。釘版時請使用該套件實際存在的 tag 或 commit。

## Git 依賴

Unity registry 依賴會由 UPM 自動解析，例如 Nexus 宣告的 Addressables。Git URL 依賴無法由子套件的 `package.json` 自動解析，必須一起列在使用端的 `Packages/manifest.json`；缺少時套件會直接編譯失敗。

### Nexus

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.harufamily.framework.nexus": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/Nexus"
  }
}
```

### LogicGraph

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.framework.logicgraph": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/LogicGraph"
  }
}
```

### AssetPipeline

```json
{
  "dependencies": {
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.tools.assetpipeline": "https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline"
  }
}
```

完整功能、整合方式與限制請見各套件 README；依賴與上架規則見 [`CONVENTIONS.md`](./CONVENTIONS.md) §1.1。
