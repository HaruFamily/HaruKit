# Pin Tools Bookmarks

Unity Editor extension for Inspector asset bookmarks and selection history.

## Install

Add this repository to the Tools Loader Google Sheet. Tools Loader reads
`tools-loader.manifest.json` and installs the repository's `Assets/` content into the current project.

## Data storage

Each user's bookmarks and history are stored locally in `UserSettings/PinInspectorData.json`.
The file is not included in the package or source control.

## Commands

- `PinTools/Bookmarks Window`
- `Alt+A`: Previous Inspector selection
- `Alt+D`: Next Inspector selection
- `Alt+S`: Toggle bookmark
- `Alt+B`: Open Bookmarks Window
