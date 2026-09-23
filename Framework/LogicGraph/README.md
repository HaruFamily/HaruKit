# LogicGraph

目前套件版本：`2.0.0`（Unity 2021.3 以上）。

> **使用 AI Agent 修改本套件、新增 Action／Formula 或串接前，請要求它先閱讀並遵守 [`Documentation~/Maintenance.md`](Documentation~/Maintenance.md)（維護手冊）。**
> 可以直接對 Agent 說：「先讀 LogicGraph 和 GraphKit 的維護手冊，照裡面的規則做。」
> 透過 UPM 安裝時，手冊位於 `Library/PackageCache/com.harufamily.framework.logicgraph@<hash>/Documentation~/Maintenance.md`。Agent 的搜尋工具可能略過 `Library/`，請把路徑直接告訴它，或使用 [GraphKit README 的路由範本](../../DependencyCore/GraphKit/README.md#給-agent-的路由範本)。

LogicGraph 是用來編寫與執行序列化 Action 圖的框架：依時機分派非同步動作、型別化公式、具名 Token、可重用的圖資產、Property、驗證、深層複製，以及描述編譯。它**不定義領域型別、時機值或節點行為**，這些由使用端提供。

## 功能

- `LogicGraph<TTiming, TPack>` 依使用端定義的時機 enum 與執行上下文分派非同步動作。
- `ActionBase<TPack>` 執行副作用；`FormulaBase<TResult, TPack>` 非同步計算型別化的值。
- Slot 可以是常數保底值、內嵌節點、可重用資產、具名 Token（公式），或 Property（讀目前值）。
- Action 以 `PropertySlot<TResult, TSlot>` 寫入 Property；讀取不會觸發寫入者。
- 資產的 Token 就是它的參數；每次呼叫資產都有獨立的 Token 作用域，可由呼叫端綁定覆蓋。
- 驗證在執行前擋下無效來源、重複時機或 Token 名稱、不相容的綁定，以及圖或資產循環。
- `DeepCopy()` 保留多型 `SerializeReference`、共享參照、循環與 Unity 物件引用。
- 節點編輯器來自 GraphKit，不依賴 Odin。所有時機在同一張畫布，各是一顆 root。

## Requirements

- Unity 2021.3 或更新版本
- [GraphKit](../../DependencyCore/GraphKit)：
  `https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit`
- [UniTask](https://github.com/Cysharp/UniTask)：
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

UPM 無法從本套件的 manifest 解析這兩個 Git 依賴，請先安裝。缺少時 Unity 會直接編譯失敗，而不是靜默停用功能。

GraphKit 負責節點載體、編輯器契約、節點屬性、深層複製與節點編輯器；LogicGraph 加上時機分派、非同步執行，以及泛型的 Action／Formula Slot 與資產。兩者的命名空間刻意分開：

```csharp
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
```

## Install

在 Unity Package Manager 加入這個 Git URL：

```
https://github.com/HaruFamily/HaruKit.git?path=/Framework/LogicGraph
```

或加進 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.framework.logicgraph": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/LogicGraph"
  }
}
```

需要可重現的建置時，把 URL 釘在 tag 或 commit。

## 選擇擴充點

| 目標 | 從哪裡開始 | 需要知道的事 |
|---|---|---|
| 新增副作用 | `ActionBase<TPack>.OnExecute` | Pack、子 Slot、取消 |
| 在既有族加一種計算 | 該族的 Formula 基底與 `OnEvaluate` | 結果型別與子 Slot |
| 新增公式族 | Formula／FormulaAsset／Slot；`PinTools/LogicGraph/Add Formula Type` | 族身分是具體 Slot 型別，不只是結果型別 |
| 讓動作寫出值給後面讀 | `PropertySlot<TResult, TSlot>.Write(value, tokens)` | 只替換目前值；只有 `InitializeProperties()` 會清空 |
| 把圖接進遊戲 | [完整範例](#完整範例) | Owner、驗證、runtime 副本、觸發 |
| 限制時機或加領域驗證 | `ILogicGraphUsage<TTiming, TPack>` | [可選的使用規則](#從一個欄位開始) |
| 自訂值型別的輸入框 | GraphKit 的 `HGValueDrawer<T>` | Editor-only drawer 與明確的 Tool context |

一般的 Action／Formula 擴充不需要自訂 Port、文件 session 或編輯器 binding，那些屬於 [GraphKit](../../DependencyCore/GraphKit) 的進階 Tool 整合。

## 完整範例

這個範例把呼叫端提供的基礎值加上圖裡設定的加成後輸出到 Console，用到的型別全部自己定義：一個時機、一個 Pack、一個公式族、一個 Formula、一個 Action、一個資產 Owner，以及一個場景執行器。

安裝依賴後，把下面四個檔案放進 `Assets` 底下的 runtime 資料夾（不是 `Editor`）。如果使用 asmdef，要引用 `HaruFamily.Framework.LogicGraph`、`HaruFamily.DependencyCore.GraphKit` 與 `UniTask`。這些是要複製進專案的原始碼，不是自動安裝的範例。

### 1. 上下文與公式族：`DemoIntAsset.cs`

```csharp
using System;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;

