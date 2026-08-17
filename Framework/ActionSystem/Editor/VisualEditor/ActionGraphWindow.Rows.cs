namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 節點內的參數列與清單列繪製與互動。
/// </summary>
public partial class ActionGraphWindow
{
    private void DrawRows(AGNode node, List<AGRow> rows, Rect nodeRect)
    {
        foreach (var row in rows)
        {
            if (row.Hidden) continue;
            var rowRect = new Rect(nodeRect.x, nodeRect.y + row.LocalY, nodeRect.width, row.Height);
            if (rowRect.yMax > nodeRect.yMax) continue;
            // 底要畫在所有內容之下，而且元素展開出來的子列也算同一段，所以在這裡統一畫，不放進元素控制項。
            if (row.ListOwner != null) DrawListRowBackground(row, rowRect);

            switch (row.Kind)
            {
                case AGRowKind.Slot:
                    if (row.IsListElement) DrawListElementControls(row, rowRect, nodeRect);
                    DrawSlotRow(row, rowRect);
                    break;
                case AGRowKind.Value:
                    if (row.IsListElement) DrawListElementControls(row, rowRect, nodeRect);
                    DrawValueRow(row, rowRect);
                    break;
                case AGRowKind.Group:
                    if (row.IsListElement) DrawListElementControls(row, rowRect, nodeRect);
                    if (!row.HideLabel)
                    {
                        var groupRect = Indent(rowRect, row);
                        GUI.Label(groupRect,
                            AGStyles.Elide(row.Label, AGStyles.RowLabel, groupRect.width, AGReflect.FieldDescription(row.Field)),
                            AGStyles.RowLabel);
                    }
                    DrawRows(node, row.Children, nodeRect);
                    break;
                case AGRowKind.List:
                    DrawList(node, row, rowRect, nodeRect);
                    break;
            }
        }
    }

    /// <summary>
    /// 清單畫成「一段」而不是一堆同高的列：底帶包住整段、左側一條縱線串起元素、尾端是整列寬的新增列。
    /// 折疊時只留標題列，子列由 MeasureRows 標成 Hidden。
    /// </summary>
    private void DrawList(AGNode node, AGRow row, Rect rowRect, Rect nodeRect)
    {
        int count = row.List?.Count ?? 0;
        bool fixedSize = row.List != null && row.List.IsFixedSize;

        // 底帶要先畫（在所有內容之下），所以這裡就得知道整段的下緣。
        float bandBottom = row.Collapsed
            ? rowRect.yMax
            : Mathf.Min(nodeRect.yMax, nodeRect.y + row.AddRowY + AGGraph.RowHeight);
        var band = ListBandRect(row, rowRect);
        band.height = bandBottom - rowRect.y;
        if (band.height > 0f) AGStyles.RoundedFill(band, AGStyles.ListBand, 3f);

        DrawListHeader(row, rowRect, count);
        if (row.Collapsed) return;

        // 元素的底由 DrawListRowBackground 逐列畫（含展開出來的子列），疊在這層底帶之上。
        DrawRows(node, row.Children, nodeRect);
        if (ReferenceEquals(dragListRow, row)) DrawListInsertLine(row, nodeRect, band);

        var addRect = new Rect(nodeRect.x, nodeRect.y + row.AddRowY, nodeRect.width, AGGraph.RowHeight);
        if (addRect.yMax > nodeRect.yMax) return;

        // 整列寬的按鈕：60px 的小鈕在 0.45 倍縮放下只剩 27px，按不到也讀不到。
        var addBtn = new Rect(band.x + 4f, addRect.y + 2f, band.width - 8f, AGGraph.RowHeight - 4f);
        if (fixedSize)
        {
            GUI.Label(addBtn, new GUIContent("陣列長度固定", "陣列長度在程式或 Inspector 決定，這裡不能增刪；需要增刪請把欄位改成 List<T>"),
                AGStyles.ListAdd);
            return;
        }
        AGStyles.RoundedFrame(addBtn, AGStyles.ListRule, 3f);
        if (GUI.Button(addBtn, new GUIContent(count == 0 ? "＋ 新增第一項" : "＋ 新增", "在清單尾端加一項"), AGStyles.ListAdd))
            AddListItem(row);
    }

    /// <summary>拖曳重排的插入位置：一條線就夠，不需要動到資料。</summary>
    private void DrawListInsertLine(AGRow row, Rect nodeRect, Rect band)
    {
        if (dragListTarget < 0 || dragListTarget >= row.Children.Count) return;
        var child = row.Children[dragListTarget];
        float y = nodeRect.y + child.LocalY;
        if (dragListTarget > dragListIndex) y += child.Height;      // 往下搬時線畫在目標列的下緣
        AGStyles.Fill(new Rect(band.x + 2f, y - 1f, band.width - 4f, 2f), AGStyles.Link);
    }

