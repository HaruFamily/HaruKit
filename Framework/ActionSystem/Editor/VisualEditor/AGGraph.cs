namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public enum AGRowKind
{
    /// <summary>參數欄位：有接點，四種狀態（常數／公式／資產／變數）。</summary>
    Slot,
    /// <summary>一般值欄位：沒有接點，直接編輯。</summary>
    Value,
    /// <summary>巢狀資料的分組標題。</summary>
    Group,
    /// <summary>清單型參數的標題列。</summary>
    List,
}

/// <summary>節點上的一列。Slot 列右端有接點，其餘沒有。</summary>
public class AGRow
{
    public AGRowKind Kind;
    public string Label;
    public int Depth;

    public object Slot;              // Kind == Slot
    public Type ResultType;          // Slot 的結果型別；ActionSlot 為 null
    public bool IsActionSlot;
    public NamedFormulaSlot AssetBinding;

    public object Target;            // Kind == Value：欄位所屬物件
    public FieldInfo Field;
    public bool IsEnum;
    public bool HideLabel;

    public IList List;               // Kind == List
    public Type ElementType;
    public bool Collapsed;           // Kind == List：折疊時子列不畫、不可互動

    public List<AGRow> Children = new();

    /// <summary>欄位在節點內的唯一路徑（`/action/steps[2]/value`），折疊狀態靠它記憶。</summary>
    public string Path;

    /// <summary>這一列屬於哪個節點。折疊與分支收合的 key 都是「節點 Id + Path」，繪製時不必再回頭找主人。</summary>
    public string OwnerNodeId;

    /// <summary>清單元素本身：只有它畫序號欄與刪除鈕。</summary>
    public bool IsListElement;

    /// <summary>
    /// 所屬的清單標題列與索引。**元素展開出來的子列也會帶著它**，斑馬紋才涵蓋整段；
    /// 只有元素標題有底、內部欄位沒有的話，看起來會像清單只有一行。
    /// </summary>
    public AGRow ListOwner;
    public int ListIndex = -1;

    /// <summary>
    /// 左側額外留白。清單元素要留位置給序號／拖曳把手，而**它展開出來的子列也必須繼承**，
    /// 否則子列會比自己的父標題還靠左，看起來像壞掉。
    /// </summary>
    public float LeftPad;

    // 排版結果（每次重畫填）
    public float LocalY;
    public float Height;
    public float AddRowY;            // 清單列的「新增項目」列位置
    public bool Hidden;              // 被折疊的清單蓋住：不畫、不畫接點、不可當拉線目標
    public Rect ScreenRect;
    public Vector2 PortPos;
    public bool HasPort => Kind == AGRowKind.Slot;

    /// <summary>可以拉線的欄位：折疊起來的列不算，否則會接到看不見的東西。</summary>
    public bool IsLinkable => Kind == AGRowKind.Slot && !Hidden;
}

/// <summary>編輯區上的一個節點。</summary>
public class AGNode
{
    public GraphNode Carrier;             // 這個節點的載體；HEAD 節點為 null（載體是頭端本身）
    public object Obj;                    // ActionSystemNode（公式 / 動作）；資產、變數、空節點為 null
    public UnityEngine.Object Asset;      // 資產節點目前指到的資產（可為 null＝尚未指定）
    public bool IsAssetNode;              // 資產節點（不論有沒有指定資產）
    /// <summary>載體上的標註名稱（沒標註為 null）。它是「這顆節點是這張圖的對外端點」，不是一種內容。</summary>
    public string TokenName;
    public Type ResultType;               // 資產／變數節點的結果型別
    public string Id;
    public string Title;                  // Header 主文字＝具體型別／變數／資產名稱，節點靠它辨識
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
    /// 被 Slot 的分支收合收起來：不畫、不命中、不能當拉線目標。純視覺，資料一點都沒變。
    /// 圖照樣建到底——引用數要走完整張圖才算得準，收起來只是最後一步的標記。
    /// </summary>
    public bool Hidden;
    public string Tips;
    /// <summary>註解框被打開但還沒有內容：只有這顆節點被選取時才成立，取消選取就收起來。</summary>
    public bool NoteOpen;

