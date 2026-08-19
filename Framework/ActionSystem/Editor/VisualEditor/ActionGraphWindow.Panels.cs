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
    /// 變數庫：這張圖有哪些對外端點。新增、改名、刪除都在這裡，點一筆進入它自己的畫布。
    /// 資產焦點下列的是那個資產的變數（＝它對呼叫端的參數介面）。
    /// </summary>
    private void DrawTokenLibrary(Rect r, float top, bool inAsset)
    {
        GUI.Label(new Rect(r.x + 6f, top, r.width - 12f, 16f),
            new GUIContent("變數（對外端點）", "點一筆進入它自己的畫布；沒接來源時它就是具名常數"), AGStyles.Tiny);

        DrawCreateEndpointButton(new Rect(r.x + 4f, top + 18f, r.width - 8f, 20f));
        DrawRemoveEndpointButton(new Rect(r.x + 4f, top + 40f, r.width - 8f, 20f));

        var searchRect = new Rect(r.x + 4f, top + 64f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋變數"));
        tokenSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), tokenSearch);

        var listRect = new Rect(r.x + 2f, top + 88f, r.width - 4f, r.yMax - top - 90f);
        var tokens = AGModel.ReadTokens(CurrentEndpoints());
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
            bool isFocus = ReferenceEquals(focus.Endpoint, token.Endpoint);
            // 深綠→琥珀，和畫布上的變數節點同一條漸層。
            DrawCellBackground(row, AGStyles.HeaderToken, AGStyles.HeaderFormula, i % 2 == 1, isFocus);

            var endpoint = token.Endpoint;
            bool renaming = DrawInlineName(new Rect(row.x + 8f, row.y + 2f, row.width - 70f, 18f), endpoint,
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, token.Key ?? "",
                AGStyles.RowLabel, "雙擊可改名；外部（Inspector）用這個名字查它的值", name =>
                {
                    if (model.RenameEndpoint(endpoint, name, CurrentEndpoints(), out string error))
                    {
                        MarkGraphChanged();
                        return true;
                    }
                    ShowNotification(new GUIContent(error));
                    return false;
                });
            var typeRect = new Rect(row.xMax - 58f, row.y + 6f, 42f, 15f);
            AGStyles.RoundedFill(typeRect, AGStyles.HeaderFormula, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(token.TypeName, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);

            if (HasTokenIssue(token, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            if (renaming) continue;               // 正在改名的這一格不吃點擊，否則同一下會又改名又切焦點

            var e = Event.current;
            // 右鍵不做事：改名雙擊、刪除是上面那顆「－ 移除變數」，選單只是多一層要記的東西。
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                dragEndpoint = token.Endpoint;
                pendingVariableFocus = token.Endpoint;
                e.Use();
            }
            if (e.type == EventType.MouseDrag && ReferenceEquals(dragEndpoint, token.Endpoint)) dragEndpointActive = true;
            if (e.type == EventType.MouseUp && ReferenceEquals(pendingVariableFocus, token.Endpoint)
                && !dragEndpointActive && row.Contains(e.mousePosition))
            {
                pendingVariableFocus = null;
                dragEndpoint = null;
                // 再點一次目前這格＝退出，不必去找返回鈕。
                if (isFocus) ExitVariable();
                else EnterVariable(token.Endpoint);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>進入這個變數自己的畫布。</summary>
    private void JumpToToken(AGToken token) => EnterVariable(token?.Endpoint);

    /// <summary>新增變數：先選結果型別，因為它決定端點的取值欄位，之後不再更動。</summary>
    private void ShowCreateEndpointMenu()
    {
        var scope = CurrentEndpoints();
        if (scope == null) return;

        var menu = new GenericMenu();
        foreach (var (resultType, slotType) in model.FormulaKinds())
        {
            var captured = slotType;
            menu.AddItem(new GUIContent(AGReflect.ResultTypeName(resultType)), false, () =>
            {
                var endpoint = model.CreateEndpoint(scope, captured, out string error);
                if (endpoint == null)
                {
                    ShowNotification(new GUIContent(error));
                    return;
                }
                MarkGraphChanged();
                EnterVariable(endpoint);
            });
        }
        menu.ShowAsContext();
    }

    /// <summary>
    /// 「＋ 新增變數」：單擊開型別選單；把變數格**拖到這顆按鈕上放開＝複製那一個**（內容一起複製）。
    /// </summary>
    private void DrawCreateEndpointButton(Rect rect)
    {
        var e = Event.current;
        bool dropping = dragEndpointActive && dragEndpoint != null;
        bool hover = rect.Contains(e.mousePosition);

        if (dropping && hover) AGStyles.Fill(rect, new Color(0.24f, 0.50f, 0.34f, 0.75f));

        bool clicked = GUI.Button(rect, new GUIContent(
            dropping ? "複製變數" : "＋ 新增變數",
            "新增一個變數；把左邊的變數拖到這裡＝複製它"));

        // 拖曳放開不會讓 GUI.Button 回 true（它沒在自己身上收到 MouseDown），所以自己判。
        if (dropping && hover && e.rawType == EventType.MouseUp)
        {
            DuplicateEndpoint(dragEndpoint);
            ClearPendingLibraryDrag();
            e.Use();
            return;
        }
        if (clicked && !dropping) ShowCreateEndpointMenu();
    }

    /// <summary>
    /// 「－ 移除變數」：單擊刪掉**目前正在編輯**的那一個，或把變數格**拖到這顆按鈕上放開**刪掉被拖的那一個。
    /// 兩條路都要先表態（先點開它，或把它拖過來），所以不再問一次確認框——刪完用提示說明怎麼救回來。
    /// </summary>
    private void DrawRemoveEndpointButton(Rect rect)
    {
        var e = Event.current;
        bool dropping = dragEndpointActive && dragEndpoint != null;
        bool hover = rect.Contains(e.mousePosition);

        // 拖曳中鋪一層紅底當落點：拖著變數在畫面上跑時，看得到「放這裡會刪掉」才敢放手。
        // 字只拿掉開頭的「－」，不改寫成一句話——按鈕上的字換來換去比底色還吵。
        if (dropping && hover) AGStyles.Fill(rect, new Color(0.62f, 0.24f, 0.26f, 0.75f));

        bool hasFocusEndpoint = focus.Endpoint != null;
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && (dropping || hasFocusEndpoint);
        bool clicked = GUI.Button(rect, new GUIContent(
            dropping ? "移除變數" : "－ 移除變數",
            hasFocusEndpoint
                ? "移除目前編輯中的變數；也可以把左邊的變數直接拖到這裡"
                : "先點一個變數進去，或把變數拖到這裡"));
        GUI.enabled = wasEnabled;

        if (dropping && hover && e.rawType == EventType.MouseUp)
        {
            RemoveEndpoint(dragEndpoint);
            ClearPendingLibraryDrag();
            e.Use();
            return;
        }
        if (clicked && hasFocusEndpoint) RemoveEndpoint(focus.Endpoint);
    }

    /// <summary>複製一個變數，並進去複本的畫布——複製完通常就是要改它。</summary>
    private void DuplicateEndpoint(GraphEndpoint source)
    {
        var scope = CurrentEndpoints();
        if (scope == null) return;

        model.BreakUndoMerge();                   // 複製自成一步
        var copy = model.DuplicateEndpoint(source, scope, out string error);
        if (copy == null) { ShowNotification(new GUIContent(error)); return; }

        MarkGraphChanged();
        ShowNotification(new GUIContent($"已複製成 '{copy.Name}'"));
        EnterVariable(copy);
    }

    /// <summary>
    /// 移除一個變數：指著它的節點會一起清空（`AGModel.DeleteEndpoint`）。
    /// 不問確認——Owner 焦點 Ctrl+Z 復原得回來，資產焦點按「取消」可整批捨棄，提示裡直接寫出來。
    /// </summary>
    private void RemoveEndpoint(GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        // scope 與引用數都要在 ExitVariable 之前取：退出變數焦點會換掉「現在在編誰」，清單也就跟著換了。
        var scope = CurrentEndpoints();
        if (scope == null) return;
        int used = AGModel.CountReferences(endpoint, SlotsInCurrentGraph());
        string name = string.IsNullOrEmpty(endpoint.Name) ? "（未命名）" : endpoint.Name;

        model.BreakUndoMerge();                   // 刪除自成一步，不跟前一個編輯合併成同一次復原
        if (ReferenceEquals(focus.Endpoint, endpoint)) ExitVariable();
        model.DeleteEndpoint(endpoint, scope, CurrentCarrierScope());
        MarkGraphChanged();

        string undoHint = focus.Kind == AGFocusKind.Asset ? "「取消」可整批捨棄" : "Ctrl+Z 可復原";
        ShowNotification(new GUIContent(used > 0
            ? $"已移除 '{name}'：{used} 個欄位變成空節點（{undoHint}）"
            : $"已移除 '{name}'（{undoHint}）"));
        Repaint();
    }

    /// <summary>圖改了：資產焦點記在資產交易上，Owner 焦點記在工作副本上。</summary>
    private void MarkGraphChanged()
    {
        if (focus.Kind == AGFocusKind.Asset) MarkAssetContentChanged();
        else reportStale = true;
        Invalidate();
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

            bool renaming = DrawInlineName(new Rect(row.x + 8f, row.y + 2f, row.width - 64f, 18f), asset,
                asset.name, asset.name, AGStyles.RowLabel, "雙擊可改名（改的是 .asset 檔名）",
                name => RenameAssetFile(asset, name));
            string kind = entry.IsAction ? "ACT" : AGReflect.ResultTypeName(entry.ResultType);
            var typeRect = new Rect(row.xMax - 54f, row.y + 6f, 46f, 15f);
            AGStyles.RoundedFill(typeRect, payload, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(kind, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);

            if (renaming) continue;               // 正在改名的這一格不吃點擊

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
                // 再點一次目前這格＝退出（在它的變數子畫布時先回到資產本體，由 EnterAsset 處理）。
                if (isFocus && focus.Endpoint == null) LeaveAsset();
                else EnterAsset(asset, shown[i].slotType);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>
    /// 改資產檔名（.meta 由 AssetDatabase 一起處理）。名稱重複、非法字元由 Unity 回錯誤字串，
    /// 這時維持編輯狀態讓使用者改，不吞掉錯誤。
    /// </summary>
    private bool RenameAssetFile(UnityEngine.Object asset, string name)
    {
        if (asset == null || string.IsNullOrWhiteSpace(name)) return false;
        if (asset.name == name) return true;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
        {
            ShowNotification(new GUIContent("這個資產沒有檔案路徑，無法改名。"));
            return false;
        }

        string error = AssetDatabase.RenameAsset(path, name);
        if (!string.IsNullOrEmpty(error))
        {
            ShowNotification(new GUIContent(error));
            return false;
        }
        AssetDatabase.SaveAssets();
        AGAssetIndex.Refresh();
        // 檔名已經寫進磁碟、圖的內容沒動，所以只重建圖（節點 Header 與清單要換字），
        // 不可走 Invalidate()——那會把資產或 Owner 標成未存檔，還會佔一格 Undo。
        graphDirty = true;
        Repaint();
        return true;
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
        object target = token?.Endpoint;
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

    private void DrawDragVariableGhost()
    {
        if (dragEndpoint == null) return;
        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        AGStyles.GradientFill(r, AGStyles.HeaderToken, AGStyles.HeaderFormula, CellCornerRadius);
        GUI.Label(r, dragEndpoint.Name ?? "（未命名）", AGStyles.Chip);
    }

    /// <summary>
    /// 放置模式的殘影：長什麼樣就是等一下會生出來的那顆空節點，配色與標題都照 placeholder 走。
    /// 沒有「放開」這個訊號，所以要寫清楚怎麼落下、怎麼取消。
    /// </summary>
    private void DrawPlacingGhost()
    {
        bool isAction = placingSlot != null && AGReflect.IsActionSlotType(placingSlot.GetType());
        Color kind = isAction ? AGStyles.HeaderAction : AGStyles.HeaderFormula;

        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        AGStyles.RoundedFill(r, kind, CellCornerRadius);
        GUI.Label(r, isAction ? "（選擇 Action）" : "（選擇 Formula）", AGStyles.Chip);
        GUI.Label(new Rect(r.x, r.yMax + 2f, 200f, 16f), "點一下放置　Esc 取消", AGStyles.Tiny);
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

    /// <summary>
    /// 刪掉一顆時機節點＝刪掉那個群組與它底下的動作。底下還有動作時先問過；
    /// 確認框開在那顆節點的 Header 旁邊（`GraphToWindowRect`），不是螢幕中央。
    /// </summary>
    private void RemoveTimingGroup(AGNode node)
    {
        if (node?.Obj == null) return;

        int count = (AGReflect.Get(node.Obj, "Actions") as IList)?.Count ?? 0;
        if (count > 0)
        {
            RequestConfirm(GraphToWindowRect(new Rect(node.Pos.x, node.Pos.y, node.Width, AGGraph.HeaderHeight)),
                $"'{node.Title}' 底下還有 {count} 個動作，會一起刪掉。確定嗎？",
                "刪除", () => ConfirmRemoveTimingGroup(node));
            return;
        }
        ConfirmRemoveTimingGroup(node);
    }

    private void ConfirmRemoveTimingGroup(AGNode node)
    {
        if (node?.Obj == null) return;

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

        var users = AGReferenceIndex.Users(asset as ScriptableObject);
        int count = users.Count;
        GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 16f),
            count > 0 ? $"共 {count} 個對象" : "專案裡沒有已存檔的對象引用它", AGStyles.Tiny);

        var listRect = new Rect(r.x + 2f, r.y + 40f, r.width - 4f, r.height - 92f);
        AGStyles.Fill(listRect, AGStyles.PanelList);

        var content = new Rect(0f, 0f, listRect.width - 16f, count * 24f + 4f);
        referenceScroll = GUI.BeginScrollView(listRect, referenceScroll, content);
        for (int i = 0; i < count; i++)
        {
            var so = users[i];
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

        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 50f, r.width - 8f, 20f), "重新掃描專案"))
        {
            AGReferenceIndex.Refresh();
            ShowNotification(new GUIContent($"找到 {AGReferenceIndex.Users(asset as ScriptableObject).Count} 個引用"));
        }
        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 26f, r.width - 8f, 20f), "全部重新驗證"))
            VerifyAllUsers(asset);
    }

    /// <summary>把引用者全部重驗一次。只有驗證結果真的翻轉的才寫檔，其餘一個都不動。</summary>
    private void VerifyAllUsers(UnityEngine.Object asset)
    {
        int ok = 0, fail = 0, touched = 0;
        foreach (var so in AGReferenceIndex.Users(asset as ScriptableObject))
        {
            if (so == null || so is not IActionSystemOwner owner) continue;

            bool was = owner.IsActionSystemValidated();
            owner.VerifyActionSystem();
            bool now = owner.IsActionSystemValidated();

            if (now) ok++; else fail++;
            if (was == now) continue;

            EditorUtility.SetDirty(so);
            touched++;
        }
        if (touched > 0) AssetDatabase.SaveAssets();
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

        // Owner 的 Core 驗證狀態。未驗證的圖 runtime 直接擋下不執行，而這件事原本只有資產焦點的
        // 右欄（別人的清單）看得到，自己這張畫布反而看不出來。
        if (focus.Kind != AGFocusKind.Asset && model?.Owner is IActionSystemOwner owner && !owner.IsActionSystemValidated())
            GUI.Label(new Rect(head.xMax - 470f, head.y + 3f, 192f, 16f),
                "✗ 這份圖未驗證，存檔後才會執行", AGStyles.RowLabelError);

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
