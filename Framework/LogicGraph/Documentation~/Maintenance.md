# LogicGraph 維護手冊

給 AI Agent 與維護者。修改 LogicGraph、新增 Action／Formula／公式族，或在使用端串接它之前先讀完本檔，並遵守其中的規則。

## 0. 使用本手冊的規則

- 本檔只寫**現在成立**的事實與規則。程式碼是行為的證據；本檔與程式不一致時，先確認實際行為，再在同一個 commit 修正本檔或程式。
- 改到本檔描述的行為、契約、檔案位置或限制時，**同一個 commit 更新本檔**。沒有更新手冊的行為修改視為未完成。
- 歷史與改動理由寫進 commit 訊息或使用端專案的決策紀錄，不寫在這裡。
- LogicGraph 建在 GraphKit 之上。節點載體、Slot 基底、編輯器與 DeepCopy 的規則以 GraphKit 手冊（`DependencyCore/GraphKit/Documentation~/Maintenance.md`）為準，本檔不重複。

## 1. 定位與邊界

LogicGraph 是**依時機分派、非同步執行**的 Action／Formula 圖框架。它提供時機分派、泛型 Action／Formula／Slot、可重用的圖資產、具名 Token、Property 的執行期儲存、驗證、深層複製與描述編譯。它**不定義任何領域型別、時機值或節點行為**。

| 組件 | 平台 | 引用 |
|---|---|---|
| `HaruFamily.Framework.LogicGraph` | 全部 | UniTask、GraphKit |
| `HaruFamily.Framework.LogicGraph.Editor` | Editor | LogicGraph、GraphKit、GraphKit.Editor |
| `HaruFamily.Framework.LogicGraph.Editor.Tests` | Editor | 上述與 UniTask |

硬性邊界：

1. LogicGraph 不得引用任何使用端（遊戲專案）型別。
2. GraphKit 不得反過來引用 LogicGraph。LogicGraph 需要但 GraphKit 沒有的能力，先判斷是不是兩個使用端（LogicGraph、AssetPipeline）都需要：都需要才在 GraphKit 補非泛型契約，否則留在 LogicGraph。
3. Core 不得為編輯器保留反向委派 hook；Editor 組件單向呼叫 Runtime。
4. 不引入 service locator 或腳本語言模型。

## 2. 目錄地圖

| 路徑 | 內容 |
|---|---|
| `Runtime/Engine/` | `LogicGraph<TTiming, TPack>`（本體、`.Compile`、`.Editor` 三個 partial）、`ActionTimingGroup`、`LogicGraphUsage`、`ILogicGraphTimingOwner`、`CompilePass` |
| `Runtime/Action/` | `ActionBase<TPack>`、`ActionAssetBase<TPack>`、`ActionSlot<TPack>`、`PropertySlot<TResult, TSlot>`／`SetPropertySlot<TResult, TSlot>` |
| `Runtime/Formula/` | `FormulaBase<TResult, TPack>`、`FormulaAsset<TResult, TPack>`、`FormulaSlot<TResult, TAsset, TFormula, TPack>` |
| `Runtime/Token/` | `TokenTable<TPack>`（具名求值、資產參數 scope、取消與觀察） |
| `Runtime/Property/` | `PropertyStore`（Property 目前值的執行期儲存，internal） |
| `Editor/` | `LogicGraphEditor`（正式入口與驗證）、`LogicGraphDrawer`（Inspector 卡片）、`LogicGraphAutoVerifySweep`、`FormulaKindScaffolder` |
| `Editor/Tests/` | `GraphDeepCopyTests`、`LogicGraphExecutionTests`、`LogicGraphValidationTests`、`LogicGraphUsageTests` |

`LogicGraph.Editor.cs` 整檔包在 `#if UNITY_EDITOR`，但必須留在 Runtime 組件，因為它是 `LogicGraph<,>` 的 partial。

## 3. 核心模型

### 3.1 圖與時機

- `LogicGraph<TTiming, TPack>` 實作 GraphKit 的 `IGraphDocument`，能力是共用資產、Token、Property 三種全開。
- 一個時機是一顆 `ActionTimingGroup<TTiming, TPack>` root，本體是有順序的 `ActionSlot<TPack>` 清單。所有時機共用一張畫布，候選池在 `LogicGraph.Orphans`。
- `TriggerAction(timing, pack[, cancellationToken, executionName])` 每次建立新的 `TokenTable`，依清單順序 await 每個動作。
- `LogicGraph<,>`、`FormulaAssetBase`、`ActionAssetBase` 實作 GraphKit 的 `IGraphViewStateOwner`，`_viewState` 存節點圖的收合版面（規則見 GraphKit 手冊硬規則 13）。它只給編輯器用，不參與執行、驗證與 `InvalidateValidation()`。

### 3.2 Action 與 Formula

