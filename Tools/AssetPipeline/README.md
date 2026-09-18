# HaruKit Tools AssetPipeline

Current package version: `2.0.0` (Unity 2021.3+).

Editor-only UPM package for ordered asset processing. It has no Runtime assembly
and does not enter player builds.

The package is a **framework only**. It provides the pipeline asset, Catalog
model, synchronous formula families, action/Slot bases, and validator. Concrete
steps and formulas that touch project assets live in the consuming project, so
adding a pipeline step does not require editing this package.

## Requirements

- Unity 2021.3 or later
- [GraphKit](../../DependencyCore/GraphKit):
  `https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit`

Unity Package Manager cannot resolve the GraphKit Git dependency from this
package manifest, so install it first; otherwise compilation fails by design.

No other external packages. Addressables is **not** a dependency — the
Addressable formula family is part of the concrete content layer and belongs in
the consuming project.

## Install

```json
{
  "dependencies": {
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.tools.assetpipeline": "https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline"
  }
}
```

`HaruFamily/Asset Pipeline/Open` creates or selects the default asset at
`Assets/Editor/HaruFamily/AssetPipeline/AssetPipeline.asset`. Select it and
press **開啟節點圖編輯器** on the graph card to edit the pipeline in the GraphKit
node editor.

## Flow

1. 在 AssetPipeline 視窗的目錄庫建立資產群組，或把 Project 資產拖進既有群組。
2. 在節點圖建立 `PrototypeAssetCatalog` 讀取既有群組，或建立
   `DynamicAssetCatalog` 接收前面動作產生的資產。
3. Catalog 底下的每個 ListCell 可接一個 packed filter；未接時輸出完整 Catalog。
   一般公式欄位接 ListCell 的輸出，而不是直接接 Catalog 容器。
4. 產出資產的動作使用 `CatalogOutputSlot.Write(...)` 寫入 Dynamic Catalog。
   `reset` 決定這次寫入先清空或繼續累積；Prototype Catalog 是唯讀的，不能接產出端。
5. 驗證會檢查動作與公式相容性、Token、Catalog 結構、Prototype 群組引用，以及
   Dynamic Catalog 是否先寫後讀。Prototype Catalog 隨時可讀，不受動作順序限制。
6. 「執行管線」在執行前再次驗證，通過後依 root 動作清單順序同步執行並輸出報告。

## Editing

節點圖由 GraphKit 提供。`AssetPipeline.graph` 是一個實作 `IGraphDocument` 的
序列化欄位，編輯器靠這個介面認出它，不認識 AssetPipeline 任何型別。

Inspector 把這個欄位畫成一張卡片（`GraphDrawer`）：左緣色條是驗證狀態，卡上
只有「開啟節點圖編輯器」與「驗證」兩個入口，圖的內容不在 Inspector 展開。資產
群組與維護操作收在下方的折疊分區裡。

一個公式欄位可以是常數、接一顆內嵌公式節點、指向具名Token，或接到 Catalog
ListCell 的輸出。AssetPipeline 不提供共用公式／動作資產節點；Slot 的
`AssetBaseType` 為空。目錄本身不是公式，也不能直接接到一般公式欄位。

順序仍然是唯一真相：節點圖只換了編輯方式，動作依然嚴格照清單順序跑。

## Extending

新增一個動作或公式**不需要改這個套件**。在使用端專案的 Editor 資料夾裡：

```csharp
using System;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Tools.AssetPipeline;

[HGNode("我的動作", "做一件事", "動作")]
[Serializable]
public class MyAction : ActionBase
{
    public FormulaAsset_ObjectList targets = new FormulaAsset_ObjectList();

    public override void Execute()
    {
        foreach (var target in targets.Evaluate()) { /* … */ }
    }
}
```

- 動作繼承 `ActionBase` 並覆寫同步的 `Execute()`。
- 公式繼承對應族的基底（`Formula_Int`、`Formula_Object` 等）並覆寫同步求值。
- 讀舊式 prototype key 的公式實作 `IPrototypeKeyReader`；新圖優先使用
  `PrototypeAssetCatalog` 與目錄庫的穩定 id。
- 需要輸出資產給後續動作時，在動作上宣告 `CatalogOutputSlot`，並呼叫
  `output.Write(assets)`；不要自行維護 Dynamic Catalog 的生命週期。
- `AssetPipeline.current`、`Report` 與 `ReportFormulaWarning` 是給動作用的公開 API。

## Package Contents

- `Node` / `Slot`：同步動作與公式基底、欄位基底、動作頭端
- `Graph` / `GraphVerifier`：`IGraphDocument` 實作與驗證（含 Dynamic Catalog 時序）
- `AssetCatalog`：Prototype／Dynamic Catalog、ListCell 與產出 Slot
- `FormulaAsset_*`：族宣告與欄位容器（Bool / Float / Int / String / Folder /
  Object / AudioClip / GameObject / TextAsset）
- `AssetPipeline*`：管線資產、資產群組、群組批次執行、Inspector

## Existing Assets Migration

原始腳本的 `.meta` GUID 保留，`m_Script` 參照仍然有效。但 **v2.0.0 的資料模型
與 v1 不相容**：`IPipelineAsset` 清單換成節點圖、`FormulaAssetBase` 換成
`FormulaSlotBase` 子類。既有的 v1 序列化資料需要重建，沒有自動遷移。型別名前綴移除也不提供 `MovedFrom` 相容層，現有節點圖資料須重建。

## Tests And Limits

`Tests/Editor/GraphVerifierTests.cs` covers Catalog compatibility, Prototype
references, Dynamic Catalog read/write ordering, disabled actions, and invalid
Slots. `GraphKitCrossToolSessionTests.cs` verifies that the package consumes
GraphKit's public session API. Run both as EditMode tests in Unity Test Runner.

執行期仍使用 static `AssetPipeline.current` 與 report handler，因此不支援同時
執行多條管線。
