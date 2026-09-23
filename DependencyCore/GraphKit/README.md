# GraphKit

目前套件版本：`1.1.0`（Unity 2021.3 以上）。

> **使用 AI Agent 修改或串接本套件前，請要求它先閱讀並遵守 [`Documentation~/Maintenance.md`](Documentation~/Maintenance.md)（維護手冊）。**
> 可以直接對 Agent 說：「先讀 GraphKit 的維護手冊，照裡面的規則做。」
> 透過 UPM 安裝時，手冊位於 `Library/PackageCache/com.harufamily.dependencycore.graphkit@<hash>/Documentation~/Maintenance.md`。`Library/` 通常被 `.gitignore` 排除，Agent 的搜尋工具可能找不到，請把路徑直接告訴它，或把下方的[路由範本](#給-agent-的路由範本)貼進專案的 `CLAUDE.md`／`AGENTS.md`。

GraphKit 是**沒有領域語意**的序列化節點圖框架。它提供節點載體、圖層契約、`[HG*]` 節點屬性、深層複製、診斷資料，以及完整的 IMGUI 節點編輯器。它**不排程、不求值節點**；可選的執行觀察只記錄使用端回報的執行狀態。

任何領域都可以用一個序列化欄位或明確的 `HGDocumentBinding<TDocument>` 暴露 `IGraphDocument` 來使用這個編輯器。目前的使用端是 [LogicGraph](../../Framework/LogicGraph) 與 [AssetPipeline](../../Tools/AssetPipeline)，但兩者都不是必要依賴。

## 功能

- **單一節點載體 `GraphNode`**：保存 Id、座標、備註、停用狀態，以及五種內容之一：空、內嵌內容、共用資產、具名 Token、Property。換來源時保留 Id、座標和所有連入線。
- **具名 Token `GraphToken`**：有自己的畫布與候選池；引用它的節點存物件參照，不存名字字串。
- **Property `GraphProperty`**：圖內的可寫儲存位置。ProtoProperty 放在變數庫，LocalProperty 是節點私有定義；讀取不觸發寫入者，讀後寫回不算循環。
- **非泛型契約**：`IGraphDocument`、`IGraphHead`、`IOrphanPool`、`ITokenOwner`、`IPropertyOwner` 等，讓編輯器不必認識使用端型別。文件以 `HGCapabilities` 宣告要啟用哪些庫。
- **零欄位的 Slot 基底**：`FormulaSlotBase`、`ActionSlotBase`、`PropertySlotBase` 沒有序列化欄位，使用端加泛型子類不會改變序列化格式。
- **公式族**：族身分是具體 Slot 型別；可選擇開放「結果型別相容」的跨族接收，並用黑名單排除。
- **`GraphDeepCopy`**：保留多型 `SerializeReference`、共享參照、循環與 Unity 物件引用。
- **編輯器**：工作副本編輯、存檔前驗證、Undo／Redo、左右可停駐的 Token 庫／變數庫／資產庫、遺失型別自動清理、執行觀察與 Hold。不依賴 Odin。

## Requirements

- Unity 2021.3 或更新版本

不需要其他外部套件。Editor 組件只引用 Runtime 組件。

## Install

在 Unity Package Manager 加入這個 Git URL：

```
https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit
```

或加進 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit"
  }
}
```

需要可重現的建置時，把 URL 釘在 tag 或 commit。

## 組件與命名空間

| 組件 | 平台 | 引用 |
|---|---|---|
| `HaruFamily.DependencyCore.GraphKit` | 全部 | — |
| `HaruFamily.DependencyCore.GraphKit.Editor` | Editor | `HaruFamily.DependencyCore.GraphKit` |

Runtime 型別在 `HaruFamily.DependencyCore.GraphKit`，Editor 型別在 `HaruFamily.DependencyCore.GraphKit.Editor`。**序列化型別的類別名、命名空間與組件名稱都是資料契約**，不要當成一般整理隨手改名。

## 快速開始

### 開啟一份文件

每個文件欄位使用一個型別化 binding，`documentId` 在 Owner 內保持穩定：

```csharp
var binding = new HGDocumentBinding<MyDocument>(
    "MyTool.GraphB",
    owner => ((MyOwner)owner).GraphB,
    (owner, document) => ((MyOwner)owner).GraphB = document,
    MyDocument.Create);