- Action 繼承 `ActionBase<TPack>`，覆寫 `protected OnExecute(pack, tokens)`；Formula 繼承 `FormulaBase<TResult, TPack>`，覆寫 `protected OnEvaluate(pack, tokens)`。本體的 `Execute`／`Evaluate` 是 internal 派發入口，作者不直接呼叫。
- 組合節點透過公開的 Slot API 呼叫子節點：`await childFormula.Evaluate(pack, tokens)`、`await childAction.Execute(pack, tokens)`。**永遠把收到的 `pack` 和 `tokens` 往下傳**；Slot 負責停用處理、保底值、作用域和執行觀察。
- 長時間的非同步工作要傳入 `tokens.CancellationToken`。
- 每個具體節點都要標 `[HGNode]`，否則節點名退回類別名。

### 3.3 公式族

- 一個族＝三個型別：Formula 基底（計算）、`FormulaAsset<TResult, TPack>` 子類（可重用資產）、`FormulaSlot<TResult, TAsset, TFormula, TPack>` 子類（欄位與族身分）。
- **族身分是具體 Slot 型別**，不是結果型別。兩個都回 `string` 的 Slot 是兩個族，各自可以有同名 Token。
- 新增結果種類只要新增這三個型別，編輯器掃描 `FormulaSlotBase` 子類自動列出，不需要登記。`PinTools/LogicGraph/Add Formula Type` 可產生樣板。同一族的新計算只要再繼承該族的 Formula 基底，**不要再產生一個族**。
- Slot 建構子只有 `XSlot()` 和 `XSlot(TResult defaultValue)`。有沒有接來源由 `_node` 決定。
- 跨族接收：Slot 覆寫 `AllowCompatibleResult` 回 true 後，可額外接受「Pack 相同、結果型別可指派給本族」的其他族公式，並以 `ExcludedFormulaFamilies` 排除特定族（連同子類）。同族永遠優先且不受黑名單影響。不做數值轉換或向下轉型。Token、Property、Formula Asset 不受此開關放寬。

### 3.4 Token 與資產參數

- Token 是**具名的計算**，不是快取變數：每次查詢都重新求值，隨機公式每次結果不同。不做記憶化。
- `TokenTable` 的鍵是（族, 名稱）。外部查詢一律帶族：`await tokens.Resolve<int>(typeof(MyIntSlot), "Amount", pack)`。
- 共用資產（Formula Asset／Action Asset）的 Token 清單就是它的參數介面。呼叫端節點的 `Bindings`（`NamedFormulaSlot`）依（族, 名稱）配對，`OverrideEnabled` 為 false 時用資產內部值。
- 資產建立自己的 child `TokenTable`，資產內容讀不到呼叫端的 Token，只有 binding 在呼叫端求值。
- 遞迴解析由 `TokenTable` 擋下，回預設值並警告。

### 3.5 Property

- Action 以 `PropertySlot<TResult, TSlot>` 宣告寫入端（例如 `PropertySlot<string, KeySlot>`），呼叫 `Write(value, tokens)` 替換目前值。欄位用一般內嵌序列化，**不要在封閉泛型欄位上加 `[SerializeReference]`**。泛型約束不會證明 TResult 與 TSlot 的結果型別一致，宣告時要自己配對。
- 目前值存在非序列化的 `PropertyStore`：每個圖實例一個 root scope，資產呼叫依（資產 × 呼叫節點）建立子 scope。同一位置再次執行沿用舊值。
- **只有 `InitializeProperties()` 會清空目前值。** `InvalidateValidation()` 只影響編輯與驗證狀態。`DeepCopy()` 不複製執行狀態，複本從未寫入開始。
- 讀取 Property 只取目前值，不執行寫入它的 Action；寫入端不是求值依賴。

## 4. 硬規則

1. **序列化身分是資料契約**（類別名、命名空間、組件名）。改之前先掃使用端資產裡實際的 `{class, ns, asm}` 記錄，並取得擁有者同意。
2. **`ActionSlot._disabled` 等反向旗標不可改名成 `_enabled`。**
3. `FormulaSlotBase`／`ActionSlotBase` 在 GraphKit，泛型的 `FormulaSlot<,,,>`／`ActionSlot<>` 在本套件，兩者**分檔**，基底維持零欄位。
4. **未驗證的圖不執行。** `TriggerAction` 與 `CreateTokenTable` 在 `_validated` 為 false 時記錄錯誤並跳過；runtime 不可自行補驗證。`MarkValidated()` 只能用在程式建立且已自行保證正確的圖。
5. 程式修改圖之後呼叫 `InvalidateValidation()`，再用 Inspector 或 Editor-only 的 `LogicGraphEditor.Verify(owner)` 驗證。無參數的 `graph.Verify()` 只驗 Core 規則，拿不到 Owner 的 Usage。
6. **驗證有兩趟**：第一趟不穿透停用節點（殘缺是錯誤），第二趟補走停用子樹（殘缺降為警告）。順序不可顛倒；只要還有一條啟用路徑指著同一個載體，殘缺仍是錯誤。GraphKit 的 `HGValidator` 要同步同一套判準。
7. **不要把 Node 的序列化欄位當跨呼叫的快取**；每次執行的上下文放在 Pack。
8. 缺來源或停用的公式回 Slot 保底值；**節點本體丟出的例外照常往外傳**，保底值不是通用例外處理。runtime 遇到型別不符或空節點時，每個 Slot 只警告一次並回保底值。
9. Usage 的 `ConfigureGraph` 與驗證 callback 只宣告規則，**不可修改 Owner、圖或資產**；驗證 callback 使用傳入的文件，不讀回 Owner 上的圖。
10. 候選池（`Orphans`）不執行、不驗證、不算資產引用。正式資料的邊界是動作樹加上每個 Token 的取值子樹。

