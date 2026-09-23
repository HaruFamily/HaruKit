namespace HaruFamily.DependencyCore.GraphKit.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// 一列的<b>視覺形狀</b>：畫幾顆接點、怎麼排。<b>不承載邏輯身分</b>。
    /// </summary>
    // 這個軸只回答「這一列長什麼樣」。「這一列掛的是什麼」由 payload 自己回答（HGRow.HasSlot／List／Field）。
    // 兩者不可互相推導：用 Kind 兼判 payload，每個呼叫點就得再補一次 row.InputSlot == null。
    public enum HGRowKind
    {
        /// <summary>沒有接點：一般值欄位，直接編輯。</summary>
        NoPort,
        /// <summary>右側一顆輸入接點：參數欄位，四種狀態（常數／公式／資產／Token）。</summary>
        InputPort,
        /// <summary>巢狀資料的分組標題。</summary>
        Group,
        /// <summary>清單型參數的標題列。折疊時標題列會畫一顆代表接點，那是 List 這個形狀自己的特例。</summary>
        List,
    }

    /// <summary>一次焦點的完整節點圖。每次資料變動就整份重建，不做增量。</summary>
    public class HGGraphView
    {
        public List<HGNodeView> Nodes = new();
        public List<HGLink> Links = new();
        public List<HGPort> Ports = new();
        public List<GraphDiagnostic> Diagnostics = new();
        public bool Normalized;
        public Dictionary<HGPortKey, HGPort> PortsByKey = new();
        public Dictionary<GraphNode, HGPort> PrimaryOutputs = new();
        public Dictionary<GraphSlotBase, HGPort> PrimaryInputs = new();

        // 同一個載體被多個欄位指到＝共用來源：只畫一個節點，連線各自一條。GraphNode 沒有覆寫 Equals，預設就是參考比對。
        public Dictionary<GraphNode, HGNodeView> ByCarrier = new();
        public Dictionary<GraphSlotBase, HGNodeView> BySlot = new();

        /// <summary>
        /// 這張圖裡有幾個欄位指著同一個載體。給「隱藏子樹時要不要留下共用節點」用——隱藏是視覺操作，
        /// 只算畫得出來的引用。要問「停用會影響幾個欄位」是全域問題，那走 HaruGraphWindow.CarrierUsers。
        /// </summary>
        public Dictionary<GraphNode, int> CarrierUsers = new();

    }

    /// <summary>編輯區上的一個節點。</summary>
    public class HGNodeView
    {
        public GraphNode Carrier;             // 這個節點的載體；HEAD 節點為 null（載體是頭端本身）
        public object Obj;                    // GraphNodeContent（公式 / 動作）；資產、Token、空節點為 null
        public UnityEngine.Object Asset;      // 資產節點目前指到的資產（可為 null＝尚未指定）
        public bool IsAssetNode;              // 資產節點（不論有沒有指定資產）
        /// <summary>這顆節點指到的具名Token（不是Token節點就是 null）。內容住在Token自己的畫布。</summary>
        public GraphToken Token;
        public bool IsTokenNode;           // Token節點（不論有沒有指定Token）
        /// <summary>這顆節點指到的 Property 定義（不是 Property 節點就是 null）。定義住圖層清單。</summary>
        public GraphProperty Property;
        /// <summary>Property 節點下方輸入的持久綁定；不是 ParentSlot 的重畫。</summary>
        public GraphPropertyInputSlot PropertyInput;
        public HGRow PropertyInputRow;
        /// <summary>Property 節點（不論有沒有指定定義）。讀它只取目前值，不執行寫入它的動作。</summary>
        public bool IsPropertyNode;
        /// <summary>Header 左緣那顆接點畫不畫。沒有任何欄位指得到的包節點沒有它：接不上就不該看得到圓。</summary>
        public bool HasOutputPort = true;
        public Type ResultType;               // 資產／Token節點的結果型別
        public string Id;
        public string Title;                  // Header 主文字＝具體型別／Token／資產名稱，節點靠它辨識
        public string Chip;                   // Header 右側的結果型別標籤（契約），null 就不畫
        public string Desc;
        public bool IsRoot;
        /// <summary>這顆是時機群組節點（Header＝時機名、本體＝該時機的動作清單）。刪除與右鍵選單都要認它。</summary>
        public bool IsTimingGroup;
        public bool IsPlaceholder;            // Slot 尚未指定具體 Action／Formula
        public bool IsActionNode;
        /// <summary>自己或某個祖先被停用：整段不會求值，畫布上要一起壓暗。多路徑共用時只要有一條啟用就是 false。</summary>
        public bool InDisabledSubtree;
        /// <summary>
        /// 自己或某個祖先掛在「未勾覆蓋」的資產參數底下：呼叫端根本不會採用這一段（資產用自己內部的預設），
        /// 所以畫布上除了壓暗還要鎖住，編了也不會生效。與 Disabled 不同：停用是暫時關掉，仍在編輯中。
        /// </summary>
        public bool InLockedSubtree;
        /// <summary>
        /// 被 Slot 的分支收合收起來：不畫、不命中、不能當拉線目標。純視覺，資料一點都沒變。
        /// 圖照樣建到底——引用數要走完整張圖才算得準，收起來只是最後一步的標記。
        /// </summary>
        public bool Hidden;
        public string Tips;
        /// <summary>註解框被打開但還沒有內容：只有這顆節點被選取時才成立，取消選取就收起來。</summary>
        public bool NoteOpen;

        public GraphSlotBase ParentSlot;      // 這個節點接在哪個 Slot 上（root / orphan 為 null）
        public HGRow ParentRow;

        public List<HGRow> Rows = new();
        public Rect TitleRect;                // Header 名稱區（graph space）：拖曳抓取區，繪製時寫入
        /// <summary>Header 右端的 ▾（graph space）：換來源的唯一入口。整塊名稱區可按會跟拖曳打架。</summary>
        public Rect SourceMenuRect;
        public Vector2 Pos;
        public float Width = HGGraph.NodeWidth;
        public float Height = 60f;
        public float ContentHeight;
        public float TipsHeight;
        // 換來源的入口是 Header 右端的 ▾；Root HEAD 的來源走它自己的「來源」參數列接點，所以不畫。
        public bool HasSourceSelector => !IsRoot && (IsPlaceholder || Obj != null || IsAssetNode || IsTokenNode || IsPropertyNode);

        public Rect Rect => new Rect(Pos.x, Pos.y, Width, Height);
        public Vector2 OutputPortPosition => new Vector2(Pos.x + HGGraph.PortRadius,
            Pos.y + HGGraph.HeaderHeight * 0.5f);

        public Vector2 PropertyInputPortPosition => new Vector2(Pos.x + HGGraph.PortRadius,
            Pos.y + HGGraph.HeaderHeight + (Carrier?.IsProtoProperty == true ? HGGraph.RowHeight : 0f) + HGGraph.RowHeight * 0.5f);

    }

    /// <summary>節點上的一列。形狀走 <see cref="Kind"/>，掛了什麼走 payload 欄位，兩者不互相推導。</summary>
    public class HGRow
    {
        /// <summary>視覺形狀。要問「這一列掛了什麼」請用 <see cref="HasSlot"/>／<see cref="List"/>／<see cref="Field"/>。</summary>
        public HGRowKind Kind;
        public string Label;
        public int Depth;

        public GraphSlotBase InputSlot;  // payload：欄位的輸入
        public Type ResultType;          // Slot 的結果型別；ActionSlot 為 null
        public bool IsActionSlot;

        /// <summary>這一列寫入 Property：接點與線改用輸出色，不畫常數框。</summary>
        public bool IsProducedValue;
        public NamedFormulaSlot AssetBinding;

        public object Target;            // payload：一般值欄位所屬的物件
        public FieldInfo Field;
        public HGFieldDescriptor Descriptor;
        public IHGValueDrawer ValueDrawer;

        /// <summary>payload：這一列自己承載一整段項目（List 形狀）。</summary>
        // 「承載一段」與「屬於某一段的某一項」是兩件事，用兩組欄位表示，不從 ItemIndex 是不是 -1 推。
        public HGItemSource Items;
        public bool Collapsed;           // 只對 List 形狀有意義：折疊時子列不畫、不可互動

        /// <summary>代畫這段清單標題的 Slot 列（常數清單）。有值時清單自己不畫標題列、高度為 0，底帶與外框從這一列算起。</summary>
        public HGRow HeaderRow;

        /// <summary>清單自己的標題列不畫，標題由 <see cref="HeaderRow"/> 代畫。</summary>
        public bool HeaderHidden => HeaderRow != null;

        /// <summary>Slot 列下方的常數清單（見 AddDefaultListRow）。有值時這一列的標籤兼任該清單的折疊開關與項數。</summary>
        public HGRow DefaultListRow;

        public List<HGRow> Children = new();

        /// <summary>欄位在節點內的唯一路徑（`/action/steps[2]/value`），折疊狀態靠它記憶。</summary>
        public string Path;

        /// <summary>這一列屬於哪個節點。折疊與分支收合的 key 都是「節點 Id + Path」，繪製時不必再回頭找主人。</summary>
        public string OwnerNodeId;

        /// <summary>項目本體，而且項目控制項由共用繪製路徑代畫（序號欄、把手、✕）。展開出來的子列不畫。</summary>
        // 自己有繪製路徑的來源（容器的格子）不設這個旗標，它的 ✕ 位置與命中都不一樣。
        public bool IsItem;

        /// <summary>這一列不會被採用（沒勾覆蓋的資產參數，或整顆節點在鎖定子樹裡）：不可編、不可接線。</summary>
        public bool Locked;

        /// <summary>
        /// 這一列屬於哪一段項目的第幾項。**項目展開出來的子列也會帶著它**，斑馬紋才涵蓋整段；
        /// 只有項目標題有底、內部欄位沒有的話，看起來會像清單只有一行。
        /// </summary>
        public HGItemSource ItemSource;
        public int ItemIndex = -1;

        /// <summary>所屬的清單標題列。重排的插入位置要靠它的 <see cref="Children"/> 算，容器的格子沒有這一列。</summary>
        // 它同時是「要不要畫底帶」的判準：格子的底是自己畫的，沒有標題列包住整段。
        public HGRow ItemOwnerRow;

        /// <summary>
        /// 左側額外留白。清單元素要留位置給序號／拖曳把手，而**它展開出來的子列也必須繼承**，
        /// 否則子列會比自己的父標題還靠左，看起來像壞掉。
        /// </summary>
        public float LeftPad;

        // 視覺 metadata（欄位宣告帶來的畫法偏好，與 Kind 和 payload 都無關）
        /// <summary>欄位標了 <c>[HGEnum]</c>。最終畫不畫 enum 按鈕列仍要另算：替代預設值型別是 enum 時沒標也要畫。</summary>
        public bool ForceEnumButtons;
        public bool HideLabel;
        public int LabelWidthUnits;
        public float LabelWidthRatio;
        public bool Normalized;
        public string DrawerError;

        // 排版結果（每次重畫填）
        public float LocalY;
        public float Height;
        public float AddRowY;            // 清單列的「新增項目」列位置
        public bool Hidden;              // 被折疊的清單蓋住：不畫、不畫接點、不可當拉線目標
        public Rect ScreenRect;
        public Vector2 InputPortPosition;

        /// <summary>
        /// 這一列掛著一個欄位。<b>payload 判定，與 <see cref="Kind"/> 無關</b>——
        /// 形狀說的是畫幾顆接點，這裡說的是有沒有東西可以接。
        /// </summary>
        public bool HasSlot => InputSlot != null;

        /// <summary>右側輸入接點畫不畫。</summary>
        public bool HasInputPort => Kind == HGRowKind.InputPort;

        /// <summary>右側輸入接點是否可見：折疊起來的列不算，否則會接到看不見的東西。</summary>
        public bool IsInputPortVisible => HasInputPort && !Hidden;

    }

    public class HGLink
    {
        /// <summary>Resolved generation-local endpoints. Runtime data remains Slot -> GraphNode.</summary>
        public HGPort InputPort;
        public HGPort OutputPort;

        public HGRow ParentRow;

        /// <summary>提供端所在的節點。目標是容器上的一格時，這裡是那顆<b>容器</b>。</summary>
        public HGNodeView OutputOwner;

        /// <summary>目標是容器上的一格（InputOutputPort）時的那一列；null＝接在節點 Header 的輸出接點。</summary>
        public HGRow TargetRow;

        /// <summary>接收端所在的節點。父節點被收起來時線也要跟著不畫，否則會留一條從空白處拉出的線。</summary>
        public HGNodeView InputOwner;

        /// <summary>待解析的目標載體。容器可能比指著它的欄位更晚走到，所以解析留到建圖最後一趟。</summary>
        public GraphNode PendingCarrier;
    }

    /// <summary>
    /// 由焦點根 Slot 遞迴展開節點圖：節點 → 參數列 → 子節點，並套用記憶座標或樹狀自動排版。
    /// </summary>
    public static class HGGraph
    {
        public const float RowHeight = 20f;
        public const float HeaderHeight = 24f;
        public const float PortRadius = 7f;
        public const float PortDiameter = PortRadius * 2f;
        public const float GridSize = 20f;
        /// <summary>節點最後一列與下緣之間的留白：只求緊鄰排列時不黏在一起，不吃格線對齊。</summary>
        public const float NodeBottomPad = 3f;
        public const float IndentWidth = 12f;
        public const float ColumnGap = 90f;
        public const float NodeGap = 24f;
        // 預設所有節點同寬：接點排成一條垂直線、AutoLayout 的欄位不會因父節點文字長度而漂移。
        // 只有型別明確標了 [HGNodeView(Width = n)] 才例外——寬度不一會讓同一欄的右緣（接點）不成直線，
        // 是拿對齊感換欄位空間，不是預設值。300 = 15 格。
        public const float NodeWidth = 300f;

        /// <summary>
        /// 型別 chip 的欄寬（格）。chip 排在列右端（`常數框 | ✕ | chip | 接點`），**只有畫得出 chip 的列讓**，
        /// 純值列不留白。標籤與常數框的左緣因此永遠齊，右緣則是有 chip 的列窄一截。
        /// 3 格＝60px，扣掉 `SlotChipGap` 後 chip 本體 56px，`Entity`／`String` 這種 6 字母的型別名也不必截字。
        /// </summary>
        public const int SlotChipUnits = 3;
        public const float SlotChipColumn = SlotChipUnits * GridSize;
        /// <summary>chip 與標籤之間的間距，含在 chip 欄內。</summary>
        public const float SlotChipGap = 4f;

        /// <summary>沒指定時，標籤欄佔一列的比例。算完往上進位到整格，欄寬才和宣告單位同一套。</summary>
        // 0.3：預設寬節點（15 格）給標籤 5 格＝100px，約 8 個中文字。再高一階會進位到 6 格，常數框就少一格。
        public const float LabelRatio = 0.3f;

        /// <summary>
        /// 一列的標籤欄寬度（px）。`overrideUnits`＝`[HGLabel(Width = n)]` 的格數，`overrideRatio`＝`[HGLabel(WidthRatio = n)]` 的 0～1；
        /// 兩個都是 0 就走預設比例。格數是絕對值直接乘；比例路徑一律進位到整格，欄寬永遠落在格線上。
        /// 預設寬的節點（15 格）＝ 300 × 0.3 = 90px → 進位 5 格 = 100px。
        /// </summary>
        // 比例不設上限：節點變寬時標籤跟著長，和 Unity Inspector 拉寬時的行為一致。
        // 指定值不夾範圍：這個數字寫在原始碼裡，標歪了畫面當場看得出來，夾掉只會讓人以為屬性沒生效。
        // Width 優先於 WidthRatio。兩個都標不報錯：這是編輯器排版，標錯畫面當場看得出來，Log 只會洗版。
        public static float LabelWidthOf(float rowWidth, int overrideUnits, float overrideRatio)
        {
            if (overrideUnits > 0) return overrideUnits * GridSize;

            float ratio = overrideRatio > 0f ? overrideRatio : LabelRatio;
            return SnapUpToGrid(rowWidth * ratio);
        }

        /// <summary>清單元素左側的控制欄：序號與拖曳把手各佔一半，兩者都常態顯示。</summary>
        public const float ListGutter = 30f;
        /// <summary>清單元素右側保留給刪除鈕的寬度。永遠保留（hover 才畫），欄位寬度才不會跳動。</summary>
        public const float ListDeleteWidth = 16f;
        /// <summary>超過這個項數的清單預設折疊：不折的話一個動作序列就能把節點撐到幾百 px 高。</summary>
        public const int ListAutoCollapseCount = 6;

        private static readonly HashSet<string> SkipFields = new()
    {
        "_dictKey",
    };

        /// <summary>
        /// 建圖。每個 root 畫成一顆固定 HEAD；orphans 是本焦點的候選節點（含拖進畫布的獨立 Token／資產節點）。
        /// headTitle 是編輯對象自己的名稱（動作標籤／Token名／資產名），直接當 HEAD 的 Header。
        ///
        /// root 有兩種：多數焦點給的是**一個** Slot 頭端；Timing 焦點給的是**全部** ActionTimingGroup 物件，
        /// 每個畫成一顆節點，本體就是那個時機的動作清單。同一張畫布才拉得到跨時機的共用來源。
        /// </summary>
        public static HGGraphView Build(HGModel model, IReadOnlyList<object> roots, IList orphans, string focusId,
            string headTitle, IReadOnlyDictionary<string, bool> listCollapse = null,
            string noteOpenId = null, ICollection<string> noteCollapsed = null, object headCarrier = null,
            IReadOnlyDictionary<string, Type> orphanHints = null, IHGEditorMetadataProvider metadata = null)
        {
            var view = new HGGraphView();
            try
            {

            // 每次重建都重新登記 id → 載體，座標與備註的讀寫才找得到人。
            model.ClearCarriers();

            // 一顆 HEAD 都沒有仍要往下走：時機畫布可能還沒建任何時機節點，但候選節點得畫得出來。
            var rootIds = new HashSet<string>();
            foreach (var root in roots ?? Array.Empty<object>())
            {
                if (root == null) continue;
                var rootNode = root is GraphSlotBase rootSlot
                    ? MakeHeadNode(model, rootSlot, focusId, headTitle, headCarrier)
                    : MakeGroupNode(model, root, metadata, view.Diagnostics);
                if (!rootIds.Add(rootNode.Id)) throw new InvalidOperationException("Duplicate root identity: " + rootNode.Id);
                Collect(model, rootNode, view, 0, listCollapse, false, false, metadata, view.Diagnostics);
            }

            if (orphans != null)
            {
                foreach (var o in orphans)
                {
                    if (o is not GraphNode carrier) continue;
                    if (view.ByCarrier.ContainsKey(carrier)) continue;
                    // 候選沒有父欄位，型別只能靠建立當下記下的族。沒有族就是純空節點，接上欄位後自然有型別。
                    Type hint = null;
                    orphanHints?.TryGetValue(carrier.EnsureId(), out hint);
                    // 候選不需要額外標記：沒有連入線本身就是訊號。
                    var node = MakeNodeForCarrier(model, carrier, null, null, hint, metadata, view.Diagnostics);
                    Collect(model, node, view, 0, listCollapse, false, false, metadata, view.Diagnostics);
                }
            }

            ApplyViewState(model, view, noteOpenId, noteCollapsed);
            foreach (var node in view.Nodes)
                foreach (var row in AllRows(node.Rows))
                    if (row.Normalized) view.Normalized = true;
            AutoLayout(model, view);
            foreach (var node in view.Nodes)
                foreach (var row in AllRows(node.Rows))
                    if (!string.IsNullOrEmpty(row.DrawerError))
                        view.Diagnostics.Add(new GraphDiagnostic("graphkit.metadata.drawer-failed", GraphDiagnosticSeverity.Error,
                            row.DrawerError, new GraphDiagnosticLocation(nodeId: node.Id, fieldPath: row.Path)));
            return view;
            }
            catch (Exception exception)
            {
                var failed = new HGGraphView();
                failed.Diagnostics.Add(new GraphDiagnostic("graphkit.build.failed", GraphDiagnosticSeverity.Error,
                    exception.Message, new GraphDiagnosticLocation(model?.DocumentId, focusId)));
                return failed;
            }
        }

        // ===== 節點建立 =====

        /// <summary>
        /// 把一個 `ActionTimingGroup` 畫成 HEAD：Header 是時機名，本體就是那個時機的動作清單，
        /// 所以每個動作直接是清單的一列——序號、拖曳把手、刪除鈕、折疊、斑馬紋全部沿用清單那一套，
        /// 不需要為動作另做一組互動。一張畫布上有幾個時機就有幾顆。
        /// </summary>
        private static HGNodeView MakeGroupNode(HGModel model, object group, IHGEditorMetadataProvider metadata,
            List<GraphDiagnostic> diagnostics)
        {
            var node = MakeNodeForObject(group, null, null, null, metadata, diagnostics);
            node.Id = GroupHeadId(model, group);
            node.IsRoot = true;
            node.IsTimingGroup = true;
            // ActionTimingGroup 不是 ActionBase，但它的本體是 ActionSlot 清單，Header 應導向 Action 流程色。
            node.IsActionNode = true;
            node.Title = GroupTitle(model, group);
            // 群組不回傳值，chip 改寫身分：一眼分得出 root 與一般節點。文字由圖的契約提供，編輯器不寫死領域用詞。
            node.Chip = model?.Doc?.RootChip;
            node.Desc = null;

            model.RegisterCarrier(node.Id, group);
            return node;
        }

        /// <summary>時機群組節點的識別碼。識別值本身就是身分，不可重複，所以不必再配流水號。</summary>
        public static string GroupHeadId(HGModel model, object group) => "head:" + model.RootId(group);

        public static string GroupTitle(HGModel model, object group)
            => model?.Doc?.TitleOf(group) ?? $"（未指定{RootNoun(model?.Doc)}）";

        /// <summary>root 在句子裡的稱呼。圖沒提供時退回中性詞，UI 不會出現空字。</summary>
        public static string RootNoun(IGraphDocument doc)
            => string.IsNullOrWhiteSpace(doc?.RootNoun) ? "群組" : doc.RootNoun;

        /// <summary>圖有沒有啟用這組能力。沒綁定時一律當作沒有：沒有 Doc 就沒有內容，畫出來一定是空的。</summary>
        public static bool Has(IGraphDocument doc, HGCapabilities capability)
            => doc != null && (doc.Capabilities & capability) == capability;

        /// <summary>Checks the explicit Tool profile when present, then preserves the legacy document declaration.</summary>
        public static bool Has(HGEditorExtensionContext context, IGraphDocument doc, HGCapabilities capability)
        {
            var capabilities = context?.CapabilitiesOf(doc) ?? HGCapabilities.None;
            return (capabilities & capability) == capability;
        }

        /// <summary>視窗標題。沒綁定或圖沒提供時退回底層自己的名字。</summary>
        public static string WindowTitle(IGraphDocument doc)
            => string.IsNullOrWhiteSpace(doc?.WindowTitle) ? DefaultWindowTitle : doc.WindowTitle;

        /// <summary>還沒綁定任何圖時的視窗標題。</summary>
        public const string DefaultWindowTitle = "HaruGraph";

        // headCarrier：HEAD 的座標主人（HGFocus.HeadCarrier）。Token焦點傳 GraphToken、資產本體傳資產 SO，
        // 位置才記得住——資產的 HEAD 容器槽是每次進來現做的，記在它上面等於不記。其他焦點沿用 rootSlot。
        private static HGNodeView MakeHeadNode(HGModel model, GraphSlotBase rootSlot, string focusId, string headTitle, object headCarrier)
        {
            bool isAction = HGReflect.IsActionSlotType(rootSlot.GetType());
            Type resultType = isAction ? null : HGReflect.ResultType(rootSlot.GetType());
            var node = new HGNodeView
            {
                Id = HeadId(focusId),
                // 名字由焦點提供；真的沒有名字時給預設值，不留空白 Header。
                Title = string.IsNullOrWhiteSpace(headTitle) ? (isAction ? "（動作）" : "（頭端）") : headTitle,
                Chip = ChipText(isAction ? null : rootSlot?.GetType(), resultType, isAction),
                ParentSlot = rootSlot,
                IsRoot = true,
                IsActionNode = isAction,
                ResultType = resultType,
            };
            var sourceRow = SlotRow(rootSlot, "來源", 0);
            node.Rows.Add(sourceRow);
            AddDefaultListRow(node.Rows, sourceRow, null);
            model.RegisterCarrier(node.Id, headCarrier ?? rootSlot);
            return node;
        }

        public static string HeadId(string focusId) => "head:" + (focusId ?? "?");

        /// <summary>一個載體＝一個節點。內容種類決定畫成公式／動作、資產葉或編輯中的空節點。</summary>
        private static HGNodeView MakeNodeForCarrier(HGModel model, GraphNode carrier, GraphSlotBase parentSlot, HGRow parentRow,
            Type hintSlotType = null, IHGEditorMetadataProvider metadata = null, List<GraphDiagnostic> diagnostics = null)
        {
            string id = carrier.EnsureId();
            // 候選節點沒有父欄位，用建立時記下的族當代表；有父欄位時一律以父欄位為準。
            Type slotType = parentSlot?.GetType() ?? hintSlotType;
            bool slotIsAction = slotType != null && HGReflect.IsActionSlotType(slotType);
            Type slotResultType = slotType != null && !slotIsAction ? HGReflect.ResultType(slotType) : null;

            HGNodeView node;
            switch (carrier.Kind)
            {
                case NodeKind.Inline when carrier.BodyObject != null:
                    node = MakeNodeForObject(carrier.BodyObject, parentSlot, parentRow, slotResultType, metadata, diagnostics);
                    break;

                case NodeKind.Asset:
                    {
                        Type assetResult = slotResultType ?? HGReflect.AssetResultType(carrier.AssetObject);
                        node = new HGNodeView
                        {
                            Asset = carrier.AssetObject,
                            IsAssetNode = true,
                            ResultType = assetResult,
                            // Header 只表明身分；選哪一個資產是本體的參數列在做。
                            Title = "Asset",
                            Chip = ChipText(slotIsAction ? null : slotType, assetResult, assetResult == null),
                        };
                        foreach (var binding in carrier.Bindings)
                        {
                            if (binding?.Slot == null) continue;
                            var row = SlotRow(binding.Slot, binding.Name, 0);
                            row.AssetBinding = binding;
                            // Path 帶族：同名不同族是兩列，只用名字會讓兩列共用折疊狀態與焦點。
                            row.Path = "/binding/" + (binding.Slot.FamilyType?.Name ?? "?") + "/" + binding.Name;
                            node.Rows.Add(row);
                        }
                        break;
                    }

                // 與Token節點同構：定義住圖層清單，節點只是引用，所以本體也是一列「選哪一顆」的下拉。
                // 差別是 Header 直接顯示名稱——Property 沒有可下鑽的畫布，名稱是它在圖上唯一的識別。
                case NodeKind.Property:
                    {
                        var property = carrier.Property;
                        Type propertyResult = property?.ResultType;
                        node = new HGNodeView
                        {
                            Property = property,
                            PropertyInput = carrier.PropertyInput,
                            IsPropertyNode = true,
                            ResultType = propertyResult,
                            Title = carrier.IsProtoProperty ? "ProtoProperty" : "LocalProperty",
                            Chip = property?.FamilyType == null ? "未定型" : ChipText(property.FamilyType, propertyResult, false),
                        };
                        break;
                    }

                case NodeKind.Token:
                    {
                        var endpoint = carrier.Token;
                        Type variableResult = endpoint?.ResultType ?? slotResultType;
                        node = new HGNodeView
                        {
                            Token = endpoint,
                            IsTokenNode = true,
                            ResultType = variableResult,
                            // 與資產節點同一種版型：Header 只表明身分，選哪一個Token是本體那一列在做。
                            Title = "Token",
                            Chip = ChipText(endpoint?.FamilyType ?? (slotIsAction ? null : slotType), variableResult, false),
                        };
                        break;
                    }

                default:
                    {
                        // 寫入目標尚未指定 Property 時，仍要保留它的身分與來源選單；
                        // 否則會落入一般公式 placeholder，顯示錯誤的黃色「選擇 Formula」。
                        var propertySlot = parentSlot as PropertySlotBase;
                        bool isProperty = propertySlot != null;
                        Type propertyFamily = propertySlot?.FamilyType;
                        Type propertyResult = propertyFamily != null ? HGReflect.ResultType(propertyFamily) : slotResultType;
                        node = new HGNodeView
                        {
                            Title = isProperty ? "（選擇 Property）" : slotIsAction ? "（選擇 Action）" : "（選擇 Formula）",
                            Chip = ChipText(isProperty ? propertyFamily : slotIsAction ? null : slotType, propertyResult, slotIsAction),
                            ResultType = propertyResult,
                            IsPlaceholder = true,
                            IsActionNode = !isProperty && slotIsAction,
                            IsPropertyNode = isProperty,
                        };
                        break;
                    }
            }

            node.Carrier = carrier;
            node.Id = id;
            node.ParentSlot = parentSlot;
            node.ParentRow = parentRow;
            model.RegisterCarrier(id, carrier);
            return node;
        }

        /// <summary>
        /// Header 右側的契約標籤：知道是哪一族就標族名（String 與 Key 才分得開），
        /// 推不出族才退回結果型別短名；動作沒有結果型別，一律標 Action。
        /// </summary>
        // 候選節點沒有父欄位可問，但節點型別自己就屬於某一族，所以再問一次 NodeKindName——
        // 否則同一顆節點接上去叫「動態資產」、落到候選池卻變成 List<Object>。
        private static string ChipText(Type slotType, Type resultType, bool isAction, Type bodyType = null)
        {
            if (!isAction && slotType != null) return HGReflect.SlotKindName(slotType);
            if (!isAction && HGReflect.NodeKindName(bodyType) is string kind) return kind;
            if (resultType != null) return HGReflect.ResultTypeName(resultType);
            return isAction ? "Action" : null;
        }

        private static HGNodeView MakeNodeForObject(object obj, GraphSlotBase parentSlot, HGRow parentRow, Type slotResultType,
            IHGEditorMetadataProvider metadata, List<GraphDiagnostic> diagnostics)
        {
            bool isAction = HGReflect.IsActionNodeType(obj.GetType());
            Type resultType = !isAction && slotResultType != null
                ? slotResultType
                : obj is ITypedFormulaNode formula
                ? formula.ResultType
                : HGReflect.FormulaResultType(obj.GetType());
            var node = new HGNodeView
            {
                Obj = obj,
                ParentSlot = parentSlot,
                ParentRow = parentRow,
                Title = HGReflect.TypeName(obj.GetType()),
                Chip = ChipText(isAction ? null : parentSlot?.GetType(), resultType, isAction, obj.GetType()),
                Desc = HGReflect.TypeDescription(obj.GetType()),
                IsActionNode = isAction,
                ResultType = resultType,
            };
            BuildRows(obj, 0, node.Rows, new HashSet<object>(HGRefComparer.Instance), "", 0f, metadata, diagnostics);
            return node;
        }

        /// <summary>把節點與其子樹加入視圖。已經畫過的載體只補一條連線，不重複建節點。</summary>
        private static void Collect(HGModel model, HGNodeView node, HGGraphView view, int depth,
            IReadOnlyDictionary<string, bool> listCollapse, bool disabled, bool locked, IHGEditorMetadataProvider metadata,
            List<GraphDiagnostic> diagnostics)
        {
            if (depth > 24) return;                       // 資料異常時不讓編輯器堆疊爆掉
            node.InDisabledSubtree = disabled || (node.Carrier != null && node.Carrier.Disabled);
            node.InLockedSubtree = locked;
            view.Nodes.Add(node);
            if (node.Carrier != null) view.ByCarrier[node.Carrier] = node;
            if (node.ParentSlot != null) view.BySlot[node.ParentSlot] = node;

            // 容器的子節點各自是一顆完整節點：沒有 ParentSlot，所以不從擁有者畫一條線過去——
            // 它們的連入線來自真正指著它們的那些欄位。
            if (node.Obj is IGraphNodeOwner owner)
            {
                foreach (var child in owner.ChildNodes)
                {
                    if (child == null) continue;
                    if (view.ByCarrier.ContainsKey(child)) continue;

                    var childNode = MakeNodeForCarrier(model, child, null, null, null, metadata, diagnostics);
                    Collect(model, childNode, view, depth + 1, listCollapse, node.InDisabledSubtree, locked, metadata, diagnostics);
                }
            }
            // 節點 Id 到這裡才確定，所以列的歸屬也在這裡補；折疊與分支收合都靠它組 key。
            // 鎖定＝這一段不會被採用：整顆節點在鎖定子樹裡，或這一列自己是沒勾覆蓋的資產參數。
            foreach (var row in AllRows(node.Rows))
            {
                row.OwnerNodeId = node.Id;
                row.Locked = node.InLockedSubtree || (row.AssetBinding != null && !row.AssetBinding.OverrideEnabled);
            }

            // 折疊狀態要在量測之前套用：節點高度直接受它影響。節點 Id 到這裡才確定，所以不能在 BuildRows 做。
            ApplyListCollapse(node, listCollapse);
            MeasureNode(node);

            foreach (var row in AllRows(node.Rows))
            {
                if (!row.HasSlot) continue;

                var carrier = row.InputSlot.Node;
                if (carrier == null) continue;            // 常數／空槽留在列上，不長節點

                view.CarrierUsers.TryGetValue(carrier, out int users);
                view.CarrierUsers[carrier] = users + 1;

                // 共用來源：同一個載體被多個欄位指到時只有一個節點，這裡只補連線。
                if (view.ByCarrier.TryGetValue(carrier, out var existing))
                {
                    // 讀寫關係都保存；Port resolver 依 Slot 契約解析實際方向。
                    view.Links.Add(new HGLink { ParentRow = row, OutputOwner = existing, InputOwner = node });
                    view.BySlot[row.InputSlot] = existing;
                    // 這條路徑沒被停用就整顆恢復：共用節點只要還有一條會求值的路徑，它就不是停用的。
                    bool rowLocked = row.AssetBinding != null && !row.AssetBinding.OverrideEnabled;
                    if (!node.InDisabledSubtree && !rowLocked && !RowCarrierDisabled(row)) ClearDisabledSubtree(existing, view);
                    if (!node.InLockedSubtree && !rowLocked) ClearLockedSubtree(existing, view);
                    continue;
                }

                var child = MakeNodeForCarrier(model, carrier, row.InputSlot, row, null, metadata, diagnostics);
                bool bindingOff = row.AssetBinding != null && !row.AssetBinding.OverrideEnabled;
                Collect(model, child, view, depth + 1, listCollapse,
                    node.InDisabledSubtree || bindingOff || RowCarrierDisabled(row),
                    node.InLockedSubtree || bindingOff, metadata, diagnostics);

                // 連線在這裡建，父節點才記得住：畫線時要靠它判斷「線的起點還在不在畫面上」。
                // 超過深度上限被擋掉的子節點沒有進圖，也就不該有線。
                if (view.ByCarrier.TryGetValue(carrier, out var placed) && ReferenceEquals(placed, child))
                    view.Links.Add(new HGLink { ParentRow = row, OutputOwner = child, InputOwner = node });
            }

            CollectPropertyInput(model, node, view, depth, listCollapse, metadata, diagnostics);
        }

        private static void CollectPropertyInput(HGModel model, HGNodeView node, HGGraphView view, int depth,
            IReadOnlyDictionary<string, bool> listCollapse, IHGEditorMetadataProvider metadata, List<GraphDiagnostic> diagnostics)
        {
            if (!node.IsPropertyNode || node.PropertyInput == null) return;
            var row = new HGRow
            {
                Kind = HGRowKind.InputPort,
                InputSlot = node.PropertyInput,
                OwnerNodeId = node.Id,
                Path = "/property/input",
            };
            node.PropertyInputRow = row;
            // 寫入關係由 Action 的 PropertySlot 保存；此接收端不另外求值來源子樹。
        }

        /// <summary>這一列自己的載體被停用了：掛在它右側的子樹跟著壓暗。</summary>
        private static bool RowCarrierDisabled(HGRow row) => false;

        /// <summary>
        /// 共用節點先被停用路徑走到、之後又被啟用路徑指上時，把整棵子樹的壓暗狀態撤回。
        /// 自己被明確停用的節點不撤——那不是繼承來的。已經是 false 就直接回，順便擋住環。
        /// </summary>
        private static void ClearDisabledSubtree(HGNodeView node, HGGraphView view)
        {
            if (node == null || !node.InDisabledSubtree) return;
            if (node.Carrier != null && node.Carrier.Disabled) return;
            node.InDisabledSubtree = false;

            foreach (var row in AllRows(node.Rows))
            {
                if (row.InputSlot == null) continue;
                if (view.BySlot.TryGetValue(row.InputSlot, out var child)) ClearDisabledSubtree(child, view);
            }
        }

        /// <summary>共用節點被一條「有勾覆蓋」的路徑指上時撤回鎖定：只要有一條路徑會被採用，它就不是鎖的。</summary>
        private static void ClearLockedSubtree(HGNodeView node, HGGraphView view)
        {
            if (node == null || !node.InLockedSubtree) return;
            node.InLockedSubtree = false;

            foreach (var row in AllRows(node.Rows))
            {
                row.Locked = row.AssetBinding != null && !row.AssetBinding.OverrideEnabled;
                if (row.InputSlot == null) continue;
                if (view.BySlot.TryGetValue(row.InputSlot, out var child)) ClearLockedSubtree(child, view);
            }
        }

        /// <summary>清單折疊狀態的鍵：節點 Id + 欄位路徑，重建圖之後仍然指到同一個清單。</summary>
        public static string CollapseKey(string nodeId, HGRow row) => nodeId + "#" + row.Path;

        /// <summary>沒有明確記錄過的清單，項數多就預設折疊。</summary>
        private static bool DefaultCollapsed(HGRow row) => (row.Items?.Count ?? 0) > ListAutoCollapseCount;

        private static void ApplyListCollapse(HGNodeView node, IReadOnlyDictionary<string, bool> listCollapse)
        {
            foreach (var row in AllRows(node.Rows))
            {
                if (row.Kind != HGRowKind.List) continue;
                row.Collapsed = listCollapse != null && listCollapse.TryGetValue(CollapseKey(node.Id, row), out bool stored)
                    ? stored
                    : DefaultCollapsed(row);
            }
        }

        public static IEnumerable<HGRow> AllRows(List<HGRow> rows)
        {
            foreach (var r in rows)
            {
                yield return r;
                foreach (var c in AllRows(r.Children)) yield return c;
            }
        }

        // ===== 參數列 =====

        private static void BuildRows(object obj, int depth, List<HGRow> into, HashSet<object> visited, string path, float leftPad,
            IHGEditorMetadataProvider metadata, List<GraphDiagnostic> diagnostics)
        {
            if (obj == null || depth > 5 || !visited.Add(obj)) return;

            if (metadata != null && metadata.TryGetNodeDescriptor(obj.GetType(), out var descriptor))
            {
                foreach (var field in descriptor.Fields)
                {
                    string fieldPath = path + "/" + field.Id;
                    try
                    {
                    if (!field.TryGetVisibility(obj, out bool visible, out var exception))
                    {
                        diagnostics?.Add(new GraphDiagnostic("graphkit.metadata.visibility-failed", GraphDiagnosticSeverity.Error,
                            $"{obj.GetType().FullName}.{field.Id} visibility predicate failed: {exception.Message}",
                            new GraphDiagnosticLocation(fieldPath: fieldPath)));
                    }
                    if (!visible) continue;

                    if (field.Role == HGFieldRole.Slot)
                    {
                        object value = field.Read(obj);
                        bool normalized = false;
                        Exception factoryException = null;
                        if (value == null && field.TryCreateMissing(obj, out value, out factoryException)) normalized = true;
                        if (factoryException != null)
                        {
                            diagnostics?.Add(new GraphDiagnostic("graphkit.metadata.factory-failed", GraphDiagnosticSeverity.Error,
                                $"{obj.GetType().FullName}.{field.Id} null factory failed: {factoryException.Message}",
                                new GraphDiagnosticLocation(fieldPath: fieldPath)));
                        }
                        if (value is not GraphSlotBase slot) continue;
                        var row = SlotRow(slot, field.Label, depth);
                        row.Path = fieldPath;
                        row.LeftPad = leftPad;
                        row.Target = obj;
                        row.Descriptor = field;
                        row.Normalized = normalized;
                        ApplyDescriptorPresentation(row, field);
                        into.Add(row);
                        AddDefaultListRow(into, row, diagnostics);
                        continue;
                    }

                    if (field.Role == HGFieldRole.Group)
                    {
                        object value = field.Read(obj);
                        if (value == null) continue;
                        var group = new HGRow
                        {
                            Kind = HGRowKind.Group,
                            Label = field.Label,
                            Depth = depth,
                            Path = fieldPath,
                            LeftPad = leftPad,
                            Target = obj,
                            Descriptor = field,
                        };
                        ApplyDescriptorPresentation(group, field);
                        BuildRows(value, depth + 1, group.Children, visited, group.Path, leftPad, metadata, diagnostics);
                        if (group.Children.Count > 0) into.Add(group);
                        continue;
                    }

                    if (field.Role == HGFieldRole.List)
                    {
                        object value = field.Read(obj);
                        bool normalized = false;
                        Exception factoryException = null;
                        if (value == null && field.TryCreateMissing(obj, out value, out factoryException)) normalized = true;
                        if (factoryException != null)
                        {
                            diagnostics?.Add(new GraphDiagnostic("graphkit.metadata.factory-failed", GraphDiagnosticSeverity.Error,
                                $"{obj.GetType().FullName}.{field.Id} null factory failed: {factoryException.Message}",
                                new GraphDiagnosticLocation(fieldPath: fieldPath)));
                        }
                        if (value is not IList list) continue;
                        var row = new HGRow
                        {
                            Kind = HGRowKind.List,
                            Label = field.Label,
                            Depth = depth,
                            Path = fieldPath,
                            LeftPad = leftPad,
                            Target = obj,
                            Descriptor = field,
                            Items = new HGListItemSource(list, field.ValueType),
                            Normalized = normalized,
                        };
                        ApplyDescriptorPresentation(row, field);
                        BuildListChildren(row, depth + 1, visited, metadata, diagnostics);
                        into.Add(row);
                        continue;
                    }

                    metadata.TryGetValueDrawer(field.ValueType, out var drawer);
                    var valueRow = new HGRow
                    {
                        Kind = HGRowKind.NoPort,
                        Label = field.Label,
                        Depth = depth,
                        Path = fieldPath,
                        LeftPad = leftPad,
                        Target = obj,
                        Descriptor = field,
                        ValueDrawer = drawer,
                    };
                    ApplyDescriptorPresentation(valueRow, field);
                    into.Add(valueRow);
                    }
                    catch (Exception exception)
                    {
                        diagnostics?.Add(new GraphDiagnostic("graphkit.metadata.field-failed", GraphDiagnosticSeverity.Error,
                            obj.GetType().FullName + "." + field.Id + ": " + exception.Message,
                            new GraphDiagnosticLocation(fieldPath: fieldPath)));
                    }
                }
                return;
            }

            foreach (var f in HGReflect.Fields(obj.GetType()))
            {
                if (SkipFields.Contains(f.Name)) continue;
                if (HGReflect.IsHidden(f)) continue;
                if (f.IsNotSerialized) continue;
                if (f.IsStatic) continue;

                var t = f.FieldType;
                string label = HGReflect.FieldLabel(f);
                string fieldPath = path + "/" + f.Name;
                if (!HGReflect.TryIsShown(obj, f, out var visibilityError)) continue;
                if (!string.IsNullOrEmpty(visibilityError))
                    diagnostics?.Add(new GraphDiagnostic("graphkit.metadata.visibility-failed", GraphDiagnosticSeverity.Error,
                        $"{obj.GetType().FullName}.{f.Name} visibility condition failed: {visibilityError}",
                        new GraphDiagnosticLocation(fieldPath: fieldPath)));

                if (HGReflect.IsSlotType(t))
                {
                    var slot = f.GetValue(obj);
                    if (slot == null)
                    {
                        slot = HGReflect.CreateInstance(t);      // 缺 Slot 就補一個，避免整列不可編輯
                        if (slot != null) f.SetValue(obj, slot);
                    }
                    if (slot is not GraphSlotBase graphSlot) continue;
                    var row = SlotRow(graphSlot, label, depth);
                    row.Field = f;
                    row.Path = fieldPath;
                    row.LeftPad = leftPad;
                    row.ForceEnumButtons |= HGReflect.IsEnum(f);
                    row.HideLabel = HGReflect.IsLabelHidden(f);
                    into.Add(row);
                    AddDefaultListRow(into, row, diagnostics);
                    continue;
                }

                if (HGReflect.IsList(t, out var elem))
                {
                    var list = HGReflect.EnsureList(obj, f);
                    var row = new HGRow
                    {
                        Kind = HGRowKind.List,
                        Label = label,
                        Depth = depth,
                        Path = fieldPath,
                        LeftPad = leftPad,
                        Items = new HGListItemSource(list, elem),
                        Target = obj,
                        Field = f,
                        ForceEnumButtons = HGReflect.IsEnum(f),
                        HideLabel = HGReflect.IsLabelHidden(f),
                    };
                    BuildListChildren(row, depth + 1, visited, metadata, diagnostics);
                    into.Add(row);
                    continue;
                }

                if (IsLeafValue(t))
                {
                    into.Add(new HGRow
                    {
                        Kind = HGRowKind.NoPort,
                        Label = label,
                        Depth = depth,
                        Path = fieldPath,
                        LeftPad = leftPad,
                        Target = obj,
                        Field = f,
                        ForceEnumButtons = HGReflect.IsEnum(f),
                        HideLabel = HGReflect.IsLabelHidden(f),
                    });
                    continue;
                }

                // 其餘視為巢狀資料：展開成一個群組，內容遞迴。
                var value = f.GetValue(obj);
                if (value == null) continue;
                var group = new HGRow
                {
                    Kind = HGRowKind.Group,
                    Label = label,
                    Depth = depth,
                    Path = fieldPath,
                    LeftPad = leftPad,
                    Field = f,
                    HideLabel = HGReflect.IsLabelHidden(f),
                };
                BuildRows(value, depth + 1, group.Children, visited, fieldPath, leftPad, metadata, diagnostics);
                if (group.Children.Count > 0) into.Add(group);
            }
        }

        private static void ApplyDescriptorPresentation(HGRow row, HGFieldDescriptor field)
        {
            row.HideLabel = field.HideLabel;
            row.LabelWidthUnits = field.LabelWidthUnits;
            row.LabelWidthRatio = field.LabelWidthRatio;
            // SlotRow 可能已依 Slot 類別的 [HGEnum] 開啟；欄位只能再開，不能關。
            row.ForceEnumButtons |= field.ForceEnumButtons;
        }

        /// <summary>清單元素展開：Slot 元素直接成列，複合元素展開成子群組。</summary>
        private static void BuildListChildren(HGRow row, int depth, HashSet<object> visited, IHGEditorMetadataProvider metadata,
            List<GraphDiagnostic> diagnostics)
        {
            row.Children.Clear();
            if (row.Items is not HGListItemSource items) return;

            // 元素與其展開出來的子列都要讓開左側的序號欄，父子左緣才對得齊。
            float elementPad = row.LeftPad + ListGutter;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items.Get(i);
                string childPath = row.Path + "[" + i + "]";
                HGRow child;

                if (item == null)
                {
                    child = new HGRow { Kind = HGRowKind.NoPort, Label = "（空）", Depth = depth };
                }
                else if (HGReflect.IsSlotType(item.GetType()))
                {
                    // 序號已經有自己的欄位，標籤只留內容。
                    child = SlotRow((GraphSlotBase)item, SlotShortName((GraphSlotBase)item), depth);
                    // 公式元素不畫標籤，常數框吃滿整列：接了什麼順著線看子節點 Header，每列寬度也不隨接線跳動。
                    // 動作元素的標籤就是內容本身（見 DrawInputPortRow 的 labelIsContent），保留。
                    child.HideLabel = !child.IsActionSlot;
                }
                else if (IsLeafValue(item.GetType()))
                {
                    // 元素沒有 FieldInfo，清單欄位上的 [HGEnum] 只能從父列帶下來。
                    child = new HGRow
                    {
                        Kind = HGRowKind.NoPort, Label = "", Depth = depth, Target = items.List, Field = null, HideLabel = true,
                        ForceEnumButtons = row.ForceEnumButtons,
                    };
                }
                else
                {
                    child = new HGRow { Kind = HGRowKind.Group, Label = HGReflect.TypeName(item.GetType()), Depth = depth };
                    BuildRows(item, depth + 1, child.Children, visited ?? new HashSet<object>(HGRefComparer.Instance),
                        childPath, elementPad, metadata, diagnostics);
                }

                child.Path = childPath;
                child.LeftPad = elementPad;
                child.IsItem = true;
                MarkItemSubtree(child, items, row, i);
                row.Children.Add(child);
            }
        }

        /// <summary>把項目與它展開出來的子列都認到同一段的同一個索引下，讓斑馬紋覆蓋整段。</summary>
        private static void MarkItemSubtree(HGRow row, HGItemSource source, HGRow ownerRow, int index)
        {
            // 內層清單已經認領的子樹不被外層覆蓋，巢狀清單才各自算自己的奇偶。
            if (row.ItemSource != null) return;
            row.ItemSource = source;
            row.ItemOwnerRow = ownerRow;
            row.ItemIndex = index;
            foreach (var child in row.Children) MarkItemSubtree(child, source, ownerRow, index);
        }

        /// <summary>
        /// 常數本身是清單的 Slot（例如 *ListSlot）在它下方掛一段清單，沿用清單欄位的增刪、重排與元素繪製。
        /// 語意與單值常數框相同：沒接來源時採用，接了來源是保底值。
        /// </summary>
        // 只收「編輯型別就是清單、元素畫得出輸入框」的 Slot。把 DefaultEditType 換成替代型別（enum）的族不走這裡，
        // 元素本身是 Slot 的清單也不走——那種清單的元素要能接線，不是常數。
        private static void AddDefaultListRow(List<HGRow> into, HGRow slotRow, List<GraphDiagnostic> diagnostics)
        {
            if (slotRow?.InputSlot is not FormulaSlotBase slot) return;
            Type editType = HGReflect.DefaultEditType(slot, slotRow.ResultType);
            if (!HGReflect.IsList(editType, out Type elementType) || !HGValueField.CanDraw(elementType)) return;

            // 常數清單是 null 時就地補一份空清單，和清單欄位的 EnsureList 同一個做法。
            if (slot.DefaultObject is not IList list)
            {
                object instance = editType.IsArray ? Array.CreateInstance(elementType, 0) : HGReflect.CreateInstance(editType);
                if (instance is not IList created) return;
                slot.DefaultObject = created;
                list = created;
            }

            // Slot 列本身就是這段清單的標題；標籤隱藏的 Slot 列沒地方放開關，才保留清單自己的標題列。
            bool headerHidden = !slotRow.HideLabel;
            var row = new HGRow
            {
                Kind = HGRowKind.List,
                HeaderRow = headerHidden ? slotRow : null,
                // 與一般清單欄位同層：Slot 列兼任標題後，兩種清單長得一樣，排法也要一樣。
                Depth = slotRow.Depth,
                // HEAD 的來源列沒有路徑；折疊狀態只需要在同一顆節點內唯一。
                Path = (slotRow.Path ?? "/head") + "/default",
                LeftPad = slotRow.LeftPad,
                Items = new HGListItemSource(list, elementType),
                ForceEnumButtons = HGReflect.HasEnumButtons(slot.GetType()),
            };
            BuildListChildren(row, row.Depth + 1, null, null, diagnostics);
            into.Add(row);
            if (headerHidden) slotRow.DefaultListRow = row;
        }

        private static HGRow SlotRow(GraphSlotBase slot, string label, int depth)
        {
            bool isAction = HGReflect.IsActionSlotType(slot.GetType());
            // 寫入目標欄位宣告的是「要寫哪一族」，自己不是那一族，所以 chip 的型別要問 FamilyType。
            var propertySlot = slot as PropertySlotBase;
            return new HGRow
            {
                Kind = HGRowKind.InputPort,
                Label = label,
                Depth = depth,
                InputSlot = slot,
                IsActionSlot = isAction,
                IsProducedValue = propertySlot != null,
                ResultType = isAction ? null
                    : propertySlot != null ? HGReflect.ResultType(propertySlot.FamilyType)
                    : HGReflect.ResultType(slot.GetType()),
                // Token HEAD、資產參數、清單元素這幾種列沒有 FieldInfo，只有 Slot 類別能宣告按鈕列。
                ForceEnumButtons = HGReflect.HasEnumButtons(slot.GetType()),
            };
        }

        /// <summary>清單裡的 Slot 顯示它目前接了什麼，企劃不用逐一點開。</summary>
        private static string SlotShortName(GraphSlotBase slot)
        {
            bool isAction = HGReflect.IsActionSlotType(slot.GetType());

            // 動作欄位的自訂標籤優先：它存在的目的就是區分同型別的動作（「主傷害」「濺射」）。
            if (isAction)
            {
                string label = HGReflect.GetLabel(slot);
                if (!string.IsNullOrEmpty(label)) return label;
            }

            switch (slot.Node?.Kind)
            {
                case NodeKind.Inline:
                case NodeKind.Empty:
                    var f = HGReflect.GetFormula(slot);
                    return f != null ? HGReflect.TypeName(f.GetType()) : "（空）";
                case NodeKind.Asset:
                    var a = HGReflect.GetAsset(slot);
                    return a != null ? a.name : "（空資產）";
                default:
                    // 動作列右半已經不畫狀態文字，操作提示併進標籤裡，否則空著的列看不出下一步要做什麼。
                    return isAction ? "（未啟用，從接點拉線指定動作）" : "常數";
            }
        }

        // 白名單：這些型別要當成「一個值」畫一格，攤開它們的內部欄位只會畫出一堆垃圾。
        private static readonly HashSet<Type> LeafTypes = new()
    {
        typeof(Vector2), typeof(Vector3), typeof(Vector4),
        typeof(Vector2Int), typeof(Vector3Int),
        typeof(Quaternion), typeof(Color), typeof(Color32),
        typeof(Rect), typeof(RectInt), typeof(Bounds), typeof(BoundsInt),
        typeof(LayerMask), typeof(AnimationCurve), typeof(Gradient),
    };

        /// <summary>這個型別要當成單一個值畫（而不是展開成群組）。</summary>
        public static bool IsLeafValue(Type t)
        {
            if (t == null) return true;
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return true;
            if (LeafTypes.Contains(t)) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;

            // 沒列進白名單的 Unity 型別一律當 leaf：攤開引擎型別的私有欄位沒有意義，還會誤導。
            var ns = t.Namespace;
            return ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine."));
        }

        // ===== 尺寸與排版 =====

        /// <summary>
        /// 節點寬度：型別標了 `[HGNodeView(Width = n)]` 就用 n 格，否則預設 15 格。
        /// 只有內嵌節點（`node.Obj` 是具體 Action／Formula）能覆寫；資產、Token、時機、空節點都沒有型別可問，一律預設寬。
        /// </summary>
        private static float WidthOf(HGNodeView node)
        {
            int units = HGReflect.NodeWidthUnits(node.Obj?.GetType());
            return units <= 0 ? NodeWidth : units * GridSize;
        }

        public static void MeasureNode(HGNodeView node)
        {
            node.Width = WidthOf(node);
            // 節點上不畫型別說明（它是型別常數，重複出現只是噪音），改由畫布左上角的說明面板顯示選取節點的 Desc。
            // 註解則是「這一顆節點」的資訊，任何節點（含Token／資產葉節點）都能加。
            node.TipsHeight = !node.NoteOpen
                ? 0f
                // 起手一行，換行或折行才長高：註解多半是一句話，預留三行等於每顆節點都被墊高。
                : Mathf.Clamp(EditorStyles.textArea.CalcHeight(new GUIContent(node.Tips ?? ""), node.Width - 16f),
                    EditorGUIUtility.singleLineHeight, 160f);

            // 空節點還沒決定身分，沒有東西可選，維持單行。
            if (node.IsPlaceholder)
            {
                float leafY = HeaderHeight;
                if (node.TipsHeight > 0f) leafY += node.TipsHeight + 12f;
                node.ContentHeight = leafY;
                node.Height = leafY + NodeBottomPad;
                return;
            }
            // 資產、Token 與 ProtoProperty 的本體第一列是「選哪一個」的下拉。
            // 一般 Property 直接代表自身，沒有可選名稱，故不預留空白列。
            float refRows = node.IsPropertyNode
                ? RowHeight + (node.Carrier?.IsProtoProperty == true ? RowHeight : 0f)
                : node.IsAssetNode || node.IsTokenNode || node.Obj is IGraphNodeOwner ? RowHeight : 0f;
            float y = MeasureRows(node.Rows, HeaderHeight + refRows, node.Width);
            if (node.TipsHeight > 0f) y += node.TipsHeight + 10f;
            node.ContentHeight = Mathf.Max(y, HeaderHeight + 8f);
            node.Height = node.ContentHeight + NodeBottomPad;
        }

        private static void ApplyViewState(HGModel model, HGGraphView view, string noteOpenId,
            ICollection<string> noteCollapsed)
        {
            foreach (var node in view.Nodes)
            {
                if (model.TryGetNodeView(node.Id, out var tips)) node.Tips = tips;
                // 有內容的註解預設展開，收起是使用者的選擇；沒內容的只有剛按開的那一顆才顯示。
                node.NoteOpen = string.IsNullOrWhiteSpace(node.Tips)
                    ? node.Id == noteOpenId
                    : noteCollapsed == null || !noteCollapsed.Contains(node.Id);
            }
            foreach (var node in view.Nodes) MeasureNode(node);
        }

        private static float MeasureRows(List<HGRow> rows, float y, float nodeWidth)
        {
            foreach (var r in rows)
            {
                r.LocalY = y;
                r.Hidden = false;
                switch (r.Kind)
                {
                    case HGRowKind.Group:
                        r.Height = RowHeight;
                        y += RowHeight;
                        y = MeasureRows(r.Children, y, nodeWidth);
                        break;
                    case HGRowKind.List:
                        r.Height = r.HeaderHidden ? 0f : RowHeight;
                        y += r.Height;
                        if (r.Collapsed)
                        {
                            // 折疊的子列不佔高度，但接點要收斂到標題列中心：連線因此看起來是「插進這個清單」。
                            CollapseRows(r.Children, r.LocalY, r.Height);
                            r.AddRowY = r.LocalY;
                            break;
                        }
                        y = MeasureRows(r.Children, y, nodeWidth);
                        r.AddRowY = y;                // 新增項目列
                        y += RowHeight;
                        break;
                    default:
                        r.Height = DescriptorHeight(r, nodeWidth);
                        y += r.Height;
                        break;
                }
            }
            return y;
        }

        private static float DescriptorHeight(HGRow row, float nodeWidth)
        {
            if (row.Descriptor == null || row.ValueDrawer == null) return RowHeight;
            try
            {
                float width = ValueFieldRect(new Rect(0f, 0f, nodeWidth, RowHeight), row).width;
                float height = row.ValueDrawer.Measure(new HGValueDrawerContext(row.Descriptor, row.Target, row.Locked), width);
                if (float.IsNaN(height) || float.IsInfinity(height)) throw new InvalidOperationException("Drawer returned an invalid height.");
                return Mathf.Max(RowHeight, height + 3f);
            }
            catch (Exception exception)
            {
                row.DrawerError = exception.Message;
                return RowHeight;
            }
        }

        internal static Rect ValueFieldRect(Rect rect, HGRow row)
        {
            float inset = 20f + (row.IsItem ? ListDeleteWidth : 0f);
            float left = row.HideLabel ? 4f + row.LeftPad + row.Depth * IndentWidth
                : LabelWidthOf(rect.width, row.LabelWidthUnits, row.LabelWidthRatio);
            return new Rect(rect.x + left, rect.y + 1f, Mathf.Max(20f, rect.width - left - inset), rect.height - 3f);
        }

        /// <summary>把整個子樹壓到同一條列上並標記隱藏；高度保留是為了讓接點落在標題列中心。</summary>
        private static void CollapseRows(List<HGRow> rows, float y, float height)
        {
            foreach (var r in rows)
            {
                r.LocalY = y;
                r.Height = height;
                r.Hidden = true;
                CollapseRows(r.Children, y, height);
            }
        }

        /// <summary>先算樹狀自動排版，再用記憶座標覆蓋（有記憶的節點以使用者擺放為準）。</summary>
        private static void AutoLayout(HGModel model, HGGraphView view)
        {
            var children = new Dictionary<HGNodeView, List<HGNodeView>>();
            foreach (var n in view.Nodes)
                children[n] = new List<HGNodeView>();

            var roots = new List<HGNodeView>();
            foreach (var n in view.Nodes)
            {
                HGNodeView parent = null;
                if (n.ParentRow != null)
                {
                    foreach (var p in view.Nodes)
                    {
                        if (p == n) continue;
                        foreach (var r in AllRows(p.Rows))
                            if (ReferenceEquals(r, n.ParentRow)) { parent = p; break; }
                        if (parent != null) break;
                    }
                }
                if (parent != null) children[parent].Add(n);
                else roots.Add(n);
            }

            // HEAD 先排、候選後排：候選節點不該插進 HEAD 前面。
            // 刻意不用 List.Sort——它不穩定，會把候選之間的相對順序打亂。
            var heads = new List<HGNodeView>();
            var loose = new List<HGNodeView>();
            foreach (var r in roots) (r.IsRoot ? heads : loose).Add(r);

            float cursorY = 40f;
            foreach (var r in heads) cursorY = Place(r, 40f, cursorY, children) + NodeGap * 2f;
            foreach (var r in loose) cursorY = Place(r, 40f, cursorY, children) + NodeGap * 2f;

            foreach (var n in view.Nodes)
            {
                if (model.TryGetPosition(n.Id, out var pos)) n.Pos = pos;
            }
        }

        /// <summary>把節點放在 (x, y)，子節點往右排；回傳這棵子樹用掉的底部 Y。</summary>
        private static float Place(HGNodeView node, float x, float y, Dictionary<HGNodeView, List<HGNodeView>> children)
        {
            // 節點高度含 NodeBottomPad、欄距與列距都不是格線倍數，直接累加會讓整理後的節點跟拖曳出來的節點對不到同一條線。
            // 一律往上取整到格線：只會把間距撐大，不會讓相鄰節點壓在一起。
            x = SnapUpToGrid(x);
            y = SnapUpToGrid(y);

            node.Pos = new Vector2(x, y);
            float childX = SnapUpToGrid(x + node.Width + ColumnGap);
            float childY = y;
            foreach (var c in children[node])
            {
                float nextY = childY;
                if (c.IsPropertyNode && c.ParentRow?.InputSlot is PropertySlotBase)
                {
                    float inputOffset = c.PropertyInputPortPosition.y - c.Pos.y;
                    float writerOffset = c.ParentRow.LocalY + c.ParentRow.Height * 0.5f;
                    nextY = Mathf.Max(childY, node.Pos.y + writerOffset - inputOffset);
                }
                childY = Place(c, childX, nextY, children) + NodeGap;
            }

            return Mathf.Max(y + node.Height, childY - NodeGap);
        }

        /// <summary>把座標往上對齊到格線。拖曳用四捨五入，排版用進位，才不會把節點往回推去疊到上一個。</summary>
        private static float SnapUpToGrid(float value) => Mathf.Ceil(value / GridSize) * GridSize;
    }

}
