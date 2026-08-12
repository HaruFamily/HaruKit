namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

    public IList List;               // Kind == List
    public Type ElementType;

    public List<AGRow> Children = new();

    // 清單元素才有：讓繪製端知道要畫拖曳把手與刪除鈕
    public bool IsListElement;
    public AGRow ListOwner;
    public int ListIndex = -1;

    // 排版結果（每次重畫填）
    public float LocalY;
    public float Height;
    public float AddRowY;            // 清單列的「新增項目」列位置
    public Rect ScreenRect;
    public Vector2 PortPos;
    public bool HasPort => Kind == AGRowKind.Slot;
}

/// <summary>編輯區上的一個節點。</summary>
public class AGNode
{
    public object Obj;                    // ActionSystemNode（公式 / 動作）；資產、變數、空節點為 null
    public UnityEngine.Object Asset;      // 資產節點目前指到的資產（可為 null＝尚未指定）
    public bool IsAssetNode;              // 資產節點（不論有沒有指定資產）
    public string TokenKey;               // 變數節點
    public Type ResultType;               // 資產／變數節點的結果型別
    public string Id;
    public string Title;
    public string Desc;
    public bool IsRoot;
    public bool IsOrphan;
    public bool IsPlaceholder;            // 根節點還沒指定內容

    public object ParentSlot;             // 這個節點接在哪個 Slot 上（root / orphan 為 null）
    public AGRow ParentRow;

    public List<AGRow> Rows = new();
    public Vector2 Pos;
    public float Width = 280f;
    public float Height = 60f;

    public Rect Rect => new Rect(Pos.x, Pos.y, Width, Height);
    public Vector2 OutputPort => new Vector2(Pos.x, Pos.y + 14f);
}

/// <summary>一次焦點的完整節點圖。每次資料變動就整份重建，不做增量。</summary>
public class AGGraphView
{
    public List<AGNode> Nodes = new();
    public Dictionary<object, AGNode> BySlot = new(AGRefComparer.Instance);

    public AGNode FindByObject(object obj)
    {
        foreach (var n in Nodes)
            if (ReferenceEquals(n.Obj, obj)) return n;
        return null;
    }
}

/// <summary>
/// 由焦點根 Slot 遞迴展開節點圖：節點 → 參數列 → 子節點，並套用記憶座標或樹狀自動排版。
/// </summary>
public static class AGGraph
{
    public const float RowHeight = 20f;
    public const float HeaderHeight = 38f;
    public const float IndentWidth = 12f;
    public const float ColumnGap = 90f;
    public const float NodeGap = 24f;

    private static readonly HashSet<string> SkipFields = new()
    {
        "editorNodeId", "_previousAsset", "_dictKey",
    };

    /// <summary>
    /// 建圖。rootSlot 是動作清單的 ActionSlot 或 Token 的 FormulaSlot；orphans 是本焦點要一併顯示的未連接節點。
    /// </summary>
    public static AGGraphView Build(AGModel model, object rootSlot, string rootTitle, IList orphans)
    {
        var view = new AGGraphView();
        if (rootSlot == null) return view;

        var root = MakeNodeForSlot(model, rootSlot, null, true, "root");
        root.Title = string.IsNullOrEmpty(root.Title) ? rootTitle : root.Title;
        Collect(model, root, view, 0);

        if (orphans != null)
        {
            foreach (var o in orphans)
            {
                if (o == null) continue;
                var node = MakeNodeForObject(model, o, null, null);
                node.IsOrphan = true;
                Collect(model, node, view, 0);
            }
        }

        AutoLayout(model, view);
        return view;
    }

    // ===== 節點建立 =====

    // idBase：資產／變數節點沒有自己的 editorNodeId，改用「父節點 id + 列序」組出跨開關穩定的 id，座標才記得住。
    private static AGNode MakeNodeForSlot(AGModel model, object slot, AGRow parentRow, bool isRoot, string idBase)
    {
        int useType = AGReflect.UseType(slot);
        var resultType = AGReflect.ResultType(slot.GetType());

        if (useType == 1)
        {
            var formula = AGReflect.GetFormula(slot);
            if (formula != null)
            {
                var n = MakeNodeForObject(model, formula, slot, parentRow);
                n.IsRoot = isRoot;
                return n;
            }
        }

        if (useType == 2)
        {
            var asset = AGReflect.GetAsset(slot);
            return new AGNode
            {
                Asset = asset,
                IsAssetNode = true,
                ResultType = resultType,
                Id = idBase + ":asset",
                Title = asset != null ? asset.name : "（未指定資產）",
                Desc = resultType != null ? $"共用資產・{AGReflect.ResultTypeName(resultType)}" : "共用資產",
                ParentSlot = slot,
                ParentRow = parentRow,
                IsRoot = isRoot,
                Width = 230f,
            };
        }

        if (useType == 3)
        {
            string key = AGReflect.GetTokenKey(slot);
            return new AGNode
            {
                TokenKey = key ?? "",
                ResultType = resultType,
                Id = idBase + ":token",
                Title = string.IsNullOrEmpty(key) ? "（未指定變數）" : "@" + key,
                Desc = resultType != null
                    ? $"共用變數・{AGReflect.ResultTypeName(resultType)}（雙擊編輯它的公式）"
                    : "共用變數",
                ParentSlot = slot,
                ParentRow = parentRow,
                IsRoot = isRoot,
                Width = 210f,
            };
        }

        return new AGNode
        {
            Id = idBase + ":empty",
            Title = "（未指定內容）",
            Desc = "在此節點按右鍵選擇內容",
            ParentSlot = slot,
            ParentRow = parentRow,
            IsRoot = isRoot,
            IsPlaceholder = true,
            Width = 230f,
        };
    }

