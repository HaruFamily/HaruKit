# GraphKit 維護手冊

給 AI Agent 與維護者。修改、擴充 GraphKit，或在使用端串接它之前先讀完本檔，並遵守其中的規則。

## 0. 使用本手冊的規則

- 本檔只寫**現在成立**的事實與規則。程式碼是行為的證據；本檔與程式不一致時，先確認實際行為，再在同一個 commit 修正本檔或程式，不可挑方便的一邊照做。
- 改到本檔描述的行為、契約、檔案位置或限制時，**同一個 commit 更新本檔**。沒有更新手冊的行為修改視為未完成。
- 歷史、否決方案和改動理由不寫在這裡，寫進 commit 訊息或使用端專案的決策紀錄。
- 相關套件：LogicGraph（`Framework/LogicGraph/Documentation~/Maintenance.md`）、AssetPipeline（`Tools/AssetPipeline/Documentation~/Maintenance.md`）。改 GraphKit 契約時兩個使用端都要看。

## 1. 定位與邊界

GraphKit 是**沒有領域語意**的序列化節點圖：提供節點載體、圖層契約、`[HG*]` 屬性、DeepCopy、診斷資料，以及整套 IMGUI 節點編輯器 `HaruGraphWindow`。它**不排程、不求值、不決定節點做什麼**。

| 組件 | 平台 | 引用 |
|---|---|---|
| `HaruFamily.DependencyCore.GraphKit` | 全部 | 無 |
| `HaruFamily.DependencyCore.GraphKit.Editor` | Editor | GraphKit Runtime |
| `HaruFamily.DependencyCore.GraphKit.Editor.Tests` | Editor | 上面兩者 |

硬性邊界：

1. **GraphKit 不得引用 LogicGraph、AssetPipeline、UniTask 或任何使用端型別。** 依賴方向永遠是使用端 → GraphKit。
2. 目前有兩個使用端：LogicGraph（非同步、有時機）和 AssetPipeline（同步、Editor-only）。**只有其中一邊用得到的東西不要加進 GraphKit**；需要共用能力時補非泛型介面，不要把執行層型別拉進來。
3. 判斷「是不是某種圖資產」只走 `Runtime/Contracts/` 的介面（`IGraphAsset`、`IActionGraphAsset`），不可依賴執行層的具體基底類別。
4. 編輯器不引用 Odin 或其他 Inspector 外掛；節點語意只認 GraphKit 自己的 `[HG*]` 屬性。
5. Runtime 組件不可出現 `EditorWindow` 或 `UnityEditor` 相依。

## 2. 目錄地圖

| 路徑 | 內容 |
|---|---|
| `Runtime/Graph/` | `GraphNode`（載體）與 `NodeKind`、`GraphNodeContent` 與形狀基底、`GraphToken`、`GraphProperty`、`GraphSlotBase` 與 `FormulaSlotBase`／`ActionSlotBase`／`PropertySlotBase`、`NamedFormulaSlot`、`GraphDeepCopy`、`GraphExecution`（執行觀察）、`ReferenceComparer` |
| `Runtime/Contracts/` | `GraphContracts`（`IGraphHead`、`IOrphanPool`、`ITokenOwner`、`IPropertyOwner`、`IGraphDocument`、`HGCapabilities`、`ISequentialActionContainer`、`IGraphNodeOwner`、`ITypedFormulaNode`、`IGraphSink`、`IGraphExecutionDocument`、`IGraphViewStateOwner`／`GraphViewState`）、`GraphDiagnostics`、`AssetGraphSchema`、`ActionNodeAttribute`（`[HG*]` 屬性）、`IGraphOwner`、`IExternalTokenKeys` |
| `Editor/VisualEditor/Window/` | `HaruGraphWindow` 的 partial 檔，依責任分檔（見 §5） |
| `Editor/VisualEditor/Model/` | 工作副本與歷程 `HGModel`、焦點 `HGFocus`、建圖 `HGGraph`、Port `HGPort`、驗證 `HGValidator`、反射 `HGReflect`、提交守衛 `HGDocumentCommitGuard`、擴充 context、遺失型別定位 |
| `Editor/VisualEditor/Panels/` | 工具列、焦點列、Console、執行面板、Token 庫、變數庫、資產庫、引用清單 |
| `Editor/VisualEditor/Project/` | `HGTypeIndex`（可建立型別與選單）、資產索引與落點、Owner 索引、引用索引 |
| `Editor/VisualEditor/Public/` | `HGDocumentSession`（非視窗交易）與視窗命令 `HGWindowSession` |
| `Editor/VisualEditor/UI/` | 樣式與主題（`HGStyles`、`HGTheme`、`HGSkin`）、值欄位、就地改名、拖曳、庫內重排、確認框、節點狀態色 |
| `Editor/Tests/` | `HGPortTests`、`HGPublicConsumerTests`、`GraphExecutionTests` |

## 3. 核心模型

### 3.1 載體 `GraphNode`

畫面上一個節點＝資料裡一個 `GraphNode`。它保存 `Id`、座標、備註、停用旗標，以及**一種**內容：

| `NodeKind` | 值 | 內容 |
|---|---|---|
| `Empty` | 0 | 還沒選內容的空節點（合法的編輯狀態，存檔驗證會擋） |
| `Inline` | 1 | 內嵌的具體節點內容（`GraphNodeContent`） |
| `Asset` | 2 | 共用資產 ScriptableObject |
| `Token` | 4 | 具名 Token 的引用（存物件參照，不存名字字串） |
| `Property` | 6 | Property 定義的引用（LocalProperty 或 ProtoProperty） |

- **換來源＝換載體內容**：`SetBody`／`SetAsset`／`SetToken`／`SetProperty`／`Clear` 只換內容，`Id`、座標、備註和所有指向它的欄位都保留。不要用「新建一顆節點」來做換來源。
- **共用是結構性的**：多個 Slot 用 `[SerializeReference]` 指向同一個 `GraphNode` 就是共用；DeepCopy 保留共享與循環。
- 型別安全收在存取器：`GetBody<T>()`／`GetAsset<T>()` 型別不合回 null。載體本身非泛型。