    public object ParentSlot;             // 這個節點接在哪個 Slot 上（root / orphan 為 null）
    public AGRow ParentRow;

    public List<AGRow> Rows = new();
    public Rect TitleRect;                // Header 名稱區（graph space）：拖曳抓取區，繪製時寫入
    /// <summary>Header 右端的 ▾（graph space）：換來源的唯一入口。整塊名稱區可按會跟拖曳打架。</summary>
    public Rect SourceMenuRect;
    public Vector2 Pos;
    public float Width = AGGraph.NodeWidth;
    public float Height = 60f;
    public float ContentHeight;
    public float TipsHeight;
    // 換來源的入口是 Header 右端的 ▾；Root HEAD 的來源走它自己的「來源」參數列接點，所以不畫。
    public bool HasSourceSelector => !IsRoot && (IsPlaceholder || Obj != null || IsAssetNode);

    public Rect Rect => new Rect(Pos.x, Pos.y, Width, Height);
    public Vector2 OutputPort => new Vector2(Pos.x + AGGraph.PortRadius, Pos.y + AGGraph.HeaderHeight * 0.5f);
}

/// <summary>一次焦點的完整節點圖。每次資料變動就整份重建，不做增量。</summary>
public class AGGraphView
{
    public List<AGNode> Nodes = new();
    public List<AGLink> Links = new();
    public Dictionary<object, AGNode> BySlot = new(AGRefComparer.Instance);

    // 同一個載體被多個欄位指到＝共用來源：只畫一個節點，連線各自一條。GraphNode 沒有覆寫 Equals，預設就是參考比對。
    public Dictionary<GraphNode, AGNode> ByCarrier = new();

    /// <summary>
    /// 這張圖裡有幾個欄位指著同一個載體。給「隱藏子樹時要不要留下共用節點」用——隱藏是視覺操作，
    /// 只算畫得出來的引用。要問「停用會影響幾個欄位」是全域問題，那走 ActionGraphWindow.CarrierUsers。
    /// </summary>
    public Dictionary<GraphNode, int> CarrierUsers = new();

    public AGNode FindByObject(object obj)
    {
        foreach (var n in Nodes)
            if (ReferenceEquals(n.Obj, obj)) return n;
        return null;
    }
}

public class AGLink
{
    public AGRow ParentRow;
    public AGNode Target;
}

/// <summary>
/// 由焦點根 Slot 遞迴展開節點圖：節點 → 參數列 → 子節點，並套用記憶座標或樹狀自動排版。
/// </summary>
public static class AGGraph
{
    public const float RowHeight = 20f;
    public const float HeaderHeight = 20f;
    public const float PortRadius = 7f;
    public const float PortDiameter = PortRadius * 2f;
    public const float GridSize = 20f;
    /// <summary>節點最後一列與下緣之間的留白：只求緊鄰排列時不黏在一起，不吃格線對齊。</summary>
    public const float NodeBottomPad = 3f;
    public const float IndentWidth = 12f;
    public const float ColumnGap = 90f;
    public const float NodeGap = 24f;
    // 所有節點同寬：接點排成一條垂直線、AutoLayout 的欄位不會因父節點文字長度而漂移。
    public const float NodeWidth = 300f;

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
    /// headTitle 是編輯對象自己的名稱（動作標籤／變數名／資產名），直接當 HEAD 的 Header。
    ///
    /// root 有兩種：多數焦點給的是**一個** Slot 頭端；Timing 焦點給的是**全部** ActionTimingGroup 物件，
    /// 每個畫成一顆節點，本體就是那個時機的動作清單。同一張畫布才拉得到跨時機的共用來源。
    /// </summary>
    public static AGGraphView Build(AGModel model, IReadOnlyList<object> roots, IList orphans, string focusId,
        string headTitle, IReadOnlyDictionary<string, bool> listCollapse = null,
        string noteOpenId = null, ICollection<string> noteCollapsed = null)
    {
        var view = new AGGraphView();

        // 每次重建都重新登記 id → 載體，座標與備註的讀寫才找得到人。
        model.ClearCarriers();

        // 一顆 HEAD 都沒有仍要往下走：時機畫布可能還沒建任何時機節點，但候選節點得畫得出來。
        foreach (var root in roots ?? Array.Empty<object>())
        {
            if (root == null) continue;
            var rootNode = AGReflect.IsSlotType(root.GetType())
                ? MakeHeadNode(model, root, focusId, headTitle)
                : MakeGroupNode(model, root);
            Collect(model, rootNode, view, 0, listCollapse, false);
        }

        if (orphans != null)
        {
            foreach (var o in orphans)
            {
                if (o is not GraphNode carrier) continue;
                if (view.ByCarrier.ContainsKey(carrier)) continue;
                // 候選不需要額外標記：沒有連入線本身就是訊號。
                var node = MakeNodeForCarrier(model, carrier, null, null);
                Collect(model, node, view, 0, listCollapse, false);
            }
        }

        ApplyViewState(model, view, noteOpenId, noteCollapsed);
        AutoLayout(model, view);
        return view;
    }

