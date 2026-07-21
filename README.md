# HaruKit

Unity Editor / runtime 工具集，以 UPM (Unity Package Manager) 分發。依類別分組（UX / Framework / Tools…），每個工具為獨立子套件，透過 Git URL 的 `?path=` 安裝。

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

指定版本（tag）：在 URL 尾端加 `#bookmarks/v1.0.0`（tag 綁整 repo commit，各工具各打各的 tag 獨立釘選）。不加則吃 default branch 最新。

> **一次裝整組**（例：整個 UX）：UPM git URL 無法用單一 path 拉整組子套件（package 不能巢狀）。要裝一組就在 `manifest.json` 一次列該類別底下每個 `?path=` 條目。

> Private repo：安裝端須先在系統 git 設好認證（PAT）。public repo 讀取免認證。UPM 走系統 git，URL 本身不帶 token。

## 套件清單

| 類別 | 套件 | package name | 安裝 path |
|------|------|--------------|-----------|
| UX | Bookmarks | `com.harufamily.ux.bookmarks` | `?path=/UX/Bookmarks` |