### 3.2 Slot 與頭端

- 所有欄位共用非泛型基底 `GraphSlotBase`：`Node`／`SetNode` 與 `AcceptsBody`／`AcceptsAsset`／`AcceptsToken`／`AcceptsProperty`。
- `FormulaSlotBase`（求一個值）、`ActionSlotBase`（副作用，同時是畫布頭端）、`PropertySlotBase`（寫入目標，不求值）。**這些基底刻意是零序列化欄位**，使用端才能加泛型子類而不改變序列化格式。
- 族（family）的身分是**具體 Slot 型別**（`FamilyType`），不是結果型別。同為 `string` 的兩個 Slot 型別是兩個族。
- 公式接收：同族 `BodyBaseType` 優先。Slot 覆寫 `AllowCompatibleResult` 可額外接受結果型別可指派、Pack 相同的其他族，並以 `ExcludedFormulaFamilies` 排除；`CandidateBodyBaseType` 只描述候選搜尋範圍，最後一律以 `AcceptsBody` 判定。拉線、驗證、求值用同一套接受條件。

### 3.3 Token 與 Property

- `GraphToken`：具名的公式端點，有自己的畫布、取值欄位與候選池。唯一性是「族＋名稱」。Token 節點存 `GraphToken` 物件參照，改名不斷線、刪除立刻變 null 由驗證報錯。**不要改回字串 key**。
- `GraphProperty`：圖內的可寫儲存位置。ProtoProperty 住在 `IPropertyOwner.Properties`（變數庫），有初始內容；LocalProperty 是節點私有定義。讀取只取目前值、不求值寫入者；`PropertySlotBase` 是寫入目標，**不構成求值依賴**，讀後寫回同一顆不算循環。目前值由使用端的執行層保存，GraphKit 只保存定義。
- `GraphPropertyInputSlot` 只是 Property 節點接收端的身分；寫入連線的正本是寫入端 `PropertySlot.Node`。
- `PropertySlotBase` 欄位的型別 chip 依 `FamilyType` 顯示對應 FormulaSlot 族的 `[HGKind]` 名稱；未指定名稱時沿用結果型別短名。

### 3.4 文件與能力

- 可編輯的圖實作 `IGraphDocument`：roots、驗證旗標、`InvalidateValidation`／`Verify`／`DeepCopy`、`PackType`、`ItemSlotType`、root 識別值與用詞（`RootChip`、`RootNoun`、`WindowTitle`），以及 `HGCapabilities`。
- `InvalidateValidation()`（legacy Owner 為 `IGraphOwner.InvalidateGraphValidation()`）只撤銷文件的已驗證旗標，與編輯器的未存狀態無關。未存與 Undo 由 `HGModel.MarkContentChanged()`／`MarkLayoutChanged()` 管理（`HGModel.Dirty`）；**不要用 Dirty 字眼替驗證狀態命名**。
- `HGCapabilities`（`SharedAssets`、`Tokens`、`Properties`）由文件宣告。沒宣告的能力，編輯器整組收掉對應的庫、選單與右鍵。**不要讓編輯器從「清單有沒有資料」推測能力**。
- 生效能力是 `HGEditorExtensionContext.CapabilitiesOf`：context 有 `HGEditorProfile` 時**整個取 Profile 的能力，文件宣告被忽略**，不做聯集。使用端替文件新增能力時，要同時更新它正式入口 Profile 的能力，否則庫與選單不會出現。
- root 識別值型別是 `object`，編輯器只做 `Equals` 和 `ToString()`。**不要把它轉回 `Enum`**，那會把時機概念綁回 LogicGraph。
- 可選 `IGraphExecutionDocument` 提供執行觀察 source 與內容版本。

## 4. 硬規則

違反任何一條都視為錯誤修改。

1. **序列化身分是資料契約。** `[SerializeReference]` 在資產裡記錄 `{class, ns, asm}`。改類別名、namespace 或 asmdef name 等於改資料格式。要改之前，先掃使用端資產裡實際存在的記錄，再決定用 `[MovedFrom]` 接回或改寫資產，並取得擁有者同意。
2. **反向旗標不可改名。** `_disabled` 不可改成 `_enabled`：舊資產沒有這個欄位，反序列化成 false 會把全部內容關掉。
3. **零欄位基底保持零欄位。** `GraphSlotBase`、`FormulaSlotBase`、`ActionSlotBase`、`PropertySlotBase`、形狀基底只能加抽象或虛擬成員，不能加序列化欄位。
4. **編輯器只改工作副本。** 開圖時 DeepCopy 出 `HGModel.Data`，所有編輯都在副本上，存檔成功才寫回 Owner；取消就重抓 Owner。不可在編輯流程直接改 Owner 上的文件。唯一例外是已確認遺失的 SerializeReference 資料清理（§6.3）。
5. **內容有變的存檔必須通過驗證。** 視窗存檔、`HGModel.Save`、非視窗 `Commit` 都要求診斷與文件 `Verify()` 通過。唯一例外是只改版面：`HGModel.ContentDirty` 為 false（只經 `MarkLayoutChanged()`：座標與收合版面；其餘修改一律 `MarkContentChanged()`）且要寫回的副本本身已帶通過旗標時，沿用上次驗證結果直接寫回，與資產焦點的座標存檔一致；遺失型別清理與版本衝突檢查照樣執行。Undo／Redo 與遺失型別清理一律視為內容變更。沒有草稿存檔，也不可為了讓存檔通過而放寬驗證。
6. **載體存取走型別契約**（`FormulaSlotBase`、`ActionSlotBase`、`IGraphHead`、`IOrphanPool`、`ITokenOwner`、`IPropertyOwner`、`IGraphAsset`），不要用成員名反射。`MethodBase.Invoke` 不會補預設參數，簽章一變就在執行期壞掉。
7. **斷線不等於刪節點。** 節點只靠 SerializeReference 的引用路徑存活；斷線時要把失去引用的載體放回候選池（先保留可見座標），否則重建時整棵子樹會消失，存檔後資料就真的沒了。
8. **複製節點要重設 Id**（`HGModel.ResetNodeIds`），並正確帶入 `shared`（ProtoProperty 定義、目前作用域的 Token 要沿用同一顆）與 `skip` 集合。LocalProperty 定義跟著節點複製並換新 Id。
9. **圖走訪的 visited 一律用 `ReferenceComparer.Instance`**（Editor 端 `HGRefComparer`）。裸 `HashSet<object>` 對 struct 走值相等，會無聲跳過子樹。
10. **Core 驗證與視覺驗證要同步改。** 文件 `Verify()` 決定能不能存檔；`HGValidator` 是同一套規則加上可跳轉位置。只改一邊會變成「存檔鈕按得下去、卻被擋下且看不到原因」。
11. **診斷是純資料。** `GraphDiagnostic` 只存穩定 `Code` 和字串位置（document／focus／node／token／field path），不可持有 `HGRow`、`FieldInfo` 或視窗物件。Code 用穩定機器識別，不用本地化文字或方法名。
12. **面板不認識 model 與焦點。** 面板只收一次性快照與命令委派，不可反過來呼叫視窗；版面由視窗管理，面板自己的視圖狀態（高度、捲動）自己存。
13. **視窗偏好不進文件。** 庫的停駐、排序、顯示、欄寬等偏好存在 EditorPrefs（`HaruGraph.*` 鍵），不標 Dirty、不進 Undo。節點圖本身的收合版面（欄位 ⊖／⊕、清單折疊、註解框收合、節點內摺疊群組）與節點群組（§5.4）是例外：它和座標一樣是作者安排的版面，存在實作 `IGraphViewStateOwner` 的文件或資產上（`GraphViewState`），切換標未存檔、進 Undo，但以 `MarkLayoutChanged()` 記成版面修改，存檔沿用上次驗證。
14. **`[HG*]` 屬性的命名權留在 GraphKit**，所有使用端共用同一套，不可下放給使用端自訂。

