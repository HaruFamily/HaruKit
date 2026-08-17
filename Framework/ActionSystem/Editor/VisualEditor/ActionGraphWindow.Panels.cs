namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 左欄變數／資產庫、右欄時機與動作清單、資產引用清單，以及底部 Console。
/// </summary>
public partial class ActionGraphWindow
{
    // ===== 左欄：Token／Asset 庫 =====

    private void DrawLibraryPanel(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Panel);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        bool inAsset = focus.Kind == AGFocusKind.Asset;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 18f), "資料庫", AGStyles.PanelHeader);

        float tabWidth = (r.width - 8f) * 0.5f;
        if (DrawTab(new Rect(r.x + 4f, r.y + 22f, tabWidth, 22f), "變數", libraryTab == 0, AGStyles.HeaderToken)) libraryTab = 0;
        if (DrawTab(new Rect(r.x + 4f + tabWidth, r.y + 22f, tabWidth, 22f), "資產", libraryTab == 1, AGStyles.HeaderAsset)) libraryTab = 1;

        if (libraryTab == 0) DrawTokenLibrary(r, r.y + 48f, inAsset);
        else DrawAssetLibrary(r, r.y + 48f);
    }

    /// <summary>
    /// 標註索引。標註化之後這裡不再是 CRUD 面板：建立與取消都在節點右鍵，
    /// 這一欄只負責「有哪些對外端點」與「點了跳過去」。
    /// </summary>
    private void DrawTokenLibrary(Rect r, float top, bool inAsset)
    {
        GUI.Label(new Rect(r.x + 6f, top, r.width - 12f, 16f),
            new GUIContent("標註（對外端點）", "在節點上按右鍵「註冊為 Token」建立；點清單跳到那顆節點"), AGStyles.Tiny);

        var searchRect = new Rect(r.x + 4f, top + 20f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋標註"));
        tokenSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), tokenSearch);

        var listRect = new Rect(r.x + 2f, top + 44f, r.width - 4f, r.yMax - top - 46f);
        var tokens = model.ReadTokens(CurrentTokenScope());
        var shown = new List<AGToken>();
        foreach (var t in tokens)
            if (string.IsNullOrWhiteSpace(tokenSearch)
                || t.Key?.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || t.TypeName.IndexOf(tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                shown.Add(t);

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * TokenCellHeight + 4f);
        tokenScroll = GUI.BeginScrollView(listRect, tokenScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var token = shown[i];
            var row = new Rect(2f, i * TokenCellHeight + 2f, content.width - 4f, TokenCellHeight - 3f);
            bool isFocus = selectedIds.Contains(token.Node?.Id ?? "");
            // 深綠→琥珀，和畫布上被標註的節點同一條漸層。
            DrawCellBackground(row, AGStyles.HeaderToken, AGStyles.HeaderFormula, i % 2 == 1, isFocus);

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 70f, 18f),
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, AGStyles.RowLabel);
            var typeRect = new Rect(row.xMax - 58f, row.y + 6f, 42f, 15f);
            AGStyles.RoundedFill(typeRect, AGStyles.HeaderFormula, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(token.TypeName, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);

            if (HasTokenIssue(token, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 1) ShowTokenMenu(token);
                else JumpToToken(token);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>跳到那顆被標註的節點。同一張畫布，只是把視野移過去並選取它。</summary>
    private void JumpToToken(AGToken token)
    {
        if (token?.Node == null) return;
        if (focus.Kind != AGFocusKind.Asset && focus.Kind != AGFocusKind.Timing) SetFocus(AllTimingsFocus());

        selectedIds.Clear();
        if (!string.IsNullOrEmpty(token.Node.Id)) selectedIds.Add(token.Node.Id);
        // CenterOn 比對的是節點內容物件；資產節點沒有內容，退而求其次不移動視野。
        pendingCenterTarget = token.Node.BodyObject;
        graphDirty = true;
        Repaint();
    }

    private void DrawAssetLibrary(Rect r, float top)
    {
        if (GUI.Button(new Rect(r.x + 4f, top, r.width - 8f, 22f), "重新掃描資產")) AGAssetIndex.Refresh();

        var searchRect = new Rect(r.x + 4f, top + 26f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋資產"));
        assetSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y,
            searchRect.width - 20f, searchRect.height), assetSearch);

        var shown = new List<(AGAssetEntry entry, Type slotType)>();
        var slotTypes = AssetSlotTypes();
        foreach (var entry in AGAssetIndex.Entries)
        {
            Type slotType = SlotTypeForAsset(entry.Asset, slotTypes);
            if (slotType == null) continue;
            if (!string.IsNullOrWhiteSpace(assetSearch)
                && entry.Name.IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0
                && entry.TypeName.IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0
                && (entry.ResultType == null
                    || AGReflect.ResultTypeName(entry.ResultType).IndexOf(assetSearch, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            shown.Add((entry, slotType));
        }

        var listRect = new Rect(r.x + 2f, top + 50f, r.width - 4f, r.yMax - top - 52f);
        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * AssetCellHeight + 4f);
        assetLibraryScroll = GUI.BeginScrollView(listRect, assetLibraryScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i].entry;
            var asset = entry.Asset;
            var row = new Rect(2f, i * AssetCellHeight + 2f, content.width - 4f, AssetCellHeight - 3f);
            bool isFocus = focus.Kind == AGFocusKind.Asset && focus.AssetObject == asset;
            // 資產＝藍→內容型別，和畫布上的資產節點同一條漸層。
            Color payload = entry.IsAction ? AGStyles.HeaderAction : AGStyles.HeaderFormula;
            DrawCellBackground(row, AGStyles.HeaderAsset, payload, i % 2 == 1, isFocus);

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 64f, 17f), asset.name, AGStyles.RowLabel);
            string kind = entry.IsAction ? "ACT" : AGReflect.ResultTypeName(entry.ResultType);
            var typeRect = new Rect(row.xMax - 54f, row.y + 5f, 46f, 15f);
            AGStyles.RoundedFill(typeRect, payload, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(kind, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);
            GUI.Label(new Rect(row.x + 8f, row.y + 18f, row.width - 70f, 13f), entry.TypeName, AGStyles.Tiny);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                dragAsset = asset;
                pendingAssetFocus = asset;
                e.Use();
            }
            if (e.type == EventType.MouseDrag && dragAsset == asset) dragAssetActive = true;
            if (e.type == EventType.MouseUp && pendingAssetFocus == asset
                && !dragAssetActive && row.Contains(e.mousePosition))
            {
                pendingAssetFocus = null;
                dragAsset = null;
                EnterAsset(asset, shown[i].slotType);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>找目前 ActionSystem 中能承載此資產內容的 Slot；找不到代表本 Owner 不相容。</summary>
    private List<(Type acceptedAssetType, Type slotType)> AssetSlotTypes()
    {
        var result = new List<(Type acceptedAssetType, Type slotType)>();
        foreach (var (_, slotType) in model.FormulaKinds())
        {
            Type accepted = AGReflect.AssetType(slotType);
            if (accepted != null) result.Add((accepted, slotType));
        }

        Type actionSlotType = model.ActionSlotType;
        Type actionAssetType = actionSlotType != null ? AGReflect.ActionAssetType(actionSlotType) : null;
        if (actionAssetType != null) result.Add((actionAssetType, actionSlotType));
        return result;
    }

    private static Type SlotTypeForAsset(ScriptableObject asset, List<(Type acceptedAssetType, Type slotType)> slotTypes)
    {
        if (asset == null) return null;
        foreach (var candidate in slotTypes)
            if (candidate.acceptedAssetType.IsInstanceOfType(asset)) return candidate.slotType;
        return null;
    }

    /// <summary>這個標註有沒有問題。標註的問題掛在被標註節點的內容物件上（見 AGValidator）。</summary>
    private bool HasTokenIssue(AGToken token, out string reason, out bool isError)
    {
        reason = null; isError = false;
        object target = token?.Node;
        if (target == null) return false;

        foreach (var issue in Rep.Issues)
        {
            if (!ReferenceEquals(issue.Node, target)) continue;
            reason = issue.Line;
            isError = issue.IsError;
            if (isError) return true;
        }
        return reason != null;
    }

    private const float CellCornerRadius = 3f;
    // 問題色條正好填滿節點底部留白，才不會蓋掉最後一列。
    private const float IssueBarHeight = AGGraph.NodeBottomPad;

    /// <summary>
    /// 清單格底：和節點 Header 同一套語彙——身分色 + 「容器→內容」漸層，只是沖淡。
    /// payload 傳同一個顏色就是單色（動作沒有容器語意）。
    /// </summary>
    private static void DrawCellBackground(Rect row, Color kind, Color payload, bool altRow, bool focused)
    {
        AGStyles.GradientFill(row,
            AGStyles.CellTint(kind, altRow, focused),
            AGStyles.CellTint(payload, altRow, focused), CellCornerRadius);
        AGStyles.RoundedFrame(row, focused ? AGStyles.Link : AGStyles.LibraryCellBorder, CellCornerRadius);
    }

    /// <summary>Console 分頁沒有身分，走中性灰。</summary>
    private bool DrawTab(Rect r, string label, bool active)
        => DrawTab(r, label, active, AGStyles.NodeBody);

    /// <summary>左欄分頁：用該分頁清單的身分色，選中才給滿色。</summary>
    private bool DrawTab(Rect r, string label, bool active, Color kind)
    {
        AGStyles.RoundedFill(r, AGStyles.CellTint(kind, false, active), CellCornerRadius);
        GUI.Label(r, label, AGStyles.Tiny);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void ShowTokenMenu(AGToken token)
    {
        if (token?.Node == null) return;
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("跳到這顆節點"), false, () => JumpToToken(token));
        menu.AddItem(new GUIContent("重新命名…"), false, () =>
            AGPrompt.Show("標註名稱", "圖內引用是連線不受影響；會斷的是 Inspector 上指名這個字串的地方",
                token.Key, key =>
                {
                    if (!model.SetTokenName(token.Node, key, CurrentTokenScope(), out string error))
                    {
                        EditorUtility.DisplayDialog("無法改名", error, "好");
                        return;
                    }
                    Invalidate();
                    Repaint();
                }));

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("取消標註"), false, () => UnregisterTokenNode(token));
        menu.ShowAsContext();
    }

    /// <summary>
    /// 取消標註。節點與它的子樹留在原地，只是不再是對外端點——所以圖內的連線一條都不會斷，
    /// 會斷的是 Inspector 上指名這個字串的地方。
    /// </summary>
    private void UnregisterTokenNode(AGToken token)
    {
        if (token?.Node == null) return;
        int refs = model.CountReferences(token);
        string msg = refs > 0
            ? $"取消 '{token.Key}' 的標註？圖內還有 {refs} 個欄位接著這顆節點，那些連線不受影響。"
            : $"取消 '{token.Key}' 的標註？Inspector 上指名這個名字的地方會查不到值。";
        if (!EditorUtility.DisplayDialog("取消標註", msg, "取消標註", "保留")) return;

        model.BreakUndoMerge();
        model.ClearTokenName(token.Node);
        Invalidate();
        DoVerify(true);
        Repaint();
    }

    private void DrawDragAssetGhost()
    {
        if (dragAsset == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        // 沒有結果型別＝動作資產，和節點那邊同一條判定。
        bool isActionAsset = AGReflect.AssetResultType(dragAsset) == null;
        AGStyles.GradientFill(r, AGStyles.HeaderAsset,
            isActionAsset ? AGStyles.HeaderAction : AGStyles.HeaderFormula, CellCornerRadius);
        GUI.Label(r, dragAsset.name, AGStyles.Chip);
    }

    // ===== 時機選單 =====
    // 所有時機畫在同一張畫布上，一個時機一顆節點：下拉是「跳到哪一顆」，新增走畫布右鍵。
    // 舊的右欄（時機區 + 新增／移除動作鈕 + 動作清單）與「每張畫布一個時機」都已移除。

    /// <summary>畫布右上角的時機下拉：已建立的跳過去，還沒建立的直接在 createPos 建一顆。</summary>
    private void ShowTimingMenu(Vector2 createPos)
    {
        var menu = new GenericMenu();
        var groups = model.ReadGroups();

        foreach (Enum timing in Enum.GetValues(model.TimingType))
        {
            AGTimingGroup group = null;
            foreach (var candidate in groups)
                if (Equals(candidate.Timing, timing)) { group = candidate; break; }

            var captured = timing;
            if (group == null)
            {
                menu.AddItem(new GUIContent($"{timing}（尚未建立）"), false, () => AddTimingGroup(captured, createPos));
                continue;
            }

            int actionCount = group.Actions?.Count ?? 0;
            int errors = ErrorsOfGroup(group);
            string label = errors > 0
                ? $"{timing} ({actionCount})　{errors} 個錯誤"
                : $"{timing} ({actionCount})";
            menu.AddItem(new GUIContent(label), false, () => JumpToTiming(captured));
        }
        menu.ShowAsContext();
    }

    private int ErrorsOfGroup(AGTimingGroup group)
    {
        if (group?.Actions == null) return 0;
        int errors = 0;
        for (int i = 0; i < group.Actions.Count; i++)
        {
            var f = new AGFocus
            {
                Kind = AGFocusKind.Action, Timing = group.Timing,
                ActionList = group.Actions, ActionIndex = i, ActionSlot = group.Actions[i],
            };
            report.CountFor(f, out int e, out _);
            errors += e;
        }
        return errors;
    }

    /// <summary>時機節點的新增入口。已經存在的時機一律停用——一個時機只能有一顆節點。</summary>
    private void AddTimingMenuItems(GenericMenu menu, string prefix, Vector2 createPos)
    {
        foreach (Enum timing in Enum.GetValues(model.TimingType))
        {
            var content = new GUIContent(prefix + timing);
            if (model.HasGroup(timing)) { menu.AddDisabledItem(content); continue; }
            var captured = timing;
            menu.AddItem(content, false, () => AddTimingGroup(captured, createPos));
        }
    }

    private void ShowAddTimingMenu(Vector2 createPos)
    {
        var menu = new GenericMenu();
        AddTimingMenuItems(menu, "", createPos);
        menu.ShowAsContext();
    }

    /// <summary>在指定位置建立一顆時機節點。空的時機節點是合法狀態，動作由它本體的清單「＋」新增。</summary>
    private void AddTimingGroup(Enum timing, Vector2 pos)
    {
        if (timing == null) return;
        if (model.HasGroup(timing))
        {
            ShowNotification(new GUIContent($"{timing} 已經有節點了"));
            return;
        }

        model.BreakUndoMerge();
        var group = model.AddGroup(timing);
        if (group?.Group == null)
        {
            Debug.LogWarning($"[ActionGraph] 建立時機群組 '{timing}' 失敗，可能是 ActionGroups 型別不符。");
            return;
        }

        // 建在使用者按下右鍵的位置，不要丟去自動排版的角落。
        AGReflect.SetHeadPos(group.Group, SnapToGrid(pos));
        if (focus.Kind != AGFocusKind.Timing) SetFocus(AllTimingsFocus());
        selectedIds.Clear();
        selectedIds.Add(AGGraph.GroupHeadId(group.Group));
        Invalidate();
        Repaint();
    }

    /// <summary>刪掉一顆時機節點＝刪掉那個群組與它底下的動作。</summary>
    private void RemoveTimingGroup(AGNode node)
    {
        if (node?.Obj == null) return;

        int count = (AGReflect.Get(node.Obj, "Actions") as IList)?.Count ?? 0;
        if (count > 0 && !EditorUtility.DisplayDialog("刪除時機",
                $"'{node.Title}' 底下還有 {count} 個動作，會一起刪掉。確定嗎？", "刪除", "取消"))
            return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        foreach (var g in model.ReadGroups())
        {
            if (!ReferenceEquals(g.Group, node.Obj)) continue;
            model.RemoveGroup(g);
            break;
        }
        selectedIds.Remove(node.Id);
        Invalidate();
        Repaint();
    }

    /// <summary>跳到某顆時機節點。同一張畫布，所以只是把視野移過去，不換焦點。</summary>
    private void JumpToTiming(Enum timing)
    {
        if (focus.Kind != AGFocusKind.Timing) SetFocus(AllTimingsFocus());
        foreach (var g in model.ReadGroups())
        {
            if (!Equals(g.Timing, timing)) continue;
            pendingCenterTarget = g.Group;
            graphDirty = true;
            break;
        }
        Repaint();
    }

    // ===== 右欄（資產焦點）：引用清單 =====

    private void DrawReferencePanel(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Panel);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var asset = focus.AssetObject;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), "引用此資產的對象", AGStyles.PanelHeader);

        var subscribers = AGReflect.Get(asset, "_subscribers") as IList;
        int count = subscribers?.Count ?? 0;
        GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 16f),
            count > 0 ? $"共 {count} 個對象（清單可能不完整，可重建）" : "清單是空的，按下方重建掃描整個專案", AGStyles.Tiny);

        var listRect = new Rect(r.x + 2f, r.y + 40f, r.width - 4f, r.height - 92f);
        AGStyles.Fill(listRect, AGStyles.PanelList);

        var content = new Rect(0f, 0f, listRect.width - 16f, count * 24f + 4f);
        referenceScroll = GUI.BeginScrollView(listRect, referenceScroll, content);
        for (int i = 0; i < count; i++)
        {
            var so = subscribers[i] as ScriptableObject;
            var row = new Rect(0f, i * 24f, content.width, 23f);
            if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            if (so == null)
            {
                GUI.Label(new Rect(row.x + 4f, row.y + 3f, row.width - 8f, 17f), "（已遺失的對象）", AGStyles.RowLabelError);
                continue;
            }

            bool validated = so is IActionSystemOwner o && o.IsActionSystemValidated();
            GUI.Label(new Rect(row.x + 4f, row.y + 3f, row.width - 110f, 17f), so.name, AGStyles.RowLabel);
            GUI.Label(new Rect(row.xMax - 104f, row.y + 3f, 46f, 17f),
                validated ? "✓ 已驗證" : "✗ 未驗證", validated ? AGStyles.Tiny : AGStyles.RowLabelError);

            if (GUI.Button(new Rect(row.xMax - 54f, row.y + 3f, 50f, 17f), "切換"))
            {
                var target = so;
                if (ConfirmLeaveAsset())
                {
                    ExitAsset();
                    Bind(target);
                    EditorGUIUtility.PingObject(target);
                }
            }
        }
        GUI.EndScrollView();

        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 50f, r.width - 8f, 20f), "重建引用清單（掃描整個專案）"))
            RebuildReferences(asset);
        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 26f, r.width - 8f, 20f), "全部重新驗證"))
            VerifyAllSubscribers(asset);
    }

    /// <summary>掃描專案裡所有 Owner，重建這個資產的引用清單。只看磁碟上的內容，未存檔的修改不算。</summary>
    private void RebuildReferences(UnityEngine.Object asset)
    {
        var found = new List<ScriptableObject>();
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("重建引用清單", $"{i + 1}/{guids.Length}", (float)i / guids.Length))
                    break;

                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not IActionSystemOwner) continue;

                var field = AGModel.FindSystemField(so);
                var system = field?.GetValue(so);
                if (system == null) continue;

                foreach (var referenced in AGModel.ReferencedAssetsOfSystem(system))
                {
                    if (referenced != asset) continue;
                    found.Add(so);
                    break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        asset.GetType().GetMethod("ClearSubscribers")?.Invoke(asset, null);
        var register = asset.GetType().GetMethod("RegisterSubscriber");
        foreach (var so in found) register?.Invoke(asset, new object[] { so });

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"找到 {found.Count} 個引用"));
    }

    private void VerifyAllSubscribers(UnityEngine.Object asset)
    {
        if (AGReflect.Get(asset, "_subscribers") is not IList subscribers) return;
        int ok = 0, fail = 0;
        foreach (var s in subscribers)
        {
            if (s is not IActionSystemOwner owner) continue;
            owner.VerifyActionSystem();
            if (owner.IsActionSystemValidated()) ok++; else fail++;
            if (s is UnityEngine.Object so) EditorUtility.SetDirty(so);
        }
        ShowNotification(new GUIContent($"驗證完成：{ok} 通過 / {fail} 失敗"));
    }

    // ===== Console =====

    private void DrawConsole(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Console);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var head = new Rect(r.x, r.y, r.width, MinConsole);
        if (GUI.Button(new Rect(head.x + 2f, head.y + 2f, 18f, 17f), consoleCollapsed ? "▸" : "▾", EditorStyles.miniButton))
            consoleCollapsed = !consoleCollapsed;

        int errors = Rep.ErrorCount;
        int warnings = Rep.WarningCount;
        float tx = head.x + 24f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"全部 {Rep.Issues.Count}", consoleTab == 0)) consoleTab = 0;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"錯誤 {errors}", consoleTab == 1)) consoleTab = 1;
        tx += 70f;
        if (DrawTab(new Rect(tx, head.y + 2f, 68f, 17f), $"警告 {warnings}", consoleTab == 2)) consoleTab = 2;

        string verifyStatus = IsCurrentReportFresh
            ? $"完整驗證 {Rep.Time:HH:mm:ss}"
            : (focus.Kind == AGFocusKind.Asset ? assetVerifiedOnce : verifiedOnce)
                ? $"即時驗證 {Rep.Time:HH:mm:ss}"
                : "尚未驗證";
        GUI.Label(new Rect(head.xMax - 274f, head.y + 3f, 270f, 16f), verifyStatus, AGStyles.Tiny);

        if (consoleCollapsed) return;

        var listRect = new Rect(r.x + 2f, r.y + MinConsole, r.width - 4f, r.height - MinConsole - 2f);
        var shown = new List<AGIssue>();
        foreach (var issue in Rep.Issues)
        {
            if (consoleTab == 1 && !issue.IsError) continue;
            if (consoleTab == 2 && issue.IsError) continue;
            shown.Add(issue);
        }

        var content = new Rect(0f, 0f, listRect.width - 16f, shown.Count * 20f + 4f);
        consoleScroll = GUI.BeginScrollView(listRect, consoleScroll, content);
        for (int i = 0; i < shown.Count; i++)
        {
            var issue = shown[i];
            var row = new Rect(0f, i * 20f, content.width, 19f);
            if (i % 2 == 1) AGStyles.Fill(row, AGStyles.RowAlt);

            var icon = new Rect(row.x + 4f, row.y + 5f, 9f, 9f);
            AGStyles.Fill(icon, issue.IsError ? AGStyles.Error : AGStyles.Warning);
            GUI.Label(new Rect(row.x + 18f, row.y + 1f, row.width - 22f, 17f), issue.Line, AGStyles.ConsoleRow);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                JumpTo(issue);
                Event.current.Use();
            }
        }
        GUI.EndScrollView();
    }

    private void HandleConsoleResize(Rect handle)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && !consoleCollapsed && handle.Contains(e.mousePosition))
        {
            resizingConsole = true;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingConsole)
        {
            consoleHeight = Mathf.Clamp(consoleHeight - e.delta.y, 60f, position.height - 240f);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingConsole)
        {
            resizingConsole = false;
            e.Use();
        }
    }

    private void JumpTo(AGIssue issue)
    {
        // 動作的問題全部落在同一張時機畫布上，所以只要確定人在那張畫布，不必也不該切成單一動作焦點。
        if (issue.Focus == null) { }
        else if (issue.Focus.Kind == AGFocusKind.Action)
        {
            if (focus.Kind != AGFocusKind.Timing) SetFocus(AllTimingsFocus());
        }
        else if (!issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
