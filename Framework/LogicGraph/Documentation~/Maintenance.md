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
| `Runtime/Formula/` | `FormulaBase<TResult, TPack>`、`FormulaAsset<TResult, TInput, TPack>`、`FormulaSlot<TResult, TFormula, TToken, TAsset, TPack>` |
| `Runtime/Token/` | `FormulaToken<TResult, TInput, TPack>`、`TokenTable<TPack>`（具名求值、資產參數 scope、取消與觀察） |
| `Runtime/Property/` | `PropertyStore`（Property 目前值的執行期儲存，internal） |
| `Editor/` | `LogicGraphEditor`（正式入口與驗證）、`LogicGraphDrawer`（Inspector 卡片）、`LogicGraphAutoVerifySweep`、`FormulaKindScaffolder` |
| `Editor/Tests/` | `GraphDeepCopyTests`、`LogicGraphExecutionTests`、`LogicGraphValidationTests`、`LogicGraphUsageTests` |

`LogicGraph.Editor.cs` 提供共用結構驗證與 Editor 診斷入口，位於 Runtime 組件；僅 Owner Usage 探索受 `UNITY_EDITOR` 限制。`TryValidate(out error)` 在 Player 也可使用，唯讀檢查當下結構，不要求或修改已儲存驗證旗標。

## 3. 核心模型

### 3.1 圖與時機

- `LogicGraph<TTiming, TPack>` 實作 GraphKit 的 `IGraphDocument`，能力是共用資產、Token、Property 三種全開。
- 一個時機是一顆 `ActionTimingGroup<TTiming, TPack>` root，本體是有順序的 `ActionSlot<TPack>` 清單。所有時機共用一張畫布，候選池在 `LogicGraph.Orphans`。
- Timing 的節點標題與 root 選單顯示使用 enum 成員的 `[HGLabel]`，由 HGEnumLabels 共用解析；LogicGraph 實作可選 IGraphRootKeyDisplay 提供未建立時機的名稱。無標記／空白 Name 回成員名，未定義值回原始字串；執行、序列化、允許集合與排序仍使用原 enum 值。同名顯示不合併不同時機。
- `TriggerAction(timing, pack[, cancellationToken, executionName])` 每次建立新的 `TokenTable`，依清單順序 await 每個動作。
- `LogicGraph<,>`、`FormulaAssetBase`、`ActionAssetBase` 實作 GraphKit 的 `IGraphViewStateOwner`，`_viewState` 存節點圖的收合版面（規則見 GraphKit 手冊硬規則 13）。它只給編輯器用，不參與執行、驗證與 `InvalidateValidation()`。

### 3.2 Action 與 Formula

- Action 繼承 `ActionBase<TPack>`，覆寫 `protected OnExecute(pack, tokens)`；Formula 繼承 `FormulaBase<TResult, TPack>`，覆寫 `protected OnEvaluate(pack, tokens)`。本體的 `Execute`／`Evaluate` 是 internal 派發入口，作者不直接呼叫。
- 組合節點透過公開的 Slot API 呼叫子節點：`await childFormula.Evaluate(pack, tokens)`、`await childAction.Execute(pack, tokens)`。**永遠把收到的 `pack` 和 `tokens` 往下傳**；Slot 負責停用處理、保底值、作用域和執行觀察。
- 長時間的非同步工作要傳入 `tokens.CancellationToken`。
- 每個具體節點都要標 `[HGNode]`，否則節點名退回類別名。

### 3.3 公式族

