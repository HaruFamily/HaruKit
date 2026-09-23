# AssetPipeline 維護手冊

給 AI Agent 與維護者。修改 AssetPipeline（AP）、在使用端新增 Action／Formula，或排查管線執行結果之前先讀完本檔，並遵守其中的規則。

## 0. 使用本手冊的規則

- 本檔只寫**現在成立**的事實與規則。程式碼是行為的證據；本檔與程式不一致時，先確認實際行為，再在同一個 commit 修正本檔或程式。
- 改到本檔描述的行為、契約、檔案位置或限制時，**同一個 commit 更新本檔**。沒有更新手冊的行為修改視為未完成。
- 歷史與改動理由寫進 commit 訊息或使用端專案的決策紀錄，不寫在這裡。
- AP 建在 GraphKit 之上。節點載體、Slot 基底、Property 定義、編輯器與 DeepCopy 的規則以 GraphKit 手冊（`DependencyCore/GraphKit/Documentation~/Maintenance.md`）為準。

## 1. 定位與邊界

AP 是 **Editor-only、同步執行**的資產處理管線框架。一個 `AssetPipeline` 資產持有一張 `Graph`，圖內只有一條管線（單一 `ActionGroup` root），依動作清單順序執行，所有資產寫入共用一份交易，失敗時整次回復。

| 組件 | 平台 | 引用 |
|---|---|---|
| `HaruFamily.Tools.AssetPipeline.Editor` | Editor | GraphKit、GraphKit.Editor |
| `HaruFamily.Tools.AssetPipeline.Editor.Tests` | Editor | AP、GraphKit、GraphKit.Editor、LogicGraph、UniTask、Test Runner |

硬性邊界：

1. **本套件只提供框架**：管線資產、同步 Action／Formula 基底、公式族與 Slot、Property 寫入欄位、驗證、交易與結果。**碰到專案資產的具體 Action 與 Formula 住在使用端專案**，新增步驟不需要改本套件。
2. 不引用使用端型別，也不引用 Addressables；Addressables 相關的節點由使用端提供。
3. 沒有 Runtime 組件，不進 player build。
4. 執行期使用 static 的 `AssetPipeline.current` 與回報 handler，**不支援同時執行多條管線**，也禁止巢狀執行。

## 2. 目錄地圖

| 檔案 | 內容 |
|---|---|
| `AssetPipeline.cs` | 管線 ScriptableObject、`current`／`CurrentAction`、`Report`／`ReportFormulaWarning`、選單入口 |
| `AssetPipeline.Execution.cs` | `RunPipeline()`：驗證、Property 備份、依序執行、提交或回復 |
| `AssetPipeline.PipelineTab.cs` | 驗證快照、執行確認、Inspector 用的執行入口 |
| `AssetPipelineEditor.cs` | Inspector：執行按鈕、結果面板、Property 快照、遺失型別提示、回復重試 |
| `Graph.cs` | `Graph`（實作 `IGraphDocument`、`ITokenOwner`、`IPropertyOwner`）與 `ActionGroup` |
| `GraphDrawer.cs` | Inspector 上的圖卡片（開圖、驗證）、結果定位 |
| `GraphVerifier.cs` | 結構與型別驗證，產生 `GraphDiagnostic` |
| `GraphPropertyWalk.cs` | 收集整張圖用到的 Property 定義（ProtoProperty＋LocalProperty） |
| `Node.cs` | `FormulaBase<TResult, TPack>`、`ActionBase`、internal `IFormula` |
| `Slot.cs` | `FormulaSlot<TResult, TFormula>`（輸入欄位）、`ActionSlot`（動作欄位） |
| `PropertySlot.cs` | `PropertySlot<TResult, TSlot>`（Property 寫入欄位） |
| `Formula_*.cs` | 公式族與 `*Slot`：Bool、Float／ListFloat、Int／ListInt、String／StringList、Folder、Object／ObjectList、AudioClip／AudioClipList、GameObject／GameObjectList、TextAsset／TextAssetList |
| `PipelineResult.cs` | `PipelineRunResult`、`PipelineActionResult`、`PipelineActionContext`、資產紀錄、Property 快照 |
| `PipelineAssetTransaction.cs` | internal 交易與公開的 `PipelineAssetWriter` |
| `Tests/Editor/` | 見 §8 |

## 3. 資料流

1. 使用者在 GraphKit 編輯器編輯 `graph` 的工作副本，存檔時驗證通過才寫回資產。
2. Inspector 的圖卡片按「驗證」後，記下整份資產的序列化快照。**快照與目前內容不一致時執行按鈕鎖住**；改過圖或欄位都要重新驗證。按下執行後還有一次確認對話框。
3. `RunPipeline()` 使用 Owner 上**已儲存**的 `graph`，不讀編輯器尚未存檔的工作副本。執行前再驗證一次。
4. 依 `graph.Actions` 順序同步執行每個啟用的 `ActionSlot`。一步失敗就停止後續步驟並回復整次執行。
5. 動作之間傳遞資料只透過 Property：上游以 `PropertySlot.Write` 寫入，下游輸入 Slot 讀取目前值。

