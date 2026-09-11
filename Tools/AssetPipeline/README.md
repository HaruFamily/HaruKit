# HaruKit Tools AssetPipeline

Editor-only UPM package for ordered asset processing. It has no Runtime assembly and does not enter player builds.

## Install

In Unity Package Manager, choose **Add package from git URL...** and enter:

```
https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline
```

Or add this Git dependency to `Packages/manifest.json`:

```json
"com.harufamily.tools.assetpipeline": "https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline"
```

Remove the package through Package Manager, or remove that dependency from `Packages/manifest.json`. The package requires Unity 2021.3 or newer and `com.unity.addressables` 1.19.19.

Open an existing `AssetPipeline` asset and click **Open Graph**, or use `HaruFamily/Asset Pipeline/Graph`. `HaruFamily/Asset Pipeline/Open` creates or selects the default asset at `Assets/Editor/HaruFamily/AssetPipeline/AssetPipeline.asset`.

## Flow

起點：在 `AssetPipeline` 設定 Prototype assets 與線性 step list。

前一步：Validate 反射每個 active `AssetPipelineSource`，檢查缺少的 Prototype key 與在 producer 前讀取的 Dynamic key，並建立目前序列化資料的驗證快照。

當前：Run 僅在快照仍有效時啟用；確認 dialog 通過後，嚴格依 `pipelineAssets` list 順序執行。

下一步：step 可讀 Prototype/Dynamic keys；`CreatePrefabCopies` 僅在 `registerToDynamic` 啟用時產出 Dynamic key，`RegisterAssetsFromFolder` 類 step 產出其 `dynamicKey`。

終點：依 `dynamicClearTiming` 保留或清除 Dynamic assets，儲存修改並顯示 log。

## Editing

The graph is intentionally linear, not a DAG. The list order is the single source of truth. Select a card to edit fields, use **Move Up**, **Move Down**, **Delete**, or **Add Step**. Native managed-reference menus support `IPipelineAsset` steps and nested concrete `IFormula<T>` implementations without Odin.

## Existing Assets Migration

The original script `.meta` GUIDs are retained so `m_Script` references remain valid. If existing serialized assets contain managed references from an older namespace or assembly name, the consuming project must migrate those records.

## Current Limit

Execution still uses the existing static `AssetPipeline.current` and report handler while a pipeline runs. This preserves formula and Dynamic asset behavior; simultaneous pipeline execution is not supported.
