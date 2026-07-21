# PinTools

Unity Editor 工具集，以 UPM (Unity Package Manager) 分發。每個工具為獨立子套件，透過 Git URL 的 `?path=` 安裝。

## 安裝

Unity → `Window > Package Manager` → `+` → `Add package from git URL...`，貼上對應 URL。

或直接編輯 `Packages/manifest.json` 的 `dependencies`：

```json
{
  "dependencies": {
    "com.harufamily.pintools.bookmarks": "https://github.com/HaruFamily/Bookmarks.git?path=/PinTools/Bookmarks"
  }
}
```

指定版本（tag）：在 URL 尾端加 `#v1.0.0`。

> Private repo：安裝端須先在系統 git 設好認證（PAT）。UPM 走系統 git，URL 本身不帶 token。
> Windows 可用 Git Credential Manager，或 `git config --global credential.helper store` 後先手動 clone 一次快取 PAT。

## 套件清單

| 套件 | path | 說明 |
|------|------|------|
| PinTools Bookmarks | `?path=/PinTools/Bookmarks` | Editor 書籤 / pinned object inspector |
