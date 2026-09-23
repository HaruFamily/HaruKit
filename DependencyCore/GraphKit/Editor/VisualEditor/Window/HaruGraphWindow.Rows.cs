namespace HaruFamily.DependencyCore.GraphKit.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// 節點內的參數列與清單列繪製與互動。
    /// </summary>
    public partial class HaruGraphWindow
    {
        private void DrawRows(HGNodeView node, List<HGRow> rows, Rect nodeRect)
        {
            foreach (var row in rows)
            {
                if (row.Hidden) continue;
                var rowRect = new Rect(nodeRect.x, nodeRect.y + row.LocalY, nodeRect.width, row.Height);
                if (rowRect.yMax > nodeRect.yMax) continue;
                // 底要畫在所有內容之下，而且元素展開出來的子列也算同一段，所以在這裡統一畫，不放進元素控制項。
                if (row.ItemOwnerRow != null) DrawListRowBackground(row, rowRect);

                switch (row.Kind)
                {
                    case HGRowKind.NoPort:
                        if (row.IsItem) DrawListElementControls(row, rowRect, nodeRect);
                        DrawNoPortRow(row, rowRect);
                        break;
                    case HGRowKind.InputPort:
                        if (row.IsItem) DrawListElementControls(row, rowRect, nodeRect);
                        DrawInputPortRow(row, rowRect);
                        break;
                    case HGRowKind.Group:
                        if (row.IsItem) DrawListElementControls(row, rowRect, nodeRect);
                        DrawGroupRow(node, row, rowRect, nodeRect);
                        break;
                    case HGRowKind.List:
                        DrawListSection(node, row, rowRect, nodeRect);
                        break;
                }
            }
        }

        /// <summary>巢狀資料的分組標題與它底下的子列。標題只是一行字，接點由子列各自處理。</summary>
        private void DrawGroupRow(HGNodeView node, HGRow row, Rect rowRect, Rect nodeRect)
        {
            if (!row.HideLabel)
            {
                var groupRect = Indent(rowRect, row);
                GUI.Label(groupRect,
                    HGStyles.Elide(row.Label, HGStyles.RowLabel, groupRect.width,
                        row.Descriptor?.Description ?? HGReflect.FieldDescription(row.Field)),
                    HGStyles.RowLabel);
            }
            DrawRows(node, row.Children, nodeRect);
        }

        /// <summary>
        /// 清單畫成「一段」而不是一堆同高的列：底帶包住整段、左側一條縱線串起元素、尾端是整列寬的新增列。
        /// 折疊時只留標題列，子列由 MeasureRows 標成 Hidden。
        /// </summary>
        private void DrawListSection(HGNodeView node, HGRow row, Rect rowRect, Rect nodeRect)
        {
            var items = row.Items as HGListItemSource;
            int count = items?.Count ?? 0;
            bool fixedSize = items != null && !items.CanEditStructure;

            // 底帶要先畫（在所有內容之下），所以這裡就得知道整段的下緣。
            float bandBottom = row.Collapsed
                ? rowRect.yMax
                : Mathf.Min(nodeRect.yMax, nodeRect.y + row.AddRowY + HGGraph.RowHeight);
            var band = ListBandRect(row, rowRect);
            band.height = bandBottom - rowRect.y;
            if (band.height > 0f) HGStyles.RoundedFill(band, HGStyles.ListBand, 3f);

            DrawListHeader(row, rowRect, count);
            if (row.Collapsed) return;

            // 元素的底由 DrawListRowBackground 逐列畫（含展開出來的子列），疊在這層底帶之上。
            DrawRows(node, row.Children, nodeRect);
            if (ReferenceEquals(dragListRow, row)) DrawListInsertLine(row, nodeRect, band);

            var addRect = new Rect(nodeRect.x, nodeRect.y + row.AddRowY, nodeRect.width, HGGraph.RowHeight);
            if (addRect.yMax > nodeRect.yMax) return;

            // 整列寬的按鈕：60px 的小鈕在 0.45 倍縮放下只剩 27px，按不到也讀不到。
            var addBtn = new Rect(band.x + 4f, addRect.y + 2f, band.width - 8f, HGGraph.RowHeight - 4f);
            if (fixedSize)
            {
                GUI.Label(addBtn, new GUIContent("陣列長度固定", "陣列長度在程式或 Inspector 決定，這裡不能增刪；需要增刪請把欄位改成 List<T>"),
                    HGStyles.ListAdd);
                return;
            }
            HGStyles.RoundedFrame(addBtn, HGStyles.ListRule, 3f);
            if (GUI.Button(addBtn, new GUIContent(count == 0 ? "＋ 新增第一項" : "＋ 新增", "在清單尾端加一項"), HGStyles.ListAdd))
                AddListItem(items);
        }

        /// <summary>拖曳重排的插入位置：一條線就夠，不需要動到資料。</summary>
        private void DrawListInsertLine(HGRow row, Rect nodeRect, Rect band)
        {
            if (dragListTarget < 0 || dragListTarget >= row.Children.Count) return;
            var child = row.Children[dragListTarget];
            float y = nodeRect.y + child.LocalY;
            if (dragListTarget > dragListIndex) y += child.Height;      // 往下搬時線畫在目標列的下緣
            HGStyles.Fill(new Rect(band.x + 2f, y - 1f, band.width - 4f, 2f), HGStyles.Link);
        }

        /// <summary>
        /// 列的左緣＝縮排 + LeftPad（清單元素的序號欄）。子列繼承父的 LeftPad，父子左緣才對得齊。
        /// spansRow＝這個 Rect 是整列寬，右側要讓開刪除鈕；只是列內的標籤欄時傳 false。
        /// </summary>
        private static Rect Indent(Rect r, HGRow row, bool spansRow = true)
        {
            float left = 4f + row.LeftPad + row.Depth * HGGraph.IndentWidth;
            float right = 4f + (spansRow && row.IsItem ? HGGraph.ListDeleteWidth : 0f);
            return new Rect(r.x + left, r.y + 1f, Mathf.Max(8f, r.width - left - right), r.height - 2f);
        }

        /// <summary>
        /// 列右端由右往左的固定順序：**接點 → chip → ✕**。接點永遠貼齊節點右緣（所有接點要排成
        /// 一條垂直線），✕ 往左推。位置固定不隨「有沒有接來源」滑動，欄位寬度才不會跳。
        /// 這裡回傳 ✕ 佔掉的橫向空間，清單元素才有。
        /// </summary>
        private static float ListRightInset(HGRow row)
            => row.IsItem ? HGGraph.ListDeleteWidth : 0f;

        /// <summary>接點固定佔住的右緣寬度。收合鈕與 ✕ 都從這裡往左推。</summary>
        private const float InputPortReserve = HGGraph.PortDiameter;

        /// <summary>
        /// 把一列切成「標籤欄｜欄位欄」。欄寬規則只有這一份（`HGGraph.LabelWidthOf`），
        /// 欄位欄一律吃掉剩下的寬度——節點加寬（`[HGNodeView(Width)]`）多出來的空間全進欄位，不進標籤。
        /// `rightInset` 由呼叫端算：接點、chip、✕ 都住在那裡，欄位只吃剩下的。
        /// </summary>
        private static void SplitRow(Rect rowRect, HGRow row, float rightInset, out Rect labelRect, out Rect fieldRect)
        {
            var (units, ratio) = row.Descriptor != null
                ? (row.LabelWidthUnits, row.LabelWidthRatio)
                : HGReflect.LabelWidth(row.Field);
            float labelWidth = HGGraph.LabelWidthOf(rowRect.width, units, ratio);

            labelRect = Indent(new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height), row, false);
            fieldRect = new Rect(rowRect.x + labelWidth, rowRect.y + 1f,
                Mathf.Max(20f, rowRect.width - labelWidth - rightInset), rowRect.height - 3f);
        }

        /// <summary>
        /// 這一列右端要讓給型別 chip 的寬度。**只有畫得出 chip 的列讓**：純值列與動作欄位不留白，
        /// 常數框直接吃到底。左緣對齊由 `SplitRow` 保證，所以這裡只影響右緣。
        /// </summary>
        private static float ChipInset(HGRow row)
            => row.Kind == HGRowKind.InputPort && !row.IsActionSlot && row.ResultType != null && !row.HideLabel
                ? HGGraph.SlotChipColumn
                : 0f;

        /// <summary>
        /// 型別 chip，畫在列右端（由右往左：接點 → chip → ✕ → 常數框）。內容與 Header 右側那顆一致
        /// （`HGReflect.ResultTypeName`）。**寬度固定不隨文字長短浮動**：所有 chip 貼著接點排成一條垂直線。
        /// 裝不下的型別名在 chip 內截字，完整名進 tooltip。
        /// </summary>
        private static void DrawSlotChip(HGRow row, Rect rowRect)
        {
            float inset = ChipInset(row);
            if (inset <= 0f) return;

            // 走 Slot 而非結果型別：同一結果型別可能有多個族（string 有 String 與 Key），
            // 只看結果型別的話兩者的 chip 會長得一樣，企劃分不出這格收的是哪一族。
            string text = row.InputSlot is PropertySlotBase propertySlot
                ? HGReflect.SlotKindName(propertySlot.FamilyType)
                : HGReflect.SlotKindName(row.InputSlot);
            float width = inset - HGGraph.SlotChipGap;   // 間距留在 chip 與接點之間
            float height = Mathf.Min(14f, rowRect.height);
            var chipRect = new Rect(rowRect.xMax - InputPortReserve - inset,
                rowRect.y + (rowRect.height - height) * 0.5f, width, height);
            HGStyles.RoundedFill(chipRect, HGStyles.SlotChipBody, 2f);
            GUI.Label(chipRect, HGStyles.Elide(text, HGStyles.SlotChip, width - 4f, text), HGStyles.SlotChip);
        }

        /// <summary>清單元素的刪除鈕：排在接點左邊，不搶右緣那條接點垂直線。</summary>
        private static Rect DeleteRectOf(Rect rowRect, HGRow row)
            => new Rect(rowRect.xMax - InputPortReserve - ChipInset(row) - HGGraph.ListDeleteWidth,
                rowRect.y + 3f, 14f, rowRect.height - 6f);

        /// <summary>
        /// 清單底帶的左右邊界（高度由呼叫端填）。標題列與元素列都用它，斑馬紋才會和底帶切齊。
        /// listRow 傳清單標題列；元素列傳自己的 ItemOwnerRow。
        /// </summary>
        private static Rect ListBandRect(HGRow listRow, Rect rowRect)
        {
            float left = rowRect.x + 2f + listRow.LeftPad + listRow.Depth * HGGraph.IndentWidth;
            return new Rect(left, rowRect.y, Mathf.Max(8f, rowRect.xMax - left - 2f), rowRect.height);
        }

        /// <summary>
        /// 清單一列的底：斑馬紋做成雙向（一亮一暗），單向疊一層淡白在 Slot 元素上看不出來——
        /// 右半被 HGValueField 的欄位框蓋住，只剩左半在比對。拖曳／hover 再疊一層。
        /// 元素展開出來的子列也走這裡，整段才是同一條紋。
        /// </summary>
        private void DrawListRowBackground(HGRow row, Rect rowRect)
        {
            var owner = row.ItemOwnerRow;
            var band = ListBandRect(owner, rowRect);
            HGStyles.Fill(band, row.ItemIndex % 2 == 0 ? HGStyles.ListStripeEven : HGStyles.ListStripeOdd);

            if (ReferenceEquals(dragListRow, owner) && dragListIndex == row.ItemIndex)
                HGStyles.Fill(band, HGStyles.ListRowDragging);
            else if (rowRect.Contains(Event.current.mousePosition))
                HGStyles.Fill(band, HGStyles.ListRowHover);
        }

        /// <summary>
        /// 清單元素左側的序號 + 拖曳把手、右側的刪除鈕。
        /// 把手與 ✕ 都常態顯示，但刻意放在列的兩端：舊版把 ✕ 貼在把手右邊 1px，想拖曳結果刪掉。
        /// </summary>
        private void DrawListElementControls(HGRow row, Rect rowRect, Rect nodeRect)
        {
            var owner = row.ItemOwnerRow;
            if (row.ItemSource is not HGListItemSource items) return;

            var e = Event.current;
            bool hover = rowRect.Contains(e.mousePosition);
            bool dragging = ReferenceEquals(dragListRow, owner) && dragListIndex == row.ItemIndex;
            bool fixedSize = !items.CanEditStructure;

            // 序號與把手各佔控制欄一半：序號是順序資訊，把手是操作入口，兩件事不該互相取代。
            // 把手排在最前面——它是這一列的抓取點，放在最外緣最好瞄準。
            float x = rowRect.x + 4f + row.Depth * HGGraph.IndentWidth + row.LeftPad - HGGraph.ListGutter;
            var handle = new Rect(x, rowRect.y, 13f, rowRect.height);
            var index = new Rect(x + 13f, rowRect.y, 15f, rowRect.height);
            GUI.Label(index, new GUIContent((row.ItemIndex + 1) + ".", "序號即執行順序"), HGStyles.ListIndex);
            GUI.Label(handle,
                new GUIContent("≡", fixedSize ? "陣列長度固定，不能重排" : "拖曳可調整順序；右鍵有插入與刪除"),
                dragging ? HGStyles.RowLabel : HGStyles.Tiny);

            var remove = DeleteRectOf(rowRect, row);
            // 存回原本的 GUI.enabled，不能寫死 true——外層可能正把整顆鎖定節點畫成不可編輯。
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !fixedSize;
            bool clickedRemove = GUI.Button(remove,
                new GUIContent("✕", fixedSize ? "陣列不能刪除項目" : "刪除這一項（可用 Ctrl+Z 復原）"),
                HGStyles.ListAdd);
            GUI.enabled = wasEnabled;
            if (clickedRemove && !fixedSize)
            {
                BreakUndoMerge();
                items.RemoveAt(row.ItemIndex);
                Invalidate();
                return;
            }
            if (fixedSize || !wasEnabled) return;     // 鎖定子樹裡不給重排與右鍵增刪

            if (e.type == EventType.MouseDown && e.button == 1 && hover)
            {
                ShowListElementMenu(items, row.ItemIndex);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 &&
                     (handle.Contains(e.mousePosition) || index.Contains(e.mousePosition)))
            {
                dragListRow = owner;
                dragListIndex = row.ItemIndex;
                dragListTarget = row.ItemIndex;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging)
            {
                dragListTarget = ListIndexAt(owner, nodeRect, e.mousePosition.y);
                e.Use();
            }
        }

        /// <summary>依滑鼠 Y 算出要插到清單的第幾格。</summary>
        private static int ListIndexAt(HGRow owner, Rect nodeRect, float mouseY)
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

        private void ShowListElementMenu(HGListItemSource items, int index)
        {
            var menu = new GenericMenu();
            if (!items.CanEditStructure)
            {
                menu.AddDisabledItem(new GUIContent("陣列長度固定，無法增刪或重排"));
                menu.ShowAsContext();
                return;
            }

            menu.AddItem(new GUIContent("在此插入一項"), false, () =>
            {
                var item = items.CreateElement();
                if (item == null) return;
                BreakUndoMerge();
                items.Insert(index, item);
                Invalidate();
                Repaint();
            });
            menu.AddItem(new GUIContent("複製這一項"), false, () => DuplicateListItem(items, index));
            menu.AddItem(new GUIContent("往上移"), false, () => MoveListItem(items, index, index - 1));
            menu.AddItem(new GUIContent("往下移"), false, () => MoveListItem(items, index, index + 1));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("刪除這一項"), false, () =>
            {
                BreakUndoMerge();
                items.RemoveAt(index);
                Invalidate();
                Repaint();
            });
            menu.ShowAsContext();
        }

        /// <summary>
        /// 複製清單的一項並插在它下面：內容整棵深拷貝（含接上去的節點子樹），複本與原項各自獨立。
        /// 這張圖的具名Token一律共用——子樹裡指向Token的 Token 節點還是指向同一個端點，
        /// 跟著抄一份會變成不在清單裡的孤兒端點。理由與 <see cref="HGModel.DuplicateToken"/> 同一條。
        /// </summary>
        private void DuplicateListItem(HGListItemSource items, int index)
        {
            if (items == null || !items.CanEditStructure) return;
            if (index < 0 || index >= items.Count) return;

            var source = items.Get(index);
            if (source == null)
            {
                BreakUndoMerge();
                items.Insert(index + 1, null);
                Invalidate();
                Repaint();
                return;
            }

            var shared = CurrentTokens();
            var copy = GraphDeepCopy.Copy(source, shared);
            if (copy == null)
            {
                ShowNotification(new GUIContent("複製這一項失敗，詳見 Console。"));
                return;
            }

            // 識別碼一定要換：載體的 Id 決定節點座標與選取，沿用原本的會讓兩份黏在同一個位置。
            HGModel.ResetNodeIds(copy, shared);

            BreakUndoMerge();
            items.Insert(index + 1, copy);
            Invalidate();
            Repaint();
        }

        private void MoveListItem(HGListItemSource items, int from, int to)
        {
            if (items == null || !items.CanEditStructure) return;
            if (to < 0 || to >= items.Count || from == to) return;
            BreakUndoMerge();
            if (!items.Move(from, to)) return;
            Invalidate();
            Repaint();
        }

        /// <summary>右側一顆輸入接點的參數列：常數框、狀態 chip、接點命中與拉線起點都在這裡。</summary>
        private void DrawInputPortRow(HGRow row, Rect rowRect)
        {
            var slot = row.InputSlot;
            var contentKind = slot.Node?.Kind;
            bool hasIssue = Rep.HasIssue(slot, out bool isError);

            // 由右往左：接點 → chip → ✕ → 常數框。收合鈕已經併進接點自己（見 DrawInputPortGlyph），不另外佔寬。
            float inputPortInset = InputPortReserve + ChipInset(row) + ListRightInset(row) + 10f;
            SplitRow(rowRect, row, inputPortInset, out var labelRect, out var fieldRect);

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

            var labelStyle = hasIssue && isError ? HGStyles.RowLabelError : HGStyles.RowLabel;

            // 動作清單的元素：標籤本身就是「現在接了什麼」（見 SlotShortName），右半再寫一次型別名只是重複，
            // 接了什麼順著線看子節點的 Header 更完整。所以這種列讓標籤吃滿整列，右半不畫。
            // 具名的動作欄位（「True 分支」之類）不同：標籤是欄位名，右半仍要寫內容。
            bool labelIsContent = row.IsActionSlot && row.IsItem;
            if (labelIsContent) labelRect.xMax = rowRect.xMax - inputPortInset;

            if (!row.HideLabel)
            {
                // 只有動作清單的元素能就地改名：具名欄位那一列的標籤是欄位名，改了標籤也看不到。
                if (labelIsContent) DrawActionLabel(row, labelRect, labelStyle);
                else
                {
                    GUI.Label(labelRect,
                        HGStyles.Elide(row.Label, labelStyle, labelRect.width,
                            row.Descriptor?.Description ?? HGReflect.FieldDescription(row.Field)), labelStyle);
                }
            }

            // 型別 chip 排在右端、和標籤無關：常數框只有 int／bool／enum 這種有專屬 widget 的型別才隱含說得出
            // 型別，接了來源整格轉灰、或型別畫不出輸入框時就完全沒有線索。它的寬度已經從 portInset 扣掉，
            // 和常數框不重疊。
            DrawSlotChip(row, rowRect);

            // 沒有標籤的列讓欄位從縮排起點一路吃到右緣：那一列沒有標籤欄可分。
            if (row.HideLabel)
                fieldRect = new Rect(labelRect.x, rowRect.y + 1f,
                    Mathf.Max(20f, rowRect.xMax - labelRect.x - inputPortInset), rowRect.height - 3f);

            if (labelIsContent)
            {
                // 標籤已經說完：右半留白，不重複寫一次型別／資產名。
            }
            else if (row.IsActionSlot)
            {
                string text = contentKind switch
                {
                    NodeKind.Inline or NodeKind.Empty => HGReflect.GetFormula(slot) is object f ? HGReflect.TypeName(f.GetType()) : "（空）",
                    NodeKind.Asset => HGReflect.GetAsset(slot) is UnityEngine.Object a ? a.name : "（空資產）",
                    _ => "（未啟用，從接點拉線指定動作）",
                };
                GUI.Label(fieldRect, HGStyles.Elide(text, HGStyles.Tiny, fieldRect.width), HGStyles.Tiny);
            }
            // 輸出格沒有常數模式：沒接線就是沒人收，畫一格可編的保底值只會讓人以為那個值會被用到。
            else if (row.IsProducedValue)
            {
                string text = contentKind == NodeKind.Property
                    ? "→ " + (slot.Node.IsProtoProperty ? slot.Node.Property?.Name ?? "未選 Key" : "LocalProperty")
                    : (contentKind is NodeKind.Inline or NodeKind.Empty) && HGReflect.GetFormula(slot) is object target
                    ? $"→ {HGReflect.TypeName(target.GetType())}"
                    : "（未接，產出不會被收走）";
                string tip = "這一格是產出：執行時由這個步驟寫進接上的節點，不是從它取值。";
                GUI.Label(fieldRect, HGStyles.Elide(text, HGStyles.Tiny, fieldRect.width, tip), HGStyles.Tiny);
            }
            // 常數框畫的型別可以不等於結果型別（見 FormulaSlotBase.DefaultEditType）：清單這種畫不出輸入框的
            // 結果型別，可以改用一格 enum 表示「沒接線時取什麼」。拉線相容性仍然只看 row.ResultType。
            else if (!HGValueField.CanDraw(HGReflect.DefaultEditType(slot, row.ResultType)))
            {
                // 這個型別連替代的常數框都沒有。畫「此型別沒有對應的輸入介面」只會讓企劃以為欄位壞了，
                // 改成直說這一格現在接了什麼；來源仍然只能從接點拉線指定。
                string text = contentKind switch
                {
                    NodeKind.Inline or NodeKind.Empty => HGReflect.GetFormula(slot) is object uf ? HGReflect.TypeName(uf.GetType()) : "（空公式）",
                    NodeKind.Asset => HGReflect.GetAsset(slot) is UnityEngine.Object ua ? ua.name : "（空資產）",
                    NodeKind.Token => HGReflect.GetToken(slot)?.Name is string un && !string.IsNullOrEmpty(un) ? $"（Token {un}）" : "（已接 Token）",
                    _ => "（未接，用欄位預設）",
                };
                string tip = $"{HGReflect.ResultTypeName(row.ResultType)} 沒有常數保底可編，只能從接點拉線指定來源。";
                GUI.Label(fieldRect, HGStyles.Elide(text, HGStyles.Tiny, fieldRect.width, tip), HGStyles.Tiny);
            }
            else
            {
                var editType = HGReflect.DefaultEditType(slot, row.ResultType);

                // 常數框永遠在。接了公式／資產／Token時它是解析失敗的保底值，只是視覺上轉灰；鎖住時整列不可編。
                // 替代型別的常數框（editType != ResultType）語意不同：它只在沒接線時採用，不是失敗保底。
                bool isSubstitute = editType != row.ResultType;

                // 替代型別的 enum 一律畫成按鈕排：欄位上的 [HGEnum] 是為結果型別下的，替代型別借不到；
                // 按鈕排才吃得到成員的 [HGLabel]，下拉選單只會顯示 CLR 成員名。
                bool enumButtons = row.ForceEnumButtons || (isSubstitute && editType.IsEnum);
                string tooltip = contentKind switch
                {
                    null => null,
                    _ when isSubstitute => "已接來源：以接的來源為準，這一格不會被採用",
                    NodeKind.Inline or NodeKind.Empty => "已接公式：公式解析失敗時回到這個值",
                    NodeKind.Asset => "已接資產：資產缺內容時回到這個值",
                    NodeKind.Token => "已接 Token：Token 不存在或循環時回到這個值",
                    _ => null,
                };
                EditorGUI.BeginChangeCheck();
                var value = contentKind == null
                    ? HGValueField.Draw(fieldRect, editType, HGReflect.GetDefault(slot), enumButtons)
                    : HGValueField.DrawMuted(fieldRect, editType, HGReflect.GetDefault(slot), tooltip, enumButtons);
                if (EditorGUI.EndChangeCheck()) { HGReflect.SetDefault(slot, value); Invalidate(); }
            }

            // 位置要和 UpdateRowGeometry 算的 InputPortPosition、HGPort Presentation 一致，
            // 否則「看得到的圓」和「接得到的位置」會分岔。
            var inputPortRect = PortRect(PortFor(row).Presentation.Position + pan);

            EditorGUI.EndDisabledGroup();

            // 接點一個熱區兩種手勢：原地放開＝收合這一段，拖出去＝拉線。這裡只記起點，判定在 HandleCanvasInput。
            // 不在 MouseDown 當下就起拉線：想收合卻抖了一下的話，放開時會在畫布空白處建出一顆空節點。
            // 鎖住的列照樣記：它不能拉線（接上去也不會被採用），但更需要收起來。
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && inputPortRect.Contains(e.mousePosition))
            {
                inputPortClickPort = PortFor(row);
                inputPortClickStart = e.mousePosition - pan;   // 群組座標扣掉 pan＝圖面座標，才和 graphMouse 同一套
                e.Use();
            }
        }

        /// <summary>
        /// 動作列的標籤：雙擊就地改名，清空＝拿掉標籤改回顯示型別／資產名。
        /// 標籤是同型別動作之間的唯一區分（「主傷害」「濺射」），統一畫布之後這裡是唯一的改名入口。
        /// </summary>
        private void DrawActionLabel(HGRow row, Rect labelRect, GUIStyle labelStyle)
        {
            GraphSlotBase slot = row.InputSlot;
            if (row.Locked)
            {
                GUI.Label(labelRect, HGStyles.Elide(row.Label, labelStyle, labelRect.width), labelStyle);
                return;
            }

            inlineName.Draw(labelRect, slot, HGInlineRename.SiteRow, row.Label, HGReflect.GetLabel(slot) ?? "", labelStyle,
                "雙擊可改名；清空改回顯示型別／資產名", name =>
                {
                    BreakUndoMerge();
                    HGReflect.SetLabel(slot, name);
                    Invalidate();
                    return true;
                });
        }

        /// <summary>沒有接點的值列：直接編欄位；清單裡沒有 FieldInfo 的基本型別元素改用「清單 + 索引」寫回。</summary>
        private void DrawNoPortRow(HGRow row, Rect rowRect)
        {
            float rightInset = 20f + ListRightInset(row);
            SplitRow(rowRect, row, rightInset, out var labelRect, out var fieldRect);

            if (!row.HideLabel)
            {
                string description = row.Descriptor?.Description ?? HGReflect.FieldDescription(row.Field);
                GUI.Label(labelRect, HGStyles.Elide(row.Label, HGStyles.RowLabel, labelRect.width, description), HGStyles.RowLabel);
            }
            else
                fieldRect = new Rect(labelRect.x, rowRect.y + 1f,
                    Mathf.Max(20f, rowRect.xMax - labelRect.x - rightInset), rowRect.height - 3f);

            if (row.Descriptor != null && row.Target != null)
            {
                fieldRect = HGGraph.ValueFieldRect(rowRect, row);
                if (!string.IsNullOrEmpty(row.DrawerError))
                {
                    GUI.Label(fieldRect, new GUIContent("無法編輯", row.DrawerError));
                    return;
                }
                using (new EditorGUI.DisabledScope(row.Locked || row.Descriptor.ReadOnly))
                {
                    EditorGUI.BeginChangeCheck();
                    object value = null;
                    bool succeeded = false;
                    try { value = DrawDescriptorValue(fieldRect, row); succeeded = true; }
                    catch (Exception exception) { ReportDrawerFailure(row, exception.Message); }
                    bool changed = EditorGUI.EndChangeCheck();
                    if (succeeded && changed && !row.Locked && !row.Descriptor.ReadOnly)
                    {
                        EditDescriptorValue(row, value);
                    }
                }
                return;
            }

            if (row.Field != null && row.Target != null)
            {
                EditorGUI.BeginChangeCheck();
                var value = HGValueField.Draw(fieldRect, row.Field.FieldType, row.Field.GetValue(row.Target), row.ForceEnumButtons);
                if (EditorGUI.EndChangeCheck()) { row.Field.SetValue(row.Target, value); AfterValueEdit(); }
                return;
            }

            // 清單裡的基本型別元素沒有 FieldInfo，改用「來源 + 索引」寫回。
            if (!row.IsItem || row.ItemSource is not HGListItemSource items || items.ElementType == null) return;
            if (row.ItemIndex < 0 || row.ItemIndex >= items.Count) return;

            EditorGUI.BeginChangeCheck();
            var element = HGValueField.Draw(fieldRect, items.ElementType, items.Get(row.ItemIndex));
            if (EditorGUI.EndChangeCheck()) { items.Set(row.ItemIndex, element); AfterValueEdit(); }
        }

        /// <summary>自訂 drawer 只回傳編輯意圖；工作副本寫回仍由這個視窗的既有交易負責。</summary>
        private static object DrawDescriptorValue(Rect rect, HGRow row)
        {
            var descriptor = row.Descriptor;
            object current = descriptor.Read(row.Target);
            if (row.ValueDrawer == null) return HGValueField.Draw(rect, descriptor.ValueType, current, row.ForceEnumButtons);

            var context = new HGValueDrawerContext(descriptor, row.Target, row.Locked);
            var result = row.ValueDrawer.Draw(rect, context, current);
            if (result.Changed) GUI.changed = true;
            return result.Value;
        }

        private void ReportDrawerFailure(HGRow row, string message)
        {
            row.DrawerError = message;
            drawerFailures[row.OwnerNodeId + "#" + row.Path] = message;
            graph.Diagnostics.Add(new GraphDiagnostic("graphkit.metadata.drawer-failed", GraphDiagnosticSeverity.Error,
                message, new GraphDiagnosticLocation(model.DocumentId, focus?.Id, nodeId: row.OwnerNodeId, fieldPath: row.Path)));
            var target = focus.Kind == HGFocusKind.Asset ? assetReport : report;
            target.ReplaceGraphViewDiagnostics(graph.Diagnostics);
        }

        private HGSessionCommandResult EditDescriptorValue(HGRow row, object value)
        {
            if (row?.Descriptor == null || row.Locked || row.Descriptor.ReadOnly) return HGSessionCommandResult.Rejected;
            try
            {
                if (Equals(row.Descriptor.Read(row.Target), value)) return HGSessionCommandResult.NoChange;
            }
            catch (Exception exception)
            {
                ReportDrawerFailure(row, exception.Message);
                return HGSessionCommandResult.Rejected;
            }
            if (!TryMutateContent(() =>
            {
                if (!row.Descriptor.TryWrite(row.Target, value, out var error))
                    throw error ?? new ArgumentException("Drawer result does not match the field contract.");
            }, out var message))
            {
                ReportDrawerFailure(row, message);
                return HGSessionCommandResult.Rejected;
            }
            Invalidate();
            return HGSessionCommandResult.Changed;
        }

        private bool TryMutateContent(Action mutation, out string error, bool breakUndoMerge = true)
        {
            error = null;
            Action restoreDocument = null;
            Action restoreAssetHistory = null;
            HGAssetSnapshot assetSnapshot = null;
            bool wasAssetDirty = assetDirty, wasAssetContentDirty = assetContentDirty;
            string originalTokenId = focus.Kind == HGFocusKind.Token ? focus.Token?.Id : null;
            try
            {
                restoreDocument = model.CaptureRollback();
                if (restoreDocument == null) throw new InvalidOperationException("Cannot capture a transaction snapshot.");
                if (focus.Kind == HGFocusKind.Asset)
                {
                    assetSnapshot = CaptureAssetState();
                    restoreAssetHistory = assetHistory.CaptureRollback();
                    if (assetSnapshot == null) throw new InvalidOperationException("Cannot capture an asset snapshot.");
                }
                if (breakUndoMerge) BreakUndoMerge();
                mutation();
                return true;
            }
            catch (Exception exception)
            {
                if (restoreDocument != null)
                {
                    restoreDocument();
                    if (focus.Kind != HGFocusKind.Asset)
                    {
                        var token = string.IsNullOrEmpty(originalTokenId) ? null : FindToken(originalTokenId);
                        focus = token != null ? new HGFocus { Kind = HGFocusKind.Token, Token = token } : AllRootsFocus();
                    }
                }
                if (assetSnapshot != null)
                {
                    string tokenId = focus.Token?.Id;
                    focus.AssetHostSlot.SetNode(assetSnapshot.Root);
                    focus.AssetOrphans = assetSnapshot.Orphans;
                    focus.AssetTokens = assetSnapshot.Tokens;
                    focus.AssetProperties = assetSnapshot.Properties;
                    focus.Token = assetSnapshot.Tokens.Find(token => token.Id == tokenId);
                    restoreAssetHistory?.Invoke();
                    assetDirty = wasAssetDirty;
                    assetContentDirty = wasAssetContentDirty;
                }
                model.OrphanHead = focus.Head;
                ClearPortInteractionState();
                graphDirty = true;
                error = exception.Message;
                return false;
            }
        }

        /// <summary>值欄位改完的收尾。</summary>
        private void AfterValueEdit()
        {
            Invalidate();
        }

        /// <summary>清單標題：折疊箭頭 + 名稱 + 項數。箭頭與文字整塊都是開關，不必瞄準小三角。</summary>
        private void DrawListHeader(HGRow row, Rect rowRect, int count)
        {
            var labelRect = Indent(rowRect, row);
            var arrow = new Rect(labelRect.x, labelRect.y, 12f, labelRect.height);
            var text = new Rect(arrow.xMax, labelRect.y, Mathf.Max(8f, labelRect.width - 12f), labelRect.height);

            GUI.Label(arrow, row.Collapsed ? "▸" : "▾", HGStyles.Tiny);
            string caption = count == 0
                ? $"{row.Label}（尚無項目）"
                : $"{row.Label}（{count} 項，序號即執行順序）";
            var content = HGStyles.Elide(caption, HGStyles.RowLabel, text.width, "點一下摺疊／展開");
            GUI.Label(text, content, HGStyles.RowLabel);

            // 只有箭頭與文字本身是開關；標題列剩下的空白要留給拖曳節點。
            var toggle = new Rect(arrow.x, labelRect.y,
                Mathf.Min(labelRect.width, 12f + HGStyles.RowLabel.CalcSize(content).x), labelRect.height);
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !toggle.Contains(e.mousePosition)) return;
            listCollapse[HGGraph.CollapseKey(CurrentNodeId(row), row)] = !row.Collapsed;
            graphDirty = true;
            Repaint();
            e.Use();
        }

        /// <summary>折疊狀態的鍵需要節點 Id；列本身不記得自己屬於哪個節點，這裡回頭找一次。</summary>
        private string CurrentNodeId(HGRow row)
        {
            var owner = OwnerOfRow(row);
            return owner != null ? owner.Id : "";
        }

        private void AddListItem(HGListItemSource items)
        {
            if (items == null || !items.CanEditStructure) return;
            var item = items.CreateElement();
            if (item == null) return;
            BreakUndoMerge();
            items.Add(item);
            Invalidate();
        }
    }

}