namespace LogicGraphQuickStart
{
    public enum DemoTiming { Activate }

    public sealed class DemoPack
    {
        public int BaseAmount;
    }

    public abstract class DemoIntFormula : FormulaBase<int, DemoPack> { }

    public sealed class DemoIntAsset : FormulaAsset<int, DemoPack> { }

    [Serializable]
    [HGKind("Demo Int")]
    public sealed class DemoIntSlot
        : FormulaSlot<int, DemoIntAsset, DemoIntFormula, DemoPack>
    {
        public DemoIntSlot() { }
        public DemoIntSlot(int value) : base(value) { }
    }
}
```

Pack 是這次執行的上下文，由呼叫端傳入，不寫在圖裡。TokenTable 負責具名公式查詢、資產參數作用域，以及取消與執行觀察。

三個族型別各有分工：Formula 是計算基底，FormulaAsset 支援可重用的圖資產，Slot 是作者看到的欄位與族身分。這個範例不需要建立 `DemoIntAsset` 實例。之後這個族要加新的計算時，繼承 `DemoIntFormula` 即可，**不要再產生一個族**。編輯器會自動找到具體的 Slot 族，不需要另外登記。

### 2. 節點：`DemoNodes.cs`

```csharp
using System;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace LogicGraphQuickStart
{
    [Serializable]
    [HGNode("Base plus bonus", "Adds an authored bonus to the caller's base amount.", "Quick Start")]
    public sealed class BasePlusBonusFormula : DemoIntFormula
    {
        [HGLabel("Bonus")]
        public DemoIntSlot Bonus = new DemoIntSlot(2);

        protected override async UniTask<int> OnEvaluate(
            DemoPack pack, TokenTable<DemoPack> tokens)
        {
            return pack.BaseAmount + await Bonus.Evaluate(pack, tokens);
        }
    }

    [Serializable]
    [HGNode("Log amount", "Evaluates Amount and writes it to the Unity Console.", "Quick Start")]
    public sealed class LogAmountAction : ActionBase<DemoPack>
    {
        [HGLabel("Amount")]
        public DemoIntSlot Amount = new DemoIntSlot(1);

        protected override async UniTask OnExecute(
            DemoPack pack, TokenTable<DemoPack> tokens)
        {
            int amount = await Amount.Evaluate(pack, tokens);
            Debug.Log($"LogicGraph amount: {amount}");
        }
    }
}
```

固定設定用一般序列化欄位；要讓圖能提供常數、計算、資產或 Token 時才用 Slot。**永遠把收到的 `pack` 和 `tokens` 傳給子 Slot**，停用處理、保底值、作用域和執行觀察都由 Slot 負責。巢狀動作用 `ActionSlot<DemoPack>.Execute(pack, tokens)`。自己加入的長時間非同步工作要傳入 `tokens.CancellationToken`。

### 3. 共用範本 Owner：`DemoGraphDefinition.cs`

```csharp
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace LogicGraphQuickStart
{
    [CreateAssetMenu(menuName = "LogicGraph Quick Start/Definition")]
    public sealed class DemoGraphDefinition : ScriptableObject
    {
        [SerializeField] private LogicGraph<DemoTiming, DemoPack> graph = new();

        public LogicGraph<DemoTiming, DemoPack> CreateRuntimeGraph()
            => graph.DeepCopy();
    }
}
```

不需要實作任何 Owner 介面。DeepCopy 保留驗證狀態與內部共享，同時把 runtime 圖和範本隔開。Unity 物件引用（包含被引用的圖資產）仍然共用；複製不會讓無效的範本變成已驗證，也不會複製那些資產。

### 4. 從遊戲觸發：`DemoGraphRunner.cs`

```csharp
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LogicGraphQuickStart
{
    public sealed class DemoGraphRunner : MonoBehaviour
    {
        [SerializeField] private DemoGraphDefinition definition;

        private async void Start()
        {
            if (definition == null)
            {
                Debug.LogError("Assign a DemoGraphDefinition.", this);
                return;
            }

            var runtimeGraph = definition.CreateRuntimeGraph();
            var pack = new DemoPack { BaseAmount = 10 };
            var cancellation = this.GetCancellationTokenOnDestroy();
            try
            {
                await runtimeGraph.TriggerAction(
                    DemoTiming.Activate, pack, cancellation, "Quick Start");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // This runner's lifetime ended; stop its execution chain.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
```

`Start` 是 Unity 事件邊界；一般的遊戲方法應回傳並 await `UniTask`。每次觸發都建立新的 TokenTable，並依清單順序 await 動作。取消的生命週期依你的系統決定，這個範例用執行器被銷毀。

### 5. 編輯、驗證、執行

1. 用 **Create → LogicGraph Quick Start → Definition** 建立資產。
2. 從 Inspector 開啟節點圖，新增 `Activate` 時機。
3. 新增一個動作項目，來源選 **Log amount**。
4. 把 **Base plus bonus** 接到該動作的 `Amount`；`Bonus` 保持 2。
5. 存檔成功後，用 Inspector 的**驗證**按鈕確認已儲存的圖通過驗證。
6. 把 `DemoGraphRunner` 加到場景物件上並指定 definition。
7. 進入 Play Mode，Console 應出現 **LogicGraph amount: 12**。

`Amount` 沒接線時結果是常數 1；接上公式後是呼叫端的 10 加上 Bonus 的 2。沒有訊息時，檢查 definition 是否指定、`Activate` 的動作是否接好並啟用，以及驗證錯誤。未驗證的圖會記錄錯誤並跳過執行，runtime 不會自動修復。Play 中修改範本後要重新進入 Play Mode 才會建立新副本。

### 隨手要記得的規則

- 用程式修改圖之後，呼叫 `InvalidateValidation()`，再透過 Inspector 或 Editor-only 的 `LogicGraphEditor.Verify(owner)` 驗證。不要用 `MarkValidated()` 跳過錯誤。
- Token 是具名的計算，不是快取變數，每次查詢都重新求值（包含隨機公式）。查詢要帶具體的 Slot 型別：`await tokens.Resolve<int>(typeof(DemoIntSlot), "Amount", pack)` 需要圖裡另外有一個該族、名為 `Amount` 的 Token，不是指動作上同名的欄位。
- 缺來源或停用的公式回保底值；節點本體丟出的例外照常往外傳，保底值不是通用的例外處理。
- 每次執行的上下文放在 Pack，不要把節點的序列化欄位當跨呼叫的快取。

## 整合

### 從一個欄位開始

有了時機 enum 與 Pack 型別，Owner 只需要一個序列化欄位，不需要實作介面或轉發 Dirty／Verify：

```csharp
public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private LogicGraph<MyTiming, MyContext> graph = new();
}
```

用 Inspector 的開啟與驗證按鈕操作。預設所有 `MyTiming` 值都可用。需要額外規則時實作一個可選介面：

```csharp
public sealed class RestrictedSkillDefinition : ScriptableObject,
    ILogicGraphUsage<MyTiming, MyContext>
{
    [SerializeField] private LogicGraph<MyTiming, MyContext> graph = new();
    [SerializeField] private string description;

#if UNITY_EDITOR
    public void ConfigureGraph(LogicGraphUsage<MyTiming, MyContext> usage)
    {
        usage.AllowTimings(new[] { MyTiming.BeforeExecute });
        usage.RequireToken("Damage", nameof(description));
    }
#endif
}
```

- `AllowTimings` 同時控制建立選單與驗證。null 表示全部，空集合表示一個都不允許。
- `RequireToken`／`RequireTokens` 宣告圖外會用名字查詢的 Token。
- 自訂檢查用 `usage.AddValidation((graph, report) => ...)`，以 `report.Error(code, message, fieldPath)` 或 `report.Warning(...)` 回報。callback 收到的是正在驗證的文件（可能是工作副本）。
- 設定與驗證**不可修改** Owner、圖或資產；它們每次查詢時重新評估，不序列化進圖。

規則套用到該 Owner 上相同 Timing／Pack 的所有文件。多個圖欄位要從各自的 Inspector 按鈕或明確 binding 開啟。自動探索包含直接序列化欄位（含繼承的 private 欄位），排除非序列化的 runtime 快取。

### Property

```csharp
[Serializable]
[HGNode("記錄目標", "把本次目標寫進 Property")]
public sealed class RememberTarget : ActionBase<MyContext>
{
    public PropertySlot<string, MyKeySlot> output = new();

    protected override UniTask OnExecute(MyContext pack, TokenTable<MyContext> tokens)
    {
        output.Write(pack.TargetId, tokens);
        return UniTask.CompletedTask;
    }
}
```

- `MyKeySlot` 代表你專案中結果為 `string` 的 Slot 族。`Write` 回傳是否真的寫入；沒接 Property、節點停用或族不符時回 false。
- 讀取端的一般 Slot 直接接 Property 節點，取得目前值；未寫入的一般 Property 回 `default(T)`，ProtoProperty 回它設定的初始內容。
- 目前值不序列化。每個圖實例一個儲存範圍，資產呼叫依呼叫位置分開保存；同一位置再次執行會沿用舊值。
- 只有 `InitializeProperties()` 會清空目前值；`InvalidateValidation()` 不會。`DeepCopy()` 的複本從未寫入開始。

### 執行觀察

LogicGraph 實作 GraphKit 的 `IGraphExecutionDocument`。`DeepCopy()` 共用觀察來源並保留內容版本，但不共用執行狀態。每次有觀察者的 `TriggerAction` 建立一個獨立 session；Action／Formula Slot 在執行內容前 await Hold。資產根節點在 `asset:<instance id>` 範圍有自己的 visit，binding 在呼叫端範圍求值。

用 `TriggerAction(timing, pack, cancellationToken, executionName)` 提供生命週期與可讀的執行名稱。釋放時呼叫該圖實例的 `CancelObservedExecutions()`，只取消**這個實例**的觀察 session；取消會傳到 await 的呼叫端。停用的節點不會進入或被 Hold。

## 圖的模型

- 一個時機是一顆 `ActionTimingGroup<TTiming, TPack>` root，本體是有順序的 `ActionSlot<TPack>` 清單。
- `FormulaSlot<TResult, TAsset, TFormula, TPack>` 求一個值，永遠有保底值。
- 節點一次只有一種來源：內嵌（Action／Formula 實例）、資產（`ActionAssetBase<TPack>` 或 `FormulaAsset<TResult, TPack>`）、Token（`GraphToken`），或 Property。
- 族身分是具體 Slot 型別。兩個都回 `string` 的 Slot 是兩個族，可以各有同名 Token。Slot 可選擇開放「結果型別相容」的跨族公式接收，並用黑名單排除特定族。

## 編寫與驗證

Inspector 卡片是任何 `LogicGraph<,>` 欄位的編輯入口，可開啟與驗證該欄位。`PinTools/LogicGraph/開啟節點圖` 與 `Assets/LogicGraph/開啟節點圖` 用來開啟只有一份文件的 Owner；`PinTools/LogicGraph/驗證全部 Owner` 重驗專案內所有 Owner。

驗證通過後圖才能執行；`TriggerAction` 與 `CreateTokenTable` 拒絕執行未驗證的圖。空的公式 Slot 是合法的常數；啟用中的空動作、重複時機、無效 Token、不相容的資產綁定、圖或資產循環都是錯誤。只能經由停用節點到達的殘缺內容降為警告。

離開 Edit Mode 時，編輯器會重驗 ScriptableObject 上的 LogicGraph 欄位。失敗只記錄在 Console，不擋 Play，但未驗證的圖在 runtime 仍會被擋下。

## 描述編譯

`Compile(template, pack, passes)` 建立 `CompileContext<TPack>`，依序執行呼叫端提供的 `ICompilePass<TPack>`。框架不定義模板語法或輸出格式，和動作執行使用同一張已驗證的 Token 表。

## 套件內容

- `Runtime/Action`：Action 基底、Action Slot、可重用 Action 資產、Property 寫入 Slot
- `Runtime/Formula`：Formula 基底、Formula Slot、可重用 Formula 資產
- `Runtime/Token`：Token 解析
- `Runtime/Property`：Property 目前值的執行期儲存
- `Runtime/Engine`：分派、驗證、編譯、使用規則
- `Editor`：正式入口、Inspector 卡片、驗證掃描、公式族樣板產生器
- `Documentation~/Maintenance.md`：維護手冊

## 測試

- `GraphDeepCopyTests`：複製、Token 重新求值、綁定、公式保底。
- `LogicGraphExecutionTests`：執行觀察、Hold、取消、作者 API 邊界、Property 儲存、跨族公式。
- `LogicGraphValidationTests`：結構化診斷、legacy Owner 整合、Property 驗證。
- `LogicGraphUsageTests`：零介面 Owner、使用規則、指定欄位與批次驗證、預設 context 提交、共用資產重驗。

在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.framework.logicgraph` 加進 `manifest.json` 的 `testables`。
