# AssetPipeline

目前套件版本：`2.0.0`（Unity 2021.3 以上）。

> **使用 AI Agent 修改本套件、新增管線 Action／Formula，或排查執行結果前，請要求它先閱讀並遵守 [`Documentation~/Maintenance.md`](Documentation~/Maintenance.md)（維護手冊）。**
> 可以直接對 Agent 說：「先讀 AssetPipeline 和 GraphKit 的維護手冊，照裡面的規則做。」
> 透過 UPM 安裝時，手冊位於 `Library/PackageCache/com.harufamily.tools.assetpipeline@<hash>/Documentation~/Maintenance.md`。Agent 的搜尋工具可能略過 `Library/`，請把路徑直接告訴它，或使用 [GraphKit README 的路由範本](../../DependencyCore/GraphKit/README.md#給-agent-的路由範本)。

Editor-only 的資產處理管線框架。用 GraphKit 節點圖排出一串動作，依序同步執行；所有資產寫入共用一份交易，任何一步失敗就整次回復。沒有 Runtime 組件，不會進入 player build。

本套件**只提供框架**：管線資產、同步 Action／Formula 基底、公式族與輸入欄位、Property 寫入欄位、驗證、交易與結果檢視。碰到專案資產的具體步驟與公式寫在使用端專案，新增步驟不需要修改本套件。

## Requirements

- Unity 2021.3 或更新版本
- [GraphKit](../../DependencyCore/GraphKit)：
  `https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit`

UPM 無法從本套件的 manifest 解析 GraphKit 這個 Git 依賴，請先安裝，否則會直接編譯失敗。

不需要其他外部套件。Addressables **不是**依賴；Addressables 相關的節點屬於使用端。

執行本套件的 EditMode 測試時，專案還需要安裝 [LogicGraph](../../Framework/LogicGraph) 與 UniTask（測試組件引用它們）。

## Install

在 Unity Package Manager 加入這個 Git URL：

```
https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline
```

或加進 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.tools.assetpipeline": "https://github.com/HaruFamily/HaruKit.git?path=/Tools/AssetPipeline"
  }
}
```

選單 `HaruFamily/Asset Pipeline/Open` 會建立或選取預設資產 `Assets/Editor/HaruFamily/AssetPipeline/AssetPipeline.asset`。

## 使用流程

1. 選取 AP 資產，在 Inspector 的圖卡片按「開啟節點圖編輯器」。
2. 在管線的動作清單加入動作，並替各欄位指定來源：常數、內嵌公式、具名 Token，或 Property。
3. 產出資產的動作把結果寫進 Property；後面的動作欄位接同一顆 Property 讀取。
4. 存檔後回到 Inspector，在圖卡片按「驗證」。**改過圖或欄位都要重新驗證**，否則執行按鈕會鎖住。
5. 按「執行管線」並確認。執行使用已儲存的圖，執行前會再驗證一次，依動作清單順序同步執行。
6. 結果面板逐步列出狀態、耗時、訊息與資產；「定位」跳回對應節點，最後列出每顆 Property 的值快照。

### 結果與回復

- 每步的狀態是成功、跳過、部分完成、失敗或未執行。失敗或部分完成時停止後續步驟，並回復這次執行的所有資產修改與 Property 值。
- 執行中出現的 Error／Exception／Assert log 也會讓該步失敗。
- 回復失敗時會保留 `Library/AssetPipelineTransactions/` 裡的日誌並在 Inspector 顯示重試按鈕；未處理的日誌會擋住下一次執行。
- Property 的目前值不存檔：同一次 Editor session 內跨次執行保留，Domain Reload 後回到未寫入狀態。

### 遺失的節點型別

節點類別被刪除或改名時，開啟圖會自動清除遺失的內容與因此失效的連線，不備份、不詢問。清理後仍須通過正常驗證才能存檔。Inspector 偵測到遺失型別時會顯示提示與「開啟節點圖修正並存檔」按鈕。

## 擴充

新增動作或公式都寫在使用端專案的 Editor 資料夾：

```csharp
using System;
using System.Collections.Generic;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Tools.AssetPipeline;
using Object = UnityEngine.Object;

[Serializable]
[HGNode("篩選名稱", "只保留名稱包含指定文字的物件", "ObjectList")]
public class FilterByName : Formula_ObjectList<NullPack>
{
    public ObjectListSlot source = new ObjectListSlot();
    public StringSlot text = new StringSlot();