    // ===== 節點建立 =====

    /// <summary>
    /// 把一個 `ActionTimingGroup` 畫成 HEAD：Header 是時機名，本體就是那個時機的動作清單，
    /// 所以每個動作直接是清單的一列——序號、拖曳把手、刪除鈕、折疊、斑馬紋全部沿用清單那一套，
    /// 不需要為動作另做一組互動。一張畫布上有幾個時機就有幾顆。
    /// </summary>
    private static AGNode MakeGroupNode(AGModel model, object group)
    {
        var node = MakeNodeForObject(group, null, null, null);
        node.Id = GroupHeadId(group);
        node.IsRoot = true;
        node.IsTimingGroup = true;
        // ActionTimingGroup 不是 ActionBase，但它的本體是 ActionSlot 清單，Header 應導向 Action 流程色。
        node.IsActionNode = true;
        node.Title = GroupTitle(group);
        node.Chip = "時機";      // 群組不回傳值，chip 改寫身分：一眼分得出時機節點與動作節點
        node.Desc = null;

        // 時機值不可就地改：改下去會跟別的群組撞同一個時機。要換時機就刪掉這顆、重新建一顆。
        node.Rows.RemoveAll(r => r.Field != null && r.Field.Name == "Timing");

        model.RegisterCarrier(node.Id, group);
        return node;
    }

    /// <summary>時機群組節點的識別碼。時機值本身就是身分，enum 不可重複，所以不必再配流水號。</summary>
    public static string GroupHeadId(object group) => "head:tim:" + GroupTitle(group);

    public static string GroupTitle(object group)
        => (AGReflect.Get(group, "Timing") as Enum)?.ToString() ?? "（未指定時機）";

    private static AGNode MakeHeadNode(AGModel model, object rootSlot, string focusId, string headTitle)
    {
        bool isAction = AGReflect.IsActionSlotType(rootSlot.GetType());
        Type resultType = isAction ? null : AGReflect.ResultType(rootSlot.GetType());
        var node = new AGNode
        {
            Id = HeadId(focusId),
            // 名字由焦點提供；真的沒有名字時給預設值，不留空白 Header。
            Title = string.IsNullOrWhiteSpace(headTitle) ? (isAction ? "（動作）" : "（頭端）") : headTitle,
            Chip = ChipText(resultType, isAction),
            ParentSlot = rootSlot,
            IsRoot = true,
            IsActionNode = isAction,
            ResultType = resultType,
        };
        node.Rows.Add(SlotRow(rootSlot, "來源", 0));
        model.RegisterCarrier(node.Id, rootSlot);
        return node;
    }

    public static string HeadId(string focusId) => "head:" + (focusId ?? "?");