- 一個族的配對正本是 FormulaSlot<TResult, TFormula, TToken, TAsset, TPack>。Formula、Token、Asset 須符合相同 TResult／TPack；具體類別通常與 Slot 同放在 XAsset.cs。族身分仍是具體 Slot 型別。
- **族身分是具體 Slot 型別**，不是結果型別。兩個都回 `string` 的 Slot 是兩個族，各自可以有同名 Token。
- 新增結果種類需 Formula／Token／Asset／Slot 四個型別；編輯器讀 Slot 宣告列出配對，不維護另一張 Token 清單。HaruFamily/LogicGraph/Add Formula Type 產生四件套及 Slot 的 Preserve 標記；同族的新計算只繼承 Formula 基底。
- Slot 建構子只有 `XSlot()` 和 `XSlot(TResult defaultValue)`。有沒有接來源由 `_node` 決定。
- 未接來源的求值走 `protected virtual TResult EvaluateUnlinked(TPack pack)`，預設回 `_default`。具體 Slot 可配合 GraphKit 的 `DefaultEditType`／`DefaultObject`，把序列化選項依當次 Pack 解析成結果。一般 Evaluate、IFormulaSlot、Token 及資產參數共用此入口，不以隱藏 Evaluate 或另一個 Resolve API 分流。此入口只處理 `Node == null`；已接但停用、空節點或來源失敗仍回原本保底值，不改取未接線選項。
- 跨族接收：Slot 覆寫 `AllowCompatibleResult` 回 true 後，可額外接受「Pack 相同、結果型別可指派給本族」的其他族公式，並以 `ExcludedFormulaFamilies` 排除特定族（連同子類）。同族永遠優先且不受黑名單影響。不做數值轉換或向下轉型。Token、Property、Formula Asset 不受此開關放寬。

### 多族輸入 UnionSlot

- `UnionSlot<TResult, TPack, TA, TB>` 提供 2、3、4、5 個來源族的泛型版本，共用求值入口；TA～TE 是具有公開無參數建構子的具體 FormulaSlotBase，同一 Pack、不同族，且實作 `IFormulaSlot<TPack>`。不接受巢狀 Union。
- 使用端以具體可序列化子類實作 `OnEvaluate(pack, tokens)`，對 `SelectedSlot` 分支呼叫其正式 `Evaluate` 並轉為 TResult。外層只做配置及前後取消檢查；不重複 EnterNode，不把 Slot 求值降成裸 Formula 呼叫。
- Union 自己序列化 TResult `_default`，由 Default／DefaultObject 編輯；未接線、Empty 或來源載體停用時直接回此值，不呼叫子類轉換。分支初始化為第一族只為保存來源；SetSelectedSlot 不改 Union 預設值。接線用 SetNode 自動選族，斷線保留 Union 預設值；有效來源仍由內層 Slot 處理 Token／Property／Asset 與來源失敗保底，子類轉換其結果。
- Union 是輸入欄位，不作為新的 Token／Property 宣告族。具體 Token 可宣告 Union 輸入與另一個原生輸出族；TokenTable 以 Token.FamilyType 登記，求值 Token.Slot。資產覆蓋按對外族配對，公式輸出仍由既有 FormulaBase 決定。
- 程式原地替換來源載體內容後，須對受影響 Union 再呼叫 `SetNode(carrier)` 重新選族；編輯器換來源與非視窗 ReplaceSource 在同一交易內完成。配置不一致由 Core／Editor 驗證回報。
- GraphKit 的載體／選族／畫面規則見其維護手冊「多族輸入」。`UnionSlotTests` 涵蓋二族求值、四種來源、五族切換與獨立 TResult 預設值、序列化／DeepCopy、取消與 Editor 換來源／Undo。

### 3.4 Token 與資產參數

- FormulaToken<TResult, TInput, TPack> 與 FormulaAsset<TResult, TInput, TPack> 都由 TInput 指定根輸入，TInput 須實作相同 TResult／TPack 的 IFormulaSlot 並可建立，可為 Union。兩者不宣告 Family 參數；Token 的 OutputSlot 由 GraphKit FormulaFamily 反查 Slot 配對。
- SetSlot 只接受宣告的 TInput；Core／Editor 驗證 ConfigurationError，防止序列化資料繞過 setter。Slot 本身沒有 Token factory，也不覆寫自身 FamilyType。
- FormulaToken.IsObsolete 在輸入不符合 TInput 或 Token 不再被任何 Slot 宣告時成立；配對歧義仍由 ConfigurationError 報錯，不當成可刪資料。普通 GraphToken 若使用已宣告具體 Token 的 Slot，同樣過時。接線、Core 驗證、TokenTable.Register 與資產 scope 都拒絕過時 Token；編輯期由 GraphKit 的工作副本清理流程處理，不自動換成另一個 class。
- FormulaAsset 以 SerializeReference 保存完整 TInput；Root 是 Input.Node，ContentObject 是 Input。未接線求值 Input 的常數／上下文預設，已接來源經 Input 正式 Evaluate。每次呼叫建立資產 Token／Property scope，參數覆蓋與取消沿用既有流程；Slot 負責根節點觀察，不再重複 EnterNode。根可接公式、Asset、Token、Property，仍須通過接收契約與循環驗證。
- 公式資產編輯／Undo／儲存必須複製完整 Input 與庫資料，不能只保存 Root，否則 Union 預設值與設定會遺失。GraphKit 使用 IFormulaGraphAsset；ActionAsset 維持動作根契約。

