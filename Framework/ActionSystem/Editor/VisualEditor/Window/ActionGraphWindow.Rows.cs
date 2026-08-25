namespace HaruFamily.Framework.ActionSystem.Editor
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
    /// 列右端由右往左的固定順序：**接點 → chip → ✕**。接點永遠貼齊節點右緣（所有接點要排成
    /// 一條垂直線），✕ 往左推。位置固定不隨「有沒有接來源」滑動，欄位寬度才不會跳。
    /// 這裡回傳 ✕ 佔掉的橫向空間，清單元素才有。
    /// </summary>
    private static float ListRightInset(AGRow row)
        => row.IsListElement ? AGGraph.ListDeleteWidth : 0f;

    /// <summary>接點固定佔住的右緣寬度。收合鈕與 ✕ 都從這裡往左推。</summary>
    private const float PortReserve = AGGraph.PortDiameter;

    /// <summary>
    /// 把一列切成「標籤欄｜欄位欄」。欄寬規則只有這一份（`AGGraph.LabelWidthOf`），
    /// 欄位欄一律吃掉剩下的寬度——節點加寬（`[ASNode(Width)]`）多出來的空間全進欄位，不進標籤。
    /// `rightInset` 由呼叫端算：接點、chip、✕ 都住在那裡，欄位只吃剩下的。
    /// </summary>
    private static void SplitRow(Rect rowRect, AGRow row, float rightInset, out Rect labelRect, out Rect fieldRect)
    {
        var (units, ratio) = AGReflect.LabelWidth(row.Field);
        float labelWidth = AGGraph.LabelWidthOf(rowRect.width, units, ratio);

        labelRect = Indent(new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height), row, false);
        fieldRect = new Rect(rowRect.x + labelWidth, rowRect.y + 1f,
            Mathf.Max(20f, rowRect.width - labelWidth - rightInset), rowRect.height - 3f);
    }

    /// <summary>
    /// 這一列右端要讓給型別 chip 的寬度。**只有畫得出 chip 的列讓**：純值列與動作欄位不留白，
    /// 常數框直接吃到底。左緣對齊由 `SplitRow` 保證，所以這裡只影響右緣。
    /// </summary>
    private static float ChipInset(AGRow row)
        => row.Kind == AGRowKind.Slot && !row.IsActionSlot && row.ResultType != null && !row.HideLabel
            ? AGGraph.SlotChipColumn
            : 0f;

    /// <summary>
    /// 型別 chip，畫在列右端（由右往左：接點 → chip → ✕ → 常數框）。內容與 Header 右側那顆一致
    /// （`AGReflect.ResultTypeName`）。**寬度固定不隨文字長短浮動**：所有 chip 貼著接點排成一條垂直線。
    /// 裝不下的型別名在 chip 內截字，完整名進 tooltip。
    /// </summary>
    private static void DrawSlotChip(AGRow row, Rect rowRect)
    {
        float inset = ChipInset(row);
        if (inset <= 0f) return;

        string text = AGReflect.ResultTypeName(row.ResultType);
        float width = inset - AGGraph.SlotChipGap;   // 間距留在 chip 與接點之間
        float height = Mathf.Min(14f, rowRect.height);
        var chipRect = new Rect(rowRect.xMax - PortReserve - inset,
            rowRect.y + (rowRect.height - height) * 0.5f, width, height);
        AGStyles.RoundedFill(chipRect, AGStyles.SlotChipBody, 2f);
        GUI.Label(chipRect, AGStyles.Elide(text, AGStyles.SlotChip, width - 4f, text), AGStyles.SlotChip);
    }

    /// <summary>清單元素的刪除鈕：排在接點左邊，不搶右緣那條接點垂直線。</summary>
    private static Rect DeleteRectOf(Rect rowRect, AGRow row)
        => new Rect(rowRect.xMax - PortReserve - ChipInset(row) - AGGraph.ListDeleteWidth,
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

        var remove = DeleteRectOf(rowRect, row);
        // 存回原本的 GUI.enabled，不能寫死 true——外層可能正把整顆鎖定節點畫成不可編輯。
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !fixedSize;
        bool clickedRemove = GUI.Button(remove,
            new GUIContent("✕", fixedSize ? "陣列不能刪除項目" : "刪除這一項（可用 Ctrl+Z 復原）"),
            AGStyles.ListAdd);
        GUI.enabled = wasEnabled;
        if (clickedRemove && !fixedSize)
        {
            BreakUndoMerge();
            owner.List.RemoveAt(row.ListIndex);
            Invalidate();
            return;
        }
        if (fixedSize || !wasEnabled) return;     // 鎖定子樹裡不給重排與右鍵增刪

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
            BreakUndoMerge();
            owner.List.Insert(index, item);
            Invalidate();
            Repaint();
        });
        menu.AddItem(new GUIContent("往上移"), false, () => MoveListItem(owner, index, index - 1));
        menu.AddItem(new GUIContent("往下移"), false, () => MoveListItem(owner, index, index + 1));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("刪除這一項"), false, () =>
        {
            BreakUndoMerge();
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
        BreakUndoMerge();
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

        // 由右往左：接點 → chip → ✕ → 常數框。收合鈕已經併進接點自己（見 DrawPortGlyph），不另外佔寬。
        float portInset = PortReserve + ChipInset(row) + ListRightInset(row) + 10f;
        SplitRow(rowRect, row, portInset, out var labelRect, out var fieldRect);

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

        // 沒勾覆蓋＝資產用自己內部的預設，這一列填什麼都不會被採用，所以連名稱帶欄位一起鎖住。
        EditorGUI.BeginDisabledGroup(row.Locked);

        var labelStyle = hasIssue && isError ? AGStyles.RowLabelError : AGStyles.RowLabel;

        // 動作清單的元素：標籤本身就是「現在接了什麼」（見 SlotShortName），右半再寫一次型別名只是重複，
        // 接了什麼順著線看子節點的 Header 更完整。所以這種列讓標籤吃滿整列，右半不畫。
        // 具名的動作欄位（「True 分支」之類）不同：標籤是欄位名，右半仍要寫內容。
        bool labelIsContent = row.IsActionSlot && row.IsListElement;
        if (labelIsContent) labelRect.xMax = rowRect.xMax - portInset;

        if (!row.HideLabel)
        {
            // 只有動作清單的元素能就地改名：具名欄位那一列的標籤是欄位名，改了標籤也看不到。
            if (labelIsContent) DrawActionLabel(row, labelRect, labelStyle);
            else
            {
                GUI.Label(labelRect,
                    AGStyles.Elide(row.Label, labelStyle, labelRect.width, AGReflect.FieldDescription(row.Field)), labelStyle);
            }
        }

        // 型別 chip 排在右端、和標籤無關：常數框只有 int／bool／enum 這種有專屬 widget 的型別才隱含說得出
        // 型別，接了來源整格轉灰、或型別畫不出輸入框時就完全沒有線索。它的寬度已經從 portInset 扣掉，
        // 和常數框不重疊。
        DrawSlotChip(row, rowRect);

        // 沒有標籤的列讓欄位從縮排起點一路吃到右緣：那一列沒有標籤欄可分。
        if (row.HideLabel)
            fieldRect = new Rect(labelRect.x, rowRect.y + 1f,
                Mathf.Max(20f, rowRect.xMax - labelRect.x - portInset), rowRect.height - 3f);

        if (labelIsContent)
        {
            // 標籤已經說完：右半留白，不重複寫一次型別／資產名。
        }
        else if (row.IsActionSlot)
        {
            string text = useType switch
            {
                1 => AGReflect.GetFormula(slot) is object f ? AGReflect.TypeName(f.GetType()) : "（空）",
                2 => AGReflect.GetAsset(slot) is UnityEngine.Object a ? a.name : "（空資產）",
                _ => "（未啟用，從接點拉線指定動作）",
            };
            GUI.Label(fieldRect, AGStyles.Elide(text, AGStyles.Tiny, fieldRect.width), AGStyles.Tiny);
        }
        // 常數框畫的型別可以不等於結果型別（見 FormulaSlotBase.DefaultEditType）：清單這種畫不出輸入框的
        // 結果型別，可以改用一格 enum 表示「沒接線時取什麼」。拉線相容性仍然只看 row.ResultType。
        else if (!AGValueField.CanDraw(AGReflect.DefaultEditType(slot, row.ResultType)))
        {
            // 這個型別連替代的常數框都沒有。畫「此型別沒有對應的輸入介面」只會讓企劃以為欄位壞了，
            // 改成直說這一格現在接了什麼；來源仍然只能從接點拉線指定。
            string text = useType switch
            {
                1 => AGReflect.GetFormula(slot) is object uf ? AGReflect.TypeName(uf.GetType()) : "（空公式）",
                2 => AGReflect.GetAsset(slot) is UnityEngine.Object ua ? ua.name : "（空資產）",
                3 => AGReflect.GetEndpoint(slot)?.Name is string un && !string.IsNullOrEmpty(un) ? $"（變數 {un}）" : "（已接變數）",
                _ => "（未接，用欄位預設）",
            };
            string tip = $"{AGReflect.ResultTypeName(row.ResultType)} 沒有常數保底可編，只能從接點拉線指定來源。";
            GUI.Label(fieldRect, AGStyles.Elide(text, AGStyles.Tiny, fieldRect.width, tip), AGStyles.Tiny);
        }
        else
        {
            var editType = AGReflect.DefaultEditType(slot, row.ResultType);

            // 常數框永遠在。接了公式／資產／變數時它是解析失敗的保底值，只是視覺上轉灰；鎖住時整列不可編。
            // 替代型別的常數框（editType != ResultType）語意不同：它只在沒接線時採用，不是失敗保底。
            bool isSubstitute = editType != row.ResultType;

            // 替代型別的 enum 一律畫成按鈕排：欄位上的 [ASEnum] 是為結果型別下的，替代型別借不到；
            // 按鈕排才吃得到成員的 [ASLabel]，下拉選單只會顯示 CLR 成員名。
            bool enumButtons = row.IsEnum || (isSubstitute && editType.IsEnum);
            string tooltip = useType switch
            {
                0 => null,
                _ when isSubstitute => "已接來源：以接的來源為準，這一格不會被採用",
                1 => "已接公式：公式解析失敗時回到這個值",
                2 => "已接資產：資產缺內容時回到這個值",
                3 => "已接變數：變數不存在或循環時回到這個值",
                _ => null,
            };
            EditorGUI.BeginChangeCheck();
            var value = useType == 0
                ? AGValueField.Draw(fieldRect, editType, AGReflect.GetDefault(slot), enumButtons)
                : AGValueField.DrawMuted(fieldRect, editType, AGReflect.GetDefault(slot), tooltip, enumButtons);
            if (EditorGUI.EndChangeCheck()) { AGReflect.SetDefault(slot, value); Invalidate(); }
        }

        // 位置要和 UpdateRowGeometry 算的 PortPos、PortRectOf 畫的圓一致，三者對不上就會變成
        // 「看得到的圓」和「接得到的位置」不同一個地方。
        var portRect = new Rect(rowRect.xMax - PortReserve,
            rowRect.y + rowRect.height * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter);

        EditorGUI.EndDisabledGroup();

        // 接點一個熱區兩種手勢：原地放開＝收合這一段，拖出去＝拉線。這裡只記起點，判定在 HandleCanvasInput。
        // 不在 MouseDown 當下就起拉線：想收合卻抖了一下的話，放開時會在畫布空白處建出一顆空節點。
        // 鎖住的列照樣記：它不能拉線（接上去也不會被採用），但更需要收起來。
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && portRect.Contains(e.mousePosition))
        {
            portClickRow = row;
            portClickStart = e.mousePosition - pan;   // 群組座標扣掉 pan＝圖面座標，才和 graphMouse 同一套
            e.Use();
        }
    }

    /// <summary>
    /// 就地改名：平常畫成一般標籤，雙擊變輸入框，Enter 提交、Esc 取消、點到別處也提交。
    /// display 是平常顯示的字（可能是自動名），editSeed 是進入編輯時填進去的字（實際存的名字）。
    /// submit 回傳 false＝名稱不合法，維持編輯狀態讓使用者改。回傳 true 代表這一格正在編輯，
    /// 呼叫端要跳過自己的點擊處理，否則同一下會又改名又切焦點。
    /// </summary>
    private bool DrawInlineName(Rect rect, object target, string display, string editSeed,
        GUIStyle style, string tooltip, Func<string, bool> submit)
    {
        var e = Event.current;
        if (!ReferenceEquals(editingNameTarget, target))
        {
            GUI.Label(rect, AGStyles.Elide(display, style, rect.width, tooltip), style);
            if (e.type != EventType.MouseDown || e.button != 0 || e.clickCount != 2) return false;
            if (!rect.Contains(e.mousePosition)) return false;

            editingNameTarget = target;
            editingNameDraft = editSeed ?? "";
            editingNameSubmit = submit;
            GUI.FocusControl(null);
            e.Use();
            Repaint();
            return true;
        }

        // 每幀重存：submit 是 closure，換一份資料就是換一個委派，留舊的會寫到上一輪的物件上。
        editingNameSubmit = submit;

        // 鍵盤事件要在畫欄位**之前**判斷：TextField 會把 Return 吃掉，畫完再問就永遠問不到。
        bool enter = e.type == EventType.KeyDown
            && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool escape = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

        GUI.SetNextControlName(InlineNameControl);
        editingNameDraft = EditorGUI.TextField(rect, editingNameDraft);
        EditorGUI.FocusTextInControl(InlineNameControl);

        // 點到別的地方＝提交：改名是小編輯，留著一個開著的輸入框比直接收掉更容易誤觸。
        bool clickedAway = e.type == EventType.MouseDown && !rect.Contains(e.mousePosition);
        if (!clickedAway && !enter && !escape) return true;

        if (escape) CancelInlineName();
        else CommitInlineName();
        if (enter || escape) e.Use();             // 點走的那一下要留給底下的控制項處理
        Repaint();
        return true;
    }

    private const string InlineNameControl = "agInlineName";

    /// <summary>
    /// 提交目前開著的就地改名（沒有就什麼都不做）。名稱不合法時保持編輯狀態。
    /// **畫布也要呼叫它**：`HandleCanvasInput` 會把點擊 `e.Use()` 掉，畫在它後面的左欄與焦點標題列
    /// 因此看不到那一下 MouseDown，自己收不了尾。
    /// </summary>
    private void CommitInlineName()
    {
        if (editingNameTarget == null) return;
        if (editingNameSubmit != null && !editingNameSubmit(editingNameDraft.Trim())) return;
        CancelInlineName();
    }

    private void CancelInlineName()
    {
        editingNameTarget = null;
        editingNameDraft = "";
        editingNameSubmit = null;
        GUI.FocusControl(null);
    }

    /// <summary>
    /// 動作列的標籤：雙擊就地改名，清空＝拿掉標籤改回顯示型別／資產名。
    /// 標籤是同型別動作之間的唯一區分（「主傷害」「濺射」），統一畫布之後這裡是唯一的改名入口。
    /// </summary>
    private void DrawActionLabel(AGRow row, Rect labelRect, GUIStyle labelStyle)
    {
        object slot = row.Slot;
        if (row.Locked)
        {
            GUI.Label(labelRect, AGStyles.Elide(row.Label, labelStyle, labelRect.width), labelStyle);
            return;
        }

        DrawInlineName(labelRect, slot, row.Label, AGReflect.GetLabel(slot) ?? "", labelStyle,
            "雙擊可改名；清空改回顯示型別／資產名", name =>
            {
                BreakUndoMerge();
                AGReflect.SetLabel(slot, name);
                Invalidate();
                return true;
            });
    }

    private void DrawValueRow(AGRow row, Rect rowRect)
    {
        float rightInset = 20f + ListRightInset(row);
        SplitRow(rowRect, row, rightInset, out var labelRect, out var fieldRect);

        if (!row.HideLabel)
            GUI.Label(labelRect, AGStyles.Elide(row.Label, AGStyles.RowLabel, labelRect.width, AGReflect.FieldDescription(row.Field)), AGStyles.RowLabel);
        else
            fieldRect = new Rect(labelRect.x, rowRect.y + 1f,
                Mathf.Max(20f, rowRect.xMax - labelRect.x - rightInset), rowRect.height - 3f);

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
        BreakUndoMerge();
        row.List.Add(item);
        Invalidate();
    }

    private static object DefaultOf(Type t)
        => t == typeof(string) ? "" : Activator.CreateInstance(t);
}

}