## 4. 核心模型

### 4.1 Formula

- 同步公式繼承 `FormulaBase<TResult, TPack>`，只覆寫 `protected OnEvaluate(pack)`。AP 的輸入欄位一律使用 `NullPack`，所以公式寫成 `Formula_Int<NullPack>` 這種形式。
- 輸入欄位 `IntSlot`、`ObjectListSlot`、`FolderSlot` 等綁定對應族的 NullPack 版本；`slot.Evaluate()` 不需要參數。
- 輸入欄位的來源是常數、內嵌公式、具名 Token 或 Property 之一。AP 不支援共用資產節點（`AssetBaseType` 為 null）。
- 空槽、停用、型別不符、公式例外一律回保底值並寫公式警告，**不中斷執行**。Token 遞迴在執行期回保底值。
- 每顆公式只有一種結果型別；`List<AudioClip>` 不會自動當成 `List<Object>`。`ObjectSlot` 開啟了跨族相容，可接受結果是任何 Unity Object 子類的公式；清單欄位沒有這個例外。

### 4.2 Action

- 動作繼承 `ActionBase`，覆寫 `protected OnExecute(PipelineActionContext context)`。`ActionBase.Execute` 是 internal 派發入口，由 `RunPipeline()` 管理完整執行。
- 組合動作宣告 `public ActionSlot child = new();`，在 `OnExecute` 裡呼叫 `child.Execute(context)`，**沿用同一個 context**：子動作的寫入、Fail／Skip 與結果都歸入父步驟與同一份交易。回傳的 bool 只表示「有沒有呼叫並正常返回」，不表示工作成功；要停止後續子動作時檢查 `context.Result.HasFailure` 並 return。
- 需要依序執行多個子動作的容器實作 GraphKit 的 `ISequentialActionContainer`，驗證會依宣告順序遞迴檢查子動作。

### 4.3 Property

- `Graph` 實作 `IPropertyOwner`，ProtoProperty 定義存在圖裡，跟著工作副本、存檔與 Undo 走。
- 寫入端宣告 `PropertySlot<TResult, TSlot>`（例如 `PropertySlot<List<Object>, ObjectListSlot>`），呼叫 `Write(value)` **只替換目前值**，不追加、不合併、不複製。沒接、停用或族不符時回 false 且不寫入。
- 讀取端的一般輸入 Slot 直接接 Property 節點。未寫入的一般 Property 回 `default(T)`（清單是 null），ProtoProperty 回初始內容。參考型別拿到同一份引用，不做複製隔離。
- **目前值不序列化**：同一個 Editor session 內跨次執行保留，沒有動作再寫入就沿用上次的值；Domain Reload 或重新載入圖之後回到未寫入狀態。

### 4.4 交易與結果

- 所有資產修改必須經由 `context.Assets`（`PipelineAssetWriter`）：`CopyPrefab`、`EditPrefab`、`EditAssetSet`、`CreateAssetSet`。Writer 自動記錄完成的寫入，作者不要重複登記。
- 一般失敗用 `context.Fail(message, path)`；正常無事可做用 `context.Skip(message)`。執行中出現的 Error／Exception／Assert log 也會讓該步失敗。
- 狀態：Success／Skipped／Partial／Failed／NotRun。已有完成項目再失敗是 Partial；Failed 與 Partial 都停止後續並回復整次執行。正常跳過不回復。
- 交易在首次寫入前備份檔案與 `.meta`；目標有執行前未儲存的修改時拒絕捕捉。Prefab 覆寫保留目的地 GUID。自動命名的多檔產出可事先宣告資料夾範圍，回復時移除範圍內本次新增的檔案。
- 日誌與備份在 `Library/AssetPipelineTransactions/<id>/`；回復失敗時保留日誌並回報 RecoveryRequired，Inspector 可重試，未處理的日誌會擋住下一次執行。
- 執行前備份整張圖用到的每顆 Property（由 `GraphPropertyWalk.Collect` 決定範圍）的寫入狀態，以及目前值與初始內容兩者的一層 `IList` 內容；失敗時一起回復。巢狀容器與自訂可變物件不在回復範圍。
- 交易只涵蓋經由 writer 登記的寫入與宣告的產出範圍，**不攔截**任意 `AssetDatabase`、檔案系統或第三方回呼的副作用。

## 5. 硬規則