- Token 是**具名的計算**，不是快取變數：每次查詢都重新求值，隨機公式每次結果不同。不做記憶化。
- `TokenTable` 的鍵是（族, 名稱）。外部查詢一律帶族：`await tokens.Resolve<int>(typeof(MyIntSlot), "Amount", pack)`。
- 共用資產（Formula Asset／Action Asset）的 Token 清單就是它的參數介面。呼叫端節點的 `Bindings`（`NamedFormulaSlot`）依（族, 名稱）配對，`OverrideEnabled` 為 false 時用資產內部值。
- 資產建立自己的 child `TokenTable`，資產內容讀不到呼叫端的 Token，只有 binding 在呼叫端求值。
- 遞迴解析由 `TokenTable` 擋下，回預設值並警告。
- `TokenTable.CreateEvaluationScope(cancellationToken)` 用於延後求值：複製 Token／資產 binding 對照與 caller scope 鏈，沿用相同 PropertyScope，目前值保持即時。每次建立獨立的循環集合與取消 token，不沿用原執行的 observer session／Hold／取消狀態。此入口不延長使用端物件的生命週期；使用端須管理登記、回收、操作取消及等待後的身分檢查，不能直接重用已結束時機的 TokenTable 執行狀態。

### 3.5 Property

- 外部寫入不受保留設定限制。未保留的外部值暫存在圖實例，下一次 TriggerAction 在開始時接收並消耗；執行途中再收到的寫入留給下一次，當前執行不受影響。CreateTokenTable 的獨立查詢取得當時快照，不消耗待執行輸入；明確 InitializeProperties 一併清除待執行值。觸發前已取消或驗證失敗不消耗；已開始的執行在成功、失敗、取消後均清除自己的暫存值。

- 所有定義只在圖／Asset 的 `Properties` 庫，節點只引用。scope 不為未登記引用隱式建值；驗證檢查庫歸屬。新定義 `RetainValue=false`，初始值可明確為 null；此旗標與保存時間獨立。

- Action 以 `PropertySlot<TResult, TSlot>` 宣告寫入端（例如 `PropertySlot<string, KeySlot>`），呼叫 `Write(value, tokens)` 替換目前值。欄位用一般內嵌序列化，**不要在封閉泛型欄位上加 `[SerializeReference]`**。泛型約束不會證明 TResult 與 TSlot 的結果型別一致，宣告時要自己配對。
- 目前值存在非序列化的 `PropertyStore`：每個圖實例一個 root scope，資產呼叫依（資產 × 呼叫節點）建立子 scope。同一位置的保留值跨次沿用。RetainValue=false 時，每次 TriggerAction 使用獨立的暫存 scope；finally 在成功、失敗及取消時清空。CreateEvaluationScope 不延長暫存生命週期，結束後讀回初始值、寫入拒絕；獨立 CreateTokenTable 的暫存值隨表存活。
- `InitializeProperties()` 主動清空保留值與活動表的暫存值。 `InvalidateValidation()` 只影響編輯與驗證狀態。`DeepCopy()` 不複製執行狀態，複本從未寫入開始。
- 圖外要改目前值時呼叫 `LogicGraph.WriteProperty(family, name, value)`：以（族, 名稱）找變數庫定義，RetainValue 不限制寫入，值須可指派給結果型別，只寫 root scope，失敗回 false。`GraphProperty.SetValue` 只改定義物件上的值，LogicGraph 執行期不讀它。
- 讀取 Property 只取目前值，不執行寫入它的 Action；寫入端不是求值依賴。
- `TokenTable.ScopeIdentity` 為 Property scope 的不透明 runtime 身分；同一圖跨次執行及 CreateEvaluationScope 沿用，同資產不同呼叫節點各自獨立。無 Property scope 的手建表有自身身分，延後表沿用該身分；不可序列化此值或把它當作資產 ID。