    protected override List<Object> OnEvaluate(NullPack pack)
    {
        var result = new List<Object>();
        string keyword = text.Evaluate() ?? string.Empty;
        foreach (Object item in source.Evaluate() ?? new List<Object>())
            if (item != null && item.name.Contains(keyword)) result.Add(item);
        return result;
    }
}

[Serializable]
[HGNode("記錄物件", "把物件清單寫進 Property", "動作")]
public class RecordObjects : ActionBase
{
    public ObjectListSlot targets = new ObjectListSlot();
    public PropertySlot<List<Object>, ObjectListSlot> output = new();

    protected override void OnExecute(PipelineActionContext context)
    {
        List<Object> items = targets.Evaluate();
        if (items == null || items.Count == 0) { context.Skip("沒有目標。"); return; }
        output.Write(items);
        foreach (Object item in items) context.Result.Record(PipelineItemStatus.Collected, item);
    }
}
```

- 動作繼承 `ActionBase`，覆寫同步的 `OnExecute(PipelineActionContext context)`。**資產修改一律經由 `context.Assets`**（`CopyPrefab`、`EditPrefab`、`EditAssetSet`、`CreateAssetSet`）才會納入交易；失敗用 `context.Fail(...)`，正常無事可做用 `context.Skip(...)`。
- 組合動作宣告 `public ActionSlot child = new();`，在 `OnExecute` 裡呼叫 `child.Execute(context)`；子動作的寫入與結果歸入同一步、同一份交易。
- 公式繼承對應族的 NullPack 版本（例如 `Formula_Int<NullPack>`），只覆寫 `OnEvaluate(pack)`。輸入欄位的 `Evaluate()` 自動提供 NullPack。
- 每顆公式只有一種結果型別；`List<AudioClip>` 不會自動當成 `List<Object>`。`ObjectSlot` 例外，可接受結果是任何 Unity Object 子類的公式。
- 寫出資料用 `PropertySlot<TResult, TSlot>.Write(value)`，只替換目前值。
- 需要在執行中建立資產的公式，只在 `AssetPipeline.CurrentAction` 不為 null 時透過它的 `Assets` 建立；預覽時不可產生副作用。
- `AssetPipeline.Report` 寫入該步訊息，`AssetPipeline.ReportFormulaWarning` 寫入公式警告。

## 內建公式族

| 族 | 輸入欄位 |
|---|---|
| Bool | `BoolSlot` |
| Int／List Int | `IntSlot`、`ListIntSlot` |
| Float／List Float | `FloatSlot`、`ListFloatSlot` |
| String／String List | `StringSlot`、`StringListSlot` |
| Folder | `FolderSlot`（常數可以是資料夾資產或路徑字串） |
| Object／Object List | `ObjectSlot`、`ObjectListSlot` |
| AudioClip／List | `AudioClipSlot`、`AudioClipListSlot` |
| GameObject／List | `GameObjectSlot`、`GameObjectListSlot` |
| TextAsset／List | `TextAssetSlot`、`TextAssetListSlot` |

本套件只提供族與欄位，具體公式由使用端撰寫。

## 套件內容

- `AssetPipeline*`：管線資產、執行流程、Inspector
- `Graph`／`GraphDrawer`／`GraphVerifier`／`GraphPropertyWalk`：`IGraphDocument` 實作、圖卡片、驗證、Property 收集
- `Node`／`Slot`／`PropertySlot`：同步 Action／Formula 基底、輸入與動作欄位、Property 寫入欄位
- `Formula_*`：公式族與輸入欄位
- `PipelineResult`／`PipelineAssetTransaction`：結果、Context、交易與資產寫入器
- `Documentation~/Maintenance.md`：維護手冊

## 舊資料遷移

輸入欄位從 `FormulaAsset_*` 改名為 `*Slot` 時，以 `MovedFrom` 記錄原 namespace／assembly／class，欄位名與腳本 GUID 保留。v2 的資料模型與 v1 不相容（v1 的 `IPipelineAsset` 清單改成節點圖），v1 的序列化資料需要重建，沒有自動遷移。

## 測試與限制

`Tests/Editor/` 涵蓋執行與交易回復、Property 讀寫與回復、驗證、跨工具 session 與庫內順序。在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.tools.assetpipeline` 加進 `manifest.json` 的 `testables`，並安裝 LogicGraph 與 UniTask。

執行期使用 static 的 `AssetPipeline.current` 與回報 handler，**不支援同時執行多條管線**。交易只涵蓋經由 `context.Assets` 登記的寫入，不攔截任意 `AssetDatabase`、檔案或第三方回呼的副作用。
