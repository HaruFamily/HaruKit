# HaruKit Tools AssetPipeline

Editor-only UPM package for ordered asset processing. It has no Runtime assembly
and does not enter player builds.

The package is a **framework only**. It provides the pipeline asset, the asset
group model, the formula families, and the validator. The concrete steps and
formulas that actually touch your project live in the consuming project, so
adding a pipeline step never requires editing this package.

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

起點：在 `AssetPipeline` 設定 Prototype 資產群組與節點圖上的步驟清單。

前一步：節點圖卡片上的「驗證」檢查兩層。節點圖層（`APGraphVerifier`）看步驟內容完不完整、
公式型別相不相容、具名變數有沒有重名或循環，以及 **dynamic key 的產出者有沒有
排在讀取者之前**；資產群組層看 prototype key 有沒有對應群組、群組是不是空的。
通過後建立目前序列化資料的驗證快照。

當前：「執行管線」僅在快照仍有效時啟用；確認 dialog 通過後，嚴格依節點圖 root
底下的步驟順序執行。

下一步：步驟可讀 Prototype／Dynamic key；產出 dynamic key 的步驟實作
`IDynamicKeyProducer`，讀取的公式實作 `IDynamicKeyReader`。

終點：依 `dynamicClearTiming` 保留或清除 Dynamic 資產，儲存修改並顯示 log。

## Editing

節點圖由 GraphKit 提供。`AssetPipeline.graph` 是一個實作 `IGraphDocument` 的
序列化欄位，編輯器靠這個介面認出它，不認識 AssetPipeline 任何型別。

Inspector 把這個欄位畫成一張卡片（`APGraphDrawer`）：左緣色條是驗證狀態，卡上
只有「開啟節點圖編輯器」與「驗證」兩個入口，圖的內容不在 Inspector 展開。資產
群組與維護操作收在下方的折疊分區裡。

一個欄位（`FormulaAsset_*`）可以是常數、接一顆內嵌公式節點，或指向一個具名變數。
舊版的 `data` / `assetData` 三態由 `GraphNode.Kind` 取代；舊的 AssetSource 模式
改成「接一顆讀 `AssetPipelineSource` 的葉節點公式」。

順序仍然是唯一真相：節點圖只換了編輯方式，步驟依然嚴格照清單順序跑。

## Extending

新增一個步驟或公式**不需要改這個套件**。在使用端專案的 Editor 資料夾裡：

```csharp
using HaruFamily.Framework.LogicGraph;
using HaruFamily.Tools.AssetPipeline;

[HGNode("我的步驟", "做一件事", "步驟")]
[Serializable]
public class MyStep : APActionBase
{
    public FormulaAsset_ObjectList targets = new FormulaAsset_ObjectList();

    public override void Execute()
    {
        foreach (var target in targets.Evaluate()) { /* … */ }
    }
}
```

- 步驟繼承 `APActionBase`；產出 dynamic key 的再實作 `IDynamicKeyProducer`。
- 公式繼承對應族的基底（`Formula_Int`、`Formula_Object`…）；讀 key 的再實作
  `IPrototypeKeyReader` / `IDynamicKeyReader`，驗證器才看得到它讀了什麼。
- `AssetPipeline.current`、`Report`、`ReportFormulaWarning`、`RegisterDynamicAssets`
  是給步驟用的公開 API。

## Package Contents

- `APNode` / `APSlot`：節點基底、欄位基底、步驟頭端
- `APGraph` / `APGraphVerifier`：`IGraphDocument` 實作與驗證（含 dynamic key 時序）
- `FormulaAsset_*`：族宣告與欄位容器（Bool / Float / Int / String / Folder /
  Object / AudioClip / GameObject / TextAsset）
- `AssetPipeline*`：管線資產、資產群組、群組批次執行、Inspector

## Existing Assets Migration

原始腳本的 `.meta` GUID 保留，`m_Script` 參照仍然有效。但 **v2.0.0 的資料模型
與 v1 不相容**：`IPipelineAsset` 清單換成節點圖、`FormulaAssetBase` 換成
`FormulaSlotBase` 子類。既有的 v1 序列化資料需要重建，沒有自動遷移。

## Current Limit

執行期仍使用 static `AssetPipeline.current` 與 report handler，因此不支援同時
執行多條管線。