HaruGraphWindow.OpenForDocument(owner, binding, extensions);
```

- 編輯器在清理遺失型別後 DeepCopy 出工作副本；存檔時驗證通過才寫回 Owner，取消則捨棄工作副本。
- `HaruGraphWindow.OpenFor(owner)` 是舊的便利入口，只接受恰好有一個 `IGraphDocument` 欄位的 Owner。
- Setter 只能指派傳入的文件引用。需要偵測「同一個文件實例被外部原地修改」時，在 binding 提供無副作用的 `readRevision`。

### 不開視窗編輯

```csharp
if (HGDocumentSession<MyDocument>.TryOpen(owner, binding, out var session))
{
    var ports = session.CreatePortRegistry();
    ports.AddInput(inputKey, inputSlot, inputPolicy, inputPresentation);
    ports.AddOutput(outputKey, outputSource, outputPolicy, outputPresentation);
    if (session.Connect(ports, outputKey, inputKey) == HGSessionCommandResult.Changed)
        session.Commit();
}
```

registry 只對所屬 session 的目前版次有效；每次成功的命令、Undo、Redo、Commit、Cancel 之後都要重建。操作實際視窗時改用 `window.GetDocumentCommands()` 取得的 `HGWindowSession`。

清單新增：`ports.AddListAppend(key, list, elementType, ownerNode, presentation)` 在清單標題登記新增接點，`session.Connect(ports, outputKey, key)` 會在清單尾端新增一項並接上來源（一步 Undo）。只收元素是 Slot（PropertySlot 除外）的可增刪清單；`ownerNode` 是清單所在節點的載體，用來擋循環，根上的清單傳 `null`。視窗中這顆接點自動出現在 `Query()` 快照，同樣以 `Connect` 使用。

### 擴充點

| 需求 | 入口 |
|---|---|
| 自訂 Port 或 Port 別名 | `IHGEditorExtensionProvider` 與 `HGPortBuildContext` |
| 自訂節點欄位描述 | `IHGEditorMetadataProvider` 與 `HGNodeDescriptor` |
| 自訂值型別的輸入框 | `HGValueDrawer<T>` |
| 領域驗證規則 | `IHGEditorDiagnosticProvider`，或 Runtime Owner 實作 `IGraphDomainDiagnostics` |
| 執行觀察與 Hold | `IGraphExecutionDocument`、`GraphExecutionSession` |

各擴充點的規則與限制見[維護手冊](Documentation~/Maintenance.md) §6。

### 節點屬性

```csharp
[Serializable]
[HGNode("If", "條件為真回 A，否則回 B", "分流")]
public class IntIf : MyIntFormula
{
    [HGLabel("條件"), HGDescription("為真時採用 True 分支")]
    public MyBoolSlot condition = new MyBoolSlot(true);
}
```

`[HGNode]` 不會繼承，每個具體型別都要自己標。Slot 族用 `[HGKind(name, group, priority)]` 決定在建立選單中的名稱、分類與排序。

## 測試

`Editor/Tests/` 涵蓋 Port 模型、公開 API 的非視窗與視窗命令、descriptor 與 drawer、診斷、提交、來源替換、節點刪除，以及執行觀察與 Hold。在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.dependencycore.graphkit` 加進 `manifest.json` 的 `testables`。

手勢、版面與資產序列化來回仍需要在 Unity 專案中實際操作確認。

## 給 Agent 的路由範本

把下面這段貼進使用端專案的 `CLAUDE.md` 或 `AGENTS.md`，Agent 才會在修改前讀到手冊：

```markdown
## HaruKit 套件
修改或串接下列套件前，先讀對應的維護手冊並遵守：
- GraphKit：`Library/PackageCache/com.harufamily.dependencycore.graphkit@*/Documentation~/Maintenance.md`
- LogicGraph：`Library/PackageCache/com.harufamily.framework.logicgraph@*/Documentation~/Maintenance.md`
- AssetPipeline：`Library/PackageCache/com.harufamily.tools.assetpipeline@*/Documentation~/Maintenance.md`
（以 `file:` 或 submodule 引用時，改成實際的套件資料夾路徑。）
```
