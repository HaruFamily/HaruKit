namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 畫布群組：建立、繪製、整組移位、改名、改色，以及拖節點放手時的進出判定。
/// 群組是版面，正本在 <see cref="GraphViewState"/>，所有修改走 <see cref="MarkViewStateChanged"/>。
/// </summary>
// 框由成員目前的外框當場算出，不存起來：List 增減、折疊、註解都會改變節點高度，
// 每次重建圖都重新量過，框每幀跟著算就不必另外掛更新點。文件上的 Rect 只在編輯群組時順手寫回，
// 平時只當「沒有可見成員」時的位置，所以展開清單不會讓文件變成未存檔。
public partial class HaruGraphWindow
{
    /// <summary>目前畫布的群組，依繪製順序（越後面越上層）。文件不保存版面時沒有群組。</summary>
    private IEnumerable<GraphNodeGroup> CurrentNodeGroups()
    {
        var state = CurrentViewState();
        if (state == null || focus == null) yield break;
        foreach (var group in state.NodeGroups)
            if (group != null && group.Scope == focus.Id) yield return group;
    }

    /// <summary>
    /// 可見成員的外框加內距，標題列（與分頁列）貼在上方。被 ⊖ 收起而隱藏的成員不算；
    /// 不在作用中分頁的成員照算，切分頁時框的大小與標題列位置才不會動。
    /// 收合時只剩標題列，寬度用收合前寫回的框；成員都被群組外的欄位收起時用文件上存的框；
    /// 完全沒有成員時回到「建立群組」的預設尺寸。
    /// </summary>
    private Rect NodeGroupHull(GraphNodeGroup group)
    {
        if (group.Collapsed)
            return new Rect(group.Rect.x, group.Rect.y, Mathf.Max(group.Rect.width, NodeGroupMinSize.x), NodeGroupHeaderHeight);
        var empty = new Rect(group.Rect.position, EmptyNodeGroupSize);
        if (graph == null) return empty;
        var members = new HashSet<string>(group.Members);
        bool any = false;
        float xMin = 0f, yMin = 0f, xMax = 0f, yMax = 0f;
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id) || !members.Contains(node.Id)) continue;
            if (node.Hidden && !tabHiddenMembers.Contains(node.Id)) continue;
            Rect r = node.Rect;
            if (!any)
            {
                xMin = r.xMin; yMin = r.yMin; xMax = r.xMax; yMax = r.yMax;
                any = true;
                continue;
            }
            xMin = Mathf.Min(xMin, r.xMin); yMin = Mathf.Min(yMin, r.yMin);
            xMax = Mathf.Max(xMax, r.xMax); yMax = Mathf.Max(yMax, r.yMax);
        }
        if (!any) return HasAnyMember(group) ? group.Rect : empty;

        var hull = Rect.MinMaxRect(xMin - NodeGroupPadding, yMin - NodeGroupPadding - NodeGroupTopInset(group), xMax + NodeGroupPadding, yMax + NodeGroupPadding);
        hull.width = Mathf.Max(hull.width, NodeGroupMinSize.x, NodeGroupTabStripWidth(group));
        hull.height = Mathf.Max(hull.height, NodeGroupMinSize.y);
        return hull;
    }

    /// <summary>成員區上方被標題列與分頁列佔掉的高度。分頁列常駐，只有收合時不畫。</summary>
    private static float NodeGroupTopInset(GraphNodeGroup group)
        => NodeGroupHeaderHeight + (group.Collapsed ? 0f : NodeGroupTabHeight);

    /// <summary>分頁列上的頁數：沒有分頁資料的群組也畫一頁「分頁 1」。</summary>
    private static int NodeGroupTabCount(GraphNodeGroup group) => Mathf.Max(1, group.Tabs.Count);

    /// <summary>分頁列要的寬度：框至少這麼寬，每一頁的標籤與「＋」才放得下。</summary>
    private static float NodeGroupTabStripWidth(GraphNodeGroup group)
    {
        float width = NodeGroupTabInset * 2f + NodeGroupTabAddWidth;
        for (int i = 0; i < NodeGroupTabCount(group); i++) width += NodeGroupTabWidth(group, i);
        return width;
    }

    private static float NodeGroupTabWidth(GraphNodeGroup group, int index)
    {
        string name = NodeGroupTabName(group, index);
        return Mathf.Clamp(HGStyles.Tiny.CalcSize(new GUIContent(name)).x + 16f, NodeGroupTabMinWidth, NodeGroupTabMaxWidth);
    }

    private static string NodeGroupTabName(GraphNodeGroup group, int index)
    {
        string name = index < group.Tabs.Count ? group.Tabs[index]?.Name : null;
        return string.IsNullOrEmpty(name) ? $"{DefaultNodeGroupTabTitle} {index + 1}" : name;
    }

    /// <summary>游標（graph space）落在群組的第幾頁標籤上；沒有分頁、收合中或不在標籤上回 -1。</summary>
    private int NodeGroupTabAt(GraphNodeGroup group, Vector2 graphPoint)
    {
        if (!group.HasTabs || group.Collapsed) return -1;
        Rect frame = NodeGroupRectOf(group);
        for (int i = 0; i < NodeGroupTabCount(group); i++)
        {
            Rect tab = NodeGroupTabRect(group, frame, i);
            if (tab.x >= frame.xMax) break;
            tab.xMax = Mathf.Min(tab.xMax, frame.xMax);
            if (tab.Contains(graphPoint)) return i;
        }
        return -1;
    }

    /// <summary>第 index 頁標籤的框（跟著傳進來的群組框算，畫面與 graph space 都能用）。</summary>
    private static Rect NodeGroupTabRect(GraphNodeGroup group, Rect frame, int index)
    {
        float x = frame.x + NodeGroupTabInset;
        for (int i = 0; i < index; i++) x += NodeGroupTabWidth(group, i);
        return new Rect(x, frame.y + NodeGroupHeaderHeight, NodeGroupTabWidth(group, index), NodeGroupTabHeight);
    }

    /// <summary>畫面與命中用的框：整組拖曳中用暫存框，拖節點期間用凍結框，其餘當場包住成員。</summary>
    private Rect NodeGroupRectOf(GraphNodeGroup group)
    {
        if (ReferenceEquals(group, dragNodeGroup)) return nodeGroupDragRect;
        if (frozenNodeGroupRects.TryGetValue(group, out var frozen)) return frozen;
        return NodeGroupHull(group);
    }

    private static Rect NodeGroupHeaderRect(Rect r) => new(r.x, r.y, r.width, NodeGroupHeaderHeight);

    /// <summary>游標下最上層群組的標題列。</summary>
    private GraphNodeGroup NodeGroupHeaderAt(Vector2 graphPoint)
    {
        GraphNodeGroup found = null;
        foreach (var group in CurrentNodeGroups())
            if (NodeGroupHeaderRect(NodeGroupRectOf(group)).Contains(graphPoint)) found = group;
        return found;
    }

    /// <summary>游標所在的最上層群組（含標題列）。拖節點放手時用它決定進出。</summary>
    private GraphNodeGroup NodeGroupContaining(Vector2 graphPoint)
    {
        GraphNodeGroup found = null;
        foreach (var group in CurrentNodeGroups())
            if (NodeGroupRectOf(group).Contains(graphPoint)) found = group;
        return found;
    }

    // ===== 繪製 =====

    /// <summary>群組壓在連線與節點底下，所以自己開一次縮放畫布，在 DrawLinks 之前畫。順便重算成員的染色表。</summary>
    private void DrawNodeGroups(Rect canvas, Vector2 graphMouse)
    {
        nodeGroupColors.Clear();
        if (graph == null) return;
        var groups = new List<GraphNodeGroup>(CurrentNodeGroups());
        if (groups.Count == 0) return;

        var nodeIds = new HashSet<string>();
        foreach (var node in graph.Nodes)
            if (!string.IsNullOrEmpty(node.Id)) nodeIds.Add(node.Id);
        foreach (var group in groups)
        {
            Color color = NodeGroupColorOf(group);
            foreach (var id in group.Members)
                if (nodeIds.Contains(id)) nodeGroupColors[id] = color;
        }

        // 代理接點畫在哪一側，看這一代的連線；和 DrawLinks 用同一份扇形資料。
        RebuildFoldFan();

        // 放手就會落進去的群組：外框加亮，拖曳當下就看得出結果。
        GraphNodeGroup dropTarget = dragNode != null && dragMoved ? NodeGroupContaining(graphMouse) : null;

        // 標題改名是先畫先拿事件的控制項：游標在節點上時，這一下屬於節點，不能被壓在底下的標題吃掉。
        var e = Event.current;
        bool block = e.isMouse && NodeAt(graphMouse) != null;
        EventType before = e.type;
        if (block) e.type = EventType.Ignore;
        BeginZoomedCanvas(canvas);
        try
        {
            foreach (var group in groups) DrawNodeGroup(group, ReferenceEquals(group, dropTarget));
        }
        finally
        {
            EndZoomedCanvas();
            if (block) e.type = before;
        }
    }

    private void DrawNodeGroup(GraphNodeGroup group, bool dropTarget)
    {
        Rect r = NodeGroupRectOf(group);
        var visual = new Rect(r.position + pan, r.size);
        Color color = NodeGroupColorOf(group);
        CountNodeGroupMembers(group, out int present, out int visible);
        Rect header = NodeGroupHeaderRect(visual);
        bool selected = group.Id == selectedNodeGroupId;
        HGStyles.Fill(visual, WithAlpha(color, 0.13f));
        HGStyles.Fill(header, WithAlpha(color, 0.55f));
        HGStyles.Frame(visual, selected ? HGStyles.NodeBorderSelected : color, selected || dropTarget ? 2f : 1f);

        // 標題列左端的色塊＝換色入口。按下由按鈕吃掉，所以不會被當成拖曳整組。
        var swatch = new Rect(header.x + 8f, header.y + 6f, 12f, 12f);
        HGStyles.Fill(swatch, color);
        HGStyles.Frame(swatch, HGStyles.HeaderInk);
        if (GUI.Button(swatch, new GUIContent("", "換群組色"), GUIStyle.none))
            OpenNodeGroupColorPopup(group, new Rect(r.x + 8f, r.y + 6f, 12f, 12f));

        // 收合鈕：只影響顯示，成員與連線都還在。
        var fold = new Rect(swatch.xMax + 4f, header.y + 3f, 16f, 18f);
        if (GUI.Button(fold, new GUIContent(group.Collapsed ? "▸" : "▾", group.Collapsed ? "展開群組" : "收合群組"), HGStyles.HeaderButton))
            ToggleNodeGroupCollapsed(group);
        DrawNodeGroupProxies(group, visual, color);

        // 有成員被群組外的欄位收起時寫「看得到/全部」；它們的連線以虛線接到標題列兩端的代表接點。
        string countText = !group.Collapsed && visible < present ? $"({visible}/{present})" : $"({present})";
        var countContent = new GUIContent(countText);
        float countWidth = HGStyles.Tiny.CalcSize(countContent).x;
        var countRect = new Rect(header.xMax - countWidth - 8f, header.y + 4f, countWidth, 16f);
        GUI.Label(countRect, countContent, HGStyles.Tiny);

        var titleRect = new Rect(fold.xMax + 4f, header.y + 3f, Mathf.Max(0f, countRect.x - fold.xMax - 10f), 18f);
        var target = group;
        inlineName.Draw(titleRect, group, HGInlineRename.SiteNodeGroup, group.Title, group.Title, HGStyles.NodeTitle,
            "雙擊改名；拖曳標題列＝整組移動；右鍵＝群組選單", name => RenameNodeGroup(target, name));

        if (!group.Collapsed) DrawNodeGroupTabs(group, visual, color);
    }

    /// <summary>
    /// 標題列下方常駐的分頁列：點一下切換、雙擊改名、右鍵開分頁選單，最後面的「＋」新增一頁。
    /// 沒有分頁資料的群組畫一頁「分頁 1」。
    /// 分頁列畫在群組的縮放畫布裡，比 HandleCanvasInput 先拿到事件，所以按在上面不會變成框選。
    /// </summary>
    private void DrawNodeGroupTabs(GraphNodeGroup group, Rect visualFrame, Color color)
    {
        var e = Event.current;
        int active = group.ActiveTab;
        int count = NodeGroupTabCount(group);
        var strip = new Rect(visualFrame.x, visualFrame.y + NodeGroupHeaderHeight, visualFrame.width, NodeGroupTabHeight);
        HGStyles.Fill(strip, WithAlpha(color, 0.22f));

        // 「＋」接在最後一頁後面；框至少有分頁列那麼寬，所以放得下。
        Rect last = NodeGroupTabRect(group, visualFrame, count - 1);
        var add = new Rect(last.xMax, strip.y, NodeGroupTabAddWidth, NodeGroupTabHeight);
        if (GUI.Button(add, new GUIContent("+", "新增分頁"), HGStyles.HeaderButton))
        {
            inlineName.Commit();
            AddNodeGroupTab(group);
        }

        for (int i = 0; i < count; i++)
        {
            Rect tab = NodeGroupTabRect(group, visualFrame, i);
            if (tab.x >= strip.xMax) break;
            tab.xMax = Mathf.Min(tab.xMax, strip.xMax);
            bool isActive = i == active;
            if (isActive) HGStyles.Fill(tab, WithAlpha(color, 0.55f));
            else if (tab.Contains(e.mousePosition)) HGStyles.Fill(tab, WithAlpha(color, 0.35f));
            if (isActive) HGStyles.Fill(new Rect(tab.x, tab.yMax - 2f, tab.width, 2f), color);

            var label = new Rect(tab.x + 6f, tab.y + 2f, Mathf.Max(0f, tab.width - 12f), tab.height - 4f);
            var owner = group;
            int index = i;
            string name = NodeGroupTabName(group, i);
            // 改名狀態以分頁物件當身分；還沒有分頁資料的「分頁 1」借群組當身分（site 和群組標題不同，不會一起進編輯）。
            object renameTarget = i < group.Tabs.Count ? group.Tabs[i] : group;
            if (renameTarget == null) GUI.Label(label, HGStyles.Elide(name, HGStyles.Tiny, label.width, null), HGStyles.Tiny);
            else if (inlineName.Draw(label, renameTarget, HGInlineRename.SiteNodeGroupTab, name, name,
                    HGStyles.Tiny, "點一下切換；雙擊改名；右鍵＝分頁選單", text => RenameNodeGroupTab(owner, index, text)))
                continue;

            if (e.type != EventType.MouseDown || !tab.Contains(e.mousePosition)) continue;
            if (e.button == 0 && e.clickCount == 1)
            {
                inlineName.Commit();
                SetNodeGroupActiveTab(group, i);
                e.Use();
            }
            else if (e.button == 1)
            {
                SelectNodeGroup(group);
                ShowNodeGroupTabMenu(group, i);
                e.Use();
            }
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    /// <summary>群組的實際顏色：勾了自訂色就用它，否則取主題調色盤。</summary>
    private static Color NodeGroupColorOf(GraphNodeGroup group)
        => group.UseCustomColor ? group.CustomColor : HGStyles.NodeGroupColor(group.ColorIndex);

    /// <summary>節點本體底色：群組成員混入群組色，其餘照舊。Header 不染，身分色維持最顯眼。</summary>
    private Color NodeBodyColor(HGNodeView node)
        => !string.IsNullOrEmpty(node.Id) && nodeGroupColors.TryGetValue(node.Id, out var color)
            ? HGStyles.NodeGroupBody(color)
            : HGStyles.NodeBody;

    /// <summary>
    /// 群組成員本體左緣的色條：縮小畫面時混色幾乎看不見，色條在任何縮放下都認得出。
    /// 畫在節點之後，不被參數列的底色蓋掉；避開 Header 與下方圓角。
    /// </summary>
    private void DrawNodeGroupStripe(HGNodeView node)
    {
        if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(node.Id)) return;
        if (!nodeGroupColors.TryGetValue(node.Id, out var color)) return;
        float height = node.Height - HGGraph.HeaderHeight - NodeCornerRadius;
        if (height <= 0f) return;
        HGStyles.Fill(new Rect(node.Pos.x + pan.x, node.Pos.y + pan.y + HGGraph.HeaderHeight, 3f, height), color);
    }

    // ===== 建立、改名、改色、刪除 =====

    private void AddNodeGroupMenuItems(GenericMenu menu, Vector2 graphMouse)
    {
        int count = 0;
        foreach (var node in SelectedNodes())
            if (!node.Hidden) count++;
        string label = count > 0 ? $"以選取節點建立群組 ({count})" : "建立群組";
        if (CurrentViewState() == null || focus == null || focus.Kind == HGFocusKind.None)
        {
            menu.AddDisabledItem(new GUIContent(label + "（這份文件不保存版面）"));
            return;
        }
        menu.AddItem(new GUIContent(label), false, () => CreateNodeGroup(graphMouse));
    }

    /// <summary>有選取時收選取節點為成員（一顆節點只屬於一個群組）；沒有選取時在指定位置建一個空群組。</summary>
    private void CreateNodeGroup(Vector2 graphMouse)
    {
        var state = CurrentViewState();
        if (state == null || focus == null || graph == null) return;

        var groups = new List<GraphNodeGroup>(CurrentNodeGroups());
        int palette = Mathf.Max(1, HGStyles.NodeGroupPaletteCount);
        BreakUndoMerge();
        var group = new GraphNodeGroup(focus.Id, DefaultNodeGroupTitle, new Rect(SnapToGrid(graphMouse), EmptyNodeGroupSize), groups.Count % palette);
        state.AddNodeGroup(group);
        groups.Add(group);
        foreach (var node in SelectedNodes())
            if (!node.Hidden) AssignNodeGroup(node.Id, group, groups);
        group.SetRect(NodeGroupHull(group));
        MarkViewStateChanged();
        Repaint();
    }

    private bool RenameNodeGroup(GraphNodeGroup group, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? DefaultNodeGroupTitle : name.Trim();
        if (group.SetTitle(name)) MarkViewStateChanged();
        Repaint();
        return true;
    }

    /// <summary>群組標題列右鍵：順序比照節點右鍵，刪除在最上面，接著是只作用在這個群組的畫布操作。</summary>
    private void ShowNodeGroupMenu(GraphNodeGroup group)
    {
        var menu = new GenericMenu();
        // 只刪群組，成員節點保留。
        menu.AddItem(new GUIContent("刪除"), false, () => DeleteNodeGroup(group));
        menu.AddSeparator("");
        AddNodeGroupScopeItems(menu, group);
        // 換色在標題列的色塊，右鍵不重複放。最後一段和節點右鍵一樣是整張畫布的操作。
        AddCanvasMenuItems(menu, () => menu.AddSeparator(""));
        menu.ShowAsContext();
    }

    /// <summary>
    /// 分頁右鍵：刪除這一頁在最上面（成員回到第一頁），其餘與標題列右鍵相同。
    /// 新增是分頁列最後面的「＋」，改名是雙擊標籤，都不放右鍵。只剩一頁時不能刪。
    /// </summary>
    private void ShowNodeGroupTabMenu(GraphNodeGroup group, int index)
    {
        var menu = new GenericMenu();
        const string deleteLabel = "刪除此分頁（成員移到第一頁）";
        if (group.HasTabs) menu.AddItem(new GUIContent(deleteLabel), false, () => DeleteNodeGroupTab(group, index));
        else menu.AddDisabledItem(new GUIContent(deleteLabel + "（只剩一頁）"));
        menu.AddSeparator("");
        AddNodeGroupScopeItems(menu, group);
        AddCanvasMenuItems(menu, () => menu.AddSeparator(""));
        menu.ShowAsContext();
    }

    /// <summary>
    /// 在最後面加一頁並切過去。群組原本沒有分頁時連第一頁一起建，原有成員都留在第一頁。
    /// 新的一頁是空的，框的大小不變；之後拖進來的節點落在這一頁。
    /// </summary>
    private void AddNodeGroupTab(GraphNodeGroup group)
    {
        BreakUndoMerge();
        if (!group.HasTabs && group.Tabs.Count == 0) group.AddTab($"{DefaultNodeGroupTabTitle} 1");
        int index = group.AddTab($"{DefaultNodeGroupTabTitle} {group.Tabs.Count + 1}");
        group.SetActiveTab(index);
        group.SetCollapsed(false);
        MarkViewStateChanged();
        graphDirty = true;
        Repaint();
    }

    private void DeleteNodeGroupTab(GraphNodeGroup group, int index)
    {
        BreakUndoMerge();
        if (!group.RemoveTab(index)) return;
        MarkViewStateChanged();
        graphDirty = true;
        Repaint();
    }

    /// <summary>切換分頁只改顯示哪些成員；框包住所有分頁的成員，所以大小與標題列位置都不動。</summary>
    private void SetNodeGroupActiveTab(GraphNodeGroup group, int index)
    {
        BreakUndoMerge();
        if (!group.SetActiveTab(index)) return;
        // 被切走的那一頁若有選取中的節點，選取留著也看不到，Delete 會刪到看不見的節點。
        foreach (var id in group.Members)
            if (group.TabOf(id) != index) selectedIds.Remove(id);
        MarkViewStateChanged();
        graphDirty = true;
        Repaint();
    }

    private bool RenameNodeGroupTab(GraphNodeGroup group, int index, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? $"{DefaultNodeGroupTabTitle} {index + 1}" : name.Trim();
        // 還沒有分頁資料的「分頁 1」：改名時才建出這一頁，名字才存得住。
        if (group.Tabs.Count == 0 && index == 0)
        {
            BreakUndoMerge();
            group.AddTab(name);
            MarkViewStateChanged();
        }
        else if (group.SetTabName(index, name)) MarkViewStateChanged();
        Repaint();
        return true;
    }

    /// <summary>
    /// 只作用在這個群組的聚焦與整理。「全部節點／整理版面」在每個選單都指整張畫布，
    /// 所以群組範圍明寫「此群組」，不借用同一個字。標題列右鍵與群組內部空白右鍵共用。
    /// </summary>
    private void AddNodeGroupScopeItems(GenericMenu menu, GraphNodeGroup group)
    {
        menu.AddItem(new GUIContent("聚焦此群組"), false, () => FrameNodeGroup(group));
        menu.AddItem(new GUIContent("整理此群組"), false, () => ArrangeNodeGroup(group));
    }

    private void FrameNodeGroup(GraphNodeGroup group)
    {
        if (graph == null) return;
        FrameRect(NodeGroupHull(group));
    }

    /// <summary>
    /// 只整理這個群組的可見成員，而且留在群組原本的左上角：依成員之間的父子關係分欄（父在左、來源在右），
    /// 同一欄照目前的上下順序排。不動群組以外的節點，所以不走全畫布的 AutoLayout。
    /// </summary>
    private void ArrangeNodeGroup(GraphNodeGroup group)
    {
        if (graph == null) return;
        var memberIds = new HashSet<string>(group.Members);
        var members = new List<HGNodeView>();
        foreach (var node in graph.Nodes)
            if (!node.Hidden && !string.IsNullOrEmpty(node.Id) && memberIds.Contains(node.Id)) members.Add(node);
        if (members.Count == 0) return;

        var byId = new Dictionary<string, HGNodeView>();
        float originX = float.MaxValue, originY = float.MaxValue;
        foreach (var node in members)
        {
            byId[node.Id] = node;
            originX = Mathf.Min(originX, node.Pos.x);
            originY = Mathf.Min(originY, node.Pos.y);
        }

        var columns = new SortedDictionary<int, List<HGNodeView>>();
        foreach (var node in members)
        {
            int depth = NodeGroupDepth(node, byId);
            if (!columns.TryGetValue(depth, out var column)) columns[depth] = column = new List<HGNodeView>();
            column.Add(node);
        }

        BreakUndoMerge();
        var origin = SnapToGrid(new Vector2(originX, originY));
        float x = origin.x;
        foreach (var column in columns.Values)
        {
            column.Sort((a, b) => a.Pos.y.CompareTo(b.Pos.y));
            float y = origin.y;
            float width = 0f;
            foreach (var node in column)
            {
                node.Pos = new Vector2(x, y);
                model.SetPosition(node.Id, node.Pos);
                y = SnapUp(y + node.Height + ArrangeGap);
                width = Mathf.Max(width, node.Width);
            }
            x = SnapUp(x + width + ArrangeGap * 2f);
        }
        MarkPositionsChanged();
        group.SetRect(NodeGroupHull(group));
        MarkViewStateChanged();
        Repaint();
    }

    private static float SnapUp(float value) => Mathf.Ceil(value / HGGraph.GridSize) * HGGraph.GridSize;

    /// <summary>在群組內往上數幾層父節點也是成員；只沿建圖時的父欄位走，共用節點算第一次走到的那條。</summary>
    private static int NodeGroupDepth(HGNodeView node, Dictionary<string, HGNodeView> members)
    {
        int depth = 0;
        var seen = new HashSet<HGNodeView>();
        var current = node;
        while (current?.ParentRow != null && seen.Add(current))
        {
            if (!members.TryGetValue(current.ParentRow.OwnerNodeId ?? "", out var parent)) break;
            current = parent;
            depth++;
        }
        return depth;
    }

    /// <summary>把一顆節點加入群組（同時從同畫布其他群組移出），記成版面修改。</summary>
    private void JoinNodeGroup(string nodeId, GraphNodeGroup group)
    {
        var groups = new List<GraphNodeGroup>(CurrentNodeGroups());
        if (AssignNodeGroup(nodeId, group, groups)) MarkViewStateChanged();
    }

    /// <summary>開群組顏色面板。swatchGraphRect 是色塊在 graph space 的位置，換成視窗座標當錨點。</summary>
    private void OpenNodeGroupColorPopup(GraphNodeGroup group, Rect swatchGraphRect)
    {
        string id = group.Id;
        BreakUndoMerge();
        RequestPopup(GraphToWindowRect(swatchGraphRect), new HGNodeGroupColorPopup(
            HGStyles.NodeGroupPalette, group.ColorIndex, group.UseCustomColor, group.CustomColor,
            index => ChangeNodeGroupColor(id, g => g.SetPaletteColor(index)),
            color => ChangeNodeGroupColor(id, g => g.SetCustomColor(color))));
    }

    /// <summary>面板回呼用 Id 找群組：面板開著時 Undo 可能已經換掉群組物件。</summary>
    private void ChangeNodeGroupColor(string id, Func<GraphNodeGroup, bool> change)
    {
        foreach (var group in CurrentNodeGroups())
        {
            if (group.Id != id) continue;
            if (change(group)) MarkViewStateChanged();
            break;
        }
        Repaint();
    }

    private void DeleteNodeGroup(GraphNodeGroup group)
    {
        var state = CurrentViewState();
        if (state == null) return;
        BreakUndoMerge();
        if (state.RemoveNodeGroup(group)) MarkViewStateChanged();
        if (group.Id == selectedNodeGroupId) selectedNodeGroupId = null;
        Repaint();
    }

    /// <summary>選取群組：和節點選取互斥，選群組時清掉節點選取，Delete 才不會刪到節點。</summary>
    private void SelectNodeGroup(GraphNodeGroup group)
    {
        selectedNodeGroupId = group?.Id;
        selectedIds.Clear();
        Repaint();
    }

    /// <summary>刪除選取中的群組，成員節點保留。沒有選取群組（或它已不在目前畫布）時回 false。</summary>
    private bool DeleteSelectedNodeGroup()
    {
        if (string.IsNullOrEmpty(selectedNodeGroupId)) return false;
        foreach (var group in CurrentNodeGroups())
        {
            if (group.Id != selectedNodeGroupId) continue;
            DeleteNodeGroup(group);
            return true;
        }
        selectedNodeGroupId = null;
        return false;
    }

    // ===== 成員進出 =====

    /// <summary>一顆節點只屬於一個群組：加入 target 的同時從同畫布其他群組移出。target 為 null＝移出所有群組。</summary>
    private static bool AssignNodeGroup(string nodeId, GraphNodeGroup target, List<GraphNodeGroup> groups)
    {
        if (string.IsNullOrEmpty(nodeId)) return false;
        bool changed = false;
        foreach (var group in groups)
            changed |= group.SetMember(nodeId, ReferenceEquals(group, target));
        return changed;
    }

    /// <summary>開始拖節點：把每個群組的框凍結在拖曳前的樣子，框不跟著被拖的成員長。</summary>
    private void FreezeNodeGroupRects()
    {
        frozenNodeGroupRects.Clear();
        foreach (var group in CurrentNodeGroups()) frozenNodeGroupRects[group] = NodeGroupHull(group);
    }

    /// <summary>
    /// 拖完節點放手：滑鼠落在哪個凍結框（含標題列，取最上層）就把被拖的節點都收進去，落在框外就移出群組。
    /// 之後解除凍結並把各群組的框寫回。回傳有沒有改到群組。
    /// </summary>
    private bool ApplyDropMembership(IEnumerable<HGNodeView> moved, Vector2 graphMouse)
    {
        var groups = new List<GraphNodeGroup>(CurrentNodeGroups());
        if (groups.Count == 0)
        {
            frozenNodeGroupRects.Clear();
            return false;
        }

        GraphNodeGroup target = NodeGroupContaining(graphMouse);
        bool changed = false;
        foreach (var node in moved)
            changed |= AssignNodeGroup(node.Id, target, groups);

        // 放在某一頁的標籤上＝搬到那一頁並切過去；其餘位置落在作用中的分頁。
        int tab = target != null ? NodeGroupTabAt(target, graphMouse) : -1;
        if (tab >= 0)
        {
            foreach (var node in moved) changed |= target.SetMemberTab(node.Id, tab);
            changed |= target.SetActiveTab(tab);
            graphDirty = true;
        }

        var frozen = new Dictionary<GraphNodeGroup, Rect>(frozenNodeGroupRects);
        frozenNodeGroupRects.Clear();
        foreach (var group in groups)
        {
            // 收合中的群組只剩標題列，框寫回會丟掉收合前的大小：只改成員，不動框。
            if (group.Collapsed) continue;
            // 最後一個成員離開的群組留在原位，尺寸退回預設。
            Rect hull = NodeGroupHull(group);
            if (!HasAnyMember(group) && frozen.TryGetValue(group, out var before)) hull = new Rect(before.position, EmptyNodeGroupSize);
            changed |= group.SetRect(hull);
        }
        return changed;
    }

    /// <summary>目前畫布上有沒有這個群組的成員（含被收起而隱藏的）。</summary>
    private bool HasAnyMember(GraphNodeGroup group)
    {
        if (graph == null) return false;
        var members = new HashSet<string>(group.Members);
        foreach (var node in graph.Nodes)
            if (!string.IsNullOrEmpty(node.Id) && members.Contains(node.Id)) return true;
        return false;
    }

    /// <summary>成員數：present＝畫布上找得到的，visible＝其中沒被 ⊖ 收起的（只因分頁而藏的照算）。</summary>
    private void CountNodeGroupMembers(GraphNodeGroup group, out int present, out int visible)
    {
        present = visible = 0;
        if (graph == null) return;
        var members = new HashSet<string>(group.Members);
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id) || !members.Contains(node.Id)) continue;
            present++;
            if (!node.Hidden || tabHiddenMembers.Contains(node.Id)) visible++;
        }
    }

    // ===== 收合與代理接點 =====

    /// <summary>收合前先把目前的框寫回，展開前後的位置與寬度才對得上。</summary>
    private void ToggleNodeGroupCollapsed(GraphNodeGroup group)
    {
        BreakUndoMerge();
        if (!group.Collapsed) group.SetRect(NodeGroupHull(group));
        group.SetCollapsed(!group.Collapsed);
        MarkViewStateChanged();
        graphDirty = true;
        Repaint();
    }

    /// <summary>節點所在的群組若收合中就展開、不在作用中的分頁就切過去（Console 跳到被收起的成員時用）。</summary>
    private void ExpandNodeGroupOf(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || !collapsedMembers.TryGetValue(nodeId, out var group)) return;
        bool changed = group.SetCollapsed(false);
        changed |= group.SetActiveTab(group.TabOf(nodeId));
        if (!changed) return;
        MarkViewStateChanged();
        graphDirty = true;
    }

    /// <summary>
    /// 重算「節點 Id → 所屬群組」與「節點 Id → 收起它的群組」。ApplyVisibility 開頭呼叫。
    /// 不在作用中分頁的成員等同局部收合：同樣隱藏、連線同樣走標題列的實線代理。
    /// </summary>
    private void CollectCollapsedMembers()
    {
        collapsedMembers.Clear();
        tabHiddenMembers.Clear();
        nodeGroupMembers.Clear();
        foreach (var group in CurrentNodeGroups())
            foreach (var id in group.Members)
            {
                if (string.IsNullOrEmpty(id)) continue;
                nodeGroupMembers[id] = group;
                if (group.Collapsed || !group.IsOnActiveTab(id)) collapsedMembers[id] = group;
            }
    }

    /// <summary>
    /// 在可達性算完之後才藏：收起的成員不擋住走訪。本來看得到、只因分頁而藏的成員另外記下，
    /// 框要把它們算進去；本來就被 ⊖ 收起的不記。
    /// </summary>
    private void HideCollapsedMembers()
    {
        if (collapsedMembers.Count == 0) return;
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id) || !collapsedMembers.TryGetValue(node.Id, out var group)) continue;
            if (!group.Collapsed && !node.Hidden) tabHiddenMembers.Add(node.Id);
            node.Hidden = true;
        }
    }

    /// <summary>接點所屬節點被哪個收合中的群組收起來；沒有就回 null。</summary>
    private GraphNodeGroup CollapsedGroupOf(HGPort port)
    {
        if (port == null || collapsedMembers.Count == 0) return null;
        var owner = OwnerNodeOfPort(port);
        return owner != null && !string.IsNullOrEmpty(owner.Id) && collapsedMembers.TryGetValue(owner.Id, out var group) ? group : null;
    }

    /// <summary>
    /// 連線一端實際畫在哪：被群組收起來（收合或不在作用中分頁）的一端改到標題列的代理接點，朝向另一端所在的那一側
    /// （另一端在框中心左邊就接左緣）。回傳這一端是不是代理。
    /// </summary>
    private bool ResolveLinkEnd(HGPort port, HGPort other, out Vector2 position, out float direction)
    {
        position = port.Presentation.Position;
        direction = PortDirection(port);
        var group = CollapsedGroupOf(port);
        if (group == null) return false;

        Rect frame = NodeGroupRectOf(group);
        var otherGroup = CollapsedGroupOf(other);
        Vector2 otherAnchor = otherGroup != null ? NodeGroupRectOf(otherGroup).center : other.Presentation.Position;
        bool left = otherAnchor.x < frame.center.x;
        // 一律接標題列：分頁隱藏時框是完整大小，接框的中段會落在其他成員身上。
        position = new Vector2(left ? frame.xMin : frame.xMax, frame.y + NodeGroupHeaderHeight * 0.5f);
        direction = left ? -1f : 1f;
        return true;
    }

    /// <summary>
    /// 代理接點伸出的線依另一端高度排序、等角散開，規則與折疊清單的扇形相同。
    /// 已經屬於折疊清單扇形的線不重複處理；兩端都是代理的線以輸入端那側為準。
    /// 跨出群組的殘影和實線代理同一側就排在同一把扇子裡，標題列那端的殘影才不會疊成一條。
    /// </summary>
    private void RebuildProxyFan()
    {
        proxyFan.Clear();
        boundaryGhostFan.Clear();
        proxyFanGroups.Clear();
        if (graph == null || nodeGroupMembers.Count == 0) return;

        var otherY = new Dictionary<(HGLink link, int end), float>();
        foreach (var link in graph.Links)
        {
            // 殘影在 foldFan 裡只代表欄位端（清單代表接點）的角度，標題列那端仍要在這裡排扇形。
            if (!IsLinkVisible(link))
            {
                if (!IsLinkGhost(link) && !TryHiddenMemberLink(link, out _, out _, out _)) continue;
                for (int end = 0; end < 2; end++)
                {
                    if (!TryNodeGroupBoundaryEnd(link, end, out var boundaryGroup, out var boundaryOther)) continue;
                    Vector2 otherPosition = BoundaryGhostTarget(link, end, boundaryOther, out _);
                    string boundaryKey = boundaryGroup.Id + (otherPosition.x < NodeGroupRectOf(boundaryGroup).center.x ? "L" : "R");
                    AddProxyFanEntry(boundaryKey, (link, end), otherPosition.y, otherY);
                }
                continue;
            }
            if (foldFan.ContainsKey(link)) continue;
            HGPort proxyPort = CollapsedGroupOf(link.InputPort) != null ? link.InputPort
                : CollapsedGroupOf(link.OutputPort) != null ? link.OutputPort : null;
            if (proxyPort == null) continue;
            HGPort other = ReferenceEquals(proxyPort, link.InputPort) ? link.OutputPort : link.InputPort;
            ResolveLinkEnd(proxyPort, other, out _, out float side);
            ResolveLinkEnd(other, proxyPort, out var otherPos, out _);
            string key = CollapsedGroupOf(proxyPort).Id + (side < 0f ? "L" : "R");
            AddProxyFanEntry(key, (link, -1), otherPos.y, otherY);
        }

        foreach (var list in proxyFanGroups.Values)
        {
            list.Sort((a, b) => otherY[a].CompareTo(otherY[b]));
            float step = list.Count > 1 ? Mathf.Min(FoldFanStep, FoldFanMax * 2f / (list.Count - 1)) : 0f;
            for (int i = 0; i < list.Count; i++)
            {
                float angle = (i - (list.Count - 1) * 0.5f) * step;
                if (list[i].end < 0) proxyFan[list[i].link] = angle;
                else boundaryGhostFan[list[i]] = angle;
            }
        }
    }

    private void AddProxyFanEntry(string key, (HGLink link, int end) entry, float y, Dictionary<(HGLink link, int end), float> otherY)
    {
        if (!proxyFanGroups.TryGetValue(key, out var list)) proxyFanGroups[key] = list = new List<(HGLink link, int end)>();
        list.Add(entry);
        otherY[entry] = y;
    }

    /// <summary>
    /// 殘影從標題列往外連時朝向的點：另一端若也被別的群組藏起來，改朝那個群組的標題列，
    /// 兩個群組之間的殘影才會互相對準，不會指向已經看不到的成員接點。
    /// </summary>
    private Vector2 BoundaryGhostTarget(HGLink link, int end, HGPort other, out float direction)
    {
        direction = PortDirection(other);
        if (!TryNodeGroupBoundaryEnd(link, 1 - end, out var otherGroup, out var self)) return other.Presentation.Position;
        NodeGroupHeaderPortFacing(otherGroup, self.Presentation.Position, out var position, out direction);
        return position;
    }

    /// <summary>
    /// 標題列兩端的代表接點：有代理線或虛線的一側畫 ◎；拉線中可以放進群組時兩端都畫 ○（游標靠近的那顆畫 ◎）。
    /// 它不是 HGPort，不能起手。
    /// </summary>
    private void DrawNodeGroupProxies(GraphNodeGroup group, Rect visualFrame, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        float y = visualFrame.y + NodeGroupHeaderHeight * 0.5f;
        bool droppable = CanDropLinkOnNodeGroup();
        var mouse = Event.current.mousePosition;
        for (int side = 0; side < 2; side++)
        {
            var center = new Vector2(side == 0 ? visualFrame.xMin : visualFrame.xMax, y);
            bool used = proxyFanGroups.ContainsKey(group.Id + (side == 0 ? "L" : "R"));
            if (!used && !droppable) continue;
            bool hovered = droppable && (mouse - center).sqrMagnitude <= NodeGroupPortHitRadius * NodeGroupPortHitRadius;
            HGStyles.DrawInputPort(PortRect(center), droppable ? HGStyles.NodeBorderSelected : color, used || hovered);
        }
    }

    /// <summary>目前拉的線能不能放進群組建立節點：規則同「拉到空白處建立空節點」。</summary>
    private bool CanDropLinkOnNodeGroup()
    {
        if (!linking || linkPort == null) return false;
        if (linkPort.IsOutput && linkPort.Source is not HGPropertyWriteSource) return false;
        var slot = (linkPort.Source as HGPropertyWriteSource)?.Slot ?? linkPort.InputSlot;
        return slot != null && slot is not GraphPropertyInputSlot;
    }

    /// <summary>游標下（graph space）是哪個群組標題列的代表接點；取最上層，沒有回 null。</summary>
    private GraphNodeGroup NodeGroupHeaderPortAt(Vector2 graphPoint)
    {
        GraphNodeGroup found = null;
        foreach (var group in CurrentNodeGroups())
        {
            Rect frame = NodeGroupRectOf(group);
            float y = frame.y + NodeGroupHeaderHeight * 0.5f;
            float r2 = NodeGroupPortHitRadius * NodeGroupPortHitRadius;
            if ((graphPoint - new Vector2(frame.xMin, y)).sqrMagnitude <= r2
                || (graphPoint - new Vector2(frame.xMax, y)).sqrMagnitude <= r2) found = group;
        }
        return found;
    }

    /// <summary>
    /// 拉線放進群組時新節點的位置：可見成員（作用中分頁）的左下方，框會跟著長；沒有可見成員時放在標題列下方。
    /// 新節點進作用中的分頁（<see cref="GraphNodeGroup.SetMember"/>）。
    /// </summary>
    private Vector2 NodeGroupSpawnPosition(GraphNodeGroup group)
    {
        Rect frame = group.Collapsed ? group.Rect : NodeGroupHull(group);
        bool anyVisible = false;
        float bottom = frame.y + NodeGroupTopInset(group);
        if (graph != null && !group.Collapsed)
        {
            var members = new HashSet<string>(group.Members);
            foreach (var node in graph.Nodes)
            {
                if (node.Hidden || string.IsNullOrEmpty(node.Id) || !members.Contains(node.Id)) continue;
                bottom = anyVisible ? Mathf.Max(bottom, node.Rect.yMax) : node.Rect.yMax;
                anyVisible = true;
            }
        }
        return new Vector2(frame.x + NodeGroupPadding, bottom + NodeGroupPadding);
    }

    /// <summary>新建的節點加入群組；群組收合中就展開，才看得到剛建的節點。</summary>
    private void JoinNodeGroupAndReveal(string nodeId, GraphNodeGroup group)
    {
        JoinNodeGroup(nodeId, group);
        if (group.SetCollapsed(false)) MarkViewStateChanged();
        graphDirty = true;
    }

    /// <summary>
    /// 連線的一端是被群組外的欄位收起的成員（群組本身沒收合）、另一端看得到：這條線改畫成虛線接到那個群組的標題列。
    /// 兩端都在同一個群組裡的不算；群組收合造成的隱藏走實線代理（<see cref="ResolveLinkEnd"/>）。
    /// </summary>
    private bool TryHiddenMemberLink(HGLink link, out GraphNodeGroup group, out HGPort visibleEnd, out HGPort memberEnd)
    {
        group = null;
        visibleEnd = memberEnd = null;
        if (link?.InputPort == null || link.OutputPort == null || nodeGroupMembers.Count == 0) return false;
        for (int i = 0; i < 2; i++)
        {
            HGPort member = i == 0 ? link.InputPort : link.OutputPort;
            HGPort other = i == 0 ? link.OutputPort : link.InputPort;
            var owner = OwnerNodeOfPort(member);
            if (owner == null || !owner.Hidden || string.IsNullOrEmpty(owner.Id)) continue;
            if (collapsedMembers.ContainsKey(owner.Id) || !nodeGroupMembers.TryGetValue(owner.Id, out var memberGroup)) continue;
            var otherOwner = OwnerNodeOfPort(other);
            if (otherOwner == null || otherOwner.Hidden || !other.Presentation.Visible) continue;
            if (!string.IsNullOrEmpty(otherOwner.Id) && nodeGroupMembers.TryGetValue(otherOwner.Id, out var otherGroup)
                && ReferenceEquals(otherGroup, memberGroup)) continue;
            group = memberGroup;
            visibleEnd = other;
            memberEnd = member;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 成員被群組外的欄位收起、另一端看得到但不是一般殘影（例如成員自己往外接的欄位）：
    /// 看得到的那一端畫一段殘影，朝向成員所在群組的標題列。回傳有沒有畫。
    /// </summary>
    private bool DrawHiddenMemberEnd(HGLink link, bool traced)
    {
        if (!TryHiddenMemberLink(link, out var group, out var visibleEnd, out _)) return false;
        Vector2 visiblePos = visibleEnd.Presentation.Position;
        NodeGroupHeaderPortFacing(group, visiblePos, out var headerPos, out float headerDir);
        DrawLinkGhost(visiblePos, PortDirection(visibleEnd), headerPos, headerDir, GhostColor(link, traced), GhostThickness(traced));
        return true;
    }

    /// <summary>
    /// 殘影線跨出群組時，群組標題列朝向另一端的代表接點畫一段殘影往外連；兩端各自在不同群組就兩邊都畫。
    /// 樣子與欄位收起的殘影相同：實線短線與圓角，進斜線後轉虛線並淡出；同一側多條時依 <see cref="RebuildProxyFan"/> 扇形散開。不參與命中。
    /// </summary>
    private void DrawNodeGroupBoundaryGhosts(HGLink link, bool traced)
    {
        for (int end = 0; end < 2; end++)
        {
            if (!TryNodeGroupBoundaryEnd(link, end, out var group, out var other)) continue;
            Vector2 target = BoundaryGhostTarget(link, end, other, out float targetDir);
            NodeGroupHeaderPortFacing(group, target, out var headerPos, out float headerDir);
            boundaryGhostFan.TryGetValue((link, end), out float angle);
            DrawLinkGhost(headerPos, headerDir, target, targetDir, GhostColor(link, traced), GhostThickness(traced), angle);
        }
    }

    /// <summary>
    /// 殘影的某一端若是被群組藏起來的成員（群組收合、不在作用中分頁或被 ⊖ 收起），改朝那個群組標題列的代表接點；
    /// 其餘照接點本身的位置。回傳有沒有改到標題列。
    /// </summary>
    private bool ResolveGhostEnd(HGLink link, HGPort port, Vector2 towards, out Vector2 position, out float direction)
    {
        position = port.Presentation.Position;
        direction = PortDirection(port);
        int end = ReferenceEquals(port, link.InputPort) ? 0 : 1;
        if (!TryNodeGroupBoundaryEnd(link, end, out var group, out _)) return false;
        NodeGroupHeaderPortFacing(group, towards, out position, out direction);
        return true;
    }

    /// <summary>
    /// 連線的第 end 端（0＝輸入、1＝輸出）屬於某個群組、那顆節點被隱藏，而另一端不在同一個群組：
    /// 由群組標題列代替它連出去。節點還顯示著（例如被另一個沒收起的來源撐著）時，它自己那端的殘影就夠了，不算。
    /// </summary>
    private bool TryNodeGroupBoundaryEnd(HGLink link, int end, out GraphNodeGroup group, out HGPort other)
    {
        group = null;
        other = null;
        if (link?.InputPort == null || link.OutputPort == null || nodeGroupMembers.Count == 0) return false;
        HGPort port = end == 0 ? link.InputPort : link.OutputPort;
        other = end == 0 ? link.OutputPort : link.InputPort;
        var owner = OwnerNodeOfPort(port);
        if (owner == null || !owner.Hidden || string.IsNullOrEmpty(owner.Id) || !nodeGroupMembers.TryGetValue(owner.Id, out group)) return false;
        var otherOwner = OwnerNodeOfPort(other);
        if (otherOwner == null || string.IsNullOrEmpty(otherOwner.Id)
            || !nodeGroupMembers.TryGetValue(otherOwner.Id, out var otherGroup) || !ReferenceEquals(otherGroup, group)) return true;
        group = null;
        return false;
    }

    /// <summary>群組標題列朝向 target 那一側的代表接點位置與朝外方向。</summary>
    private void NodeGroupHeaderPortFacing(GraphNodeGroup group, Vector2 target, out Vector2 position, out float direction)
    {
        Rect frame = NodeGroupRectOf(group);
        bool left = target.x < frame.center.x;
        position = new Vector2(left ? frame.xMin : frame.xMax, frame.y + NodeGroupHeaderHeight * 0.5f);
        direction = left ? -1f : 1f;
    }

    private static Color GhostColor(HGLink link, bool traced)
        => LinkColor(link.OutputOwner.InDisabledSubtree || link.OutputOwner.InLockedSubtree, traced, link.ParentRow.IsProducedValue);

    private static float GhostThickness(bool traced) => traced ? LinkThickness + 2f : LinkThickness;


    // ===== 整組移位 =====

    /// <summary>按在群組標題列＝整組移動，包含被收起而隱藏的成員。</summary>
    private bool TryBeginNodeGroupDrag(Vector2 graphMouse)
    {
        if (graph == null) return false;
        var group = NodeGroupHeaderAt(graphMouse);
        if (group == null) return false;

        nodeGroupDragOrigin = NodeGroupHull(group);
        nodeGroupDragRect = nodeGroupDragOrigin;
        nodeGroupDragStart = graphMouse;
        dragNodeGroup = group;
        SelectNodeGroup(group);
        var members = new HashSet<string>(group.Members);
        nodeGroupMemberStarts.Clear();
        foreach (var node in graph.Nodes)
            if (!string.IsNullOrEmpty(node.Id) && members.Contains(node.Id)) nodeGroupMemberStarts[node.Id] = node.Pos;
        GUI.FocusControl(null);
        return true;
    }

    private void DragNodeGroup(Vector2 graphMouse)
    {
        // 位移取整格：成員原本就吸附在格線上，搬完仍在格線上。
        Vector2 offset = SnapToGrid(graphMouse - nodeGroupDragStart);
        nodeGroupDragRect = new Rect(nodeGroupDragOrigin.position + offset, nodeGroupDragOrigin.size);
        foreach (var node in graph.Nodes)
            if (nodeGroupMemberStarts.TryGetValue(node.Id, out var origin)) node.Pos = origin + offset;
    }

    /// <summary>放開才寫回：框與成員座標在同一步 Undo 裡。沒動過就不寫，避免點一下標題就要求存檔。</summary>
    private void EndNodeGroupDrag()
    {
        var group = dragNodeGroup;
        dragNodeGroup = null;
        if (group == null || nodeGroupDragRect == nodeGroupDragOrigin)
        {
            nodeGroupMemberStarts.Clear();
            return;
        }

        BreakUndoMerge();
        if (graph != null)
            foreach (var node in graph.Nodes)
                if (nodeGroupMemberStarts.ContainsKey(node.Id)) model.SetPosition(node.Id, node.Pos);
        group.SetRect(group.Collapsed ? new Rect(nodeGroupDragRect.position, group.Rect.size) : nodeGroupDragRect);
        nodeGroupMemberStarts.Clear();
        MarkViewStateChanged();
        Repaint();
    }

    /// <summary>上一次拖曳沒收到 MouseUp（在畫布外放開）時丟掉暫存並重建，節點回到文件上的座標。</summary>
    private void CancelNodeGroupDrag()
    {
        frozenNodeGroupRects.Clear();
        if (dragNodeGroup == null) return;
        dragNodeGroup = null;
        nodeGroupMemberStarts.Clear();
        graphDirty = true;
    }
}
}
