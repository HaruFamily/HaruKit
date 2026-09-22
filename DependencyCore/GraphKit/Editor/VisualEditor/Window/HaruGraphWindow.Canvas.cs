namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 中欄畫布：焦點標頭、zoom group、格線、連線與節點本體繪製。
/// </summary>
public partial class HaruGraphWindow
{
    // ===== 中欄 =====

    private void DrawCenter(Rect r)
    {
        HGStyles.Fill(r, HGStyles.Canvas);

        RefreshExecutionSource();
        var header = new Rect(r.x, r.y, r.width, HeaderHeight);
        float executionHeight = executionSource != null || !string.IsNullOrEmpty(executionReadError) ? HGExecutionPanel.Height : 0f;
        float headingHeight = HeaderHeight + executionHeight;

        float consoleH = console.LayoutHeight(r.height - headingHeight - 80f);
        canvasRect = new Rect(r.x, r.y + headingHeight, r.width, r.height - headingHeight - consoleH);
        var consoleRect = new Rect(r.x, canvasRect.yMax, r.width, consoleH);
        var consoleHandle = new Rect(consoleRect.x, consoleRect.y - 3f, consoleRect.width, ResizeHandleWidth);

        if (console.HandleResize(consoleHandle, position.height - 240f)) Repaint();

        DrawCanvas(canvasRect);
        HGFocusHeaderPanel.Draw(header, FocusHeaderView(), inlineName);
        if (executionHeight > 0f) DrawExecutionPanel(new Rect(r.x, header.yMax, r.width, executionHeight));
        console.Draw(consoleRect, ConsoleView(), JumpTo);
        DrawResizeGrip(consoleHandle, false, console.IsResizing);
    }

    /// <summary>
    /// 把焦點狀態打包成資訊列的一次性快照。四種型態各有自己的版面，所以型態要明講。
    /// </summary>
    private HGFocusHeaderView FocusHeaderView()
    {
        if (focus.Kind == HGFocusKind.Asset)
        {
            if (focus.Token != null)
                return new HGFocusHeaderView
                {
                    Kind = HGFocusHeaderKind.AssetToken,
                    NameTarget = focus.Token,
                    NameDisplay = focus.Token.Name ?? "",
                    NameTooltip = "雙擊可改名",
                    NameSubmit = name => RenameFocusToken(focus.Token, name),
                    Title = focus.Title,
                };

            var asset = focus.AssetObject;
            if (asset != null)
                return new HGFocusHeaderView
                {
                    Kind = HGFocusHeaderKind.Asset,
                    NameTarget = asset,
                    NameDisplay = asset.name,
                    NameTooltip = "雙擊可改名（改的是 .asset 檔名）",
                    NameSubmit = name => RenameAssetFile(asset, name),
                };

            return new HGFocusHeaderView { Kind = HGFocusHeaderKind.Asset, Title = focus.Title };
        }

        if (focus.Kind == HGFocusKind.Token)
            return new HGFocusHeaderView
            {
                Kind = HGFocusHeaderKind.Token,
                NameTarget = focus.Token,
                NameDisplay = focus.Token?.Name ?? "",
                NameTooltip = "雙擊可改名",
                NameSubmit = name => RenameFocusToken(focus.Token, name),
                Description = focus.Token?.Slot?.Node == null
                    ? "沒接來源＝具名常數，值直接填在 HEAD 的來源欄位。"
                    : "這個 Token 的值由下面這棵子樹算出來。外部用它的名字查值。",
            };

        if (focus.Kind == HGFocusKind.Action && focus.ActionSlot != null)
        {
            var slot = focus.ActionSlot;
            var formula = HGReflect.GetFormula(slot);
            string desc = formula != null
                ? HGReflect.TypeDescription(formula.GetType())
                : "這個動作還沒有內容，請從根節點下拉選擇。";
            if (HGReflect.GetDisabled(slot)) desc += "　（已停用，不會執行）";

            return new HGFocusHeaderView
            {
                Kind = HGFocusHeaderKind.Action,
                NameTarget = slot,
                NameDisplay = focus.Title,
                NameTooltip = "雙擊可改名",
                NameSubmit = name =>
                {
                    HGReflect.SetLabel(slot, name);
                    Invalidate();
                    return true;
                },
                Description = desc,
            };
        }

        return new HGFocusHeaderView
        {
            Kind = HGFocusHeaderKind.Plain,
            Title = focus.Title,
            Description = PlainFocusDescription(),
        };
    }

    private string PlainFocusDescription()
    {
        if (focus.Kind == HGFocusKind.Root)
        {
            int groups = 0, actions = 0;
            foreach (var g in model.ReadRootGroups())
            {
                groups++;
                actions += g.Items?.Count ?? 0;
            }
            return groups > 0
                ? $"{groups} 個{RootNoun}、{actions} 個動作。{RootNoun}節點可自由擺位；跨{RootNoun}共用來源直接拉線即可。"
                : $"還沒有任何{RootNoun}節點。在畫布空白處按右鍵新增一個。";
        }
        if (focus.Kind == HGFocusKind.None)
            return $"從右上角的{RootNoun}下拉跳到某個{RootNoun}，或從左欄選一個 Token 開始編輯。";
        return "";
    }

    /// <summary>Token畫布的標題就地改名。名字是外部查詢的 key，改名不影響圖內連線（那是物件參照）。</summary>
    private bool RenameFocusToken(GraphToken endpoint, string name)
    {
        if (endpoint == null) return false;
        if (model.RenameToken(endpoint, name, CurrentTokens(), out string error))
        {
            MarkGraphChanged();
            return true;
        }
        ShowNotification(new GUIContent(error));
        return false;
    }