    private static AGNode MakeNodeForObject(AGModel model, object obj, object parentSlot, AGRow parentRow)
    {
        var node = new AGNode
        {
            Obj = obj,
            ParentSlot = parentSlot,
            ParentRow = parentRow,
            Title = AGReflect.TypeName(obj.GetType()),
            Desc = AGReflect.TypeDescription(obj.GetType()),
        };
        node.Id = obj is ActionSystemNode n ? n.EnsureEditorNodeId() : obj.GetHashCode().ToString();
        BuildRows(obj, 0, node.Rows, new HashSet<object>(AGRefComparer.Instance));
        return node;
    }

    /// <summary>把節點與其子樹加入視圖。</summary>
    private static void Collect(AGModel model, AGNode node, AGGraphView view, int depth)
    {
        if (depth > 24) return;                       // 資料異常時不讓編輯器堆疊爆掉
        view.Nodes.Add(node);
        if (node.ParentSlot != null) view.BySlot[node.ParentSlot] = node;
        MeasureNode(node);

        int index = 0;
        foreach (var row in AllRows(node.Rows))
        {
            index++;
            if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;

            // 公式、資產、變數三種來源一律長成節點，用連線表達引用關係；常數留在列上。
            int useType = AGReflect.UseType(row.Slot);
            bool hasChild = useType switch
            {
                1 => AGReflect.GetFormula(row.Slot) != null,
                2 => true,
                3 => !row.IsActionSlot,
                _ => false,
            };
            if (!hasChild) continue;

            var child = MakeNodeForSlot(model, row.Slot, row, false, $"{node.Id}#{index}");
            Collect(model, child, view, depth + 1);
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

    private static void BuildRows(object obj, int depth, List<AGRow> into, HashSet<object> visited)
    {
        if (obj == null || depth > 5 || !visited.Add(obj)) return;

        foreach (var f in AGReflect.Fields(obj.GetType()))
        {
            if (SkipFields.Contains(f.Name)) continue;
            if (f.IsNotSerialized) continue;
            if (f.IsStatic) continue;

            var t = f.FieldType;
            string label = AGReflect.FieldLabel(f);

            if (AGReflect.IsSlotType(t))
            {
                var slot = f.GetValue(obj);
                if (slot == null)
                {
                    slot = AGReflect.CreateInstance(t);      // 缺 Slot 就補一個，避免整列不可編輯
                    if (slot != null) f.SetValue(obj, slot);
                }
                if (slot == null) continue;
                into.Add(SlotRow(slot, label, depth));
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
                    List = list,
                    ElementType = elem,
                    Target = obj,
                    Field = f,
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
                    Target = obj,
                    Field = f,
                });
                continue;
            }

            // 其餘視為巢狀資料：展開成一個群組，內容遞迴。
            var value = f.GetValue(obj);
            if (value == null) continue;
            var group = new AGRow { Kind = AGRowKind.Group, Label = label, Depth = depth };
            BuildRows(value, depth + 1, group.Children, visited);
            if (group.Children.Count > 0) into.Add(group);
        }
    }

    /// <summary>清單元素展開：Slot 元素直接成列，複合元素展開成子群組。</summary>
    public static void BuildListChildren(AGRow row, int depth, HashSet<object> visited)
    {
        row.Children.Clear();
        if (row.List == null) return;

        for (int i = 0; i < row.List.Count; i++)
        {
            var item = row.List[i];
            string label = $"{i + 1}.";
            AGRow child;

            if (item == null)
            {
                child = new AGRow { Kind = AGRowKind.Value, Label = label + " （空）", Depth = depth };
            }
            else if (AGReflect.IsSlotType(item.GetType()))
            {
                child = SlotRow(item, label + " " + SlotShortName(item), depth);
            }
            else if (IsLeafValue(item.GetType()))
            {
                child = new AGRow { Kind = AGRowKind.Value, Label = label, Depth = depth, Target = row.List, Field = null };
            }
            else
            {
                child = new AGRow { Kind = AGRowKind.Group, Label = label, Depth = depth };
                BuildRows(item, depth + 1, child.Children, visited ?? new HashSet<object>(AGRefComparer.Instance));
            }

            child.IsListElement = true;
            child.ListOwner = row;
            child.ListIndex = i;
            row.Children.Add(child);
        }
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
        // 資產／變數節點是葉節點，沒有參數列，高度固定。
        if (node.IsAssetNode) { node.Height = 64f; return; }
        if (node.TokenKey != null) { node.Height = 56f; return; }

        float y = HeaderHeight;
        y = MeasureRows(node.Rows, y);
        node.Height = Mathf.Max(y + 6f, HeaderHeight + 8f);
    }

    private static float MeasureRows(List<AGRow> rows, float y)
    {
        foreach (var r in rows)
        {
            r.LocalY = y;
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