## 5. 編輯器結構

`HaruGraphWindow` 是依責任拆分的 partial class，**所有欄位只宣告在 `HaruGraphWindow.cs`**，其餘分部檔只放方法：

| 檔案 | 責任 |
|---|---|
| `HaruGraphWindow.cs` | 欄位與常數、`OnGUI`、版面與分隔、`EnsureGraph`、`Invalidate`、`LiveVerify`、快捷鍵、工具列 |
| `.Session.cs` | 開窗入口與選單、`Bind`、存檔／取消／驗證交易、資產焦點進出 |
| `.Panels.cs` | 左右庫區分派、庫的停駐與顯示、Token／Property／資產庫命令、`ConsoleView()` |
| `.Canvas.cs` | 畫布、zoom、格線、連線、節點與 Header 繪製 |
| `.Rows.cs` | 參數列與清單列的繪製與互動 |
| `.Input.cs` | 畫布輸入、框選、複製貼上、刪除、排版與定位 |
| `.Link.cs` | 拉線相容性快取、命中測試、接線與斷線 |
| `.Source.cs` | 換來源、Token／資產／Property 節點建立與拖放、轉存、右鍵選單 |
| `.Execution.cs` | 執行觀察、session 選擇、Hold |
| `.NodeGroups.cs` | 畫布節點群組：建立、繪製、整組移位、分頁、註解、改色、選取、右鍵選單、拖放進出的成員判定 |

資料流：Owner → 明確 binding 或 legacy 欄位探索 → DeepCopy 成 `HGModel.Data` → `HGFocus` 決定中間畫布在編輯哪些 root → `HGGraph.Build` 每次資料變動整份重建節點與列 → 視窗用 IMGUI 繪製 → 存檔時清理遺失型別、DeepCopy、驗證、通過才寫回。

**節點搜尋（Ctrl+F）**：畫布上按 Ctrl+F，或按工具列的「搜尋」鈕，開畫布內搜尋列 `HGNodeSearchBar`（`Panels/`，畫布右上角），搜目前焦點 `graph.Nodes` 的全部節點，包括被收起的，不跨焦點。比對節點名稱（Token／資產／Property 補引用對象，`NodeSearchName`）、chip、註解，以及所屬群組的註解與所在分頁名稱；空白分詞，每詞都要命中同一顆節點，不分大小寫，所以群組名可以和節點名組合著篩。群組的修改不重建圖，`MarkViewStateChanged` 會作廢命中快取。輸入時只高亮：命中的節點加淡選取色外框，沒命中的壓暗；沒有命中時不壓暗。Enter／↓／F3 跳下一筆，Shift+Enter／↑／Shift+F3 跳上一筆，頭尾循環；跳轉走 `FocusDocumentNode`：被收起的先 `RevealNode`（展開欄位、清單、群組與分頁，寫回 `GraphViewState`，記成版面修改），再選取並置中。因為會寫版面，輸入時不自動跳。Esc／✕ 關閉，視角停在目前這一筆。搜尋列在 TextField 之前自己判 Enter／Esc／方向鍵；點回畫布時放掉輸入框焦點，畫布快捷鍵才收得到。游標壓在搜尋列上時，底下的節點、Header 工具列與群組標題都要遮掉指標事件（IMGUI 讓先畫的先拿事件）。

### 5.1 IMGUI 陷阱