    // ===== 畫布 =====

    private void DrawCanvas(Rect r)
    {
        HGStyles.Fill(r, HGStyles.Canvas);
        DrawGrid(r);

        var e = Event.current;
        Vector2 clipMouse = e.mousePosition - r.position;
        HandleLinkNavigation(e, clipMouse);
        Vector2 graphMouse = clipMouse / zoom - pan;
        bool mouseInCanvas = r.Contains(e.mousePosition);
        var headerActionsNode = UpdateHeaderActions(graphMouse, mouseInCanvas);
        bool overHeaderActions = headerActionsNode != null
            && HeaderActionsRect(headerActionsNode).Contains(graphMouse) && mouseInCanvas;

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
                HGPort snappedPort = SnappedCompatiblePort(graphMouse);
                HGNodeView linkTarget = OwnerNodeOfPort(snappedPort);
                EventType pointerEvent = e.type;
                bool shieldPointer = overHeaderActions && (e.isMouse || e.type == EventType.ScrollWheel);
                // 底層包含直接讀 Event 的列控制項，不能只靠 GUI.enabled 防止穿透。
                if (shieldPointer) e.type = EventType.Ignore;
                try
                {
                    foreach (var node in graph.Nodes)
                    {
                        if (node.Hidden) continue;
                        DrawNode(node, ReferenceEquals(node, linkTarget), snappedPort);
                    }
                    DrawExtensionPorts(snappedPort);
                    DrawEmptyTimingHint();
                }
                finally
                {
                    if (shieldPointer) e.type = pointerEvent;
                }
                if (boxSelecting)
                {
                    var box = BoxRect();
                    var visual = new Rect(box.position + pan, box.size);
                    HGStyles.Fill(visual, new Color(0.42f, 0.78f, 1f, 0.10f));
                    HGStyles.Frame(visual, HGStyles.Link);
                }
                if (headerActionsNode != null) DrawHeaderActions(headerActionsNode);
                // 浮動工具列空白處也攔截指標事件，避免操作穿透到下方節點或畫布。
                if (overHeaderActions && (e.isMouse || e.type == EventType.ScrollWheel)) e.Use();
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
            Handles.color = HGStyles.Grid;
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
                if (!IsLinkVisible(link)) continue;
                if (IsTracedLink(link) != tracedPass) continue;
                // 停用子樹的線一起壓暗，才看得出整段路徑都不會被求值。
                DrawGraphLine(link.InputPort.Presentation.Position, link.OutputPort.Presentation.Position,
                    link.OutputOwner.InDisabledSubtree || link.OutputOwner.InLockedSubtree, tracedPass,
                    link.ParentRow.IsProducedValue);
            }
        }
        if (linking && linkPort != null)
        {
            bool producedValue = IsPropertyWritePort(linkPort);
            DrawGraphLine(linkPort.Presentation.Position, LinkPreviewEnd(graphMouse), false, false, producedValue);
        }
        Handles.EndGUI();
    }

    /// <summary>
    /// 這條線接在選取的節點上：兩端任一端被選取就算。純視覺，不改資料也不影響命中測試。
    /// </summary>
    private bool IsTracedLink(HGLink link)
        => selectedIds.Count > 0
            && (selectedIds.Contains(link.OutputOwner.Id) || selectedIds.Contains(link.ParentRow.OwnerNodeId));

    /// <summary>
    /// 一顆時機節點都還沒有時的入口。有節點之後就不再出現——刻意不在開窗時自動建第一個時機，
    /// 那會在使用者還沒編輯前就把資產標成未存檔。
    /// </summary>
    private void DrawEmptyTimingHint()
    {
        if (focus.Kind != HGFocusKind.Root || graph.Nodes.Count > 0) return;

        var rect = new Rect(new Vector2(40f, 40f) + pan, new Vector2(HGGraph.NodeWidth, HGGraph.HeaderHeight + 4f));
        HGStyles.RoundedFill(rect, HGStyles.NodeBody, NodeCornerRadius);
        HGStyles.RoundedFrame(rect, HGStyles.NodeBorder, NodeCornerRadius, 1f);
        if (GUI.Button(rect, $"＋ 新增第一個{RootNoun}節點", HGStyles.ListAdd))
            ShowAddTimingMenu(new Vector2(40f, 40f));
    }

    private const float TimingOverlayWidth = 190f;

    /// <summary>
    /// 畫布右上角的 root 下拉：所有 root 都在同一張畫布上，所以它是「跳到哪一顆」而不是「切換畫布」。
    /// 選到還沒建立的就在畫面中央建一顆。和說明面板一樣畫在 zoom clip 外，縮到 0.45 也讀得到。
    /// </summary>
    private void DrawTimingOverlay(Rect canvas)
    {
        if (model == null) return;

        var r = new Rect(canvas.xMax - TimingOverlayWidth - 8f, canvas.y + 8f, TimingOverlayWidth, 22f);
        if (EditorGUI.DropdownButton(r, new GUIContent(RootNoun, $"跳到某個{RootNoun}節點，或新增一個"), FocusType.Keyboard))
            ShowTimingMenu(CanvasCenterInGraph());
    }

    /// <summary>畫布中心的 graph 座標：從下拉新增的時機節點放這裡，使用者才看得到它。</summary>
    private Vector2 CanvasCenterInGraph()
        => new Vector2(canvasRect.width * 0.5f / zoom - pan.x - HGGraph.NodeWidth * 0.5f,
                       canvasRect.height * 0.5f / zoom - pan.y);

    private const float InfoOverlayWidth = 300f;

    /// <summary>
    /// 畫布左上角的說明面板：型別說明是型別常數，畫在每個節點上只是重複噪音，改成只顯示目前選取節點的說明。
    /// 畫在 zoom clip 外，所以不隨縮放改變大小；純顯示，不吃滑鼠事件。
    /// </summary>
    private void DrawNodeInfoOverlay(Rect canvas)
    {
        if (graph == null || selectedIds.Count != 1) return;

        HGNodeView node = null;
        foreach (var n in graph.Nodes)
        {
            if (!selectedIds.Contains(n.Id)) continue;
            node = n;
            break;
        }
        if (node == null) return;

        // 有物件卻沒有說明＝作者忘了寫 [HGNodeView] 描述，直接講出來，不要靜默留白。
        string desc = !string.IsNullOrWhiteSpace(node.Desc) ? node.Desc
            : node.Obj != null ? "（這個型別沒有 [HGNodeView] 說明）"
            : null;

        float width = Mathf.Min(InfoOverlayWidth, canvas.width - 16f);
        if (width < 80f) return;

        float textWidth = width - 16f;
        float descHeight = desc == null ? 0f : HGStyles.NodeDesc.CalcHeight(new GUIContent(desc), textWidth);
        var panel = new Rect(canvas.x + 8f, canvas.y + 8f, width, 20f + descHeight + 10f);

        HGStyles.RoundedFill(panel, new Color(0.10f, 0.11f, 0.13f, 0.88f), 4f);
        HGStyles.RoundedFrame(panel, HGStyles.NodeBorder, 4f);
        GUI.Label(new Rect(panel.x + 2f, panel.y + 4f, textWidth, 18f),
            HGStyles.Elide(node.Title, HGStyles.OverlayTitle, textWidth), HGStyles.OverlayTitle);
        if (desc != null)
            GUI.Label(new Rect(panel.x + 2f, panel.y + 22f, textWidth, descHeight), desc, HGStyles.NodeDesc);
    }

    /// <summary>graph space → window space；zoom clip 外要用視窗座標的地方（選單錨點、直線）走這裡。</summary>
    private Rect GraphToWindowRect(Rect graphRect)
        => new Rect(canvasRect.position + (graphRect.position + pan) * zoom, graphRect.size * zoom);

    private void DrawGraphLine(Vector2 graphFrom, Vector2 graphTo, bool dim = false, bool traced = false,
        bool output = false)
    {
        Vector2 from = canvasRect.position + (graphFrom + pan) * zoom;
        Vector2 to = canvasRect.position + (graphTo + pan) * zoom;
        if (!ClipLine(canvasRect, ref from, ref to)) return;

        // 顏色表達「這條線接的是選取中的節點」，其次是「這條是輸出不是取值」；
        // 透明度仍歸停用管——三件事互不覆蓋，選取最優先。
        Color color = traced ? HGStyles.NodeBorderSelected : output ? HGStyles.OutputPortColor : Color.white;
        if (dim) color.a *= HGStyles.LinkDisabled.a;

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
    /// 鉛筆只在操作區展開時顯示，與 Enable 的實心／空心圓區分。
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

    private static bool DrawNoteToggle(Rect r, bool open, bool hasNote, bool visible)
    {
        if (visible)
        {
            if (open) HGStyles.RoundedFill(r, HGStyles.HeaderOverlay, 2f);
            GUI.Label(r, "✎", open || hasNote ? HGStyles.HeaderButton : HGStyles.HeaderButtonDim);
        }
        using (new EditorGUI.DisabledScope(!visible))
            return GUI.Button(r, new GUIContent("", !visible ? "" :
                open ? "收起註解（內容保留）" : hasNote ? "展開註解" : "加上註解"), GUIStyle.none);
    }

    /// <summary>工具列以實心／空心圓表示啟用狀態。</summary>
    private static bool DrawEnableToggle(Rect r, bool enabled, int users, bool visible)
    {
        if (visible) GUI.Label(r, enabled ? "●" : "○", HGStyles.HeaderButton);
        // 載體是共用單位，停用一顆被多個欄位指著的節點會同時影響全部引用處，講清楚才不會變成遠端的靜默行為。
        string tip = !enabled
            ? (users > 1 ? $"已停用：{users} 個欄位改用保底值。點一下啟用" : "已停用：引用它的欄位改用保底值。點一下啟用")
            : (users > 1 ? $"停用這顆節點（{users} 個欄位會一起改用保底值）" : "停用這顆節點，引用它的欄位改用保底值");
        using (new EditorGUI.DisabledScope(!visible))
            return GUI.Button(r, new GUIContent("", visible ? tip : ""), GUIStyle.none);
    }

    private static bool DrawHoldToggle(Rect rect, bool held, bool waiting, bool visible, bool canToggle)
    {
        if (visible)
        {
            if (waiting) HGStyles.RoundedFill(rect, HGStyles.Warning, 2f);
            GUI.Label(rect, held ? "▶" : "Ⅱ", held ? HGStyles.HeaderButton : HGStyles.HeaderButtonDim);
        }
        string tooltip = held ? "解除 Hold；已抵達的呼叫繼續執行" : "設定 Hold；抵達此節點時先等待";
        if (!canToggle) tooltip = "請在 Play Mode 選擇可控制的執行鏈，並使用相符的已儲存文件。";
        using (new EditorGUI.DisabledScope(!visible || !canToggle))
            return GUI.Button(rect, new GUIContent("", visible ? tooltip : ""), GUIStyle.none);
    }

    private Rect HeaderActionsRect(HGNodeView node)
    {
        int count = 1;
        if (node.Carrier != null) count += executionSource != null ? 2 : 1;
        float width = count * 20f + 8f;
        return new Rect(node.Pos.x + node.Width - width, node.Pos.y - 22f, width, 22f);
    }

    private HGNodeView UpdateHeaderActions(Vector2 graphMouse, bool mouseInCanvas)
    {
        string previous = headerActionsNodeId;
        HGNodeView headerActionsNode = null;
        if (graph != null && mouseInCanvas && Event.current.type != EventType.MouseLeaveWindow
            && !linking && dragNode == null && !boxSelecting)
        {
            // 工具列緊貼 Header，游標跨越邊界時不會經過讓它收起的空隙。
            if (previous != null)
            {
                foreach (var node in graph.Nodes)
                {
                    if (node.Hidden || node.IsRoot || node.Id != previous) continue;
                    if (HeaderActionsRect(node).Contains(graphMouse) || GUIUtility.hotControl != 0)
                        headerActionsNode = node;
                    break;
                }
            }
            if (headerActionsNode == null)
            {
                // 與節點繪製順序一致，重疊時只讓最上層節點取得滑入狀態。
                foreach (var node in graph.Nodes)
                {
                    if (node.Hidden) continue;
                    var body = new Rect(node.Pos, new Vector2(node.Width, node.Height));
                    if (!body.Contains(graphMouse)) continue;
                    var header = new Rect(node.Pos, new Vector2(node.Width,
                        HGGraph.HeaderHeight - HGNodeStatus.StripHeight));
                    headerActionsNode = !node.IsRoot && header.Contains(graphMouse) ? node : null;
                }
            }
        }
        headerActionsNodeId = headerActionsNode?.Id;
        if (previous != headerActionsNodeId || Event.current.type == EventType.MouseMove) Repaint();
        return headerActionsNode;
    }

    private void DrawHeaderActions(HGNodeView node)
    {
        var area = HeaderActionsRect(node);
        area.position += pan;
        var background = HGStyles.NodeBody;
        background.a = 0.85f;
        HGStyles.RoundedFill(area, background, 4f);
        float right = area.xMax - 7f;
        if (node.Carrier != null)
        {
            if (executionSource != null)
            {
                var holdToggle = new Rect(right - 14f, area.y + 4f, 14f, 14f);
                bool held = NodeHasHold(node);
                var execution = NodeExecution(node);
                bool canToggle = held || !string.IsNullOrEmpty(node.Carrier.Id) && ExecutionMatchesView
                    && EditorApplication.isPlaying && selectedExecution?.IsFinished != true;
                if (DrawHoldToggle(holdToggle, held, execution.HasValue && execution.Value.Waiting > 0, true, canToggle))
                    ToggleNodeHold(node);
                right -= 20f;
            }
            var enableToggle = new Rect(right - 14f, area.y + 4f, 14f, 14f);
            if (DrawEnableToggle(enableToggle, !node.Carrier.Disabled, CarrierUsers(node.Carrier), true))
            {
                BreakUndoMerge();
                model.SetNodeDisabled(node.Id, !node.Carrier.Disabled);
                Invalidate();
                Repaint();
            }
            right -= 20f;
        }
        var noteToggle = new Rect(right - 14f, area.y + 4f, 14f, 14f);
        if (!DrawNoteToggle(noteToggle, node.NoteOpen, !string.IsNullOrWhiteSpace(node.Tips), true)) return;
        if (node.NoteOpen)
        {
            ReleaseNoteFocus(node.Id);
            noteCollapsed.Add(node.Id);
            noteOpenId = null;
        }
        else
        {
            // 空註解框需保持節點選取，才不會被下一幀的自動收合移除。
            noteCollapsed.Remove(node.Id);
            noteOpenId = node.Id;
            selectedIds.Add(node.Id);
        }
        graphDirty = true;
        Repaint();
    }

    /// <summary>
    /// Header 底色：HEAD 深紫紅、Action 洋紅、Formula 琥珀、Asset 靛藍、Token 深綠。
    /// 容器型節點用漸層表達「容器 → 它承載的東西」：Action 型資產是靛藍→洋紅，Token是深綠→結果型別色。
    /// </summary>
    private static void HeaderColors(HGNodeView node, out Color from, out Color to)
    {
        // HEAD 從流程入口深紫紅漸層到目前焦點可接的內容色。
        if (node.IsRoot)
        {
            from = HGStyles.HeaderHead;
            to = node.IsActionNode ? HGStyles.HeaderAction : HGStyles.HeaderFormula;
            return;
        }
        if (node.IsTokenNode)
        {
            from = HGStyles.HeaderToken;
            to = HGStyles.HeaderFormula;
            return;
        }
        // Property 色 → 公式色：節點本身是引用，但它對下游提供的是一個型別化的值，兩種身分都要看得出來。
        if (node.IsPropertyNode)
        {
            from = HGStyles.HeaderProperty;
            to = HGStyles.HeaderFormula;
            return;
        }
        if (node.IsAssetNode)
        {
            from = HGStyles.HeaderAsset;
            to = node.ResultType == null ? HGStyles.HeaderAction : HGStyles.HeaderFormula;
            return;
        }
        // 被寫入的節點（IGraphSink）仍是求值得出結果的公式。
        if (node.Obj is IGraphSink)
        {
            from = HGStyles.HeaderFormula;
            to = HGStyles.HeaderFormula;
            return;
        }
        from = to = node.IsActionNode ? HGStyles.HeaderAction : HGStyles.HeaderFormula;
    }

    private void DrawNode(HGNodeView node, bool isLinkTarget, HGPort snappedPort)
    {
        var rect = new Rect(node.Pos + pan, new Vector2(node.Width, node.Height));

        HGStyles.RoundedFill(rect, HGStyles.NodeBody, NodeCornerRadius);
        var header = new Rect(rect.x, rect.y, rect.width, HGGraph.HeaderHeight);
        HeaderColors(node, out Color headerFrom, out Color headerTo);
        HGStyles.HeaderFill(header, headerFrom, headerTo, NodeCornerRadius);

        // 固定保留操作區寬度；Hover／選取只改可見性，不讓名稱與 chip 在游標下移動。
        // 節點問題與執行狀態共用 Header 下緣色帶；參數列問題仍標在列上。
        // 資產／空節點自己沒有物件，問題掛在父欄位上，改查父欄位才看得到。
        object issueTarget = node.Obj
            ?? (node.IsAssetNode || node.IsTokenNode || node.IsPropertyNode || node.IsPlaceholder
                ? node.ParentSlot : null);
        bool hasNodeIssue = Rep.HasIssue(issueTarget, out bool nodeError);
        var execution = NodeExecution(node);

        float headerRight = rect.xMax - 4f;

        if (!node.IsRoot)
        {
            var expand = new Rect(headerRight - 14f, rect.y + 3f, 14f, 14f);
            if (headerActionsNodeId == node.Id) HGStyles.RoundedFill(expand, HGStyles.HeaderOverlay, 2f);
            GUI.Label(expand, new GUIContent("▴", "滑入 Header 展開工具列"), HGStyles.HeaderButton);
            headerRight = expand.x - 3f;
        }

        if (!string.IsNullOrEmpty(node.Chip))
        {
            float chipWidth = Mathf.Min(HGStyles.NodeChip.CalcSize(new GUIContent(node.Chip)).x, 96f);
            var chipRect = new Rect(headerRight - chipWidth, rect.y + 3f, chipWidth, 14f);
            HGStyles.RoundedFill(chipRect, HGStyles.HeaderOverlay, 3f);
            GUI.Label(chipRect, HGStyles.Elide(node.Chip, HGStyles.NodeChip, chipWidth), HGStyles.NodeChip);
            headerRight = chipRect.x - 2f;
        }

        // 左端只讓開輸出接點；右端保留工具列提示與 chip。
        float titleInset = node.IsRoot || !node.HasOutputPort ? 0f : HGGraph.PortDiameter + 2f;

        float titleWidth = Mathf.Max(24f, headerRight - rect.x - titleInset);
        // Header 下緣固定留給狀態帶；名稱與來源按鈕的繪製、命中共用上方內容區。
        float headerContentHeight = HGGraph.HeaderHeight - HGNodeStatus.StripHeight;
        var titleRect = new Rect(rect.x + titleInset, rect.y, titleWidth, headerContentHeight);
        // 命中測試在 zoom clip 外做，所以存 graph space。
        node.TitleRect = new Rect(titleRect.position - pan, titleRect.size);

        float textWidth = titleWidth;
        node.SourceMenuRect = new Rect();
        if (node.HasSourceSelector)
        {
            // 只有右端這顆 ▾ 是換來源的按鈕，名稱區其餘部分留給拖曳。
            // 整塊可按會讓「想搬節點」變成「開了選單」——Header 本來就是唯一的拖曳抓取區。
            // 空節點也一樣，不再例外：它同樣要能被拖著擺位。
            var lift = HGStyles.HeaderOverlay;
            var arrow = new Rect(titleRect.xMax - SourceArrowWidth, titleRect.y + 2f,
                SourceArrowWidth, titleRect.height - 4f);
            var hot = arrow;
            HGStyles.RoundedFill(hot, new Color(lift.r, lift.g, lift.b, lift.a * 0.65f), 3f);
            GUI.Label(arrow, new GUIContent("▾", node.IsPlaceholder ? "選擇來源" : "換來源"), HGStyles.HeaderButton);
            // 命中測試在 zoom clip 外做，所以存 graph space。
            node.SourceMenuRect = new Rect(hot.position - pan, hot.size);
            textWidth = titleWidth - SourceArrowWidth - 2f;
        }
        // 色塊帶是底圖、沒有自己的熱區，問題提示併進名稱區：整條名稱都是可滑到的地方。
        string titleTip = !hasNodeIssue ? null
            : nodeError ? "此節點有錯誤，詳見 Console" : "此節點有警告，詳見 Console";
        GUI.Label(titleRect, HGStyles.Elide(node.Title, HGStyles.NodeTitle, textWidth, titleTip), HGStyles.NodeTitle);

        // 資產、Token 與 ProtoProperty 的本體是一列「選哪一個」的下拉。
        // 一般 Property 是在這顆節點建立的私有暫存位置，沒有可重指向的名稱清單。
        // 掛在未勾覆蓋的參數底下＝這一段不會被採用，整顆節點鎖住：控制項灰掉、拉線與清單編輯都擋掉。
        using (new EditorGUI.DisabledScope(node.InLockedSubtree))
        {
            if (node.IsPropertyNode)
            {
                if (node.Carrier?.IsProtoProperty == true) DrawReferencePickerRow(node, rect);
                DrawPropertyValueRow(node, rect);
                DrawRows(node, node.Rows, rect);
            }
            else if (node.IsAssetNode || node.IsTokenNode)
            {
                DrawReferencePickerRow(node, rect);
                DrawRows(node, node.Rows, rect);
            }
            else if (node.Obj is IGraphNodeOwner nodeOwner)
            {
                DrawRows(node, node.Rows, rect);
                DrawChildNodeRow(node, nodeOwner, rect);
            }
            else if (!node.IsPlaceholder) DrawRows(node, node.Rows, rect);
        }

        if (node.TipsHeight > 0f)
        {
            float noteTop = rect.y + node.ContentHeight - node.TipsHeight - 4f;
            var tipsField = new Rect(rect.x + 8f, noteTop, rect.width - 16f, node.TipsHeight);
            var noteRect = new Rect(rect.x + 4f, noteTop - 3f, rect.width - 8f, node.TipsHeight + 6f);
            HGStyles.Fill(noteRect, HGStyles.NodeNote);
            HGStyles.Frame(noteRect, HGStyles.NodeNoteBorder);
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

        // 暗紗蓋在內容之上、問題色條之下：不會求值的節點要一眼看出來，但它的錯誤與警告仍然要讀得到。
        // 暗紗只是貼圖：停用（Disabled）仍可編輯，鎖定（未勾覆蓋的參數底下）才由 DisabledScope 擋掉輸入。
        if (node.InDisabledSubtree || node.InLockedSubtree)
            HGStyles.RoundedFill(rect, HGStyles.DisabledVeil, NodeCornerRadius);

        var statusColor = HGNodeStatus.ColorOf(hasNodeIssue, nodeError, execution);
        if (statusColor.HasValue)
        {
            // 固定使用 Header 預留區，不因縮放向上侵入控制項。
            float stripHeight = HGNodeStatus.StripHeight;
            var statusBar = new Rect(rect.x + 1f, header.yMax - stripHeight, rect.width - 2f, stripHeight);
            HGStyles.Fill(statusBar, statusColor.Value);
            string statusTip = execution.HasValue ? HGNodeStatus.Describe(execution.Value) : "";
            if (hasNodeIssue) statusTip += nodeError ? "\n此節點有錯誤，詳見 Console" : "\n此節點有警告，詳見 Console";
            GUI.Label(statusBar, new GUIContent("", statusTip));
        }

        bool selected = selectedIds.Contains(node.Id);
        // 拉線期間：可以接的 Node 整個亮外框，滑鼠實際吸到的那個再加粗。
        bool linkCandidate = linking && IsCompatible(PortFor(node));
        Color borderColor = isLinkTarget ? HGStyles.Link
            : linkCandidate ? new Color(HGStyles.Link.r, HGStyles.Link.g, HGStyles.Link.b, 0.55f)
            : selected ? HGStyles.NodeBorderSelected
            : node.IsRoot ? HGStyles.HeadBorder : HGStyles.NodeBorder;
        float thickness = isLinkTarget || selected ? 2f : linkCandidate ? 1.5f : node.IsRoot ? 2f : 1f;

        // HEAD 是整張圖的起點，再套一圈外光暈把它和一般節點分開（顏色會被選取／拉線狀態蓋過，光暈不會）。
        if (node.IsRoot)
        {
            var halo = new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f);
            HGStyles.RoundedFrame(halo, new Color(HGStyles.HeadBorder.r, HGStyles.HeadBorder.g, HGStyles.HeadBorder.b, 0.35f),
                NodeCornerRadius + 3f, 1f);
        }

        HGStyles.RoundedFrame(rect, borderColor, NodeCornerRadius, thickness);
        DrawNodePorts(node, snappedPort);
    }

    /// <summary>
    /// 資產節點本體唯一的一列：像一般參數列那樣「標籤 + 下拉」，選的是「指到哪一個資產」。
    /// 換身分（Formula／Asset）是 Header 那顆 ▾ 的事，這裡只換對象。
    /// </summary>
    /// <summary>
    /// 子節點擁有者的本體第一列：顯示現在有幾格，右邊一顆「＋」加一格。
    /// </summary>
    private void DrawChildNodeRow(HGNodeView node, IGraphNodeOwner owner, Rect nodeRect)
    {
        var row = new Rect(nodeRect.x, nodeRect.y + HGGraph.HeaderHeight, nodeRect.width, HGGraph.RowHeight);
        float addWidth = 24f;

        int count = 0;
        foreach (var child in owner.ChildNodes)
            if (child != null) count++;

        GUI.Label(new Rect(row.x + 6f, row.y + 1f, row.width - addWidth - 14f, row.height - 2f),
            count == 0 ? "還沒有任何一格" : $"{count} 格", HGStyles.RowLabel);

        var addRect = new Rect(row.xMax - addWidth - 6f, row.y + 1f, addWidth, row.height - 3f);
        if (!GUI.Button(addRect, new GUIContent("＋", "新增一格"), EditorStyles.miniButton)) return;

        BreakUndoMerge();
        PreserveVisibleNodePositions();
        owner.CreateChild();
        Invalidate();
        MarkGraphChanged();
    }


    private void DrawReferencePickerRow(HGNodeView node, Rect nodeRect)
    {
        if (node.IsPropertyNode && node.Carrier?.IsProtoProperty != true) return;

        var row = new Rect(nodeRect.x, nodeRect.y + HGGraph.HeaderHeight, nodeRect.width, HGGraph.RowHeight);
        float labelWidth = row.width * 0.34f;

        bool isToken = node.IsTokenNode;
        bool isProperty = node.IsPropertyNode;
        // Property 這一列的標籤同時是身分標記：Header 顯示的是名稱，所以「有沒有初始內容」只剩這裡說得出來。
        string kindLabel = isToken ? "Token"
            : isProperty ? "Key"
            : "資產";
        GUI.Label(new Rect(row.x + 6f, row.y + 1f, labelWidth - 8f, row.height - 2f),
            HGStyles.Elide(kindLabel, HGStyles.RowLabel, labelWidth - 8f), HGStyles.RowLabel);

        var picker = new Rect(row.x + labelWidth, row.y + 1f, row.width - labelWidth - 8f, row.height - 3f);
        string label = isToken
            ? (node.Token != null ? node.Token.Name ?? "（未命名）" : "（未指定）")
            : isProperty
                ? (node.Property != null ? node.Property.Name ?? "（未命名）" : "（未指定）")
                : (node.Asset != null ? node.Asset.name : "（未指定）");

        if (!EditorGUI.DropdownButton(picker,
                HGStyles.Elide(label, EditorStyles.miniPullDown, picker.width - 20f), FocusType.Keyboard)) return;

        if (isToken) ShowTokenPicker(node, picker);
        else if (isProperty) ShowPropertyPicker(node, picker);
        else ShowAssetPicker(node, picker);
    }

    /// <summary>Property 寫入 Input 固定在這列左側；讀取 Output 仍在 Header。</summary>
    private static void DrawPropertyValueRow(HGNodeView node, Rect nodeRect)
    {
        float offset = node.Carrier?.IsProtoProperty == true ? HGGraph.RowHeight : 0f;
        var row = new Rect(nodeRect.x, nodeRect.y + HGGraph.HeaderHeight + offset, nodeRect.width, HGGraph.RowHeight);
        GUI.Label(new Rect(row.x + HGGraph.PortDiameter + 6f, row.y + 1f,
                row.width - HGGraph.PortDiameter - 12f, row.height - 2f),
            new GUIContent("寫入", "由左側 Input 指定寫入這顆 Property 的目標"), HGStyles.RowLabel);
    }

    /// <summary>列出目前變數庫的全部 ProtoProperty；換族時由交易中斷不相容的讀寫線。</summary>
    private void ShowPropertyPicker(HGNodeView node, Rect anchor)
    {
        var options = new List<HGSourceOption>();
        foreach (var property in CurrentProperties() ?? new List<GraphProperty>())
        {
            if (property == null) continue;
            if (!property.Proto) continue;
            var captured = property;
            options.Add(new HGSourceOption
            {
                Name = property.Name ?? "（未命名）",
                IsCurrent = ReferenceEquals(node.Property, property),
                Apply = () => ChangeNodeToProperty(node, captured),
            });
        }

        if (options.Count == 0)
        {
            ShowNotification(new GUIContent("變數庫尚無 ProtoProperty；先到左欄變數庫新增一顆"));
            return;
        }
        HGTypeCatalog.ShowSourcePicker(anchor, options, "選擇 Property");
    }

    /// <summary>換這顆節點指到的 Property。載體不變，所以 Id、座標與所有連入邊都保留。</summary>
    private void ChangeNodeToProperty(HGNodeView node, GraphProperty property)
    {
        if (node?.Carrier == null || property == null) return;
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        node.Carrier.SetProtoProperty(property);
        BreakIncompatiblePropertyLinks(node.Carrier, property);
        Invalidate();
        MarkGraphChanged();
        BreakUndoMerge();
    }

    /// <summary>只列這個欄位收得下的資產。</summary>
    private void ShowAssetPicker(HGNodeView node, Rect anchor)
    {
        var options = new List<HGSourceOption>();
        foreach (var entry in HGAssetIndex.Entries)
        {
            if (entry.Asset == null || !CanReplaceAssetNode(node, entry.Asset)) continue;
            var asset = entry.Asset;
            options.Add(new HGSourceOption
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
        HGTypeCatalog.ShowSourcePicker(anchor, options, "選擇資產");
    }

    /// <summary>外框完成後最後畫接點；圓點完整位於 Node 內側。</summary>
    private void DrawNodePorts(HGNodeView node, HGPort snappedPort)
    {
        bool dim = node.InDisabledSubtree || node.InLockedSubtree || node.Carrier?.Disabled == true;
        foreach (var row in HGGraph.AllRows(node.Rows))
        {
            // 折疊的清單：子列的接點會全部疊在標題列上，所以只在標題列畫一顆代表「裡面有連線」，
            // 沒有它的話連線會停在節點邊緣的空白處，看起來像斷掉。
            if (row.Kind == HGRowKind.List && row.Collapsed)
            {
                if (!HasConnectedElement(row)) continue;
                var aggregateKey = new HGPortKey(row.OwnerNodeId, row.Path, HGPortRole.Aggregate);
                if (!graph.PortsByKey.TryGetValue(aggregateKey, out var aggregate) || !aggregate.Presentation.Visible) continue;
                DrawSemanticPort(aggregate, AggregatePortColor(row), dim, snappedPort);
                continue;
            }
            var inputPort = PortFor(row);
            if (inputPort?.Presentation.Visible != true) continue;
            if (inputPort.Presentation is IHGPortPresentationAnchor anchor && !ReferenceEquals(anchor.Node, node)) continue;
            var inputPortRect = PortRect(inputPort.Presentation.Position + pan);
            DrawSemanticPort(inputPort, InputPortColor(row), dim || row.Locked || row.InputSlot.Node?.Disabled == true, snappedPort);
            if (row.InputSlot is not PropertySlotBase) DrawInputPortGlyph(row, inputPortRect);

        }

        if (node.IsRoot || !node.HasOutputPort) return;
        if (node.IsPropertyNode)
        {
            var inputKey = new HGPortKey(node.Id, "/property/input", HGPortRole.Input);
            if (graph.PortsByKey.TryGetValue(inputKey, out var propertyInput) && propertyInput.Presentation.Visible)
                DrawSemanticPort(propertyInput, HGStyles.OutputPortColor, dim, snappedPort);
        }
        var headerPort = PortFor(node);
        if (headerPort?.Presentation.Visible == true)
            DrawSemanticPort(headerPort, PortErrorColor(node.Obj ?? node.ParentSlot, HGStyles.OutputPortLive), dim, snappedPort);
    }

    /// <summary>Ports owned by Tool-specific adapters are drawn without adding a central concrete-type branch.</summary>
    private void DrawExtensionPorts(HGPort snappedPort)
    {
        foreach (var port in graph.Ports)
        {
            bool hasBuiltInAnchor = port.Presentation is IHGPortPresentationAnchor anchor
                && (anchor.Node != null || anchor.Row != null);
            if (hasBuiltInAnchor || !port.Presentation.Visible) continue;
            Color color = IsPropertyWritePort(port) ? HGStyles.OutputPortColor
                : port.IsInput && port.InputSlot?.Node == null ? HGStyles.InputPortEmpty : HGStyles.OutputPortLive;
            object issueTarget = port.IsInput ? port.InputSlot
                : port.Source?.OutputNode?.BodyObject;
            var owner = OwnerNodeOfPort(port);
            bool dim = port.Presentation.Locked || owner?.InDisabledSubtree == true
                || owner?.InLockedSubtree == true || port.Source?.OutputNode?.Disabled == true;
            DrawSemanticPort(port, PortErrorColor(issueTarget, color), dim, snappedPort);
        }
    }

    private bool IsPropertyWritePort(HGPort port)
    {
        // 寫入 Property 的欄位走輸出色，圖上才看得出資料往哪邊流。
        if (port?.Source is HGPropertyWriteSource || port?.InputSlot is GraphPropertyInputSlot) return true;
        return false;
    }

    private Color PortErrorColor(object target, Color color)
        => target != null && Rep.HasIssue(target, out bool error) && error ? HGStyles.InputPortError : color;

    private void DrawSemanticPort(HGPort port, Color color, bool dim, HGPort snappedPort)
    {
        var rect = PortRect(port.Presentation.Position + pan);
        // 錯誤色保持可讀；停用只壓暗用途色，不換另一種色相。
        bool hasError = color == HGStyles.InputPortError;
        if (dim && !hasError) color.a *= 0.45f;
        if (port.IsOutput) HGStyles.DrawOutputPort(rect, color);
        else HGStyles.DrawInputPort(rect, color);
        if (!linking || !IsCompatible(port)) return;

        // 相容與吸附只改外圈，中心保留灰白／寫入青藍／錯誤紅。
        float thickness = ReferenceEquals(port, snappedPort) ? 2f : 1f;
        // 固定圖面外擴量，Header 接點的圈不侵入下緣 20～24 的狀態帶。
        const float inset = 1f;
        var ring = new Rect(rect.x - inset, rect.y - inset, rect.width + inset * 2f, rect.height + inset * 2f);
        HGStyles.RoundedFrame(ring, hasError ? HGStyles.InputPortError : HGStyles.Link, ring.width * 0.5f, thickness);
    }

    /// <summary>
    /// 接了來源的接點兼收合開關：圓上疊 `+`／`-`，solo 再墊一層底。沒接來源的接點不畫字——
    /// 那種列沒有子樹可收，圓上乾乾淨淨剛好也說明「這裡只能拉線」。
    /// 不掛 tooltip：接點在滑鼠移動的必經路徑上，跳說明框只會擋住底下的圖。
    /// </summary>
    private void DrawInputPortGlyph(HGRow row, Rect inputPortRect)
    {
        if (row.InputSlot.Node == null) return;

        string key = HGGraph.CollapseKey(row.OwnerNodeId, row);
        bool solo = soloSlotKey == key;
        bool hidden = effectiveHidden.Contains(key);

        // solo 額外墊一層底：它和一般展開都顯示 -，靠底色分辨「只看這一段」。
        if (solo) HGStyles.RoundedFill(inputPortRect, HGStyles.HeaderOverlay, HGGraph.PortRadius);

        GUI.Label(inputPortRect, hidden && !solo ? "+" : "-", HGStyles.InputPortGlyph);
    }

    /// <summary>折疊的清單裡有沒有已經接上來源的元素。</summary>
    private static bool HasConnectedElement(HGRow listRow)
    {
        foreach (var child in HGGraph.AllRows(listRow.Children))
            if (child.HasSlot && child.InputSlot.Node != null) return true;
        return false;
    }

    private Color InputPortColor(HGRow row)
    {
        bool hasIssue = Rep.HasIssue(row.InputSlot, out bool isError);
        if (hasIssue && isError) return HGStyles.InputPortError;
        // 輸出接點不分空／接：它的顏色是在講方向，接上與否看得到線。
        if (row.IsProducedValue) return HGStyles.OutputPortColor;
        return row.InputSlot.Node != null
            ? HGStyles.InputPortLive
            : HGStyles.InputPortEmpty;
    }

    private Color AggregatePortColor(HGRow listRow)
    {
        bool allWrites = true;
        foreach (var child in HGGraph.AllRows(listRow.Children))
        {
            if (!child.HasSlot) continue;
            if (Rep.HasIssue(child.InputSlot, out bool error) && error) return HGStyles.InputPortError;
            if (child.InputSlot.Node != null && !child.IsProducedValue) allWrites = false;
        }
        return allWrites ? HGStyles.OutputPortColor : HGStyles.InputPortLive;
    }

    /// <summary>把每一列的圖面座標（命中測試與接點）更新成目前的節點位置。</summary>
    private static void UpdateRowGeometry(HGNodeView node, List<HGRow> rows)
    {
        foreach (var row in rows)
        {
            row.ScreenRect = new Rect(node.Pos.x, node.Pos.y + row.LocalY, node.Width, row.Height);
            row.InputPortPosition = new Vector2(node.Pos.x + node.Width - HGGraph.PortRadius,
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
