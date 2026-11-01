# 混合式流程編輯器驗收

這份清單區分適用契約、測試碼與實際驗收證據。原始碼或靜態檢查完成，不代表 Unity 編譯、MCP 協定、磁碟重讀或畫面已驗收；每次修改後重新記錄本版結果。

## 使用端適用範圍

| 使用端 | 流程宣告與結構操作 | 保留的邊界 |
|---|---|---|
| LogicGraph | ActionTimingGroup 以 ISequentialActionContainer 回傳實際 Actions；專案條件／迴圈依明確流程宣告展開 | Pack／Slot 族必須相容；未知 Action 不猜子清單，Asset／Token 以引用邊界處理 |
| AssetPipeline | ActionGroup 以 ISequentialActionContainer 回傳實際 Actions；已明確宣告的同步子動作可展開 | 不把 AP Action 換成 LogicGraph 非同步 Action；AP 動作 Slot 不收 Token／動作資產，不內建使用端條件或迴圈型別 |
| 自訂 IGraphDocument | 預設 root 契約或明確 root adapter，加上 IGraphFlowContainer／ISequentialActionContainer／context FlowMetadata | 未宣告流程時維持完整 Node；存在 List<ActionSlot> 欄位不代表它必定執行。非實際可寫清單不開放結構操作 |

MCP 流程語意快照與原 Node 畫布共用同一份圖資料，不用座標決定順序。未知容器在語意快照保留為步驟並停止展開；原 Node 畫布仍可讀取其序列化欄位並巢狀編輯資料，不推斷其執行方式。共用身分不因投影而複製，不求值或執行。工具操作細節與限制以 [MCP 手冊](Mcp/Maintenance.md#flow-authoring-and-background-layout-review) 為正本。

## 自動測試清單

從 UPM 安裝時將 GraphKit 加入 testables；LogicGraph／AssetPipeline 的使用端測試須在含對應套件的宿主中執行。可選 MCP 測試另須滿足 MCP for Unity／JSON／Unity 版本條件，見 [安裝說明](Mcp/Installation.md)。不將使用端引用加回 GraphKit 的核心或可選 MCP 組件。 宿主測試若直接覆寫 LogicGraph Action 的 UniTask 方法，測試 asmdef 須明確引用 UniTask；僅引用 LogicGraph 不會傳遞其組件引用。

| 範圍 | Test Runner 篩選名稱 | 檢查內容 |
|---|---|---|
| 原畫布巢狀 | HGNestedSlotTests | 單值／共用、多層公式及動作清單、深度邊界，不回填存放位置 |
| LogicGraph 投影 | FlowProjectionKeepsBranchOrderAndSharedUsesWithoutChangingSession | 分支順序、共用使用位置及 session 不變 |
| 基本流程交易 | FlowEditingKeepsSingleUndoSharedCarriersAndSavedRoundTrip | 新增／移除／重排、Undo／Redo、孤兒保留、保存與取消 |
| 結構操作 | FlowTransferPreservesSlotsSharingAndRejectsDescendantOrStaleTargets；FlowWrappingIsAtomicAndKeepsOriginalStepIdentityThroughNestedContainers | 跨分支搬移、包裝、身分及原子失敗回復 |
| 自訂文件 MCP | FlowProtocolUsesFreshPathsAndPreservesSharedSlotsWithAtomicRollback；BackgroundGeometryAndImmutablePoliciesSeparateLayoutFromContentAndProtectGroups | generation、batch、背景分頁、保護及純版面差異 |
| 未知容器 MCP | UndeclaredCustomContainerStopsFlowExpansionWhileNodeGeometryRemainsReadable | 不猜未宣告子流程，保留 Node 幾何讀取，拒絕猜測路徑且草稿／Owner 不變 |
| LogicGraph＋AP 宿主 MCP | HybridFlowProtocolRoundTripsAcrossLogicGraphAndAssetPipeline（兩組 TestCase） | 真實使用端 Slot／Pack、搬移及 Undo／Redo、共用保留、原生欄位與位置命令、保存／重匯入／重開 |
| 組件邊界 | HaruGraphMcpBoundaryTests.IntegrationAndTestsReferenceOnlyGenericAssemblies | 編譯後 Runtime／Editor 核心無反向 MCP 或使用端依賴，可選 MCP／測試僅依賴泛用組件 |
| 既有 Node／跨工具 session | GraphKitCrossToolSessionTests；GenericMcpRootTransactionWorksForAssetPipelineAndIndependentLogicGraph | 原有 Node 命令、root 操作、隔離及保存入口 |

GraphKit 內的 portable 測試不引用 LogicGraph 或 AP；宿主協定測試位於使用端測試組件。協定測試直接呼叫公開 HandleCommand，仍不等於實際 MCP 客戶端傳輸已連線。測試僅建立、保存及清理自己的暫存資產，不執行生產管線或玩法 Action。

## 人工畫面與保存驗收

1. Unity 匯入腳本並產生必要 `.meta`，先確認 Console 編譯無錯，再跑對應 EditMode 測試。保存完整 Test Runner 結果，不以測試碼存在代替通過。
2. 以專用測試圖檢查空／非空 root、條件、迴圈、共用、未知、Token／資產引用。流程順序須與真實清單相同；原 Node 畫布可直接操作巢狀，原手排、Group／Tab、收合與註解仍可用。
3. 在原畫布新增、編輯原生值／來源、搬移連續範圍及包裝。每次操作核對清單、共用引用、Undo／Redo、取消全部變更、保存重開。拒絕自己／子流程、不相容族與唯讀欄位。
4. 設定保護後，用背景 geometry／layout_preview 查其他虛擬分頁；使用者視窗、可見 Tab 與草稿 generation 不應改變。核對 layout_diff 的內容與保護結果，再按既有保存規則操作並重讀。保護期間 Undo／Redo 的限制見 MCP 手冊。
5. 逐頁人工檢查外框重疊、文字、線路及長線。SVG 是量測示意，不能替代 IMGUI 視覺驗收；geometry 的端點距離估計不能當作實際線長或交叉數。

## 證據與獨立安裝

每輪分別記錄：來源版本／未提交修改、靜態檢查、Unity 編譯、使用端 EditMode／協定測試、保存重讀、人工畫面、實際 MCP 傳輸及獨立專案安裝。任何未執行項目保留待驗證，舊版本通過不涵蓋目前修改。

獨立安裝沿用 Validation~/prepare_clean_project.py，僅在明確授權後建立專案外的新目錄並執行 Unity；程序與限制見 [MCP 維護手冊](Mcp/Maintenance.md#reproducible-independent-installation)。含可選 MCP 與不含 MCP 的核心安裝分開驗證，宿主內結果不能代替乾淨專案證據。

失敗時回傳完整 Console／Test Runner 訊息、工具 action／generation／錯誤資料及相關畫面。保存狀態不明時先檢查檔案與 recovery 資料，不盲目重試或覆寫後續編輯。

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit混合式第六階段.md`（來源及靜態邊界核對；Unity／畫面／獨立安裝待驗證）

多層與動作清單新增驗收：執行 HGNestedSlotTests 全部案例，拖曳動作 Header 在清單間插入並檢查 Slot Id／Label／Disabled、Undo／取消／保存重開；帶 Slot 設定、手動收合、同 Group／Tab 及共用來源逐項核對。人工入口為原 Node 畫布。

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit多層巢狀與流程介面整併.md`（來源靜態核對；Unity／拖放／視覺待驗證）