- 變數庫各 Property 獨立展開，不限制同時展開數量；拖入資產只展開目標項。展開狀態由面板按 Id 保存，Reset 時清除，不進 Dirty／Undo。多個清單展開時，項目重排只收集正在拖曳之 Property 的目標位置。
- Token 庫與變數庫的格子是同一組操作：雙擊改名、拖到「＋」上放開＝複製、拖到「－」上放開＝移除，右鍵選單（改名／複製／移除）是同一組的可見入口。複製 ProtoProperty（`HGModel.DuplicateProperty`）是建立新的儲存位置：定義與初始內容深拷貝（資產只抄參考）、換新 Id、名字「原名 複本」，圖上沒有節點指向它；拖到畫布才是引用。右鍵改名走 `HGInlineRename.Begin`：選單回呼在 OnGUI 之外執行，只記狀態不碰鍵盤焦點，焦點由下一次 `Draw` 搶。
- 座標：graph → clip 是 `(graphPos + pan) × zoom`；自訂命中測試（節點、連線、框選）一律在 graph space 計算。
- **先畫節點、再處理畫布互動**，控制項才會先吃掉事件。畫布平移前仍要確認 `GUIUtility.hotControl == 0`。
- **畫布快捷鍵要讓給文字輸入**：左右庫與焦點列畫在畫布之後，游標停在畫布上時按鍵會先到 `HandleCanvasInput`。它的 `KeyDown` 分支在 `inlineName.IsEditing` 或 `EditorGUIUtility.editingTextField` 時一律不處理（F、Delete、Ctrl 系列都不攔），只有 F3（搜尋跳下一筆）排在這個判斷之前。新增畫布快捷鍵要放在判斷之後；改名框只看 `IsEditing`，因為它重新搶焦點的那一幀並未握著焦點。
- **節點重疊時輸入跟著畫面走**：`graph.Nodes` 的順序就是繪製順序（越後越上層）。IMGUI 依繪製順序分發事件，所以 `DrawCanvas` 在 `hotControl == 0` 的 MouseDown／DragUpdated／DragPerform 先用 `NodeAt` 找出最上層節點，其餘節點畫的時候把事件設成 `Ignore`，畫完還原成遮罩前的型別（不可還原成原始型別，會把已 `Use()` 的按鍵復活）。接點命中（`InputPortAt`／`OutputPortAt`）同樣只認最上層節點。
- 連線：一般線在節點之前畫（壓在節點下），選取中節點的線在 `EndZoomedCanvas` 之後畫（浮在最上層）。形狀固定為「水平短線 → 圓角 → 斜直線 → 圓角 → 水平短線」（`BuildLinkPath`），方向依接點位在節點左半或右半決定，不依輸入／輸出。點線剪斷（`LinkAt`）用同一份路徑，點在節點上時只剪得到浮在上層的線。清單折疊時，元素的線仍從標題列的代表接點畫出（接點本身不可起手或放線），多條時依目標高度扇形等角散開（`RebuildFoldFan`），這是頭端不水平的唯一例外；剪線一次只斷一個元素。
- 元素是 Slot、可增刪的清單，標題列有一顆新增接點（內部 `IHGListAppendBinding`：取值清單是輸入角色 `HGListAppendPortBinding`，`InputSlot` 是尚未放進清單的預備元素；PropertySlot 清單是寫入輸出 `HGListAppendWriteBinding`，清單所在節點沒有載體時不掛）：拖出去或從對向接點拉進來＝在尾端新增一項並接上，一步 Undo；原地點一下＝收起／展開所有元素接出去的子樹（Alt＝solo，與 Slot 接點同一套；清單再展開時，個別收起的元素維持收起），清單列的折疊只在標題文字。提交一律「先 `Commit()` 加進清單、再走一般接線」，在同一個變更交易裡。它會出現在 `HGWindowSnapshot`，`HGWindowSession.Connect` 走同一個交易，`Disconnect` 拒絕它。Aggregate 接點的契約不變。
- 收合欄位的線畫成殘影（實線短線＋圓角，進斜線後轉漸淡虛線；淡出長度固定，兩端太近才依斜線長度比例縮短）。欄位端一定畫，目標節點仍顯示時目標端也畫；殘影不參與命中。欄位在折疊清單裡時，欄位端從清單的代表接點起畫，和看得到的元素線同一把扇（`RebuildFoldFan` 也收殘影）。
- 接點：外環永遠畫，中心實心點表示有接線（○／◎）；顏色只表達用途與錯誤，不表達接不接。
- MouseDown 落在某節點上就把它提到最上層（`RaiseNode`），順序記在視窗的 `raisedNodeIds`，每次重建圖後由 `ApplyNodeOrder` 套回；純視圖狀態，不進文件與 Undo。順序改變時要清 `GUIUtility.keyboardControl`：控制項 id 依繪製順序發，不清的話輸入中的框會對到別顆節點的欄位。
- 平移只用中鍵；Alt 是 Slot 顯示開關的 solo 手勢。
- `GenericMenu`／`AdvancedDropdown` 的回呼在 OnGUI 之外執行，`Event.current` 是 null；需要滑鼠位置時在建立選單時先捕捉。
- hover 才顯示的控制項：`GUI.Button` 本身每次都要建立，只換內容，否則 control id 會錯位。
- 拉線相容性在起手時對整張圖算一次並快取；拖曳中若改圖，必須重新計算。

### 5.2 主題與配色

- 顏色的唯一來源是 `HGTheme` 的欄位（初始值＝預設主題）；`HGStyles` 以同名屬性轉讀。繪製碼不寫 `new Color(...)` 常數，只能從主題色衍生。套件只內建預設主題；新增顏色鍵時，初始值就是預設主題的值，使用者的主題檔缺這個鍵時會沿用它。錯誤紅與警告琥珀留在 `HGStyles`，不進主題。
- 主題檔 `*.graphtheme.json` 放在專案或套件內任何位置，只寫要改的鍵，顏色可寫 `"#RRGGBB(AA)"`。選擇存 EditorPrefs `HaruGraph.Theme`（主題檔 GUID），入口是視窗分頁的 ⋮ 選單；讀不到時 Log 並退回預設。
- 節點圖不跟 Unity Light／Dark 主題：視窗與確認框的 `OnGUI` 包在 `HGSkin.Scope()`，只在 Repaint 暫時改寫 EditorStyles／`GUI.skin` 的字色與底圖並在 finally 還原；`HGStyles` 的 GUIStyle 一律用 `Ink` 設滿所有狀態字色。新增繪製入口要同樣包 Scope。主題勾選框樣式沒有可拉伸的中段，Toggle 的 rect 一律走 `HGSkin.ToggleRect`（14px 見方），直接傳整條欄位會把貼圖邊緣拉成一條尾巴。ObjectField 選取鈕、Color／Curve／Gradient 欄位與選單仍由 Unity 繪製。
- 新增會快取顏色的貼圖或樣式，要掛進 `HGStyles.ResetCache()` 或 `HGSkin.ResetCache()`，換主題時才會重建。