## 4. 硬規則

1. **序列化身分是資料契約**（類別名、命名空間、組件名）。改之前先掃使用端資產裡實際的 `{class, ns, asm}` 記錄，並取得擁有者同意。
2. **`ActionSlot._disabled` 等反向旗標不可改名成 `_enabled`。**
3. `FormulaSlotBase`／`ActionSlotBase` 在 GraphKit，泛型的 `FormulaSlot<,,,,>`／`ActionSlot<>` 在本套件，兩者**分檔**，基底維持零欄位。
4. **執行檢查實際內容，不依賴編輯器旗標。** `TriggerAction` 與 `CreateTokenTable` 首次使用每份 runtime 圖時執行 Core 結構驗證，快取當次結果。`TriggerAction` 先檢查取消；圖不合法時，每次呼叫均以 `InvalidOperationException` 使 UniTask 失敗，含時機／執行名稱與診斷，不執行動作，也不建立觀察 session。呼叫端須沿用失敗／清理路徑，不得當作動作完成；不保證回滾呼叫前的副作用。獨立 `CreateTokenTable` 失敗只記錄一次錯誤並回空表，供描述等讀值流程保底。DeepCopy 不繼承 runtime 快取。`MarkValidated()` 不可繞過 runtime 檢查。Owner 的領域配置與 Default 回退由使用端載入入口處理。
5. 程式修改圖之後呼叫 `InvalidateValidation()`，同時撤銷編輯器旗標與 runtime 檢查快取。Inspector 與 `LogicGraphEditor.Verify(owner)` 可手動檢查 Usage；`TryValidate` 與無 Owner 的 `Verify()` 只驗 Core 規則。
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

一個 Owner 有多個 LogicGraph 欄位時，要從 Inspector 按鈕或明確 binding 開啟，選單入口只接受唯一文件欄位。`LogicGraphAutoVerifySweep` 僅由 Verify All Owners 選單執行，不在進入 Play 時掃描資產。

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

新增 Formula 基底、`FormulaAsset` 子類、`FormulaSlot` 子類三個型別（或用 `HaruFamily/LogicGraph/Add Formula Type`）。Slot 標 `[HGKind(name, group, priority)]` 控制在建立選單中的名稱與位置。確認沒有另一個族已經代表同一種語意。

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

`Compile(template, pack, passes)` 依呼叫端傳入的 `ICompilePass<TPack>` 順序處理 `CompileContext`。Core 不規定模板語法；Core 結構不合法的圖取到空 Token 表，pass 查不到值就保留原文。

## 9. 驗證

- `GraphDeepCopyTests`：複製、Token 重新求值、binding、公式保底。
- `LogicGraphExecutionTests`：執行、資產根停用、觀察、Hold、取消、作者 API 邊界、Property 寫入與跨族求值。
- `LogicGraphValidationTests`：結構化診斷、legacy Owner 整合、Property 讀寫與變數庫驗證。
- `LogicGraphUsageTests`：零介面 Owner、Usage 規則、指定欄位與批次驗證、預設 context 提交、共用資產重驗。
- 在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.framework.logicgraph` 加進 `manifest.json` 的 `testables`。**沒有實際執行的測試不可回報為通過。**

> Trace: verified 2026-10-29 · doc `Doc/2026-10-2/DECISION_Property單一模型與資料保留.md`（原始碼靜態核對；使用者確認本輪 TestRunner 修正，驗證範圍見決策文件）
