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
    public string TokenKey;               // 變數節點
    public Type ResultType;               // 資產／變數節點的結果型別
    public string Id;
    public string Title;                  // Header 主文字＝具體型別／變數／資產名稱，節點靠它辨識
    public string Chip;                   // Header 右側的結果型別標籤（契約），null 就不畫
    public string Desc;
    public bool IsRoot;
    public bool IsPlaceholder;            // Slot 尚未指定具體 Action／Formula
    public bool IsActionNode;
    public string Tips;
    /// <summary>註解框被打開但還沒有內容：只有這顆節點被選取時才成立，取消選取就收起來。</summary>
    public bool NoteOpen;

    public object ParentSlot;             // 這個節點接在哪個 Slot 上（root / orphan 為 null）
    public AGRow ParentRow;

    public List<AGRow> Rows = new();
    public Rect TitleRect;                // Header 名稱區（graph space）：整塊就是換來源的按鈕，繪製時寫入
    public Vector2 Pos;
    public float Width = AGGraph.NodeWidth;
    public float Height = 60f;
    public float ContentHeight;
    public float TipsHeight;
    // 換來源的入口是 Header 的名稱區；Root HEAD 的來源走它自己的「來源」參數列接點，所以不畫。
    public bool HasSourceSelector => !IsRoot && (IsPlaceholder || Obj != null || TokenKey != null || IsAssetNode);

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
    /// 建圖。rootSlot 畫成固定 HEAD；orphans 是本焦點的候選節點（含拖進畫布的獨立 Token／資產節點）。
    /// headTitle 是編輯對象自己的名稱（動作標籤／變數名／資產名），直接當 HEAD 的 Header。
    /// </summary>
    public static AGGraphView Build(AGModel model, object rootSlot, IList orphans, string focusId, string headTitle,
        IReadOnlyDictionary<string, bool> listCollapse = null, string noteOpenId = null,
        ICollection<string> noteCollapsed = null)
    {
        var view = new AGGraphView();
        if (rootSlot == null) return view;

        // 每次重建都重新登記 id → 載體，座標與備註的讀寫才找得到人。
        model.ClearCarriers();

        var root = MakeHeadNode(model, rootSlot, focusId, headTitle);
        Collect(model, root, view, 0, listCollapse);

        if (orphans != null)
        {
            foreach (var o in orphans)
            {
                if (o is not GraphNode carrier) continue;
                if (view.ByCarrier.ContainsKey(carrier)) continue;
                // 候選不需要額外標記：沒有連入線本身就是訊號。
                var node = MakeNodeForCarrier(model, carrier, null, null);
                Collect(model, node, view, 0, listCollapse);
            }
        }

        ApplyViewState(model, view, noteOpenId, noteCollapsed);
        AutoLayout(model, view);
        return view;
    }

    // ===== 節點建立 =====

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

    /// <summary>一個載體＝一個節點。內容種類決定畫成公式／動作、資產葉、變數葉或編輯中的空節點。</summary>
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
                break;
            }

            case NodeKind.Token:
            {
                string key = carrier.TokenKey ?? "";
                Type tokenResult = slotResultType ?? DeclaredTokenType(model, key);
                node = new AGNode
                {
                    TokenKey = key,
                    ResultType = tokenResult,
                    Title = "Token",
                    Chip = ChipText(tokenResult, false),
                };
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
        model.RegisterCarrier(id, carrier);
        return node;
    }

    /// <summary>
    /// 候選池裡的變數節點沒有父欄位可以推型別，改查變數宣告本身。
    /// 型別不只影響 chip：拉線相容性（`CanConnectLink`）與「編輯這個變數」都比對 `AGNode.ResultType`。
    /// </summary>
    private static Type DeclaredTokenType(AGModel model, string key)
    {
        if (model == null || string.IsNullOrEmpty(key)) return null;
        foreach (var token in model.ReadTokens())
            if (token.Key == key) return token.ResultType;
        return null;
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
        IReadOnlyDictionary<string, bool> listCollapse)
    {
        if (depth > 24) return;                       // 資料異常時不讓編輯器堆疊爆掉
        view.Nodes.Add(node);
        if (node.Carrier != null) view.ByCarrier[node.Carrier] = node;
        if (node.ParentSlot != null) view.BySlot[node.ParentSlot] = node;
        if (node.ParentRow != null) view.Links.Add(new AGLink { ParentRow = node.ParentRow, Target = node });
        // 折疊狀態要在量測之前套用：節點高度直接受它影響。節點 Id 到這裡才確定，所以不能在 BuildRows 做。
        ApplyListCollapse(node, listCollapse);
        MeasureNode(node);

        foreach (var row in AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;

            var carrier = AGReflect.GetNode(row.Slot);
            if (carrier == null) continue;            // 常數／空槽留在列上，不長節點
            if (row.IsActionSlot && carrier.Kind == NodeKind.Token) continue;   // 動作欄位不接變數

            // 共用來源：同一個載體被多個欄位指到時只有一個節點，這裡只補連線。
            if (view.ByCarrier.TryGetValue(carrier, out var existing))
            {
                view.Links.Add(new AGLink { ParentRow = row, Target = existing });
                view.BySlot[row.Slot] = existing;
                continue;
            }

            var child = MakeNodeForCarrier(model, carrier, row.Slot, row);
            Collect(model, child, view, depth + 1, listCollapse);
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
        int useType = AGReflect.UseType(slot);
        bool isAction = AGReflect.IsActionSlotType(slot.GetType());
        switch (useType)
        {
            case 1:
                var f = AGReflect.GetFormula(slot);
                return f != null ? AGReflect.TypeName(f.GetType()) : "（空）";
            case 2:
                var a = AGReflect.GetAsset(slot);
                return a != null ? a.name : "（空資產）";
            case 3:
                var k = AGReflect.GetTokenKey(slot);
                return string.IsNullOrEmpty(k) ? "（空變數）" : "@" + k;
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

        // 葉節點：Header 決定身分，本體有一列「選哪一個變數／資產」的下拉。
        // 空節點還沒決定身分，沒有東西可選，維持單行。
        if (node.IsAssetNode || node.TokenKey != null || node.IsPlaceholder)
        {
            float leafY = HeaderHeight;
            if (!node.IsPlaceholder) leafY += RowHeight;
            if (node.TipsHeight > 0f) leafY += node.TipsHeight + 12f;
            node.ContentHeight = leafY;
            node.Height = leafY + NodeBottomPad;
            return;
        }
        float y = MeasureRows(node.Rows, HeaderHeight);
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

        float cursorY = 40f;
        foreach (var r in roots)
        {
            cursorY = Place(r, 40f, cursorY, children) + NodeGap * 2f;
        }

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