## 5. 串接（給使用端）

1. 定義時機 enum 與執行上下文型別（`TPack`）。
2. 在 Owner 放一個序列化欄位 `LogicGraph<TTiming, TPack>`，不需要實作任何 Owner 介面。Inspector 會出現開圖與驗證卡片。
3. 需要限制時機或加領域驗證時，Owner 實作 `ILogicGraphUsage<TTiming, TPack>`（`ConfigureGraph` 包在 `#if UNITY_EDITOR`）：
   - `AllowTimings`：null＝全部，空集合＝一個都不允許；同時影響建立選單與驗證。
   - `RequireToken(s)`：宣告圖外會用名字查詢的 Token。
   - `AddValidation((graph, report) => ...)`：用 `report.Error(code, message, fieldPath)`／`Warning` 回報。
4. 執行時用 `definition.graph.DeepCopy()` 建立 runtime 副本，再 `await copy.TriggerAction(timing, pack, cancellationToken, name)`。
5. 執行對象釋放時呼叫該副本的 `CancelObservedExecutions()`；取消會傳到 await 的呼叫端，呼叫端要自己收尾。

一個 Owner 有多個 LogicGraph 欄位時，要從 Inspector 按鈕或明確 binding 開啟，選單入口只接受唯一文件欄位。`LogicGraphAutoVerifySweep` 在離開 Edit Mode 時重驗所有 Owner，只記錄錯誤、不擋 Play。

## 6. 常見修改

### 新增 Action

```csharp
[Serializable]
[HGNode("記錄數值", "計算 Amount 並寫到 Console", "範例")]
public sealed class LogAmountAction : ActionBase<MyPack>
{
    [HGLabel("數值")]
    public MyIntSlot Amount = new MyIntSlot(1);

    protected override async UniTask OnExecute(MyPack pack, TokenTable<MyPack> tokens)
    {
        int amount = await Amount.Evaluate(pack, tokens);
        Debug.Log(amount);
    }
}
```

固定設定用一般序列化欄位；要讓圖提供常數、計算、資產或 Token 時才用 Slot。

### 新增 Formula

繼承既有族的 Formula 基底並覆寫 `OnEvaluate`，加 `[Serializable]` 與 `[HGNode]`。不需要登記。

### 新增公式族

新增 Formula 基底、`FormulaAsset` 子類、`FormulaSlot` 子類三個型別（或用 `PinTools/LogicGraph/Add Formula Type`）。Slot 標 `[HGKind(name, group, priority)]` 控制在建立選單中的名稱與位置。確認沒有另一個族已經代表同一種語意。

### 改 Core

- [ ] 沒有引用使用端型別；GraphKit 沒有因此需要引用 LogicGraph
- [ ] Slot 基底仍是零序列化欄位
- [ ] 驗證規則改動同步到 GraphKit `HGValidator`
- [ ] 求值路徑保留停用、保底、觀察、例外與取消流程
- [ ] 新增或改名的 `.cs` 都有 `.meta`
- [ ] README 與本手冊已同步

## 7. 執行觀察

`LogicGraph<,>` 實作 GraphKit 的 `IGraphExecutionDocument`。`DeepCopy()` 共用觀察 source 並保留當下的內容版本；`InvalidateValidation()` 只推進該文件的版本。每次有觀察者的 `TriggerAction` 建立一個 session；`ActionSlot.Execute`／`FormulaSlot.Evaluate` 在有效且未停用的載體入口建立 visit，**先 await Hold 再執行內容**，成功、例外、取消都回報。資產 scope 為 `asset:<instance id>`；binding 在呼叫端 scope 求值。獨立的 `CreateTokenTable` 查詢不算時機 session。

## 8. 描述編譯

`Compile(template, pack, passes)` 依呼叫端傳入的 `ICompilePass<TPack>` 順序處理 `CompileContext`。Core 不規定模板語法；未驗證的圖取到空 Token 表，pass 查不到值就保留原文。

## 9. 驗證

- `GraphDeepCopyTests`：複製、Token 重新求值、binding、公式保底。
- `LogicGraphExecutionTests`：執行、資產根停用、觀察、Hold、取消、作者 API 邊界、Property 寫入與跨族求值。
- `LogicGraphValidationTests`：結構化診斷、legacy Owner 整合、Property 讀寫與變數庫驗證。
- `LogicGraphUsageTests`：零介面 Owner、Usage 規則、指定欄位與批次驗證、預設 context 提交、共用資產重驗。
- 在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.framework.logicgraph` 加進 `manifest.json` 的 `testables`。**沒有實際執行的測試不可回報為通過。**