1. **資產修改只能經由 `context.Assets`。** 直接呼叫 `AssetDatabase`／`File` 寫入的變更不在交易內，失敗時回復不了。
2. **不要在 Action 裡呼叫全專案 `AssetDatabase.SaveAssets()`**；writer 只儲存相關資產。
3. **交易型別是 internal**，使用端不自行建立、提交或回復交易；`AssetPipeline.current` 與 `CurrentAction` 對使用端唯讀。
4. **Property 的備份、快照、回復一律用 `GraphPropertyWalk.Collect(graph)`**。只讀 `graph.Properties` 會漏掉節點私有的 LocalProperty。
5. **驗證不檢查「先寫後讀」**：未寫入的 Property 是合法狀態。不要為此加回時序檢查。
6. **執行一律使用已儲存的圖並重新驗證**；不可為了方便讀取編輯器的工作副本或跳過驗證。
7. 公式要在「預覽」（沒有 `AssetPipeline.CurrentAction`）時不產生資產副作用；需要建立資產的公式只能在 AP 執行中、經 `CurrentAction.Assets.CreateAssetSet` 建立。
8. 序列化身分是資料契約。改公式族或 Slot 的型別名時用 `[MovedFrom]` 指定原 namespace／assembly／class，保留欄位名與腳本 GUID。
9. `Formula_*` 檔案同時放族與 Slot；**同一個結果型別只保留一個族**，除非兩邊各自有具體公式與欄位宣告。

## 6. 常見修改

### 新增 Action（使用端）

```csharp
[Serializable]
[HGNode("收集資料夾資產", "把資料夾裡的資產寫進 Property", "動作")]
public class CollectAssets : ActionBase
{
    [HGLabel("資料夾")]
    public FolderSlot folder = new FolderSlot("Assets");

    [HGLabel("產出 Property")]
    public PropertySlot<List<Object>, ObjectListSlot> output = new();

    protected override void OnExecute(PipelineActionContext context)
    {
        string path = FolderSlot.GetFolderPath(folder.Evaluate());
        if (!AssetDatabase.IsValidFolder(path)) { context.Fail("資料夾無效。", path); return; }

        var assets = new List<Object>();
        foreach (string guid in AssetDatabase.FindAssets("", new[] { path }))
            assets.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));

        if (assets.Count == 0) { context.Skip("沒有資產。"); return; }
        output.Write(assets);
        foreach (Object asset in assets) context.Result.Record(PipelineItemStatus.Collected, asset);
    }
}
```

- 會修改資產時改用 `context.Assets` 的方法。
- 每個具體型別都要標 `[Serializable]` 與 `[HGNode]`。

### 新增 Formula（使用端）

繼承對應族的 NullPack 版本（例如 `Formula_ObjectList<NullPack>`），覆寫 `OnEvaluate(NullPack pack)`，標 `[Serializable]` 與 `[HGNode]`。

### 新增公式族（本套件或使用端）

在同一個檔案宣告 `Formula_X<TPack> : FormulaBase<X, TPack>` 與 `XSlot : FormulaSlot<X, Formula_X<NullPack>>`（需要時加 `[HGKind]`）。清單族在 Slot 建構子把 `_default` 設成空清單。確認沒有既有的族已經代表同一種結果。

### 改框架

- [ ] 沒有引用使用端型別或 Addressables
- [ ] 資產寫入仍全部經過 writer 與交易
- [ ] Property 的備份範圍仍走 `GraphPropertyWalk`
- [ ] `RunPipeline()` 結束時還原 `current`、`CurrentAction`、公式警告 handler
- [ ] 驗證規則與 GraphKit 的接受條件（`Accepts*`）一致
- [ ] 新增或改名的 `.cs` 都有 `.meta`
- [ ] README 與本手冊已同步

## 7. 遺失型別

- 開圖、驗證、提交前由 GraphKit 自動清理遺失的 SerializeReference 型別（不備份、不詢問），規則見 GraphKit 手冊 §6.3。
- Inspector 偵測到遺失型別時只顯示提示與「開啟節點圖修正並存檔」按鈕，不在重繪時自動清除。
- 清理後仍須通過正常驗證才能存檔；`RunPipeline()` 的執行前驗證維持。

## 8. 驗證

測試在 `Tests/Editor/`，測試組件同時引用 LogicGraph 與 UniTask，**執行 AP 測試需要專案也安裝這兩者**：

- `PipelineExecutionTests`：建立、覆寫、重複修改的提交與回復；GUID、檔案內容、記憶體狀態保留；子動作共用交易；部分失敗；LogError；缺備份；未儲存資料；多檔產出；Property 值與共用清單回復；Property 快照；循序子動作讀取；跨次保留；LocalProperty 回復；遺失型別清理。
- `PropertyGraphTests`：Property 讀寫、未寫入預設值、ProtoProperty 初始內容、DeepCopy、讀後寫回不算循環、族相容與命名驗證。
- `GraphVerifierTests`：`ObjectSlot` 跨族上轉、跨族黑名單與隔離、空動作、停用、節點循環。
- `GraphKitCrossToolSessionTests`：共用驗證器、結果定位不改動文件與 Dirty 狀態、視窗命令、跨工具 session 隔離。
- `LibraryOrderTests`：Token 視圖保序、資產重掃與改名保序。

測試會建立自己專用的暫存資料夾；已有未完成的 AP 交易時先拒絕執行，不清除別人的日誌。在 Unity Test Runner 以 EditMode 執行；從 UPM 安裝時，要把 `com.harufamily.tools.assetpipeline` 加進 `manifest.json` 的 `testables`。**沒有實際執行的測試不可回報為通過。**