    /// <summary>
    /// 列的左緣＝縮排 + LeftPad（清單元素的序號欄）。子列繼承父的 LeftPad，父子左緣才對得齊。
    /// spansRow＝這個 Rect 是整列寬，右側要讓開刪除鈕；只是列內的標籤欄時傳 false。
    /// </summary>
    private static Rect Indent(Rect r, AGRow row, bool spansRow = true)
    {
        float left = 4f + row.LeftPad + row.Depth * AGGraph.IndentWidth;
        float right = 4f + (spansRow && row.IsListElement ? AGGraph.ListDeleteWidth : 0f);
        return new Rect(r.x + left, r.y + 1f, Mathf.Max(8f, r.width - left - right), r.height - 2f);
    }

    /// <summary>
    /// 列右端由右往左的固定順序：**接點 → 收合鈕 → ✕**。接點永遠貼齊節點右緣（所有接點要排成
    /// 一條垂直線），另外兩個往左推。位置固定不隨「有沒有接來源」滑動，欄位寬度才不會跳。
    /// 這裡回傳 ✕ 佔掉的橫向空間，清單元素才有。
    /// </summary>
    private static float ListRightInset(AGRow row)
        => row.IsListElement ? AGGraph.ListDeleteWidth : 0f;

    /// <summary>接點固定佔住的右緣寬度。收合鈕與 ✕ 都從這裡往左推。</summary>
    private const float PortReserve = AGGraph.PortDiameter;

    /// <summary>收合鈕：接點左邊。</summary>
    private static Rect ViewToggleRectOf(Rect rowRect)
        => new Rect(rowRect.xMax - PortReserve - ViewToggleWidth,
            rowRect.y + rowRect.height * 0.5f - AGGraph.PortRadius + 1f, 12f, 12f);

    /// <summary>清單元素的刪除鈕：排在收合鈕左邊，不搶右緣那條接點垂直線。</summary>
    private static Rect DeleteRectOf(Rect rowRect)
        => new Rect(rowRect.xMax - PortReserve - ViewToggleWidth - AGGraph.ListDeleteWidth,
            rowRect.y + 3f, 14f, rowRect.height - 6f);

    /// <summary>
    /// 清單底帶的左右邊界（高度由呼叫端填）。標題列與元素列都用它，斑馬紋才會和底帶切齊。
    /// listRow 傳清單標題列；元素列傳自己的 ListOwner。
    /// </summary>
    private static Rect ListBandRect(AGRow listRow, Rect rowRect)
    {
        float left = rowRect.x + 2f + listRow.LeftPad + listRow.Depth * AGGraph.IndentWidth;
        return new Rect(left, rowRect.y, Mathf.Max(8f, rowRect.xMax - left - 2f), rowRect.height);
    }

    /// <summary>
    /// 清單一列的底：斑馬紋做成雙向（一亮一暗），單向疊一層淡白在 Slot 元素上看不出來——
    /// 右半被 AGValueField 的欄位框蓋住，只剩左半在比對。拖曳／hover 再疊一層。
    /// 元素展開出來的子列也走這裡，整段才是同一條紋。
    /// </summary>
    private void DrawListRowBackground(AGRow row, Rect rowRect)
    {
        var owner = row.ListOwner;
        var band = ListBandRect(owner, rowRect);
        AGStyles.Fill(band, row.ListIndex % 2 == 0 ? AGStyles.ListStripeEven : AGStyles.ListStripeOdd);

        if (ReferenceEquals(dragListRow, owner) && dragListIndex == row.ListIndex)
            AGStyles.Fill(band, AGStyles.ListRowDragging);
        else if (rowRect.Contains(Event.current.mousePosition))
            AGStyles.Fill(band, AGStyles.ListRowHover);
    }