### 5.3 Undo

- 快照式，掛在 `HGModel.MarkContentChanged()`／`MarkLayoutChanged()`：0.4 秒內連續修改合併成一步，上限 40 步。Undo／Redo 會整份換掉 `Data`，之後要依穩定識別重新解析焦點。
- 資產焦點不在 Owner 工作副本裡，有自己的 `HGAssetHistory`（同樣 0.4 秒、40 步）。視窗層一律走 `DoUndo`／`DoRedo`／`BreakUndoMerge` 路由。
- 資產的根內容、候選池、Token 清單、Property 定義必須在**同一次** `GraphDeepCopy.Copy` 裡複製，否則 Token／Property 節點會指到不在清單裡的孤兒定義。

### 5.4 節點群組

使用者介面叫「群組」，程式一律用 NodeGroup 前綴，和 root 群組、`HGRowKind.Group` 分開。實作在 `HaruGraphWindow.NodeGroups.cs`。

**資料與框**

- 群組是版面，不是節點：`GraphViewState._groups` 裡的 `GraphNodeGroup` 記 `Scope`（焦點 Id，同一份文件的不同畫布各自有群組）、註解（`Title`，見「標題列與註解」）、`Rect`、顏色（主題調色盤索引 `ColorIndex`，或自訂色 `UseCustomColor`／`CustomColor`）、`Collapsed` 與成員節點 Id。不影響執行、驗證與節點資料；所有修改走 `MarkViewStateChanged()`。文件沒有 `GraphViewState` 時不能建立群組。
- **框由可見成員目前的外框當場算出**（`NodeGroupHull`：外框加內距，標題列與展開中的註解貼在上方，有最小尺寸），不存起來。節點高度每次重建圖都重新量過，所以 List 增減、折疊都不需要另外掛更新點。不要在建圖或繪製時寫回 `Rect`，否則展開 List 會讓文件變成未存檔；它只在編輯群組時寫回（建立、整組移動、成員進出、收合前）。
- 沒有可見成員時：收合中只剩標題列（和展開中的註解），寬度用 `Rect`；成員都被 ⊖ 收起時用 `Rect`；畫布上完全沒有成員（`HasAnyMember`）才退回預設尺寸（`EmptyNodeGroupSize`，17×5 格＝340×100）。收合中的框不是成員外框，成員進出與整組移動都不能把它寫回成 `Rect` 的大小。

**成員**

- **成員以記錄為準，只因拖放而改變。** 開始拖節點時把每個群組的框凍結在拖曳前（`FreezeNodeGroupRects`），放手時滑鼠落在哪個凍結框（含標題列，取最上層）就把被拖的節點全部收進去，落在框外就移出（`ApplyDropMembership`）。一顆節點只屬於一個群組。不要改回用節點座標判斷，也不要在尺寸變化後重查成員。
- 找不到的成員 Id 直接略過，不在建圖時清掉。全畫布的整理版面、複製/貼上都不處理群組。

**繪製與顏色**

- 群組在連線與節點之前，用自己的縮放畫布畫（`DrawNodeGroups`）。游標在節點上時，群組這一輪看到的是 `Ignore`，標題列的控制項不能搶走節點的點擊。互動優先順序：接點 → 節點 → 連線 → 群組標題列 → 空白框選。
- 群組色：預設取主題 `nodeGroupPalette`（群組存索引，跟著主題換）；自訂色存成不透明，不跟主題走。成員節點本體混入群組色（`HGStyles.NodeGroupBody`），本體左緣另畫 3px 色條（`DrawNodeGroupStripe`，畫在節點之後，縮小畫面時仍認得出）；Header 不染。框底、標題列用群組色壓透明度，外框用實色。
- 換色入口只有標題列左端的色塊：開 `HGNodeGroupColorPopup`（上排主題調色盤、下排色相／飽和／明度滑桿）。自訂色不用 `EditorGUI.ColorField`：它會另開 Unity 顏色選擇器，`PopupWindow` 失焦就關，選的顏色回不來。面板回呼用群組 Id 找物件（面板開著時 Undo 可能換掉物件）。Popup 一律走視窗的 `RequestPopup`，排到 OnGUI 結尾才開。
- 有成員被收起時，成員數寫「看得到/全部」；群組框本身不因成員被收起而變樣。

**標題列與註解**

- 標題列只有一列，由左往右：色塊 → 收合 `▾/▸` → 分頁 → 「＋」→（右端）成員數 → 工具列提示 `▴`。群組沒有另外的標題文字，第一頁的名字就是群組名；寬度下限是 `NodeGroupTabStripWidth`。兩端內容固定內縮 `NodeGroupHeaderPortInset`（接點半徑＋3）：收合群組的殘影指向標題列兩端的框邊，線頭不壓到色塊與 `▴`。
- 註解與節點同一套：滑入標題列在右上方展開工具列（`nodeGroupActionsId`，拉線、拖曳、框選、游標在節點上時不展開），`✎` 開關註解框。註解內容存在 `GraphNodeGroup.Title`（欄位名沿用舊資料），內容等於舊預設名「群組」或空白時視為沒有註解（`NodeGroupNote`）。有內容預設展開，收起記在 `GraphViewState` 的註解收合（key＝群組 Id，走 `SetNoteCollapsed`）；剛按開的空框（`nodeGroupNoteOpenId`）只跟著選取中的群組活著。編輯內容走 `MarkViewStateChanged()`。
- 註解框畫在框內、標題列正下方（`NodeGroupNoteRect`），群組收合時照樣顯示：收合的框＝標題列＋註解。成員區上方的高度一律問 `NodeGroupTopInset`（標題列＋展開中的註解），不要直接用 `NodeGroupHeaderHeight`；收合時它就是整個框的高度。節點註解維持在節點本體最下面。
- 群組沒有 Enable／停用，分頁也不停用節點：Slot 求值只讀 `GraphNode.Disabled`，看不到版面資料。

**操作**