    /// <summary>一個載體＝一個節點。內容種類決定畫成公式／動作、資產葉或編輯中的空節點。</summary>
    private static AGNode MakeNodeForCarrier(AGModel model, GraphNode carrier, object parentSlot, AGRow parentRow)
    {
        string id = carrier.EnsureId();
        Type slotResultType = parentSlot != null && !AGReflect.IsActionSlotType(parentSlot.GetType())
            ? AGReflect.ResultType(parentSlot.GetType())
            : null;

        AGNode node;
        switch (carrier.Kind)
        {
            case NodeKind.Inline when carrier.BodyObject != null:
                node = MakeNodeForObject(carrier.BodyObject, parentSlot, parentRow, slotResultType);
                break;

            case NodeKind.Asset:
            {
                Type assetResult = slotResultType ?? AGReflect.AssetResultType(carrier.AssetObject);
                node = new AGNode
                {
                    Asset = carrier.AssetObject,
                    IsAssetNode = true,
                    ResultType = assetResult,
                    // Header 只表明身分；選哪一個資產是本體的參數列在做。
                    Title = "Asset",
                    Chip = ChipText(assetResult, assetResult == null),
                };
                foreach (var binding in carrier.Bindings)
                {
                    if (binding?.Slot == null) continue;
                    var row = SlotRow(binding.Slot, binding.Name, 0);
                    row.AssetBinding = binding;
                    row.Path = "/binding/" + binding.Name;
                    node.Rows.Add(row);
                }
                break;
            }

            default:
            {
                bool isAction = parentSlot != null && AGReflect.IsActionSlotType(parentSlot.GetType());
                node = new AGNode
                {
                    Title = isAction ? "（選擇 Action）" : "（選擇 Formula）",
                    Chip = ChipText(slotResultType, isAction),
                    ResultType = slotResultType,
                    IsPlaceholder = true,
                    IsActionNode = isAction,
                };
                break;
            }
        }

        node.Carrier = carrier;
        node.Id = id;
        node.ParentSlot = parentSlot;
        node.ParentRow = parentRow;
        // 標註是載體上的一個名字，跟內容種類無關：公式、資產、甚至編輯中的空節點都可能有。
        node.TokenName = carrier.TokenName;
        model.RegisterCarrier(id, carrier);
        return node;
    }

    /// <summary>Header 右側的契約標籤：公式看結果型別，動作沒有結果型別就標 Action。</summary>
    private static string ChipText(Type resultType, bool isAction)
    {
        if (resultType != null) return AGReflect.ResultTypeName(resultType);
        return isAction ? "Action" : null;
    }

    private static AGNode MakeNodeForObject(object obj, object parentSlot, AGRow parentRow, Type slotResultType)
    {
        bool isAction = AGReflect.IsActionNodeType(obj.GetType());
        Type resultType = !isAction && slotResultType != null
            ? slotResultType
            : AGReflect.FormulaResultType(obj.GetType());
        var node = new AGNode
        {
            Obj = obj,
            ParentSlot = parentSlot,
            ParentRow = parentRow,
            Title = AGReflect.TypeName(obj.GetType()),
            Chip = ChipText(resultType, isAction),
            Desc = AGReflect.TypeDescription(obj.GetType()),
            IsActionNode = isAction,
            ResultType = resultType,
        };
        BuildRows(obj, 0, node.Rows, new HashSet<object>(AGRefComparer.Instance), "", 0f);
        return node;
    }