    /// <summary>
    /// 清單元素左側的序號 + 拖曳把手、右側的刪除鈕。
    /// 把手與 ✕ 都常態顯示，但刻意放在列的兩端：舊版把 ✕ 貼在把手右邊 1px，想拖曳結果刪掉。
    /// </summary>
    private void DrawListElementControls(AGRow row, Rect rowRect, Rect nodeRect)
    {
        var owner = row.ListOwner;
        if (owner?.List == null) return;

        var e = Event.current;
        bool hover = rowRect.Contains(e.mousePosition);
        bool dragging = ReferenceEquals(dragListRow, owner) && dragListIndex == row.ListIndex;
        bool fixedSize = owner.List.IsFixedSize;

        // 序號與把手各佔控制欄一半：序號是順序資訊，把手是操作入口，兩件事不該互相取代。
        // 把手排在最前面——它是這一列的抓取點，放在最外緣最好瞄準。
        float x = rowRect.x + 4f + row.Depth * AGGraph.IndentWidth + row.LeftPad - AGGraph.ListGutter;
        var handle = new Rect(x, rowRect.y, 13f, rowRect.height);
        var index = new Rect(x + 13f, rowRect.y, 15f, rowRect.height);
        GUI.Label(index, new GUIContent((row.ListIndex + 1) + ".", "序號即執行順序"), AGStyles.ListIndex);
        GUI.Label(handle,
            new GUIContent("≡", fixedSize ? "陣列長度固定，不能重排" : "拖曳可調整順序；右鍵有插入與刪除"),
            dragging ? AGStyles.RowLabel : AGStyles.Tiny);

        var remove = DeleteRectOf(rowRect);
        GUI.enabled = !fixedSize;
        bool clickedRemove = GUI.Button(remove,
            new GUIContent("✕", fixedSize ? "陣列不能刪除項目" : "刪除這一項（可用 Ctrl+Z 復原）"),
            AGStyles.ListAdd);
        GUI.enabled = true;
        if (clickedRemove && !fixedSize)
        {
            model.BreakUndoMerge();
            owner.List.RemoveAt(row.ListIndex);
            Invalidate();
            return;
        }
        if (fixedSize) return;

        if (e.type == EventType.MouseDown && e.button == 1 && hover)
        {
            ShowListElementMenu(owner, row.ListIndex);
            e.Use();
        }
        else if (e.type == EventType.MouseDown && e.button == 0 &&
                 (handle.Contains(e.mousePosition) || index.Contains(e.mousePosition)))
        {
            dragListRow = owner;
            dragListIndex = row.ListIndex;
            dragListTarget = row.ListIndex;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging)
        {
            dragListTarget = ListIndexAt(owner, nodeRect, e.mousePosition.y);
            e.Use();
        }
    }

    /// <summary>依滑鼠 Y 算出要插到清單的第幾格。</summary>
    private static int ListIndexAt(AGRow owner, Rect nodeRect, float mouseY)
    {
        if (owner.Children.Count == 0) return -1;
        for (int i = 0; i < owner.Children.Count; i++)
        {
            var child = owner.Children[i];
            float mid = nodeRect.y + child.LocalY + child.Height * 0.5f;
            if (mouseY < mid) return i;
        }
        return owner.Children.Count - 1;
    }

