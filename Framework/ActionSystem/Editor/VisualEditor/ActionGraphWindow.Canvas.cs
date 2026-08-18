namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 中欄畫布：焦點標頭、zoom group、格線、連線與節點本體繪製。
/// </summary>
public partial class ActionGraphWindow
{
    // ===== 中欄 =====

    private void DrawCenter(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Canvas);

        var header = new Rect(r.x, r.y, r.width, HeaderHeight);

        float consoleH = consoleCollapsed ? MinConsole : Mathf.Clamp(consoleHeight, MinConsole, r.height - HeaderHeight - 80f);
        canvasRect = new Rect(r.x, r.y + HeaderHeight, r.width, r.height - HeaderHeight - consoleH);
        var consoleRect = new Rect(r.x, canvasRect.yMax, r.width, consoleH);
        var consoleHandle = new Rect(consoleRect.x, consoleRect.y - 3f, consoleRect.width, ResizeHandleWidth);

        HandleConsoleResize(consoleHandle);

        DrawCanvas(canvasRect);
        DrawFocusHeader(header);
        DrawConsole(consoleRect);
        DrawResizeGrip(consoleHandle, false, resizingConsole);
    }

    private void DrawFocusHeader(Rect r)
    {
        AGStyles.Fill(r, AGStyles.PanelSection);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        if (focus.Kind == AGFocusKind.Asset)
        {
            var banner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 18f);
            AGStyles.Fill(banner, new Color(0.45f, 0.32f, 0.18f));
            GUI.Label(banner, "　共用資產：修改會影響所有引用它的對象。存檔是獨立的一次交易。", AGStyles.RowLabel);
            if (focus.Endpoint != null)
            {
                DrawVariableName(new Rect(r.x, r.y + 20f, r.width - 100f, 22f), focus.Endpoint);
                if (GUI.Button(new Rect(r.xMax - 96f, r.y + 22f, 90f, 18f), "← 回資產本體")) ExitVariable();
            }
            else GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 18f), focus.Title, EditorStyles.boldLabel);
            return;
        }

        if (focus.Kind == AGFocusKind.Variable)
        {
            DrawVariableName(new Rect(r.x, r.y, r.width - 100f, 22f), focus.Endpoint);
            GUI.Label(new Rect(r.x + 28f, r.y + 22f, r.width - 130f, 16f),
                focus.Endpoint?.Slot?.Node == null
                    ? "沒接來源＝具名常數，值直接填在 HEAD 的來源欄位。"
                    : "這個變數的值由下面這棵子樹算出來。外部用它的名字查值。", AGStyles.Tiny);
            if (GUI.Button(new Rect(r.xMax - 96f, r.y + 20f, 90f, 18f), "← 回時機畫布")) ExitVariable();
            return;
        }

        if (focus.Kind == AGFocusKind.Action && focus.ActionSlot != null)
        {
            DrawFocusName(r, focus.ActionSlot, focus.Title, name =>
            {
                AGReflect.SetLabel(focus.ActionSlot, name);
                Invalidate();
                return true;
            });
        }
        else GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 18f), focus.Title, EditorStyles.boldLabel);

        string desc = "";
        if (focus.Kind == AGFocusKind.Action && focus.ActionSlot != null)
        {
            var f = AGReflect.GetFormula(focus.ActionSlot);
            desc = f != null ? AGReflect.TypeDescription(f.GetType()) : "這個動作還沒有內容，請從根節點下拉選擇。";
            if (AGReflect.GetDisabled(focus.ActionSlot)) desc += "　（已停用，不會執行）";
        }
        else if (focus.Kind == AGFocusKind.Timing)
        {
            int groups = 0, actions = 0;
            foreach (var g in model.ReadGroups())
            {
                groups++;
                actions += g.Actions?.Count ?? 0;
            }
            desc = groups > 0
                ? $"{groups} 個時機、{actions} 個動作。時機節點可自由擺位；跨時機共用來源直接拉線即可。"
                : "還沒有任何時機節點。在畫布空白處按右鍵新增一個。";
        }
        else if (focus.Kind == AGFocusKind.None)
        {
            desc = "從右上角的時機下拉跳到某個時機，或從左欄選一個變數開始編輯。";
        }
        GUI.Label(new Rect(r.x + 6f, r.y + 24f, r.width - 12f, 16f), desc, AGStyles.Tiny);
    }

    /// <summary>焦點名稱平常只讀；按左側按鈕才進入編輯，再按一次才提交。</summary>
    /// <summary>變數畫布的標題就地改名。名字是外部查詢的 key，改名不影響圖內連線（那是物件參照）。</summary>
    private void DrawVariableName(Rect header, GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        DrawFocusName(header, endpoint, endpoint.Name ?? "", name =>
        {
            if (model.RenameEndpoint(endpoint, name, CurrentEndpoints(), out string error))
            {
                MarkGraphChanged();
                return true;
            }
            ShowNotification(new GUIContent(error));
            return false;
        });
    }

    private void DrawFocusName(Rect header, object target, string displayName, Func<string, bool> submit)
    {
        bool editing = ReferenceEquals(editingNameTarget, target);
        var editRect = new Rect(header.x + 4f, header.y + 3f, 20f, 20f);
        var nameRect = new Rect(editRect.xMax + 4f, header.y + 2f, header.width - 32f, 22f);
        if (GUI.Button(editRect, new GUIContent(editing ? "✓" : "✎", editing ? "提交名稱" : "編輯名稱"), EditorStyles.miniButton))
        {
            if (!editing)
            {
                editingNameTarget = target;
                editingNameDraft = displayName ?? "";
                GUI.FocusControl(null);
            }
            else if (submit(editingNameDraft.Trim()))
            {
                editingNameTarget = null;
                editingNameDraft = "";
            }
            Repaint();
        }

        if (editing)
        {
            GUI.SetNextControlName("actionGraphFocusName");
            editingNameDraft = EditorGUI.TextField(nameRect, editingNameDraft);
            EditorGUI.FocusTextInControl("actionGraphFocusName");
        }
        else GUI.Label(nameRect, displayName, AGStyles.FocusTitle);
    }

    // ===== 畫布 =====

    private void DrawCanvas(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Canvas);
        DrawGrid(r);

        var e = Event.current;
        Vector2 clipMouse = e.mousePosition - r.position;
        HandleLinkNavigation(e, clipMouse);
        Vector2 graphMouse = clipMouse / zoom - pan;
        bool mouseInCanvas = r.Contains(e.mousePosition);

        if (graph != null)
        {
            foreach (var node in graph.Nodes) UpdateRowGeometry(node, node.Rows);
            DrawLinks(graphMouse);
        }

        BeginZoomedCanvas(r);
        try
        {
            if (graph != null)
            {
                AGNode linkTarget = LinkTargetNode(graphMouse);
                foreach (var node in graph.Nodes)
                {
                    if (node.Hidden) continue;
                    DrawNode(node, ReferenceEquals(node, linkTarget));
                }
                DrawEmptyTimingHint();
                if (boxSelecting)
                {
                    var box = BoxRect();
                    var visual = new Rect(box.position + pan, box.size);
                    AGStyles.Fill(visual, new Color(0.42f, 0.78f, 1f, 0.10f));
                    AGStyles.Frame(visual, AGStyles.Link);
                }
            }
        }
        finally
        {
            EndZoomedCanvas();
        }

        DrawNodeInfoOverlay(r);
        DrawTimingOverlay(r);
        if (mouseInCanvas && !HandleAssetDrag(e, graphMouse)) HandleCanvasInput(e, graphMouse);
    }

    /// <summary>連線期間先更新視圖，讓同一事件的預覽端點仍精準對齊滑鼠。</summary>
    private void HandleLinkNavigation(Event e, Vector2 clipMouse)
    {
        if (!linking) return;
        if (e.type == EventType.ScrollWheel)
        {
            ZoomAt(clipMouse, e.delta.y);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            pan += e.delta / zoom;
            e.Use();
            Repaint();
        }
    }

    /// <summary>離開 EditorWindow 的隱式群組，建立不受外層 clip matrix 干擾的縮放畫布。</summary>
    private void BeginZoomedCanvas(Rect r)
    {
        Vector2 rootOffset = GUIUtility.GUIToScreenPoint(Vector2.zero) - position.position;
        rootGuiGroupRect = new Rect(rootOffset, position.size);

        GUI.EndGroup();

        var clippedArea = new Rect(
            r.x + rootOffset.x,
            r.y + rootOffset.y,
            r.width / zoom,
            r.height / zoom);
        GUI.BeginGroup(clippedArea);

        canvasGuiMatrix = GUI.matrix;
        var translation = Matrix4x4.TRS(clippedArea.position, Quaternion.identity, Vector3.one);
        var scale = Matrix4x4.Scale(new Vector3(zoom, zoom, 1f));
        GUI.matrix = translation * scale * translation.inverse * GUI.matrix;
    }

    /// <summary>結束縮放畫布並恢復 EditorWindow 原本的局部座標與裁切。</summary>
    private void EndZoomedCanvas()
    {
        GUI.matrix = canvasGuiMatrix;
        GUI.EndGroup();
        GUI.BeginGroup(rootGuiGroupRect);
    }

    private void DrawGrid(Rect r)
    {
        Handles.BeginGUI();
        float step = 20f * zoom;
        if (step > 4f)
        {
            Vector2 offset = new Vector2(pan.x * zoom % step, pan.y * zoom % step);
            Handles.color = AGStyles.Grid;
            for (float x = r.x + offset.x; x < r.xMax; x += step)
                Handles.DrawLine(new Vector3(x, r.y), new Vector3(x, r.yMax));
            for (float y = r.y + offset.y; y < r.yMax; y += step)
                Handles.DrawLine(new Vector3(r.x, y), new Vector3(r.xMax, y));
        }
        Handles.EndGUI();
    }

    /// <summary>連線在未縮放的視窗座標繪製，避免 GUI.matrix 旋轉造成起終點偏移。</summary>
    private void DrawLinks(Vector2 graphMouse)
    {
        Handles.BeginGUI();

        // 兩趟：先畫一般線，高亮線最後畫才不會被別的線壓在底下。
        // 一張畫布容納全部時機之後，共用來源的連入線可能來自很遠的另一個時機，這是唯一追得回去的線索。
        for (int pass = 0; pass < 2; pass++)
        {
            bool tracedPass = pass == 1;
            foreach (var link in graph.Links)
            {
                if (link.ParentRow == null || link.Target == null || link.Target.Hidden) continue;
                if (IsTracedLink(link) != tracedPass) continue;
                // 停用子樹的線一起壓暗，才看得出整段路徑都不會被求值。
                DrawGraphLine(link.ParentRow.PortPos, link.Target.OutputPort, link.Target.InDisabledSubtree, tracedPass);
            }
        }
        if (linking && (linkRow != null || linkNode != null))
        {
            Vector2 from = linkRow != null ? linkRow.PortPos : linkNode.OutputPort;
            DrawGraphLine(from, LinkPreviewEnd(graphMouse));
        }
        Handles.EndGUI();
    }

    /// <summary>
    /// 這條線接在選取的節點上：兩端任一端被選取就算。純視覺，不改資料也不影響命中測試。
    /// </summary>
    private bool IsTracedLink(AGLink link)
        => selectedIds.Count > 0
            && (selectedIds.Contains(link.Target.Id) || selectedIds.Contains(link.ParentRow.OwnerNodeId));

    /// <summary>
    /// 一顆時機節點都還沒有時的入口。有節點之後就不再出現——刻意不在開窗時自動建第一個時機，
    /// 那會在使用者還沒編輯前就把資產標成未存檔。
    /// </summary>
    private void DrawEmptyTimingHint()
    {
        if (focus.Kind != AGFocusKind.Timing || graph.Nodes.Count > 0) return;

        var rect = new Rect(new Vector2(40f, 40f) + pan, new Vector2(AGGraph.NodeWidth, AGGraph.HeaderHeight + 4f));
        AGStyles.RoundedFill(rect, AGStyles.NodeBody, NodeCornerRadius);
        AGStyles.RoundedFrame(rect, AGStyles.NodeBorder, NodeCornerRadius, 1f);
        if (GUI.Button(rect, "＋ 新增第一個時機節點", AGStyles.ListAdd))
            ShowAddTimingMenu(new Vector2(40f, 40f));
    }

    private const float TimingOverlayWidth = 190f;

    /// <summary>
    /// 畫布右上角的時機下拉：所有時機都在同一張畫布上，所以它是「跳到哪一顆」而不是「切換畫布」。
    /// 選到還沒建立的時機就在畫面中央建一顆。和說明面板一樣畫在 zoom clip 外，縮到 0.45 也讀得到。
    /// </summary>
    private void DrawTimingOverlay(Rect canvas)
    {
        if (model == null) return;

        var r = new Rect(canvas.xMax - TimingOverlayWidth - 8f, canvas.y + 8f, TimingOverlayWidth, 22f);
        if (EditorGUI.DropdownButton(r, new GUIContent("時機", "跳到某個時機節點，或新增一個"), FocusType.Keyboard))
            ShowTimingMenu(CanvasCenterInGraph());
    }

    /// <summary>畫布中心的 graph 座標：從下拉新增的時機節點放這裡，使用者才看得到它。</summary>
    private Vector2 CanvasCenterInGraph()
        => new Vector2(canvasRect.width * 0.5f / zoom - pan.x - AGGraph.NodeWidth * 0.5f,
                       canvasRect.height * 0.5f / zoom - pan.y);

    private const float InfoOverlayWidth = 300f;

    /// <summary>
    /// 畫布左上角的說明面板：型別說明是型別常數，畫在每個節點上只是重複噪音，改成只顯示目前選取節點的說明。
    /// 畫在 zoom clip 外，所以不隨縮放改變大小；純顯示，不吃滑鼠事件。
    /// </summary>
    private void DrawNodeInfoOverlay(Rect canvas)
    {
        if (graph == null || selectedIds.Count != 1) return;

        AGNode node = null;
        foreach (var n in graph.Nodes)
        {
            if (!selectedIds.Contains(n.Id)) continue;
            node = n;
            break;
        }
        if (node == null) return;

        // 有物件卻沒有說明＝作者忘了寫 [ASNode] 描述，直接講出來，不要靜默留白。
        string desc = !string.IsNullOrWhiteSpace(node.Desc) ? node.Desc
            : node.Obj != null ? "（這個型別沒有 [ASNode] 說明）"
            : null;

        float width = Mathf.Min(InfoOverlayWidth, canvas.width - 16f);
        if (width < 80f) return;

        float textWidth = width - 16f;
        float descHeight = desc == null ? 0f : AGStyles.NodeDesc.CalcHeight(new GUIContent(desc), textWidth);
        var panel = new Rect(canvas.x + 8f, canvas.y + 8f, width, 20f + descHeight + 10f);

        AGStyles.RoundedFill(panel, new Color(0.10f, 0.11f, 0.13f, 0.88f), 4f);
        AGStyles.RoundedFrame(panel, AGStyles.NodeBorder, 4f);
        GUI.Label(new Rect(panel.x + 2f, panel.y + 4f, textWidth, 18f),
            AGStyles.Elide(node.Title, AGStyles.OverlayTitle, textWidth), AGStyles.OverlayTitle);
        if (desc != null)
            GUI.Label(new Rect(panel.x + 2f, panel.y + 22f, textWidth, descHeight), desc, AGStyles.NodeDesc);
    }

    /// <summary>graph space → window space；zoom clip 外要用視窗座標的地方（選單錨點、直線）走這裡。</summary>
    private Rect GraphToWindowRect(Rect graphRect)
        => new Rect(canvasRect.position + (graphRect.position + pan) * zoom, graphRect.size * zoom);

    private void DrawGraphLine(Vector2 graphFrom, Vector2 graphTo, bool dim = false, bool traced = false)
    {
        Vector2 from = canvasRect.position + (graphFrom + pan) * zoom;
        Vector2 to = canvasRect.position + (graphTo + pan) * zoom;
        if (!ClipLine(canvasRect, ref from, ref to)) return;

        // 顏色只表達「這條線接的是選取中的節點」，透明度仍歸停用管——兩件事互不覆蓋。
        Color color = traced ? AGStyles.NodeBorderSelected : Color.white;
        if (dim) color.a *= AGStyles.LinkDisabled.a;

        Color oldColor = Handles.color;
        Handles.color = color;
        Handles.DrawAAPolyLine(traced ? LinkThickness + 2f : LinkThickness,
            new Vector3(from.x, from.y), new Vector3(to.x, to.y));
        Handles.color = oldColor;
    }

    private static bool ClipLine(Rect rect, ref Vector2 from, ref Vector2 to)
    {
        Vector2 start = from;
        Vector2 delta = to - from;
        float min = 0f;
        float max = 1f;
        if (!ClipLineEdge(-delta.x, start.x - rect.xMin, ref min, ref max)
            || !ClipLineEdge(delta.x, rect.xMax - start.x, ref min, ref max)
            || !ClipLineEdge(-delta.y, start.y - rect.yMin, ref min, ref max)
            || !ClipLineEdge(delta.y, rect.yMax - start.y, ref min, ref max)) return false;
        from = start + delta * min;
        to = start + delta * max;
        return true;
    }

    private static bool ClipLineEdge(float direction, float distance, ref float min, ref float max)
    {
        if (Mathf.Approximately(direction, 0f)) return distance >= 0f;
        float ratio = distance / direction;
        if (direction < 0f)
        {
            if (ratio > max) return false;
            if (ratio > min) min = ratio;
        }
        else
        {
            if (ratio < min) return false;
            if (ratio < max) max = ratio;
        }
        return true;
    }

    /// <summary>
    /// Header 右上角的註解開關：加了圓角方底＝註解框開著，淡的 ✎ ＝收起來了。
    /// 刻意不用圓形——圓形在這張圖裡專屬於接點，形狀不共用才不會誤讀。
    /// 收起有內容的註解時 ✎ 保持亮的，才分得出「收起來但有東西」和「根本沒寫」。
    /// </summary>
    /// <summary>註解輸入框的固定名稱：IMGUI 的控制項 id 是按繪製順序發的，收掉一個框會讓後面的框接手同一個 id。</summary>
    private static string NoteControlName(string nodeId) => "agnote:" + nodeId;

    /// <summary>
    /// 收掉註解框前先放掉鍵盤焦點。不放的話焦點連同編輯中的字串會落到下一個拿到同一個
    /// 控制項 id 的 TextArea 上，看起來就是「文字跑到別的節點去了」。
    /// </summary>
    private static void ReleaseNoteFocus(string nodeId)
    {
        if (GUI.GetNameOfFocusedControl() != NoteControlName(nodeId)) return;
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
    }

    private static bool DrawNoteToggle(Rect r, bool open, bool hasNote)
    {
        if (open) AGStyles.RoundedFill(r, AGStyles.HeaderOverlay, 2f);
        GUI.Label(r, "✎", open || hasNote ? AGStyles.HeaderButton : AGStyles.HeaderButtonDim);
        return GUI.Button(r, new GUIContent("",
            open ? "收起註解（內容保留）" : hasNote ? "展開註解" : "加上註解"), GUIStyle.none);
    }

    /// <summary>
    /// Header 右上角的停用開關：停用中＝圓角方底 + 亮的暫停圖示，啟用中＝淡的暫停圖示。
    /// 和註解開關同一套語彙，一樣刻意避開圓形——圓形在這張圖裡專屬於接點。
    /// </summary>
    private static readonly GUIContent DisableFallbackIcon = new("||");
    private static GUIContent disableIcon;

    private static bool DrawDisableToggle(Rect r, bool disabled, int users)
    {
        if (disabled) AGStyles.RoundedFill(r, AGStyles.HeaderOverlay, 2f);
        disableIcon ??= EditorGUIUtility.IconContent("d_PauseButton On");
        GUI.Label(r, disableIcon?.image != null ? disableIcon : DisableFallbackIcon,
            disabled ? AGStyles.HeaderButton : AGStyles.HeaderButtonDim);

        // 載體是共用單位，停用一顆被多個欄位指著的節點會同時影響全部引用處，講清楚才不會變成遠端的靜默行為。
        string tip = disabled
            ? (users > 1 ? $"已停用：{users} 個欄位改用保底值。點一下啟用" : "已停用：引用它的欄位改用保底值。點一下啟用")
            : (users > 1 ? $"停用這顆節點（{users} 個欄位會一起改用保底值）" : "停用這顆節點，引用它的欄位改用保底值");
        return GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
    }

    /// <summary>
    /// Header 底色：HEAD 深紫紅、Action 洋紅、Formula 琥珀、Asset 靛藍、變數 深綠。
    /// 容器型節點用漸層表達「容器 → 它承載的東西」：Action 型資產是靛藍→洋紅，變數是深綠→結果型別色。
    /// </summary>
    private static void HeaderColors(AGNode node, out Color from, out Color to)
    {
        // HEAD 從流程入口深紫紅漸層到目前焦點可接的內容色。
        if (node.IsRoot)
        {
            from = AGStyles.HeaderHead;
            to = node.IsActionNode ? AGStyles.HeaderAction : AGStyles.HeaderFormula;
            return;
        }
        if (node.IsVariableNode)
        {
            from = AGStyles.HeaderToken;
            to = AGStyles.HeaderFormula;
            return;
        }
        if (node.IsAssetNode)
        {
            from = AGStyles.HeaderAsset;
            to = node.ResultType == null ? AGStyles.HeaderAction : AGStyles.HeaderFormula;
            return;
        }
        from = to = node.IsActionNode ? AGStyles.HeaderAction : AGStyles.HeaderFormula;
    }

    private void DrawNode(AGNode node, bool isLinkTarget)
    {
        var rect = new Rect(node.Pos + pan, new Vector2(node.Width, node.Height));

        AGStyles.RoundedFill(rect, AGStyles.NodeBody, NodeCornerRadius);
        var header = new Rect(rect.x, rect.y, rect.width, AGGraph.HeaderHeight);
        HeaderColors(node, out Color headerFrom, out Color headerTo);
        AGStyles.HeaderFill(header, headerFrom, headerTo, NodeCornerRadius);

        // Header 由右往左排：停用 → 註解 ✎ → 結果型別 chip，剩下的寬度全給名稱區（＝換來源的按鈕）。
        // 節點層級的問題畫在節點底部的色條（不佔 Header，也不和身分色搶）；參數列層級的問題直接把該列標紅。
        // 資產／空節點自己沒有物件，問題掛在父欄位上，改查父欄位才看得到。
        object issueTarget = node.Obj
            ?? (node.IsAssetNode || node.IsVariableNode || node.IsPlaceholder ? node.ParentSlot : null);
        bool hasNodeIssue = Rep.HasIssue(issueTarget, out bool nodeError);

        float headerRight = rect.xMax - 4f;

        // 停用開關固定在右上角。HEAD 沒有：它的載體是頭端物件，沒有 GraphNode 可停用。
        if (node.Carrier != null)
        {
            var disableToggle = new Rect(headerRight - 14f, rect.y + 3f, 14f, 14f);
            if (DrawDisableToggle(disableToggle, node.Carrier.Disabled, CarrierUsers(node.Carrier)))
            {
                model.BreakUndoMerge();
                model.SetNodeDisabled(node.Id, !node.Carrier.Disabled);
                Invalidate();       // 停用改的是資料，不是視覺狀態
                Repaint();
            }
            headerRight = disableToggle.x - 3f;
        }

        // 註解開關排在停用鈕左邊，變數與資產葉節點也有。
        // HEAD 沒有：它的載體是頭端物件（ActionSlot／TokenEntry／資產）而不是 GraphNode，沒有存註解的欄位。
        if (!node.IsRoot)
        {
            var noteToggle = new Rect(headerRight - 14f, rect.y + 3f, 14f, 14f);
            if (DrawNoteToggle(noteToggle, node.NoteOpen, !string.IsNullOrWhiteSpace(node.Tips)))
            {
                // 收起只是收起：內容留在載體上，再打開原封不動。
                if (node.NoteOpen)
                {
                    ReleaseNoteFocus(node.Id);
                    noteCollapsed.Add(node.Id);
                    noteOpenId = null;
                }
                else
                {
                    // 空框是暫態，得確保節點被選取，否則下一幀就會被收起來。
                    noteCollapsed.Remove(node.Id);
                    noteOpenId = node.Id;
                    selectedIds.Add(node.Id);
                }
                graphDirty = true;      // 純視覺，不算改資料
                Repaint();
            }
            headerRight = noteToggle.x - 3f;
        }

        if (!string.IsNullOrEmpty(node.Chip))
        {
            float chipWidth = Mathf.Min(AGStyles.NodeChip.CalcSize(new GUIContent(node.Chip)).x, 96f);
            var chipRect = new Rect(headerRight - chipWidth, rect.y + 3f, chipWidth, 14f);
            AGStyles.RoundedFill(chipRect, AGStyles.HeaderOverlay, 3f);
            GUI.Label(chipRect, AGStyles.Elide(node.Chip, AGStyles.NodeChip, chipWidth), AGStyles.NodeChip);
            headerRight = chipRect.x - 2f;
        }

        // 左端只讓開輸出接點；停用鈕已固定在右上角。
        float titleInset = node.IsRoot ? 0f : AGGraph.PortDiameter + 2f;

        float titleWidth = Mathf.Max(24f, headerRight - rect.x - titleInset);
        var titleRect = new Rect(rect.x + titleInset, rect.y, titleWidth, AGGraph.HeaderHeight);
        // 命中測試在 zoom clip 外做，所以存 graph space。
        node.TitleRect = new Rect(titleRect.position - pan, titleRect.size);

        float textWidth = titleWidth;
        node.SourceMenuRect = new Rect();
        if (node.HasSourceSelector)
        {
            // 只有右端這顆 ▾ 是換來源的按鈕，名稱區其餘部分留給拖曳。
            // 整塊可按會讓「想搬節點」變成「開了選單」——Header 本來就是唯一的拖曳抓取區。
            // 空節點也一樣，不再例外：它同樣要能被拖著擺位。
            var lift = AGStyles.HeaderOverlay;
            var arrow = new Rect(titleRect.xMax - SourceArrowWidth, titleRect.y + 2f,
                SourceArrowWidth, titleRect.height - 4f);
            var hot = arrow;
            AGStyles.RoundedFill(hot, new Color(lift.r, lift.g, lift.b, lift.a * 0.65f), 3f);
            GUI.Label(arrow, new GUIContent("▾", node.IsPlaceholder ? "選擇來源" : "換來源"), AGStyles.HeaderButton);
            // 命中測試在 zoom clip 外做，所以存 graph space。
            node.SourceMenuRect = new Rect(hot.position - pan, hot.size);
            textWidth = titleWidth - SourceArrowWidth - 2f;
        }
        GUI.Label(titleRect, AGStyles.Elide(node.Title, AGStyles.NodeTitle, textWidth), AGStyles.NodeTitle);

        // 資產與變數的本體是一列「選哪一個」的下拉；一般節點畫自己的參數列；空節點兩者都沒有。
        if (node.IsAssetNode || node.IsVariableNode)
        {
            DrawReferencePickerRow(node, rect);
            DrawRows(node, node.Rows, rect);
        }
        else if (!node.IsPlaceholder) DrawRows(node, node.Rows, rect);

        if (node.TipsHeight > 0f)
        {
            float noteTop = rect.y + node.ContentHeight - node.TipsHeight - 4f;
            var tipsField = new Rect(rect.x + 8f, noteTop, rect.width - 16f, node.TipsHeight);
            var noteRect = new Rect(rect.x + 4f, noteTop - 3f, rect.width - 8f, node.TipsHeight + 6f);
            AGStyles.Fill(noteRect, AGStyles.NodeNote);
            AGStyles.Frame(noteRect, AGStyles.NodeNoteBorder);
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName(NoteControlName(node.Id));
            string tips = EditorGUI.TextArea(tipsField, node.Tips ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                model.SetNodeTips(node.Id, tips);
                // 內容被清空時保留空框：打字打到一半整個收掉，游標會跟著消失。
                if (string.IsNullOrWhiteSpace(tips)) noteOpenId = node.Id;
                Invalidate();
            }
        }

        // 取消選取就收掉還沒打字的空框；有內容的註解不受選取影響。
        if (node.NoteOpen && string.IsNullOrWhiteSpace(node.Tips) && !selectedIds.Contains(node.Id))
        {
            ReleaseNoteFocus(node.Id);
            noteOpenId = null;
            graphDirty = true;
            Repaint();
        }

        // 停用暗紗蓋在內容之上、問題色條之下：停用的節點要一眼看出來，但它的錯誤與警告仍然要讀得到。
        // 只是貼圖，不註冊控制項，所以底下的參數列照樣可以編輯——停用不等於鎖定。
        if (node.InDisabledSubtree)
            AGStyles.RoundedFill(rect, AGStyles.DisabledVeil, NodeCornerRadius);

        // 問題色條：貼在節點底緣，紅＝錯誤、琥珀＝警告。
        // 不做成 Header 徽章——Header 已經被身分色佔用，狀態和身分混在同一塊會互相干擾。
        if (hasNodeIssue)
        {
            var issueBar = new Rect(rect.x, rect.yMax - IssueBarHeight, rect.width, IssueBarHeight);
            AGStyles.RoundedBottomFill(issueBar, nodeError ? AGStyles.Error : AGStyles.Warning, NodeCornerRadius);
            GUI.Label(issueBar, new GUIContent("", nodeError ? "此節點有錯誤，詳見 Console" : "此節點有警告，詳見 Console"));
        }

        bool selected = selectedIds.Contains(node.Id);
        // 拉線期間：可以接的 Node 整個亮外框，滑鼠實際吸到的那個再加粗。
        bool linkCandidate = linking && linkRow != null && linkCompatibleNodeIds.Contains(node.Id);
        Color borderColor = isLinkTarget ? AGStyles.Link
            : linkCandidate ? new Color(AGStyles.Link.r, AGStyles.Link.g, AGStyles.Link.b, 0.55f)
            : selected ? AGStyles.NodeBorderSelected
            : node.IsRoot ? AGStyles.HeadBorder : AGStyles.NodeBorder;
        float thickness = isLinkTarget || selected ? 2f : linkCandidate ? 1.5f : node.IsRoot ? 2f : 1f;

        // HEAD 是整張圖的起點，再套一圈外光暈把它和一般節點分開（顏色會被選取／拉線狀態蓋過，光暈不會）。
        if (node.IsRoot)
        {
            var halo = new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f);
            AGStyles.RoundedFrame(halo, new Color(AGStyles.HeadBorder.r, AGStyles.HeadBorder.g, AGStyles.HeadBorder.b, 0.35f),
                NodeCornerRadius + 3f, 1f);
        }

        AGStyles.RoundedFrame(rect, borderColor, NodeCornerRadius, thickness);
        DrawNodePorts(node, rect);
    }

    /// <summary>
    /// 資產節點本體唯一的一列：像一般參數列那樣「標籤 + 下拉」，選的是「指到哪一個資產」。
    /// 換身分（Formula／Asset）是 Header 那顆 ▾ 的事，這裡只換對象。
    /// </summary>
    private void DrawReferencePickerRow(AGNode node, Rect nodeRect)
    {
        var row = new Rect(nodeRect.x, nodeRect.y + AGGraph.HeaderHeight, nodeRect.width, AGGraph.RowHeight);
        float labelWidth = row.width * 0.34f;

        bool isVariable = node.IsVariableNode;
        GUI.Label(new Rect(row.x + 6f, row.y + 1f, labelWidth - 8f, row.height - 2f),
            isVariable ? "變數" : "資產", AGStyles.RowLabel);

        var picker = new Rect(row.x + labelWidth, row.y + 1f, row.width - labelWidth - 8f, row.height - 3f);
        string label = isVariable
            ? (node.Endpoint != null ? node.Endpoint.Name ?? "（未命名）" : "（未指定）")
            : (node.Asset != null ? node.Asset.name : "（未指定）");

        if (!EditorGUI.DropdownButton(picker,
                AGStyles.Elide(label, EditorStyles.miniPullDown, picker.width - 20f), FocusType.Keyboard)) return;

        if (isVariable) ShowVariablePicker(node, picker);
        else ShowAssetPicker(node, picker);
    }

    /// <summary>只列這個欄位收得下的資產。</summary>
    private void ShowAssetPicker(AGNode node, Rect anchor)
    {
        var options = new List<AGSourceOption>();
        foreach (var entry in AGAssetIndex.Entries)
        {
            if (entry.Asset == null || !CanReplaceAssetNode(node, entry.Asset)) continue;
            var asset = entry.Asset;
            options.Add(new AGSourceOption
            {
                Name = entry.Name,
                IsCurrent = node.Asset == asset,
                Apply = () => ChangeNodeToAsset(node, asset),
            });
        }

        if (options.Count == 0)
        {
            ShowNotification(new GUIContent("沒有相容的共用資產"));
            return;
        }
        AGTypeCatalog.ShowSourcePicker(anchor, options, "選擇資產");
    }

    /// <summary>外框完成後最後畫接點；圓點完整位於 Node 內側。</summary>
    private void DrawNodePorts(AGNode node, Rect nodeRect)
    {
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            // 折疊的清單：子列的接點會全部疊在標題列上，所以只在標題列畫一顆代表「裡面有連線」，
            // 沒有它的話連線會停在節點邊緣的空白處，看起來像斷掉。
            if (row.Kind == AGRowKind.List && row.Collapsed)
            {
                if (!HasConnectedElement(row)) continue;
                AGStyles.Port(PortRectOf(row, nodeRect), AGStyles.PortLive);
                continue;
            }
            if (!row.IsLinkable) continue;
            AGStyles.Port(PortRectOf(row, nodeRect), SlotPortColor(row));
        }

        if (node.IsRoot) return;
        AGStyles.Port(new Rect(nodeRect.x, nodeRect.y + AGGraph.HeaderHeight * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter), AGStyles.PortLive);
    }

    /// <summary>
    /// 接點圓的位置。**永遠貼齊節點右緣**，不因為那一列是不是清單元素而縮排——
    /// 所有接點排成一條垂直線是這張圖的基本語彙，讓開刪除鈕的是 ✕ 自己（它排到接點左邊）。
    /// </summary>
    private static Rect PortRectOf(AGRow row, Rect nodeRect)
        => new Rect(nodeRect.xMax - AGGraph.PortDiameter,
            nodeRect.y + row.LocalY + row.Height * 0.5f - AGGraph.PortRadius,
            AGGraph.PortDiameter, AGGraph.PortDiameter);

    /// <summary>折疊的清單裡有沒有已經接上來源的元素。</summary>
    private static bool HasConnectedElement(AGRow listRow)
    {
        foreach (var child in AGGraph.AllRows(listRow.Children))
            if (child.Kind == AGRowKind.Slot && child.Slot != null && AGReflect.GetNode(child.Slot) != null) return true;
        return false;
    }

    private Color SlotPortColor(AGRow row)
    {
        // 從 Node 發點拉線時，收得下它的欄位接點先亮起來，使用者不用逐一試。
        if (linking && linkNode != null && linkCompatibleRows.Contains(row)) return AGStyles.Link;

        bool hasIssue = Rep.HasIssue(row.Slot, out bool isError);
        int useType = AGReflect.UseType(row.Slot);
        return hasIssue && isError ? AGStyles.PortError
            : useType == 1 || useType == 2 ? AGStyles.PortLive
            : AGStyles.PortEmpty;
    }

    /// <summary>把每一列的圖面座標（命中測試與接點）更新成目前的節點位置。</summary>
    private static void UpdateRowGeometry(AGNode node, List<AGRow> rows)
    {
        foreach (var row in rows)
        {
            row.ScreenRect = new Rect(node.Pos.x, node.Pos.y + row.LocalY, node.Width, row.Height);
            row.PortPos = new Vector2(node.Pos.x + node.Width - AGGraph.PortRadius,
                node.Pos.y + row.LocalY + row.Height * 0.5f);
            UpdateRowGeometry(node, row.Children);
        }
    }

    private void ZoomAt(Vector2 clipMouse, float wheelDelta)
    {
        float nextZoom = Mathf.Clamp(zoom - wheelDelta * 0.03f, 0.45f, 1.8f);
        if (Mathf.Approximately(nextZoom, zoom)) return;

        // 固定滑鼠下的 Graph 點，縮放期間連線起點與預覽終點都不漂移。
        Vector2 anchor = clipMouse / zoom - pan;
        zoom = nextZoom;
        pan = clipMouse / zoom - anchor;
    }
}

}
