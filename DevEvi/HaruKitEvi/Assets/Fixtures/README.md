# Fixtures

HaruKitEvi 用來手動測試 GraphKit、LogicGraph、AssetPipeline 的使用端程式與範例資產。這裡的東西不是套件的一部分，不會發佈。

## 選單

`HaruKitEvi/Fixtures/`（宿主專案自己的頂層選單，不放進 `HaruFamily`）：

| 項目 | 作用 |
|---|---|
| Create or Reset All Samples | 在 `Assets/Fixtures/Data/` 建立或覆寫四個範例資產 |
| Open LogicGraph Sample | 用 LogicGraph 的正式入口開啟 `LogicSample` |
| Open Plain GraphKit Sample | 用 `HaruGraphWindow.OpenFor` 開啟 `PlainSample` |
| Select AssetPipeline Sample | 選取 `PipelineSample`，從 Inspector 開圖、驗證、執行 |
| Run LogicGraph Sample (OnStart／OnEnd) | 在 Edit Mode 以 Seed=7 觸發已儲存的圖，結果寫到 Console |

## 內容

| 路徑 | 組件 | 內容 |
|---|---|---|
| `Runtime/Logic/` | `HaruKitEvi.Fixtures` | LogicGraph 示範領域：`EviTiming`、`EviPack`、Int／Bool／Text 三個公式族、公式與動作節點、`EviGraphDefinition`（Owner）、`EviGraphRunner`（Play Mode 觸發）、`EviShowcase`（欄位屬性展示節點） |
| `Runtime/Plain/` | `HaruKitEvi.Fixtures` | 不經 LogicGraph 的最小 `IGraphDocument`：`PlainGraphOwner`、`PlainDocument`、自訂 Slot 與只有結構的節點 |
| `Editor/Pipeline/` | `HaruKitEvi.Fixtures.Editor` | AssetPipeline 的同步公式與動作（`Pipe*`）、`PipelineCounter` 測試資產；`PipelineAudioPrefabSteps.cs` 是「依音效批次產生 Prefab」用的節點（收集音效、逐一處理、複製 Prefab、替換音效、登記 Addressable、物件名稱），因此這個組件引用 Addressables |
| `Editor/` | `HaruKitEvi.Fixtures.Editor` | 選單與三個範例圖的建立程式 |
| `Data/` | — | 選單產生的範例資產 |

## 各範例適合測什麼

- **LogicSample**：巢狀公式、公式清單、條件與迴圈的流程宣告、多個時機、Token／Property／共用資產（`Create → HaruKitEvi → Logic` 可另外建立公式與動作資產）、執行觀察（加一顆「等待」）。
- **屬性展示節點**（LogicGraph 的動作，分類「展示」）：`[HGTab]`、`[HGFoldout]`、`[HGShowIf]`、`[HGEnum]`、`[HGBool]`、`[HGOptions]`、`[HGSpinner]`、`[HGLabel]` 三種排列、Slot 清單與動作清單。
- **PlainSample**：編輯器本身的行為（內放、拖放、清單、收合、群組、存檔與 Undo），不受任何執行層影響。驗證一律通過。
- **PipelineSample**：回報、交易寫入（計數器 +1）、條件分支；最後一步「故意失敗」預設停用，啟用後執行應整次回復，計數器不變。

## 維護

- 節點型別的類別名、namespace、asmdef name 會寫進範例資產的 `[SerializeReference]`。改名後用選單重置範例即可，不需要相容處理。
- 套件 API 變動造成這裡編譯失敗時，改這裡，不要為了 Fixtures 改套件。