    /// <summary>把節點與其子樹加入視圖。已經畫過的載體只補一條連線，不重複建節點。</summary>
    private static void Collect(AGModel model, AGNode node, AGGraphView view, int depth,
        IReadOnlyDictionary<string, bool> listCollapse, bool disabled)
    {
        if (depth > 24) return;                       // 資料異常時不讓編輯器堆疊爆掉
        node.InDisabledSubtree = disabled || (node.Carrier != null && node.Carrier.Disabled);
        view.Nodes.Add(node);
        if (node.Carrier != null) view.ByCarrier[node.Carrier] = node;
        if (node.ParentSlot != null) view.BySlot[node.ParentSlot] = node;
        if (node.ParentRow != null) view.Links.Add(new AGLink { ParentRow = node.ParentRow, Target = node });
        // 節點 Id 到這裡才確定，所以列的歸屬也在這裡補；折疊與分支收合都靠它組 key。
        foreach (var row in AllRows(node.Rows)) row.OwnerNodeId = node.Id;

        // 折疊狀態要在量測之前套用：節點高度直接受它影響。節點 Id 到這裡才確定，所以不能在 BuildRows 做。
        ApplyListCollapse(node, listCollapse);
        MeasureNode(node);

        foreach (var row in AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;

            var carrier = AGReflect.GetNode(row.Slot);
            if (carrier == null) continue;            // 常數／空槽留在列上，不長節點

            view.CarrierUsers.TryGetValue(carrier, out int users);
            view.CarrierUsers[carrier] = users + 1;

            // 共用來源：同一個載體被多個欄位指到時只有一個節點，這裡只補連線。
            if (view.ByCarrier.TryGetValue(carrier, out var existing))
            {
                view.Links.Add(new AGLink { ParentRow = row, Target = existing });
                view.BySlot[row.Slot] = existing;
                // 這條路徑沒被停用就整顆恢復：共用節點只要還有一條會求值的路徑，它就不是停用的。
                bool rowDisabled = row.AssetBinding != null && !row.AssetBinding.OverrideEnabled;
                if (!node.InDisabledSubtree && !rowDisabled) ClearDisabledSubtree(existing, view);
                continue;
            }

            var child = MakeNodeForCarrier(model, carrier, row.Slot, row);
            bool childDisabled = node.InDisabledSubtree || (row.AssetBinding != null && !row.AssetBinding.OverrideEnabled);
            Collect(model, child, view, depth + 1, listCollapse, childDisabled);
        }
    }

    /// <summary>
    /// 共用節點先被停用路徑走到、之後又被啟用路徑指上時，把整棵子樹的壓暗狀態撤回。
    /// 自己被明確停用的節點不撤——那不是繼承來的。已經是 false 就直接回，順便擋住環。
    /// </summary>
    private static void ClearDisabledSubtree(AGNode node, AGGraphView view)
    {
        if (node == null || !node.InDisabledSubtree) return;
        if (node.Carrier != null && node.Carrier.Disabled) return;
        node.InDisabledSubtree = false;

        foreach (var row in AllRows(node.Rows))
        {
            if (row.Slot == null) continue;
            if (view.BySlot.TryGetValue(row.Slot, out var child)) ClearDisabledSubtree(child, view);
        }
    }

    /// <summary>清單折疊狀態的鍵：節點 Id + 欄位路徑，重建圖之後仍然指到同一個清單。</summary>
    public static string CollapseKey(string nodeId, AGRow row) => nodeId + "#" + row.Path;

    /// <summary>沒有明確記錄過的清單，項數多就預設折疊。</summary>
    private static bool DefaultCollapsed(AGRow row) => (row.List?.Count ?? 0) > ListAutoCollapseCount;