- 拖標題列會以整格為單位移動全部成員（包括被收起而隱藏的成員）；拖曳中只改暫存框與節點的顯示座標，放開才寫回，Undo 算一步。在分頁上按下也是整組拖曳的起點（`BeginNodeGroupDrag`＋`nodeGroupTabClick`），放開時沒有位移才切到那一頁（`EndNodeGroupDrag`）。
- 群組可以被選取（`selectedNodeGroupId`，用 Id 記，純視圖狀態）：左鍵或右鍵按在標題列會選取群組並清掉節點選取；左鍵按在其他地方、Ctrl+A 會放掉群組選取。Delete 時選著群組就只刪群組、成員節點保留，沒選群組才刪節點。
- 右鍵選單的全畫布段一律是「聚焦全部節點 → 整理版面」，每個選單（節點、空白處、群組標題列、群組內部空白）都有，而且永遠指整張畫布。群組範圍另外寫成「聚焦此群組（`FrameNodeGroup`）→ 整理此群組（`ArrangeNodeGroup`）」，不借用同一個字；不要用停用的選單項目當標題（看起來像停用指令，名稱含 `/` 還會變成子選單）。群組標題列右鍵：刪除（只刪群組）→ 此群組兩項 → 全畫布兩項；換色不放右鍵。群組內部空白右鍵：建立項目 → 此群組兩項 → 全畫布兩項；建立公式／動作、新增 root 節點都會同一步加入該群組（`CreateOrphan(…, group)`、`AddTimingGroup(…, joinGroup)`），不提供建立群組。
- `ArrangeNodeGroup` 只排這個群組的可見成員，留在成員原本的左上角：依成員之間的父子關係分欄（父在左），同一欄照目前上下順序排。不要改成呼叫全畫布的 `ResetLayout`／`AutoLayout`，那會把成員排到群組外。

**收合與分頁：群組關掉成員（只影響顯示）**

- 群組收合（標題列 `▾/▸`）與非作用中分頁是同一件事：成員被群組「關掉」，只影響顯示，照樣執行、照樣驗證。群組收合時全部成員都關掉；有分頁時非作用中分頁的成員關掉。`CollectGroupOffMembers` 在 `ApplyVisibility` 開頭把它們記進 `groupOffMembers`。
- **可達性走訪到被關掉的成員就停**（`MarkVisibleFrom` 的 `stopAtGroupOff`）：只靠它們才連得到的節點（包括群組外的）一起隱藏。`CollectGroupOffHidden` 再走一次不擋群組的可達性，兩次的差集加上 `groupOffMembers` 記在 `groupOffHidden`。solo 模式不走可達性，由 `HideGroupOffMembers` 補藏成員。
- **連線比照 ⊖ 收起**：一端在 `groupOffHidden` 的線（`IsGroupOffLink`）整條不畫，只在還看得到的父欄位 Port 畫殘影（`DrawGroupOffGhost`），朝向目標原本的接點；目標所在群組收合時改朝標題列朝向父欄位的那一側（原本的位置已不在框內）。目標端與標題列那段不畫，父欄位也被關掉時整條不畫，顏色同一般殘影。`MarkHiddenSlots` 略過 `groupOffHidden`，父欄位不會畫成 +。
- 標題列兩端不是接點：不畫 ◎、不能起手，也不能把線放進群組。沒有連線會接到標題列上。
- 群組展開時被群組外欄位 ⊖ 收起的成員照一般節點處理：殘影直接指向它的接點（`DrawLinkGhosts`），父欄位被藏起來的線不畫。
- `RevealNode`（Console、搜尋跳轉）會先展開節點所在的群組並切到它的分頁（`ExpandNodeGroupOf`）。

**分頁（Tab）**

- 資料在 `GraphNodeGroup`：`Tabs`（`GraphNodeGroupTab`：名稱與記在這一頁的成員 Id）與 `ActiveTab`。少於兩頁時所有成員都顯示（`HasTabs` 為 false）；成員沒記在任何一頁時算第一頁（`TabOf`）。`SetMember` 加入的新成員進作用中的那一頁，所以拖放、群組內建立節點都不必另外指定頁。`RemoveTab` 讓成員回第一頁，最後一頁不能刪。第一次新增分頁時要連第一頁一起建（`AddNodeGroupTab`）；沒有分頁資料的「分頁 1」改名時才建出那一頁。
- 分頁排在標題列內（從 `NodeGroupTabStart` 起），常駐至少一頁（`NodeGroupTabCount`），最後面的「＋」新增分頁，新增不放右鍵；收合時只畫作用中的那一頁，其餘寫成「+N」。
- 切頁是版面修改；非作用中分頁的成員走上面「群組關掉成員」那套。因分頁而藏的群組成員另外記在 `tabHiddenMembers`，**框與成員數仍把它們算成看得到**；被 ⊖ 收起的成員不記。框因此包住所有分頁的成員，切分頁時大小與標題列位置不變。
- 分頁標籤的按下、雙擊改名（site `nodeGroupTab`）、右鍵選單都在 `DrawNodeGroupTabs` 處理，比 `HandleCanvasInput` 先拿到事件。拖節點放在標籤上＝搬到那一頁並切過去（`NodeGroupTabAt`，在 `ApplyDropMembership` 裡）。`ExpandNodeGroupOf` 同時處理收合與切頁。

## 6. 串接 GraphKit（給 Tool 作者）

### 6.1 開啟文件

- 已知欄位時用 `HaruGraphWindow.OpenForDocument(owner, new HGDocumentBinding<TDocument>(documentId, get, set, create), context)`。每個文件欄位一個 binding、一個穩定的 `documentId`。
- `HaruGraphWindow.OpenFor(owner)` 是 legacy 入口，只接受恰好一個 `IGraphDocument` 欄位的 Owner。
- binding 的 setter 只能指派傳入的文件引用，不可原地修改舊文件或其他 Owner 資料，並要能接受 null。需要偵測原地外部修改時，提供無副作用的 `readRevision`，並在每個外部修改入口維護它。
- context 與 binding 不會跨 Domain Reload 保存；重新載入後要從 Tool 入口重開。

