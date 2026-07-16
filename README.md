# Pin Tools Bookmarks

Unity Editor extension for Inspector asset bookmarks and selection history.

## Install

In Unity Package Manager, select **Add package from git URL** and enter:

```
https://github.com/HaruFamily/Bookmarks.git#v1.1.1
```

## Data storage

Each user's bookmarks and history are stored locally in `UserSettings/PinInspectorData.json`.
The file is not included in the package or source control.

## Install To Assets

Use Tools Loader with this source mapping:

| Field | Value |
| --- | --- |
| Git URL | `https://github.com/HaruFamily/Bookmarks.git` |
| Ref | `v1.1.1` |
| Source Path | `PinTools/Editor/Bookmarks` |
| Install Path | `Assets/PinTools/Editor/Bookmarks` |

## Commands

- `PinTools/Bookmarks Window`
- `Alt+A`: Previous Inspector selection
- `Alt+D`: Next Inspector selection
- `Alt+S`: Toggle bookmark
- `Alt+B`: Open Bookmarks Window