    private static void ApplyListCollapse(AGNode node, IReadOnlyDictionary<string, bool> listCollapse)
    {
        foreach (var row in AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.List) continue;
            row.Collapsed = listCollapse != null && listCollapse.TryGetValue(CollapseKey(node.Id, row), out bool stored)
                ? stored
                : DefaultCollapsed(row);
        }
    }

    public static IEnumerable<AGRow> AllRows(List<AGRow> rows)
    {
        foreach (var r in rows)
        {
            yield return r;
            foreach (var c in AllRows(r.Children)) yield return c;
        }
    }

    // ===== 參數列 =====

    private static void BuildRows(object obj, int depth, List<AGRow> into, HashSet<object> visited, string path, float leftPad)
    {
        if (obj == null || depth > 5 || !visited.Add(obj)) return;

        foreach (var f in AGReflect.Fields(obj.GetType()))
        {
            if (SkipFields.Contains(f.Name)) continue;
            if (AGReflect.IsHidden(f)) continue;
            if (f.IsNotSerialized) continue;
            if (f.IsStatic) continue;

            var t = f.FieldType;
            string label = AGReflect.FieldLabel(f);
            string fieldPath = path + "/" + f.Name;

            if (AGReflect.IsSlotType(t))
            {
                var slot = f.GetValue(obj);
                if (slot == null)
                {
                    slot = AGReflect.CreateInstance(t);      // 缺 Slot 就補一個，避免整列不可編輯
                    if (slot != null) f.SetValue(obj, slot);
                }
                if (slot == null) continue;
                var row = SlotRow(slot, label, depth);
                row.Field = f;
                row.Path = fieldPath;
                row.LeftPad = leftPad;
                row.IsEnum = AGReflect.IsEnum(f);
                row.HideLabel = AGReflect.IsLabelHidden(f);
                into.Add(row);
                continue;
            }

            if (AGReflect.IsList(t, out var elem))
            {
                var list = AGReflect.EnsureList(obj, f);
                var row = new AGRow
                {
                    Kind = AGRowKind.List,
                    Label = label,
                    Depth = depth,
                    Path = fieldPath,
                    LeftPad = leftPad,
                    List = list,
                    ElementType = elem,
                    Target = obj,
                    Field = f,
                    IsEnum = AGReflect.IsEnum(f),
                    HideLabel = AGReflect.IsLabelHidden(f),
                };
                BuildListChildren(row, depth + 1, visited);
                into.Add(row);
                continue;
            }

            if (IsLeafValue(t))
            {
                into.Add(new AGRow
                {
                    Kind = AGRowKind.Value,
                    Label = label,
                    Depth = depth,
                    Path = fieldPath,
                    LeftPad = leftPad,
                    Target = obj,
                    Field = f,
                    IsEnum = AGReflect.IsEnum(f),
                    HideLabel = AGReflect.IsLabelHidden(f),
                });
                continue;
            }

            // 其餘視為巢狀資料：展開成一個群組，內容遞迴。
            var value = f.GetValue(obj);
            if (value == null) continue;
            var group = new AGRow
            {
                Kind = AGRowKind.Group,
                Label = label,
                Depth = depth,
                Path = fieldPath,
                LeftPad = leftPad,
                Field = f,
                HideLabel = AGReflect.IsLabelHidden(f),
            };
            BuildRows(value, depth + 1, group.Children, visited, fieldPath, leftPad);
            if (group.Children.Count > 0) into.Add(group);
        }
    }

    /// <summary>清單元素展開：Slot 元素直接成列，複合元素展開成子群組。</summary>
    private static void BuildListChildren(AGRow row, int depth, HashSet<object> visited)
    {
        row.Children.Clear();
        if (row.List == null) return;

        // 元素與其展開出來的子列都要讓開左側的序號欄，父子左緣才對得齊。
        float elementPad = row.LeftPad + ListGutter;

        for (int i = 0; i < row.List.Count; i++)
        {
            var item = row.List[i];
            string childPath = row.Path + "[" + i + "]";
            AGRow child;

            if (item == null)
            {
                child = new AGRow { Kind = AGRowKind.Value, Label = "（空）", Depth = depth };
            }
            else if (AGReflect.IsSlotType(item.GetType()))
            {
                // 序號已經有自己的欄位，標籤只留內容。
                child = SlotRow(item, SlotShortName(item), depth);
            }
            else if (IsLeafValue(item.GetType()))
            {
                child = new AGRow { Kind = AGRowKind.Value, Label = "", Depth = depth, Target = row.List, Field = null, HideLabel = true };
            }
            else
            {
                child = new AGRow { Kind = AGRowKind.Group, Label = AGReflect.TypeName(item.GetType()), Depth = depth };
                BuildRows(item, depth + 1, child.Children, visited ?? new HashSet<object>(AGRefComparer.Instance),
                    childPath, elementPad);
            }

            child.Path = childPath;
            child.LeftPad = elementPad;
            child.IsListElement = true;
            MarkListSubtree(child, row, i);
            row.Children.Add(child);
        }
    }

    /// <summary>把元素與它展開出來的子列都認到同一個清單索引下，讓斑馬紋覆蓋整段。</summary>
    private static void MarkListSubtree(AGRow row, AGRow owner, int index)
    {
        // 內層清單已經認領的子樹不被外層覆蓋，巢狀清單才各自算自己的奇偶。
        if (row.ListOwner != null) return;
        row.ListOwner = owner;
        row.ListIndex = index;
        foreach (var child in row.Children) MarkListSubtree(child, owner, index);
    }

    private static AGRow SlotRow(object slot, string label, int depth)
    {
        bool isAction = AGReflect.IsActionSlotType(slot.GetType());
        return new AGRow
        {
            Kind = AGRowKind.Slot,
            Label = label,
            Depth = depth,
            Slot = slot,
            IsActionSlot = isAction,
            ResultType = isAction ? null : AGReflect.ResultType(slot.GetType()),
        };
    }

    /// <summary>清單裡的 Slot 顯示它目前接了什麼，企劃不用逐一點開。</summary>
    private static string SlotShortName(object slot)
    {
        bool isAction = AGReflect.IsActionSlotType(slot.GetType());

        // 動作欄位的自訂標籤優先：它存在的目的就是區分同型別的動作（「主傷害」「濺射」）。
        if (isAction)
        {
            string label = AGReflect.GetLabel(slot);
            if (!string.IsNullOrEmpty(label)) return label;
        }

        int useType = AGReflect.UseType(slot);
        switch (useType)
        {
            case 1:
                var f = AGReflect.GetFormula(slot);
                return f != null ? AGReflect.TypeName(f.GetType()) : "（空）";
            case 2:
                var a = AGReflect.GetAsset(slot);
                return a != null ? a.name : "（空資產）";
            default:
                return isAction ? "（未啟用）" : "常數";
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

    public static void MeasureNode(AGNode node)
    {
        node.Width = NodeWidth;
        // 節點上不畫型別說明（它是型別常數，重複出現只是噪音），改由畫布左上角的說明面板顯示選取節點的 Desc。
        // 註解則是「這一顆節點」的資訊，任何節點（含變數／資產葉節點）都能加。
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
        float y = MeasureRows(node.Rows, HeaderHeight + (node.IsAssetNode ? RowHeight : 0f));
        if (node.TipsHeight > 0f) y += node.TipsHeight + 10f;
        node.ContentHeight = Mathf.Max(y, HeaderHeight + 8f);
        node.Height = node.ContentHeight + NodeBottomPad;
    }

    private static void ApplyViewState(AGModel model, AGGraphView view, string noteOpenId,
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

    private static float MeasureRows(List<AGRow> rows, float y)
    {
        foreach (var r in rows)
        {
            r.LocalY = y;
            r.Hidden = false;
            switch (r.Kind)
            {
                case AGRowKind.Group:
                    r.Height = RowHeight;
                    y += RowHeight;
                    y = MeasureRows(r.Children, y);
                    break;
                case AGRowKind.List:
                    r.Height = RowHeight;
                    y += RowHeight;
                    if (r.Collapsed)
                    {
                        // 折疊的子列不佔高度，但接點要收斂到標題列中心：連線因此看起來是「插進這個清單」。
                        CollapseRows(r.Children, r.LocalY, r.Height);
                        r.AddRowY = r.LocalY;
                        break;
                    }
                    y = MeasureRows(r.Children, y);
                    r.AddRowY = y;                // 新增項目列
                    y += RowHeight;
                    break;
                default:
                    r.Height = RowHeight;
                    y += RowHeight;
                    break;
            }
        }
        return y;
    }

    /// <summary>把整個子樹壓到同一條列上並標記隱藏；高度保留是為了讓接點落在標題列中心。</summary>
    private static void CollapseRows(List<AGRow> rows, float y, float height)
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
    private static void AutoLayout(AGModel model, AGGraphView view)
    {
        var children = new Dictionary<AGNode, List<AGNode>>();
        foreach (var n in view.Nodes) children[n] = new List<AGNode>();

        var roots = new List<AGNode>();
        foreach (var n in view.Nodes)
        {
            AGNode parent = null;
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
        var heads = new List<AGNode>();
        var loose = new List<AGNode>();
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
    private static float Place(AGNode node, float x, float y, Dictionary<AGNode, List<AGNode>> children)
    {
        node.Pos = new Vector2(x, y);
        float childX = x + node.Width + ColumnGap;
        float childY = y;
        foreach (var c in children[node])
            childY = Place(c, childX, childY, children) + NodeGap;

        return Mathf.Max(y + node.Height, childY - NodeGap);
    }
}

}