### 6.2 擴充點

| 需求 | 用法 |
|---|---|
| 自訂 Port | `IHGEditorExtensionProvider` 在 `HGPortBuildContext` 內 `AddInput`／`AddOutput`／`AddAggregate`；需要別名時 `AddInputAlias`／`AddOutputAlias` 再 `SelectPrimaryInput`／`SelectPrimaryOutput`（每個 Slot／來源只能選一次） |
| 自訂節點欄位描述 | `IHGEditorMetadataProvider` 提供完整 `HGNodeDescriptor`；提供後完整取代該型別的反射欄位 |
| 自訂值型別的輸入框 | `HGValueDrawer<T>`／`IHGValueDrawer`；**不要把領域型別加進 `HGValueField`** |
| 領域驗證 | Editor 端 `IHGEditorDiagnosticProvider`，或 Runtime Owner 實作 `IGraphDomainDiagnostics`，回傳 `GraphDiagnostic` |
| 非視窗編輯 | `HGDocumentSession<TDocument>.TryOpen` → `CreatePortRegistry` → `Connect`／`Disconnect`／`ReconnectInput`／`ReplaceSource`／`DeleteNode`／`EditValue` → `Commit` 或 `Cancel` |
| 非視窗清單新增 | `HGPortRegistry.AddListAppend(key, list, elementType, ownerNode, presentation)` 後 `Connect`：尾端新增一項並接上，一步 Undo。接受規則固定是元素型別的一般規則加「來源不可回頭用到 ownerNode」，不收自訂 policy；清單必須屬於 session 工作副本。PropertySlot 清單不支援（非視窗 session 本來就不處理 Property 寫入線） |
| 操作實際視窗 | `window.GetDocumentCommands()` 取得 `HGWindowSession`，`Query()` 讀快照，命令走視窗同一套交易 |

- Provider 只拿有界的描述與 builder，拿不到 live 的 `HGNodeView`／`HGRow`／`HGPort`。
- registry、Port key 與 handle 只在所屬 session 的目前 generation 有效；每次成功的命令、Undo、Redo、Commit、Cancel 之後都要重建 registry。

### 6.3 提交安全與遺失型別

- 提交順序：遺失型別清理 → DeepCopy → 副本 `InvalidateValidation()`／`Verify()` → 通過 → 版本檢查 → 寫回。驗證、衝突或寫入失敗都不清除 Dirty。
- 文件引用被其他入口替換時回 `Conflict`，保留工作副本與歷程。
- 遺失的 SerializeReference 型別在開圖、驗證、提交前自動清理，不備份、不詢問，只改記憶體並標記 Dirty。清理依 Unity 的 missing managed-reference ID 和序列化位置定位；Unity 回報 `-2` 時唯讀查詢 Owner 的 YAML `rid` 連結。**不可擴大成「刪掉所有 null」或「斷開所有不相容線」**。清理後仍須通過正常驗證才能存檔。

### 6.4 節點屬性

- `[HGNode(name, description, group, priority)]`（可加 `Width`，單位是 20px 格數）：節點名稱、說明、分類與排序。`Inherited = false`，**每個具體型別都要自己標**，否則節點名退回類別名。
- 欄位：`[HGLabel]`、`[HGDescription]`、`[HGHide]`（`[HideInInspector]` 視為相同）、`[HGShowIf]`（條件找不到時 fail-open 並記錄一次錯誤）、`[HGHideLabel]`、`[HGEnum]`、`[HGBool]`、`[HGFoldout]`。
- `[HGFoldout("群組名")]` 把欄位收進節點內的摺疊群組（`HGRowKind.Foldout`，對應 Odin `FoldoutGroup`）：同一個物件裡同名的欄位共用一組，群組列插在第一個成員的位置，成員接在標題列下面，與一般欄位同寬。每組各自展開／收起，可同時展開多組，預設展開；成員都沒長出列（含被 `[HGShowIf]` 整組藏掉）的群組不出現。descriptor 用各 factory 的 `foldout:` 參數。
  - 群組列的 `Height` 是整段：展開時＝標題列 ＋ 成員，收起時＝標題列。成員的 `FoldoutOwnerRow` 指向最近的群組（巢狀時外層靠群組列自己的 `FoldoutOwnerRow` 往上找）。`FinishFoldouts` 把整棵成員子樹的 `Depth` 加 1（巢狀群組逐層累加）。
  - 收起時成員壓到標題列（`CollapseRows`）並標成隱藏：接點不可見、不能起手或放線，但已接線的欄位照畫線，由標題列右緣伸出，同一組的線共用一把扇（`FoldedListOf` 先找清單鏈、再找群組鏈，回傳看得到的折疊祖先，和折疊清單走同一套 `RebuildFoldFan`）。代表接點由 `DrawFoldoutAnchorPort` 畫 ◎，不是 `HGPort`。群組名後的 ● 是組內有錯誤。
  - 展開狀態是版面，和清單折疊共用 `listCollapse`／`GraphViewState._folded`、`_unfolded`（key＝群組列的 `CollapseKey`，路徑是 `<物件路徑>/#<群組名>`）；切換走 `SetListFolded` → `MarkViewStateChanged()`，鎖定中也能切。`DefaultCollapsed` 對群組一律是 false。`RevealNode` 會沿父欄位把收起的群組一路展開。
  - 外觀：只畫標題列（`ListHeader`）與左側一條縱向導引線（`ListRule`，落在成員讓出的那格縮排裡），不畫底帶與外框。底帶＋外框是清單「凹進去的容器」的語彙；群組也用它的話，組內的清單和群組長得一樣、左緣重疊，分不出主次。只有箭頭與文字是開關，其餘空白留給拖曳節點。
  - 群組名不可含 `/`：保留給日後的群組路徑（對齊 Odin `FoldoutGroup` 的路徑語法），含 `/` 的欄位回報 `graphkit.metadata.foldout-path-reserved`（Warning）並畫在群組外。