    private void ShowListElementMenu(AGRow owner, int index)
    {
        var menu = new GenericMenu();
        if (owner.List != null && owner.List.IsFixedSize)
        {
            menu.AddDisabledItem(new GUIContent("陣列長度固定，無法增刪或重排"));
            menu.ShowAsContext();
            return;
        }

        menu.AddItem(new GUIContent("在此插入一項"), false, () =>
        {
            var item = owner.ElementType.IsValueType || owner.ElementType == typeof(string)
                ? DefaultOf(owner.ElementType)
                : AGReflect.CreateInstance(owner.ElementType);
            if (item == null && owner.ElementType != typeof(string)) return;
            model.BreakUndoMerge();
            owner.List.Insert(index, item);
            Invalidate();
            Repaint();
        });
        menu.AddItem(new GUIContent("往上移"), false, () => MoveListItem(owner, index, index - 1));
        menu.AddItem(new GUIContent("往下移"), false, () => MoveListItem(owner, index, index + 1));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("刪除這一項"), false, () =>
        {
            model.BreakUndoMerge();
            owner.List.RemoveAt(index);
            Invalidate();
            Repaint();
        });
        menu.ShowAsContext();
    }

    private void MoveListItem(AGRow owner, int from, int to)
    {
        if (owner?.List == null || owner.List.IsFixedSize) return;
        if (to < 0 || to >= owner.List.Count || from == to) return;
        model.BreakUndoMerge();
        var item = owner.List[from];
        owner.List.RemoveAt(from);
        owner.List.Insert(to, item);
        Invalidate();
        Repaint();
    }

    private void DrawSlotRow(AGRow row, Rect rowRect)
    {
        var slot = row.Slot;
        int useType = AGReflect.UseType(slot);
        bool hasIssue = Rep.HasIssue(slot, out bool isError);

        var labelRect = Indent(new Rect(rowRect.x, rowRect.y, rowRect.width * 0.42f, rowRect.height), row, false);
        if (row.AssetBinding != null)
        {
            var toggleRect = new Rect(labelRect.x + 2f, labelRect.y + 2f, 16f, labelRect.height - 4f);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUI.Toggle(toggleRect, row.AssetBinding.OverrideEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                row.AssetBinding.OverrideEnabled = enabled;
                Invalidate();
            }
            labelRect.xMin += 20f;
        }
        var labelStyle = hasIssue && isError ? AGStyles.RowLabelError : AGStyles.RowLabel;
        if (!row.HideLabel)
            GUI.Label(labelRect, AGStyles.Elide(row.Label, labelStyle, labelRect.width, AGReflect.FieldDescription(row.Field)), labelStyle);

        bool hasView = AGReflect.GetNode(slot) != null;
        // 收合鈕的位置永遠保留，即使目前沒有接來源不畫它——否則接上線的瞬間欄位會縮一截。
        float portInset = PortReserve + ViewToggleWidth + ListRightInset(row) + 10f;
        var fieldRect = row.HideLabel
            ? new Rect(labelRect.x, rowRect.y + 1f, Mathf.Max(20f, rowRect.xMax - labelRect.x - portInset), rowRect.height - 3f)
            : new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f,
                Mathf.Max(20f, rowRect.width * 0.58f - portInset), rowRect.height - 3f);

        if (row.IsActionSlot)
        {
            string text = useType switch
            {
                1 => AGReflect.GetFormula(slot) is object f ? AGReflect.TypeName(f.GetType()) : "（空）",
                2 => AGReflect.GetAsset(slot) is UnityEngine.Object a ? a.name : "（空資產）",
                _ => "（未啟用，從接點拉線指定動作）",
            };
            GUI.Label(fieldRect, AGStyles.Elide(text, AGStyles.Tiny, fieldRect.width), AGStyles.Tiny);
        }
        else
        {
            // 常數框永遠在、永遠可編輯。接了公式／資產／變數時它是解析失敗的保底值，只是視覺上轉灰。
            string tooltip = useType switch
            {
                1 => "已接公式：公式解析失敗時回到這個值",
                2 => "已接資產：資產缺內容時回到這個值",
                3 => "已接變數：變數不存在或循環時回到這個值",
                _ => null,
            };
            EditorGUI.BeginChangeCheck();
            bool muted = row.AssetBinding != null && !row.AssetBinding.OverrideEnabled;
            var value = useType == 0 && !muted
                ? AGValueField.Draw(fieldRect, row.ResultType, AGReflect.GetDefault(slot), row.IsEnum)
                : AGValueField.DrawMuted(fieldRect, row.ResultType, AGReflect.GetDefault(slot), tooltip, row.IsEnum);
            if (EditorGUI.EndChangeCheck()) { AGReflect.SetDefault(slot, value); Invalidate(); }
        }

        // 位置要和 UpdateRowGeometry 算的 PortPos、PortRectOf 畫的圓一致，三者對不上就會變成
        // 「看得到的圓」和「接得到的位置」不同一個地方。
        var portRect = new Rect(rowRect.xMax - PortReserve,
            rowRect.y + rowRect.height * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter);

        // 由右往左：接點 → 收合鈕 → ✕（清單元素才有）。
        if (hasView) DrawViewToggle(row, ViewToggleRectOf(rowRect));

        var e = Event.current;
        if (e.type == EventType.MouseDown)
        {
            if (e.button == 0 && portRect.Contains(e.mousePosition)) { BeginLinkFromRow(row); e.Use(); }
            else if (e.button == 1 && rowRect.Contains(e.mousePosition)) { ShowSlotMenu(row); e.Use(); }
        }
    }

    /// <summary>接點左邊留給收合鈕的寬度。永遠保留同一個寬度，欄位才不會因為接了東西而跳。</summary>
    private const float ViewToggleWidth = 16f;

    /// <summary>
    /// Slot 的收合鈕：收起這個欄位底下的整段子樹。Alt 按＝solo，只留這一段、其餘全收，再按一次還原。
    /// 純視覺，資料一點都沒動；圖形上刻意不用圓形——圓形在這張圖裡專屬於接點。
    /// </summary>
    private void DrawViewToggle(AGRow row, Rect r)
    {
        string key = AGGraph.CollapseKey(row.OwnerNodeId, row);
        bool solo = soloSlotKey == key;
        bool hidden = effectiveHidden.Contains(key);

        // solo 額外墊一層底：它和一般展開都顯示 -，靠底色分辨「只看這一段」。
        if (solo) AGStyles.RoundedFill(r, AGStyles.HeaderOverlay, 2f);

        GUI.Label(r, hidden && !solo ? "+" : "-", hidden ? AGStyles.HeaderButton : AGStyles.HeaderButtonDim);

        // 不掛 tooltip：這顆開關就在滑鼠移動的必經路徑上，跳說明框只會擋住底下的圖。
        // +／- 本身已經說完了它的狀態。
        if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) return;

        ToggleSlotVisibility(key, Event.current.alt);
    }

    private void DrawValueRow(AGRow row, Rect rowRect)
    {
        var labelRect = Indent(new Rect(rowRect.x, rowRect.y, rowRect.width * 0.42f, rowRect.height), row, false);
        if (!row.HideLabel)
            GUI.Label(labelRect, AGStyles.Elide(row.Label, AGStyles.RowLabel, labelRect.width, AGReflect.FieldDescription(row.Field)), AGStyles.RowLabel);

        float rightInset = 20f + ListRightInset(row);
        var fieldRect = row.HideLabel
            ? new Rect(labelRect.x, rowRect.y + 1f, Mathf.Max(20f, rowRect.xMax - labelRect.x - rightInset), rowRect.height - 3f)
            : new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y + 1f,
                Mathf.Max(20f, rowRect.width * 0.58f - rightInset), rowRect.height - 3f);

        if (row.Field != null && row.Target != null)
        {
            EditorGUI.BeginChangeCheck();
            var value = AGValueField.Draw(fieldRect, row.Field.FieldType, row.Field.GetValue(row.Target), row.IsEnum);
            if (EditorGUI.EndChangeCheck()) { row.Field.SetValue(row.Target, value); Invalidate(); }
            return;
        }

        // 清單裡的基本型別元素沒有 FieldInfo，改用「清單 + 索引」寫回。
        var owner = row.ListOwner;
        if (!row.IsListElement || owner?.List == null || owner.ElementType == null) return;
        if (row.ListIndex < 0 || row.ListIndex >= owner.List.Count) return;

        EditorGUI.BeginChangeCheck();
        var element = AGValueField.Draw(fieldRect, owner.ElementType, owner.List[row.ListIndex]);
        if (EditorGUI.EndChangeCheck()) { owner.List[row.ListIndex] = element; Invalidate(); }
    }

    /// <summary>清單標題：折疊箭頭 + 名稱 + 項數。箭頭與文字整塊都是開關，不必瞄準小三角。</summary>
    private void DrawListHeader(AGRow row, Rect rowRect, int count)
    {
        var labelRect = Indent(rowRect, row);
        var arrow = new Rect(labelRect.x, labelRect.y, 12f, labelRect.height);
        var text = new Rect(arrow.xMax, labelRect.y, Mathf.Max(8f, labelRect.width - 12f), labelRect.height);

        GUI.Label(arrow, row.Collapsed ? "▸" : "▾", AGStyles.Tiny);
        string caption = count == 0
            ? $"{row.Label}（尚無項目）"
            : $"{row.Label}（{count} 項，序號即執行順序）";
        var content = AGStyles.Elide(caption, AGStyles.RowLabel, text.width, "點一下摺疊／展開");
        GUI.Label(text, content, AGStyles.RowLabel);

        // 只有箭頭與文字本身是開關；標題列剩下的空白要留給拖曳節點。
        var toggle = new Rect(arrow.x, labelRect.y,
            Mathf.Min(labelRect.width, 12f + AGStyles.RowLabel.CalcSize(content).x), labelRect.height);
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !toggle.Contains(e.mousePosition)) return;
        listCollapse[AGGraph.CollapseKey(CurrentNodeId(row), row)] = !row.Collapsed;
        graphDirty = true;
        Repaint();
        e.Use();
    }

    /// <summary>折疊狀態的鍵需要節點 Id；列本身不記得自己屬於哪個節點，這裡回頭找一次。</summary>
    private string CurrentNodeId(AGRow row)
    {
        var owner = OwnerOfRow(row);
        return owner != null ? owner.Id : "";
    }

    private void AddListItem(AGRow row)
    {
        if (row.List == null || row.ElementType == null || row.List.IsFixedSize) return;
        // 基本型別沒有「空實例」的問題，用 default 值；其餘走無參數建構。
        var item = row.ElementType.IsValueType || row.ElementType == typeof(string)
            ? DefaultOf(row.ElementType)
            : AGReflect.CreateInstance(row.ElementType);
        if (item == null && row.ElementType != typeof(string)) return;
        model.BreakUndoMerge();
        row.List.Add(item);
        Invalidate();
    }

    private static object DefaultOf(Type t)
        => t == typeof(string) ? "" : Activator.CreateInstance(t);
}

}
