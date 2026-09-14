namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 左欄變數庫／資產庫／引用清單三區，以及底部 Console。
/// </summary>
public partial class HaruGraphWindow
{
    // ===== 左欄：Token／Asset 庫 =====

    /// <summary>
    /// 變數／資產／引用上下分區，中間可拖。刻意不用分頁：資產焦點下，變數列的是「這個資產對呼叫端的參數介面」，
    /// 而資產列是「換去編哪一個」，兩件事交替發生，分頁會逼人每次來回切一趟。
    /// 引用區只在資產焦點出現（`HasReferenceSection`），其餘焦點下另外兩區直接吃掉那段高度。
    /// 每區都各自有 ScrollView，區塊被拖小了就滾動，不會把內容切掉。
    /// </summary>
    private void DrawLibraryPanel(Rect r)
    {
        HGStyles.Fill(r, HGStyles.Panel);
        HGStyles.Frame(r, HGStyles.NodeBorder);

        // 面板標題直接當變數區的標題：上面已經沒有第三種東西，再加一條區段標題只是重複佔 20px。
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 18f),
            new GUIContent("變數庫", "對外端點；點一筆進入它自己的畫布，沒接來源時它就是具名常數"), HGStyles.PanelHeader);

        float top = r.y + 22f;

        // 圖宣告不支援共用資產時，左欄就只有變數庫一區：沒有分隔把手、沒有資料夾鈕，
        // 也不去掃專案資產。留一個永遠空的清單比收掉它更難解釋——使用者會一直找「東西為什麼沒出現」。
        if (!HasAssetSection)
        {
            tokenLibrary.Draw(new Rect(r.x, top, r.width, r.yMax - top), top + 2f,
                TokenLibraryView(), TokenLibraryCommands(), inlineName, drag);
            return;
        }

        bool showRef = HasReferenceSection;
        float avail = r.yMax - top - ResizeHandleWidth * (showRef ? 2f : 1f);
        // 視窗太矮時連各區的最小高度都放不下，這時平均分；寧可擠也不要出現負高度的 Rect。
        float share = avail / (showRef ? 3f : 2f);
        float minToken = Mathf.Min(MinTokenSection, share);
        float minAsset = Mathf.Min(MinAssetSection, share);
        float minRef = showRef ? Mathf.Min(MinRefSection, share) : 0f;

        // 夾限後寫回欄位：拖曳是累加 delta，記著的值若跟畫面上的高度不同步，下一次拖會整段跳。
        // 先夾引用區（它是最下面那一段），剩下的才輪到變數區與資產區分。
        float maxRef = Mathf.Max(minRef, avail - minToken - minAsset);
        if (showRef) refSectionHeight = Mathf.Clamp(refSectionHeight, minRef, maxRef);
        float refHeight = showRef ? refSectionHeight : 0f;
        float maxToken = Mathf.Max(minToken, avail - minAsset - refHeight);
        tokenSectionHeight = Mathf.Clamp(tokenSectionHeight, minToken, maxToken);

        var tokenRect = new Rect(r.x, top, r.width, tokenSectionHeight);
        var handle = new Rect(r.x, tokenRect.yMax, r.width, ResizeHandleWidth);
        var assetRect = new Rect(r.x, handle.yMax, r.width, r.yMax - handle.yMax - refHeight
            - (showRef ? ResizeHandleWidth : 0f));

        HandleLibrarySplitResize(handle, minToken, maxToken);

        tokenLibrary.Draw(tokenRect, tokenRect.y + 2f, TokenLibraryView(), TokenLibraryCommands(), inlineName, drag);

        // 資產區標題跟面板標題同一種寫法，三區看起來才是同級的清單，不是主從。
        GUI.Label(new Rect(assetRect.x + 4f, assetRect.y + 2f, 160f, 18f),
            new GUIContent("資產庫", "點一筆進去編它；拖到畫布上＝建一顆引用節點"), HGStyles.PanelHeader);

        // 資產落點由使用端專案決定，套件不寫死路徑。這顆是**唯一**的決定點：抽出當下不再問，
        // 沒設定就抽不出來（`HGAssetStore.TryGetUniquePath` 回 false）。所以未設定時標籤要自己喊。
        string folder = HGAssetStore.Folder;
        bool unset = string.IsNullOrEmpty(folder);
        var folderButton = new Rect(assetRect.xMax - 76f, assetRect.y + 2f, 72f, 16f);
        var folderLabel = new GUIContent(
            unset ? "選資料夾…" : "資料夾",
            unset ? "尚未指定共用資產資料夾——指定前無法從節點抽出共用資產" : $"共用資產資料夾：{folder}（點此更換）");

        var prevColor = GUI.color;
        if (unset) GUI.color = HGStyles.Warning;
        if (GUI.Button(folderButton, folderLabel, EditorStyles.miniButton) && HGAssetStore.TryPickFolder(out _))
            HGAssetIndex.Refresh();
        GUI.color = prevColor;

        assetLibrary.Draw(assetRect, assetRect.y + 24f, AssetLibraryView(), inlineName, drag, RenameAssetFile, ActivateAsset);

        DrawResizeGrip(handle, false, resizingLibrarySplit);

        if (!showRef) return;

        var refHandle = new Rect(r.x, assetRect.yMax, r.width, ResizeHandleWidth);
        var refRect = new Rect(r.x, refHandle.yMax, r.width, r.yMax - refHandle.yMax);
        HandleRefSplitResize(refHandle, minRef, maxRef);
        referenceList.Draw(refRect, focus.AssetObject as ScriptableObject, ReferenceListCommands());
        DrawResizeGrip(refHandle, false, resizingRefSplit);
    }

    /// <summary>左欄上下分隔：拖動只改變數區高度，資產區吃剩下的。夾限與 Console 那條同一套。</summary>
    private HGTokenLibraryView TokenLibraryView() => new()
    {
        Tokens = HGModel.ReadTokens(CurrentEndpoints()),
        FocusedEndpoint = focus.Endpoint,
    };

    private HGTokenLibraryCommands TokenLibraryCommands() => new()
    {
        Rename = RenameEndpointFromLibrary,
        Activate = ActivateEndpoint,
        Duplicate = DuplicateEndpoint,
        Remove = RemoveEndpoint,
        Create = ShowCreateEndpointMenu,
        IssueOf = TokenIssue,
    };

    private bool RenameEndpointFromLibrary(GraphEndpoint endpoint, string name)
    {
        if (model.RenameEndpoint(endpoint, name, CurrentEndpoints(), out string error))
        {
            MarkGraphChanged();
            return true;
        }
        ShowNotification(new GUIContent(error));
        return false;
    }

    /// <summary>變數庫選了一筆：再點一次目前這格＝退出，不必去找返回鈕。</summary>
    private void ActivateEndpoint(GraphEndpoint endpoint)
    {
        if (ReferenceEquals(focus.Endpoint, endpoint)) ExitVariable();
        else EnterVariable(endpoint);
    }

    private (string reason, bool isError) TokenIssue(HGToken token)
        => HasTokenIssue(token, out string reason, out bool isError) ? (reason, isError) : (null, false);

    private void HandleLibrarySplitResize(Rect handle, float min, float max)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition))
        {
            resizingLibrarySplit = true;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingLibrarySplit)
        {
            tokenSectionHeight = Mathf.Clamp(tokenSectionHeight + e.delta.y, min, max);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingLibrarySplit)
        {
            resizingLibrarySplit = false;
            e.Use();
        }
    }

    /// <summary>資產區與引用區之間的分隔：引用區從底部往上長，所以 delta 要反過來加。</summary>
    private void HandleRefSplitResize(Rect handle, float min, float max)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition))
        {
            resizingRefSplit = true;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingRefSplit)
        {
            refSectionHeight = Mathf.Clamp(refSectionHeight - e.delta.y, min, max);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingRefSplit)
        {
            resizingRefSplit = false;
            e.Use();
        }
    }
    /// <summary>進入這個變數自己的畫布。</summary>
    private void JumpToToken(HGToken token) => EnterVariable(token?.Endpoint);

    /// <summary>新增變數：先選結果型別，因為它決定端點的取值欄位，之後不再更動。</summary>
    private void ShowCreateEndpointMenu()
    {
        var scope = CurrentEndpoints();
        if (scope == null) return;

        var menu = new GenericMenu();
        foreach (var (resultType, slotType) in model.FormulaKinds())
        {
            var captured = slotType;
            // 用族名而非結果型別名：同結果型別的多個族（String / Key）否則會列出兩個一模一樣的項目。
            menu.AddItem(new GUIContent(HGReflect.SlotKindName(slotType)), false, () =>
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
    /// <summary>複製一個變數，並進去複本的畫布——複製完通常就是要改它。</summary>
    private void DuplicateEndpoint(GraphEndpoint source)
    {
        var scope = CurrentEndpoints();
        if (scope == null) return;

        BreakUndoMerge();                   // 複製自成一步
        var copy = model.DuplicateEndpoint(source, scope, out string error);
        if (copy == null) { ShowNotification(new GUIContent(error)); return; }

        MarkGraphChanged();
        ShowNotification(new GUIContent($"已複製成 '{copy.Name}'"));
        EnterVariable(copy);
    }

    /// <summary>
    /// 移除一個變數：指著它的節點會一起清空（`HGModel.DeleteEndpoint`）。
    /// 不問確認——Owner 焦點 Ctrl+Z 復原得回來，資產焦點按「取消」可整批捨棄，提示裡直接寫出來。
    /// </summary>
    private void RemoveEndpoint(GraphEndpoint endpoint)
    {
        if (endpoint == null) return;
        // scope 與引用數都要在 ExitVariable 之前取：退出變數焦點會換掉「現在在編誰」，清單也就跟著換了。
        var scope = CurrentEndpoints();
        if (scope == null) return;
        int used = HGModel.CountReferences(endpoint, SlotsInCurrentGraph());
        string name = string.IsNullOrEmpty(endpoint.Name) ? "（未命名）" : endpoint.Name;

        BreakUndoMerge();                   // 刪除自成一步，不跟前一個編輯合併成同一次復原
        if (ReferenceEquals(focus.Endpoint, endpoint)) ExitVariable();
        model.DeleteEndpoint(endpoint, scope, CurrentCarrierScope());
        MarkGraphChanged();

        string undoHint = focus.Kind == HGFocusKind.Asset ? "「取消」可整批捨棄" : "Ctrl+Z 可復原";
        ShowNotification(new GUIContent(used > 0
            ? $"已移除 '{name}'：{used} 個欄位變成空節點（{undoHint}）"
            : $"已移除 '{name}'（{undoHint}）"));
        Repaint();
    }

    /// <summary>圖改了：資產焦點記在資產交易上，Owner 焦點記在工作副本上。</summary>
    private void MarkGraphChanged()
    {
        if (focus.Kind == HGFocusKind.Asset) MarkAssetContentChanged();
        else reportStale = true;
        Invalidate();
        Repaint();
    }

    private HGAssetLibraryView AssetLibraryView() => new()
    {
        Entries = HGAssetIndex.Entries,
        SlotTypes = AssetSlotTypes(),
        FocusedAsset = focus.Kind == HGFocusKind.Asset ? focus.AssetObject : null,
    };

    /// <summary>資產庫選了一筆：再點一次目前這格＝退出（在它的變數子畫布時先回到資產本體，由 EnterAsset 處理）。</summary>
    private void ActivateAsset(ScriptableObject asset, Type slotType)
    {
        bool isFocus = focus.Kind == HGFocusKind.Asset && focus.AssetObject == asset;
        if (isFocus && focus.Endpoint == null) LeaveAsset();
        else EnterAsset(asset, slotType);
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
        HGAssetIndex.Refresh();
        // 檔名已經寫進磁碟、圖的內容沒動，所以只重建圖（節點 Header 與清單要換字），
        // 不可走 Invalidate()——那會把資產或 Owner 標成未存檔，還會佔一格 Undo。
        graphDirty = true;
        Repaint();
        return true;
    }

    /// <summary>找目前 LogicGraph 中能承載此資產內容的 Slot；找不到代表本 Owner 不相容。</summary>
    private List<(Type acceptedAssetType, Type slotType)> AssetSlotTypes()
    {
        var result = new List<(Type acceptedAssetType, Type slotType)>();
        foreach (var (_, slotType) in model.FormulaKinds())
        {
            Type accepted = HGReflect.AssetType(slotType);
            if (accepted != null) result.Add((accepted, slotType));
        }

        Type actionSlotType = model.ActionSlotType;
        Type actionAssetType = actionSlotType != null ? HGReflect.ActionAssetType(actionSlotType) : null;
        if (actionAssetType != null) result.Add((actionAssetType, actionSlotType));
        return result;
    }


    /// <summary>這個標註有沒有問題。標註的問題掛在被標註節點的內容物件上（見 HGValidator）。</summary>
    private bool HasTokenIssue(HGToken token, out string reason, out bool isError)
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
    // 問題色條壓在 Header 上緣。厚度不跟 NodeBottomPad 綁：那是排版留白，這是狀態標記，
    // 要在縮小後還看得見就得比留白厚。上限是圓角半徑，再厚左右上角就開始出現直邊。
    private const float IssueBarHeight = 5f;


    /// <summary>
    /// 放置模式的殘影：長什麼樣就是等一下會生出來的那顆空節點，配色與標題都照 placeholder 走。
    /// 沒有「放開」這個訊號，所以要寫清楚怎麼落下、怎麼取消。
    /// </summary>
    private void DrawPlacingGhost()
    {
        bool isAction = placingSlot != null && HGReflect.IsActionSlotType(placingSlot.GetType());
        Color kind = isAction ? HGStyles.HeaderAction : HGStyles.HeaderFormula;

        Vector2 p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 160f, 18f);
        HGStyles.RoundedFill(r, kind, CellCornerRadius);
        GUI.Label(r, isAction ? "（選擇 Action）" : "（選擇 Formula）", HGStyles.Chip);
        GUI.Label(new Rect(r.x, r.yMax + 2f, 200f, 16f), "點一下放置　Esc 取消", HGStyles.Tiny);
    }

    // ===== 時機選單 =====
    // 所有時機畫在同一張畫布上，一個時機一顆節點：下拉是「跳到哪一顆」，新增走畫布右鍵。
    // 舊的右欄（時機區 + 新增／移除動作鈕 + 動作清單）與「每張畫布一個時機」都已移除。

    /// <summary>畫布右上角的時機下拉：已建立的跳過去，還沒建立的直接在 createPos 建一顆。</summary>
    private void ShowTimingMenu(Vector2 createPos)
    {
        var menu = new GenericMenu();
        var groups = model.ReadGroups();

        foreach (var timing in model.TimingValues)
        {
            HGTimingGroup group = null;
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

    private int ErrorsOfGroup(HGTimingGroup group)
    {
        if (group?.Actions == null) return 0;
        int errors = 0;
        for (int i = 0; i < group.Actions.Count; i++)
        {
            var f = new HGFocus
            {
                Kind = HGFocusKind.Action, Timing = group.Timing,
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
        foreach (var timing in model.TimingValues)
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
    private void AddTimingGroup(object timing, Vector2 pos)
    {
        if (timing == null) return;
        if (model.HasGroup(timing))
        {
            ShowNotification(new GUIContent($"{timing} 已經有節點了"));
            return;
        }

        BreakUndoMerge();
        var group = model.AddGroup(timing);
        if (group?.Group == null)
        {
            Debug.LogWarning($"[GraphKit] 建立{RootNoun}群組 '{timing}' 失敗：識別值型別與這張圖不符。");
            return;
        }

        // 建在使用者按下右鍵的位置，不要丟去自動排版的角落。
        HGReflect.SetHeadPos(group.Group, SnapToGrid(pos));
        if (focus.Kind != HGFocusKind.Timing) SetFocus(AllTimingsFocus());
        selectedIds.Clear();
        selectedIds.Add(HGGraph.GroupHeadId(model, group.Group));
        Invalidate();
        Repaint();
    }

    /// <summary>
    /// 刪掉一顆時機節點＝刪掉那個群組與它底下的動作。底下還有動作時先問過；
    /// 確認框開在那顆節點的 Header 旁邊（`GraphToWindowRect`），不是螢幕中央。
    /// </summary>
    private void RemoveTimingGroup(HGNodeView node)
    {
        if (node?.Obj == null) return;

        int count = model.Doc?.ItemsOf(node.Obj)?.Count ?? 0;
        if (count > 0)
        {
            RequestConfirm(GraphToWindowRect(new Rect(node.Pos.x, node.Pos.y, node.Width, HGGraph.HeaderHeight)),
                $"'{node.Title}' 底下還有 {count} 個動作，會一起刪掉。確定嗎？",
                "刪除", () => ConfirmRemoveTimingGroup(node));
            return;
        }
        ConfirmRemoveTimingGroup(node);
    }

    private void ConfirmRemoveTimingGroup(HGNodeView node)
    {
        if (node?.Obj == null) return;

        BreakUndoMerge();
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
    private void JumpToTiming(object timing)
    {
        if (focus.Kind != HGFocusKind.Timing) SetFocus(AllTimingsFocus());
        foreach (var g in model.ReadGroups())
        {
            if (!Equals(g.Timing, timing)) continue;
            pendingCenterTarget = g.Group;
            graphDirty = true;
            break;
        }
        Repaint();
    }

    // ===== 左欄第三區（資產焦點）：引用清單 =====

    private HGReferenceListCommands ReferenceListCommands() => new()
    {
        VerifyAll = VerifyAllUsers,
        Open = OpenReferenceUser,
        Notify = message => ShowNotification(new GUIContent(message)),
    };

    /// <summary>切換去編某個引用者。使用者取消離開資產時回 false，清單就繼續畫。</summary>
    private bool OpenReferenceUser(ScriptableObject user)
    {
        if (!ConfirmLeaveAsset()) return false;
        ExitAsset();
        Bind(user);
        EditorGUIUtility.PingObject(user);
        return true;
    }

    /// <summary>把引用者全部重驗一次。只有驗證結果真的翻轉的才寫檔，其餘一個都不動。</summary>
    private void VerifyAllUsers(UnityEngine.Object asset)
    {
        int ok = 0, fail = 0, touched = 0;
        foreach (var so in HGReferenceIndex.Users(asset as ScriptableObject))
        {
            if (so == null || so is not IGraphOwner owner) continue;

            bool was = owner.IsGraphValidated();
            owner.VerifyGraph();
            bool now = owner.IsGraphValidated();

            if (now) ok++; else fail++;
            if (was == now) continue;

            EditorUtility.SetDirty(so);
            touched++;
        }
        if (touched > 0) AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"驗證完成：{ok} 通過 / {fail} 失敗"));
    }

    // ===== Console =====

    /// <summary>
    /// 把視窗的驗證狀態打包成 Console 的一次性快照。
    /// 「現在看的是資產還是 Owner」這種焦點判斷留在視窗，面板只收結果。
    /// </summary>
    private HGConsoleView ConsoleView()
    {
        bool isAsset = focus.Kind == HGFocusKind.Asset;

        // Owner 的 Core 驗證狀態。未驗證的圖 runtime 直接擋下不執行，而這件事原本只有資產焦點的
        // 引用清單（別人的清單）看得到，自己這張畫布反而看不出來。
        string warning = !isAsset && model?.Owner is IGraphOwner owner && !owner.IsGraphValidated()
            ? "✗ 這份圖未驗證，存檔後才會執行"
            : null;

        return new HGConsoleView
        {
            Report = Rep,
            VerifiedOnce = isAsset ? assetVerifiedOnce : verifiedOnce,
            Fresh = IsCurrentReportFresh,
            OwnerWarning = warning,
        };
    }

    private void JumpTo(HGIssue issue)
    {
        // 動作的問題全部落在同一張時機畫布上，所以只要確定人在那張畫布，不必也不該切成單一動作焦點。
        if (issue.Focus == null) { }
        else if (issue.Focus.Kind == HGFocusKind.Action)
        {
            if (focus.Kind != HGFocusKind.Timing) SetFocus(AllTimingsFocus());
        }
        else if (!issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