- `[HGEnum]` 只作用在 enum：畫成按鈕列（`[Flags]` 多選），標在 bool 上沒有效果。
- `[HGBool(trueLabel = "是", falseLabel = "否")]` 把 bool 畫成兩段按鈕，左 true、右 false；沒標的 bool 是 14px 勾選框。和 `[HGEnum]` 一樣可標在欄位或 Slot 類別上，走同一批入口（`HGRow.BoolButtons`，類別用 `HGReflect.BoolButtonsOf`、欄位用 `BoolButtons`，descriptor 用 `HGFieldDescriptor.Create(..., boolButtons:)`）；欄位與類別都標時文字以欄位為準，欄位沒標不能關掉類別宣告。
- `[HGEnum]` 也可標在 Slot 類別上（子類沿用）：該族的 enum 常數框在一般欄位、descriptor 欄位、清單元素、Token／資產 HEAD、資產參數綁定與變數庫都畫按鈕列。入口是 `HGGraph.SlotRow` 與變數庫面板，兩者都問 `HGReflect.HasEnumButtons(slotType)`。欄位與類別是 OR：欄位只能再開啟，不能關掉類別的宣告；後續設定 `ForceEnumButtons` 一律用 `|=`，不可覆蓋。清單欄位標 `[HGEnum]` 時，`BuildListChildren` 把旗標帶給純值元素列，元素繪製讀 `row.ForceEnumButtons`。
- 常數是清單的 FormulaSlot（`DefaultEditType` 本身是 `List<T>`／`T[]`，且元素 `HGValueField.CanDraw`）會在 Slot 列下方多一段常數清單，資料來源就是 `DefaultObject`，沿用清單欄位的增刪、重排與元素繪製；null 時就地補空清單。這段清單不畫自己的標題列（`HGRow.HeaderRow` 指向代畫標題的 Slot 列，底帶與外框從那一列算起），由 Slot 列的標籤畫成「`▾` 名稱 (N)」兼任折疊開關（`HGRow.DefaultListRow`）；Slot 列標籤隱藏時才保留清單自己的標題列。一般欄位、descriptor 欄位與 HEAD 來源列都會掛，資產參數綁定列與「元素本身是 Slot 的清單」不掛。變數庫裡元素畫得出輸入框的清單（純值與資產皆是）逐項用 `HGValueField` 編輯，走 `AddInitialValue`／`SetInitialItem` 命令，一律就地改容器；資產清單另外保留從 Project 拖到標題列的加入方式。
- 新增清單元素一律走 `HGListItemSource.TryCreateElement`：資產元素是空引用，**不可用 `Activator` 建立 `UnityEngine.Object`**（`GameObject` 會直接生進目前場景）。
- Slot 族：`[HGKind(name, group, priority)]`。建立公式、新增 ProtoProperty、新增 Token 三個選單共用 `HGTypeIndex.FormulaKindOptions`；group 用 `/` 分多層，priority 越小越前面。同路徑同名的族附完整型別與組件名稱消歧義。Group／Priority 不影響族身分或相容性。
- 參考 `nunit.framework` 的測試組件不會出現在建立選單。

### 6.5 執行觀察

- 使用端實作 `IGraphExecutionDocument`；觀察者 `source.Observe()`，執行端每條執行鏈開一個 `GraphExecutionSession`，每次節點呼叫 `Enter(new GraphExecutionNodeKey(nodeId, scope))`，**執行內容之前** await `WaitAsync()`，之後回報 `Complete()`／`Fail()`／`Cancel()`。
- API 在主執行緒呼叫，不使用 UniTask。Hold 屬於特定 session；最後一個 observer 離開會解除 Hold。未儲存的文件、revision 不符或資產 Root 被替換時，不套用執行顏色也不能新增 Hold。

## 7. 常見修改的檢查清單

- [ ] 改的是工作副本，不是 Owner 上的文件
- [ ] 內容修改都經過 `Invalidate()`；收合版面（折疊、顯示開關、註解框）走視窗的 `Set*` 寫回 `GraphViewState` 並記成版面修改；選取、疊放、solo 等純視覺狀態只設 graphDirty＋Repaint，不標未存檔
- [ ] 換來源用 `SetBody`／`SetAsset`／`SetToken`／`SetProperty`，保留載體身分
- [ ] 斷線前考慮共用（還有別人在用的載體不能丟進候選池），失聯的載體要回候選池
- [ ] 動到參數列：量測（`MeasureRows`）和繪製（`DrawRows`）兩邊一起改
- [ ] 動到驗證規則：文件 `Verify()` 和 `HGValidator` 一起改
- [ ] 動到拉線流程：拖曳中改圖要重算相容性快取
- [ ] 選單回呼沒有使用 `Event.current`
- [ ] 新載體有 `EnsureId()`，複製有 `ResetNodeIds()`，`shared`／`skip` 集合都有考慮
- [ ] 資產焦點的修改走 `MarkAssetContentChanged()` 或 `MarkPositionsChanged()`
- [ ] 新增的視窗欄位宣告在 `HaruGraphWindow.cs`
- [ ] 沒有新增對 LogicGraph、AssetPipeline、UniTask 或使用端的引用
- [ ] 新增或改名的 `.cs` 都有 `.meta`（見 HaruKit 根目錄 `CONVENTIONS.md`）
- [ ] 本手冊與 README 已同步

## 8. 驗證

- `Editor/Tests/`：`HGPortTests`（Port 模型與相容）、`HGPublicConsumerTests`（只用公開 API 的非視窗與視窗命令、別名、descriptor 編輯、Undo／Redo、領域錯誤阻擋、commit／rebind／cancel）、`GraphExecutionTests`（執行觀察與 Hold）。
- AssetPipeline 的 `GraphKitCrossToolSessionTests` 也只透過公開 API 使用 GraphKit，可用來確認沒有破壞使用端。
- 在 Unity Test Runner 以 EditMode 執行。從 UPM 安裝時，要把套件名稱加進使用端 `Packages/manifest.json` 的 `testables` 才看得到測試。
- 手勢、IMGUI 版面、資產序列化來回，自動測試涵蓋不到，要在 Unity 實際操作確認。**沒有實際執行的測試不可回報為通過。**
