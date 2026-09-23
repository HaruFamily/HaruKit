namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>畫一次 ProtoProperty 庫需要的全部資料。面板不認識 model 或 focus，只認這份快照。</summary>
public struct HGPropertyLibraryView
{
    /// <summary>這張圖有初始內容的 Property 定義，依清單順序。</summary>
    public List<HGProperty> Properties;
}

/// <summary>
/// ProtoProperty 庫對外的命令。面板只回報使用者做了什麼，怎麼改圖由視窗決定。
/// </summary>
public struct HGPropertyLibraryCommands
{
    /// <summary>改名。回傳 false＝名稱不合法，就地改名會維持編輯狀態。</summary>
    public Func<GraphProperty, string, bool> Rename;

    /// <summary>拖到「－」上放開＝移除被拖的那一個。</summary>
    public Action<GraphProperty> Remove;

    /// <summary>按「＋」：開型別選單。</summary>
    public Action Create;

    /// <summary>這顆 Property 有沒有驗證問題；reason 為 null＝沒有。</summary>
    public Func<HGProperty, (string reason, bool isError)> IssueOf;

    /// <summary>移動至另一個可見項目的原位置。</summary>
    public Action<GraphProperty, GraphProperty> Move;

    /// <summary>改初始內容（純值型別）。</summary>
    public Action<GraphProperty, object> SetInitialValue;

    /// <summary>把 Project 資產加進清單型的初始內容。重複與型別不符由實作跳過。</summary>
    public Action<GraphProperty, IReadOnlyList<UnityEngine.Object>> AddInitialItems;

    /// <summary>移除清單型初始內容的第 index 項。</summary>
    public Action<GraphProperty, int> RemoveInitialItem;

    /// <summary>在清單型初始內容裡搬動一項，toIndex 是搬完之後的索引。</summary>
    public Action<GraphProperty, int, int> MoveInitialItem;

    /// <summary>可逐項編輯的清單：在尾端加一個元素型別的預設值（資產為空引用）。</summary>
    public Action<GraphProperty> AddInitialValue;

    /// <summary>可逐項編輯的清單：就地改寫第 index 項。</summary>
    public Action<GraphProperty, int, object> SetInitialItem;
}

/// <summary>
/// 左欄 ProtoProperty 庫：這張圖有哪些帶初始內容的具名變數，以及每一顆裝了什麼。
/// </summary>
// 版面與互動照目錄庫做：**展開就在原地攤開內容**，不另開一塊底部編輯區。
// 理由是內容與它的擁有者要看起來是同一塊，而且落點就是那一列本身——
// 「選取一列、內容出現在別的地方」多一層狀態，也多一個「怎麼沒反應」的失敗模式。
//
// 與目錄庫刻意不同的兩點，都是因為 Property 沒有自己的畫布：
// 一、沒有進入焦點這回事，列上只有展開鈕與改名。
// 二、移除只有「拖到『－』上放開」一條路：沒有「目前編輯中的那一個」可以當表態。
public sealed class HGPropertyLibraryPanel
{
    private const float RowHeight = 24f;
    private const float ItemHeight = 20f;
    private const float Corner = 3f;
    private const float BlockCorner = 4f;
    private const float ItemIndent = 18f;
    private const float SpineX = 11f;
    private const float HandleWidth = HGLibraryReorder.HandleWidth;

    private string search = "";
    private Vector2 scroll;

    /// <summary>目前攤開的各顆 Property（存 Id，改名與重排都不影響）。</summary>
    private readonly HashSet<string> expanded = new();

    // 外層清單與展開後的項目各一份：兩邊的索引空間不同，共用一份會互相覆寫。
    private readonly HGLibraryReorder rowOrder = new();
    private readonly HGLibraryReorder itemOrder = new();

    /// <summary>正在重排項目的是哪一顆 ProtoProperty。展開的可能不只一顆，要記得是誰的項目。</summary>
    private string itemOrderOwnerId;

    /// <summary>這一輪從 Project 來的拖曳還在進行中。</summary>
    // 必須自己記旗標，不能每幀問 DragAndDrop.objectReferences：那份清單只在拖曳事件裡有意義，
    // Repaint 時問到的是上一次拖曳留下的殘值，落點高亮會閃爍、拖完還一直亮著。
    private bool projectDrag;

    /// <summary>換編輯對象時把面板自己的視圖狀態歸零。</summary>
    public void Reset()
    {
        search = "";
        scroll = Vector2.zero;
        expanded.Clear();
        projectDrag = false;
        ClearOrderDrag();
    }

    private void ClearOrderDrag()
    {
        rowOrder.Clear();
        itemOrder.Clear();
        itemOrderOwnerId = null;
    }

    // cmd 不用 in：底下的改名要在 lambda 裡叫它，而 in／ref 參數不能被 lambda 捕捉（CS1628）。
    public void Draw(Rect r, float top, in HGPropertyLibraryView view, HGPropertyLibraryCommands cmd,
        HGInlineRename inlineName, HGLibraryDrag drag)
    {
        TrackProjectDrag();
        rowOrder.BeginFrame();
        itemOrder.BeginFrame();

        DrawCreateButton(new Rect(r.x + 4f, top, r.width - 8f, 20f), cmd, drag);
        DrawRemoveButton(new Rect(r.x + 4f, top + 22f, r.width - 8f, 20f), cmd, drag);

        var searchRect = new Rect(r.x + 4f, top + 46f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋 ProtoProperty"));
        search = EditorGUI.TextField(
            new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), search);

        float listTop = top + 70f;
        var listRect = new Rect(r.x + 2f, listTop, r.width - 4f, Mathf.Max(0f, r.yMax - listTop - 2f));

        var shown = new List<HGProperty>();
        foreach (var p in view.Properties)
            if (string.IsNullOrWhiteSpace(search)
                || p.Key?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || p.TypeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                shown.Add(p);

        float contentHeight = 4f;
        foreach (var p in shown) contentHeight += BlockHeight(p.Property) + 2f;

        var content = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
        scroll = GUI.BeginScrollView(listRect, scroll, content);
        Action pending = null;

        float y = 2f;
        for (int i = 0; i < shown.Count; i++)
        {
            GraphProperty property = shown[i].Property;
            bool open = IsExpanded(property);
            float height = BlockHeight(property);

            // 展開時先鋪一塊容器再畫內容：標題列與它的項目要看起來是同一塊。
            if (open)
            {
                var block = new Rect(2f, y, content.width - 4f, height);
                HGStyles.RoundedFill(block, HGStyles.GroupTint(HGStyles.HeaderProperty), BlockCorner);
                HGStyles.RoundedFrame(block, HGStyles.HeaderProperty, BlockCorner);
            }

            float inset = open ? 2f : 0f;
            var row = new Rect(2f + inset, y + inset, content.width - 4f - inset * 2f, RowHeight - 3f);
            if (cmd.Move != null)
            {
                var handle = new Rect(row.x, row.y, HandleWidth, row.height);
                if (rowOrder.Row(handle, property.Id, i, y + height * 0.5f)) itemOrder.Clear();
                row.xMin += HandleWidth;
            }

            // 插入線畫在目標列的上緣，往下搬時改畫下緣——和畫布清單的重排提示同一套。
            if (rowOrder.IsTarget(i, out bool below))
                HGLibraryReorder.InsertLine(new Rect(2f, y, content.width - 4f, height), below);

            Action rowCommand = DrawPropertyRow(row, shown[i], open, i % 2 == 1, inlineName, drag, cmd);
            if (rowCommand != null) pending = rowCommand;

            if (!open) { y += height + 2f; continue; }

            float itemTop = y + inset + RowHeight;
            Action blockCommand = DrawExpandedContent(content.width, itemTop, property, cmd);
            if (blockCommand != null) pending = blockCommand;

            y += height + 2f;
        }

        // 兩份都要在 ScrollView 內結算：滑鼠位置與記下來的中線要在同一個座標系。
        bool moveRow = rowOrder.EndFrame(out int rowFrom, out int rowTo);
        bool moveItem = itemOrder.EndFrame(out int itemFrom, out int itemTo);
        string itemOwner = itemOrderOwnerId;
        if (!itemOrder.Active) itemOrderOwnerId = null;

        GUI.EndScrollView();

        if (moveRow && rowFrom < shown.Count && rowTo < shown.Count)
        {
            GraphProperty source = shown[rowFrom].Property;
            GraphProperty target = shown[rowTo].Property;
            pending = () => cmd.Move?.Invoke(source, target);
        }
        else if (moveItem && itemOwner != null)
        {
            GraphProperty owner = FindById(shown, itemOwner);
            if (owner != null) pending = () => cmd.MoveInitialItem?.Invoke(owner, itemFrom, itemTo);
        }

        // 結束繪製後才改資料，這一輪的索引與展開高度保持一致。
        pending?.Invoke();
    }

    private static GraphProperty FindById(List<HGProperty> shown, string id)
    {
        foreach (var item in shown)
            if (item.Property?.Id == id) return item.Property;
        return null;
    }

    private static void DrawInsertLine(Rect block, bool below)
    {
        float y = below ? block.yMax : block.y;
        HGStyles.Fill(new Rect(block.x + 2f, y - 1f, block.width - 4f, 2f), HGStyles.Link);
    }

    /// <summary>更新「現在有沒有一輪 Project 拖曳在進行」。每次 Draw 最前面跑一次。</summary>
    // **清除只認 DragExited**，那是拖曳結束（放開、取消、離開視窗）之後 Unity 一定會補的事件。
    // 不在 DragPerform 清：落點的判斷跑在本函式後面，同一幀就清掉的話放開時什麼都不會發生。
    private void TrackProjectDrag()
    {
        switch (Event.current.type)
        {
            case EventType.DragUpdated: projectDrag = HasProjectAsset(); break;
            case EventType.DragExited: projectDrag = false; break;
        }
    }

    private static bool HasProjectAsset()
    {
        foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            if (dragged != null && AssetDatabase.Contains(dragged)) return true;
        return false;
    }

    /// <summary>這一塊（標題列＋展開內容）要多高。</summary>
    private float BlockHeight(GraphProperty property)
    {
        if (!IsExpanded(property)) return RowHeight;

        // 純值只攤開一列輸入框；清單攤開全部項目，空的時候仍留一列放提示。
        if (!IsItemList(property, out _)) return RowHeight + ItemHeight + 4f;

        int count = (property.Slot?.DefaultObject as IList)?.Count ?? 0;
        // 可逐項編輯的清單尾端多一列「＋ 新增」；元素畫不出輸入框的清單，空的時候留一列放提示。
        if (IsEditableList(property)) return RowHeight + (count + 1) * ItemHeight + 4f;
        return RowHeight + Mathf.Max(1, count) * ItemHeight + 4f;
    }

    /// <summary>元素畫得出輸入框的清單（純值與資產皆是）：逐項編輯，資產清單另外保留從 Project 拖入。</summary>
    private static bool IsEditableList(GraphProperty property)
        => IsItemList(property, out Type elementType) && HGValueField.CanDraw(elementType);

    private bool IsExpanded(GraphProperty property)
        => property != null && !string.IsNullOrEmpty(property.Id) && expanded.Contains(property.Id);

    /// <summary>這顆 Property 的值是不是清單，是的話給出元素型別。</summary>
    // 問結果型別而不是目前存了什麼：空清單與 null 都要答得出「這裡收哪一種東西」，
    // 否則第一筆拖進來之前無法判定相容性。
    private static bool IsItemList(GraphProperty property, out Type elementType)
    {
        elementType = null;
        Type resultType = property?.ResultType;
        return resultType != null && HGReflect.IsList(resultType, out elementType);
    }

    /// <summary>標題列：展開鈕、名稱、型別 chip、問題點。整列同時是 Project 落點。</summary>
    private Action DrawPropertyRow(Rect row, HGProperty item, bool open, bool altRow,
        HGInlineRename inlineName, HGLibraryDrag drag, HGPropertyLibraryCommands cmd)
    {
        GraphProperty property = item.Property;
        var e = Event.current;
        bool isList = IsItemList(property, out Type elementType);
        bool hoverDrop = projectDrag && isList && row.Contains(e.mousePosition);

        HGStyles.CellBackground(row, HGStyles.HeaderProperty, HGStyles.HeaderProperty, altRow, hoverDrop, Corner);

        var foldRect = new Rect(row.x + 4f, row.y + 3f, 14f, 14f);
        // 14px 的小鈕要用無內距、置中的樣式；Chip 的左右內距會把符號裁掉一半。
        if (GUI.Button(foldRect, open ? "▾" : "▸", HGStyles.ListAdd)) SetExpanded(property.Id, !open);

        var typeRect = new Rect(row.xMax - 58f, row.y + 4f, 42f, 15f);
        HGStyles.RoundedFill(typeRect, HGStyles.HeaderFormula, Corner);
        GUI.Label(typeRect, HGStyles.Elide(item.TypeName, HGStyles.NodeChip, typeRect.width), HGStyles.NodeChip);

        var nameRect = new Rect(foldRect.xMax + 4f, row.y + 2f,
            Mathf.Max(0f, typeRect.x - foldRect.xMax - 8f), 18f);
        bool renaming = inlineName.Draw(nameRect, property, HGInlineRename.SitePropertyLib,
            string.IsNullOrEmpty(item.Key) ? "（未命名）" : item.Key, item.Key ?? "",
            HGStyles.RowLabel, "雙擊可改名；圖內的讀寫節點認的是物件本身，改名不斷線",
            name => cmd.Rename(property, name));

        var (reason, isError) = cmd.IssueOf(item);
        if (reason != null)
        {
            var dot = new Rect(row.xMax - 10f, row.y + 8f, 7f, 7f);
            HGStyles.Fill(dot, isError ? HGStyles.Error : HGStyles.Warning);
            GUI.Label(dot, new GUIContent("", reason));
        }

        if (hoverDrop) return HandleRowDrop(property, elementType, cmd);

        if (renaming) return null;             // 正在改名的這一格不吃點擊

        // 拖到「－ 移除」上要能從這一列起拖。名字那一格不起拖，雙擊改名才不會被當成拖曳。
        if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition)
            && !nameRect.Contains(e.mousePosition) && !foldRect.Contains(e.mousePosition))
        {
            drag.BeginProperty(property);
            e.Use();
        }
        if (e.type == EventType.MouseDrag && drag.IsSource(property)) drag.PromoteOnDrag();
        return null;
    }

    /// <summary>整列落點：只收這一顆收得下的資產，收不下就明確拒絕而不是靜默不動。</summary>
    private Action HandleRowDrop(GraphProperty property, Type elementType, HGPropertyLibraryCommands cmd)
    {
        var e = Event.current;
        List<UnityEngine.Object> incoming = AcceptableDrag(elementType);
        DragAndDrop.visualMode = incoming.Count > 0
            ? DragAndDropVisualMode.Copy
            : DragAndDropVisualMode.Rejected;

        if (e.type == EventType.DragUpdated) { e.Use(); return null; }
        if (e.type != EventType.DragPerform) return null;

        DragAndDrop.AcceptDrag();
        e.Use();
        // 放開就攤開它：加完看不到加了什麼，跟沒反應一樣難判斷。
        SetExpanded(property.Id);
        return incoming.Count > 0 ? () => cmd.AddInitialItems?.Invoke(property, incoming) : null;
    }

    /// <summary>展開的內容：純值一列輸入框，清單是項目列＋左側縱線。</summary>
    private Action DrawExpandedContent(float contentWidth, float itemTop, GraphProperty property,
        HGPropertyLibraryCommands cmd)
    {
        FormulaSlotBase slot = property?.Slot;
        if (slot == null) return null;

        float width = contentWidth - ItemIndent - 6f;
        if (!IsItemList(property, out Type elementType))
        {
            var fieldRect = new Rect(ItemIndent, itemTop, width, ItemHeight - 2f);
            Type editType = HGReflect.DefaultEditType(slot, slot.ResultType);
            if (!HGValueField.CanDraw(editType))
            {
                GUI.Label(fieldRect, HGStyles.Elide($"{HGReflect.ResultTypeName(editType)} 尚無編輯介面",
                    HGStyles.Tiny, width, "這個型別的初始內容還不能在這裡編輯"), HGStyles.Tiny);
                return null;
            }

            object before = slot.DefaultObject;
            object after = HGValueField.Draw(fieldRect, editType, before, HGReflect.HasEnumButtons(slot.GetType()));
            if (Equals(before, after)) return null;
            return () => cmd.SetInitialValue?.Invoke(property, after);
        }

        var items = slot.DefaultObject as IList;
        int count = items?.Count ?? 0;
        bool editable = IsEditableList(property);
        if (count == 0 && !editable)
        {
            GUI.Label(new Rect(ItemIndent, itemTop, width, ItemHeight - 2f),
                $"還是空的；把 {HGReflect.ResultTypeName(elementType)} 從 Project 拖到這一列上", HGStyles.Tiny);
            return null;
        }

        // 左側縱線把項目串回標題列：窄欄裡光靠縮排看不出層級。
        if (count > 0) HGStyles.Fill(new Rect(SpineX, itemTop - 1f, 1f, count * ItemHeight - 4f), HGStyles.HeaderProperty);
        bool enumButtons = HGReflect.HasEnumButtons(slot.GetType());

        bool mine = itemOrderOwnerId == property.Id;
        // 尚未起拖時每塊各自收集；起拖後只有擁有者能提供目標位置，避免混入其他清單的索引。
        if (!itemOrder.Active) itemOrder.BeginFrame();
        Action pending = null;
        for (int k = 0; k < count; k++)
        {
            var item = new Rect(ItemIndent, itemTop + k * ItemHeight, width, ItemHeight - 2f);

            if (mine && itemOrder.IsTarget(k, out bool below)) HGLibraryReorder.InsertLine(item, below);

            if (cmd.MoveInitialItem != null)
            {
                var handle = new Rect(item.x, item.y, HandleWidth, item.height);
                // 索引空間是「這一顆的項目」，所以 id 要帶上擁有者，展開兩顆時才不會互相認錯。
                if (itemOrder.Active && itemOrderOwnerId != property.Id)
                    GUI.Label(handle, new GUIContent("≡", "拖曳可調整順序"), HGStyles.Tiny);
                else if (itemOrder.Row(handle, property.Id + "/" + k, k, item.y + item.height * 0.5f))
                {
                    itemOrderOwnerId = property.Id;
                    rowOrder.Clear();
                }
                item.xMin += HandleWidth;
            }

            var deleteRect = new Rect(item.xMax - 16f, item.y + 1f, 14f, 16f);
            var nameRect = new Rect(item.x + 2f, item.y + 1f, Mathf.Max(0f, deleteRect.x - item.x - 4f), 16f);
            if (editable)
            {
                object before = items[k];
                object after = HGValueField.Draw(nameRect, elementType, before, enumButtons);
                if (!Equals(before, after))
                {
                    int index = k;
                    pending = () => cmd.SetInitialItem?.Invoke(property, index, after);
                }
            }
            else
            {
                string name = items[k] is UnityEngine.Object asset && asset != null ? asset.name : "（空）";
                GUI.Label(nameRect, HGStyles.Elide(name, HGStyles.RowLabel, nameRect.width), HGStyles.RowLabel);
            }

            if (GUI.Button(deleteRect, new GUIContent("✕", "從初始內容移除這一項"), HGStyles.ListAdd))
            {
                int index = k;
                pending = () => cmd.RemoveInitialItem(property, index);
            }
        }

        if (editable && cmd.AddInitialValue != null)
        {
            var addRect = new Rect(ItemIndent, itemTop + count * ItemHeight, width, ItemHeight - 2f);
            string tip = typeof(UnityEngine.Object).IsAssignableFrom(elementType)
                ? "在清單尾端加一格空引用；也可以把 Project 資產拖到標題列一次加入多個"
                : "在清單尾端加一項";
            if (GUI.Button(addRect, new GUIContent("＋ 新增", tip), HGStyles.ListAdd))
                pending = () => cmd.AddInitialValue(property);
        }

        return pending;
    }

    /// <summary>這一次拖曳裡收得下的資產。非 Project 資產與型別不符的一律不收。</summary>
    private static List<UnityEngine.Object> AcceptableDrag(Type elementType)
    {
        var result = new List<UnityEngine.Object>();
        if (elementType == null) return result;

        foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
        {
            if (dragged == null || !elementType.IsInstanceOfType(dragged)) continue;
            if (!AssetDatabase.Contains(dragged)) continue;   // 場景物件不是 Project 資產，存不進圖定義
            result.Add(dragged);
        }
        return result;
    }

    private void SetExpanded(string id, bool open = true)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (open) expanded.Add(id);
        else expanded.Remove(id);
    }

    /// <summary>「＋ 新增 ProtoProperty」：單擊開型別選單。刻意不接受拖放——沒有複製入口。</summary>
    private static void DrawCreateButton(Rect rect, HGPropertyLibraryCommands cmd, HGLibraryDrag drag)
    {
        bool dropping = drag.DroppingProperty;
        bool wasEnabled = GUI.enabled;
        // 拖著格子時停用它：讓「這裡不是落點」在手上就看得出來，不必先放開才發現沒事發生。
        GUI.enabled = wasEnabled && !dropping;
        bool clicked = GUI.Button(rect, new GUIContent("＋ 新增 ProtoProperty",
            "新增一顆有初始內容的具名變數；先選型別，再填初始值"));
        GUI.enabled = wasEnabled;

        if (clicked && !dropping) cmd.Create();
    }

    /// <summary>
    /// 「－ 移除 ProtoProperty」：把格子**拖到這顆按鈕上放開**刪掉被拖的那一個。
    /// 沒有「單擊移除選取項」那條路——這個庫沒有焦點可以當表態。
    /// </summary>
    private static void DrawRemoveButton(Rect rect, HGPropertyLibraryCommands cmd, HGLibraryDrag drag)
    {
        var e = Event.current;
        bool dropping = drag.DroppingProperty;
        bool hover = rect.Contains(e.mousePosition);

        if (dropping && hover) HGStyles.Fill(rect, HGStyles.DropRemove);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && dropping;
        GUI.Button(rect, new GUIContent(
            dropping ? "移除 ProtoProperty" : "－ 移除 ProtoProperty",
            "把左邊的格子拖到這裡移除；圖上指著它的讀寫節點會一起清空"));
        GUI.enabled = wasEnabled;

        // 拖曳放開不會讓 GUI.Button 回 true（它沒在自己身上收到 MouseDown），所以自己判。
        if (dropping && hover && e.rawType == EventType.MouseUp)
        {
            cmd.Remove(drag.Property);
            drag.Clear();
            e.Use();
        }
    }
}

}
