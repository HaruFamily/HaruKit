namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 左欄Token庫／資產庫／引用清單三區，以及底部 Console。
/// </summary>
public partial class HaruGraphWindow
{
    // ===== 左欄：Token／Asset 庫 =====

    /// <summary>
    /// Token／資產／引用上下分區，中間可拖。刻意不用分頁：資產焦點下，Token列的是「這個資產對呼叫端的參數介面」，
    /// 而資產列是「換去編哪一個」，兩件事交替發生，分頁會逼人每次來回切一趟。
    /// 引用區只在資產焦點出現（`HasReferenceSection`），其餘焦點下另外兩區直接吃掉那段高度。
    /// 每區都各自有 ScrollView，區塊被拖小了就滾動，不會把內容切掉。
    /// </summary>
    private void DrawLibraryPanel(Rect r)
    {
        HGStyles.Fill(r, HGStyles.Panel);
        HGStyles.Frame(r, HGStyles.NodeBorder);

        // 目錄庫是最上面一區。目前唯一的使用端（AssetPipeline）只宣告這一個能力，
        // 所以「只有目錄庫」是主要路徑；與其他區並存時它取固定高度，不另外長一條把手——
        // 沒有真實需求之前不為未知形狀做四區可拖版面。
        if (HasCatalogSection)
        {
            bool alone = !HasTokenSection && !HasAssetSection;
            float catalogBottom = alone ? r.yMax : r.y + 22f + MinCatalogSection;
            var catalogRect = new Rect(r.x, r.y, r.width, catalogBottom - r.y);

            GUI.Label(new Rect(catalogRect.x + 4f, catalogRect.y + 2f, 160f, 18f),
                new GUIContent("目錄庫", "手動蒐集的資產分組；先建目錄，再把 Project 的資產拖進某一列"),
                HGStyles.PanelHeader);
            catalogLibrary.Draw(catalogRect, catalogRect.y + 22f, CatalogLibraryView(), inlineName, drag,
                CatalogLibraryCommands());

            if (alone) return;
            DrawTokenAndAssetSections(new Rect(r.x, catalogBottom, r.width, r.yMax - catalogBottom));
            return;
        }

        DrawTokenAndAssetSections(r);
    }

    /// <summary>Token 區與資產區（含引用區）的上下分區。目錄庫存在時它拿到的是扣掉目錄庫之後的那一段。</summary>
    private void DrawTokenAndAssetSections(Rect r)
    {
        bool showToken = HasTokenSection;

        // 面板標題直接當 Token 區的標題：上面已經沒有第三種東西，再加一條區段標題只是重複佔 20px。
        // 圖沒宣告 Token 能力時整條標題都不畫，下一區從面板頂端開始，不留一條空標題佔位。
        if (showToken)
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 18f),
                new GUIContent("Token 庫", "對外端點；點一筆進入它自己的畫布，沒接來源時它就是具名常數"), HGStyles.PanelHeader);

        float top = showToken ? r.y + 22f : r.y;

        // 圖宣告不支援共用資產時，左欄就只有 Token 庫一區：沒有分隔把手、沒有資料夾鈕，
        // 也不去掃專案資產。留一個永遠空的清單比收掉它更難解釋——使用者會一直找「東西為什麼沒出現」。
        if (!HasAssetSection)
        {
            tokenLibrary.Draw(new Rect(r.x, top, r.width, r.yMax - top), top + 2f,
                TokenLibraryView(), TokenLibraryCommands(), inlineName, drag);
            return;
        }

        bool showRef = HasReferenceSection;
        // 把手只長在兩區之間：Token↔資產一條、資產↔引用一條，區沒出現那條把手也不存在。
        float handleCount = (showToken ? 1f : 0f) + (showRef ? 1f : 0f);
        float avail = r.yMax - top - ResizeHandleWidth * handleCount;
        // 視窗太矮時連各區的最小高度都放不下，這時平均分；寧可擠也不要出現負高度的 Rect。
        float share = avail / (1f + handleCount);
        float minToken = showToken ? Mathf.Min(MinTokenSection, share) : 0f;
        float minAsset = Mathf.Min(MinAssetSection, share);
        float minRef = showRef ? Mathf.Min(MinRefSection, share) : 0f;

        // 夾限後寫回欄位：拖曳是累加 delta，記著的值若跟畫面上的高度不同步，下一次拖會整段跳。
        // 先夾引用區（它是最下面那一段），剩下的才輪到 Token 區與資產區分。
        float maxRef = Mathf.Max(minRef, avail - minToken - minAsset);
        if (showRef) refSectionHeight = Mathf.Clamp(refSectionHeight, minRef, maxRef);
        float refHeight = showRef ? refSectionHeight : 0f;
        float maxToken = Mathf.Max(minToken, avail - minAsset - refHeight);
        if (showToken) tokenSectionHeight = Mathf.Clamp(tokenSectionHeight, minToken, maxToken);

        var tokenRect = new Rect(r.x, top, r.width, showToken ? tokenSectionHeight : 0f);
        var handle = new Rect(r.x, tokenRect.yMax, r.width, showToken ? ResizeHandleWidth : 0f);
        var assetRect = new Rect(r.x, handle.yMax, r.width, r.yMax - handle.yMax - refHeight
            - (showRef ? ResizeHandleWidth : 0f));

        if (showToken)
        {
            HandleLibrarySplitResize(handle, minToken, maxToken);
            tokenLibrary.Draw(tokenRect, tokenRect.y + 2f, TokenLibraryView(), TokenLibraryCommands(), inlineName, drag);
        }

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

        if (showToken) DrawResizeGrip(handle, false, resizingLibrarySplit);

        if (!showRef) return;

        var refHandle = new Rect(r.x, assetRect.yMax, r.width, ResizeHandleWidth);
        var refRect = new Rect(r.x, refHandle.yMax, r.width, r.yMax - refHandle.yMax);
        HandleRefSplitResize(refHandle, minRef, maxRef);
        referenceList.Draw(refRect, focus.AssetObject as ScriptableObject, ReferenceListCommands());
        DrawResizeGrip(refHandle, false, resizingRefSplit);
    }

    /// <summary>左欄上下分隔：拖動只改Token區高度，資產區吃剩下的。夾限與 Console 那條同一套。</summary>
    // ===== 目錄庫 =====
    // 目錄住在 Owner，不在 model.Data 的工作副本裡，所以這些命令都不走 MarkGraphChanged／Undo，
    // 每一條都是直接改 Owner 再 SetDirty。取消編輯不會還原目錄——這與共用資產庫一致。
    // 復原是另一件事：改之前先抄一份，真的有改到才 PushCatalogStep，記進與圖同一個 Undo 堆疊。
    // 命令失敗或沒動到東西時不記，否則堆疊裡會留下退回去什麼都看不出來的空步。

    private ICatalogOwner CatalogOwner => model?.Owner as ICatalogOwner;

    private HGCatalogLibraryView CatalogLibraryView() => new()
    {
        Catalogs = CatalogOwner?.Catalogs,
        // 畫法與拖放由編輯對象自己提供；沒實作就走面板的通用畫法，並且不接受拖放。
        Renderer = model?.Owner as IHGCatalogRenderer,
    };

    private HGCatalogLibraryCommands CatalogLibraryCommands() => new()
    {
        Create = CreateCatalog,
        Rename = RenameCatalog,
        Remove = DeleteCatalog,
        Add = AddToCatalog,
        RemoveItem = RemoveFromCatalog,
    };

    private string CreateCatalog()
    {
        var owner = CatalogOwner;
        if (owner == null) return null;

        object before = model.CaptureCatalogs();
        var catalog = owner.CreateCatalog();
        if (catalog == null) return null;

        model.PushCatalogStep(before);
        MarkOwnerDirty();
        return catalog.Id;
    }

    private bool RenameCatalog(string id, string name)
    {
        var owner = CatalogOwner;
        if (owner == null) return false;

        string oldName = HGReflect.FindCatalog(owner.Catalogs, id)?.Name;
        object before = model.CaptureCatalogs();
        if (!owner.RenameCatalog(id, name, out string error))
        {
            ShowNotification(new GUIContent(error));
            return false;
        }

        // 改成同一個名字也會回 true，那一步不必記。
        if (HGReflect.FindCatalog(owner.Catalogs, id)?.Name != oldName) model.PushCatalogStep(before);
        MarkOwnerDirty();
        return true;
    }

    private void DeleteCatalog(string id)
    {
        var owner = CatalogOwner;
        if (owner == null) return;
        if (HGReflect.FindCatalog(owner.Catalogs, id) == null) return;

        object before = model.CaptureCatalogs();
        owner.DeleteCatalog(id);
        model.PushCatalogStep(before);
        MarkOwnerDirty();
    }

    private void AddToCatalog(string id, IReadOnlyList<object> items)
    {
        var owner = CatalogOwner;
        if (owner == null) return;

        object before = model.CaptureCatalogs();
        int added = owner.AddToCatalog(id, items);
        // 一個都沒加進去通常是因為全都已經在裡面（型別不符由庫自己擋）。不說一聲會看起來像拖放壞掉。
        if (added == 0) ShowNotification(new GUIContent("這些項目已經在目錄裡了"));
        else model.PushCatalogStep(before);
        MarkOwnerDirty();
    }

    private void RemoveFromCatalog(string id, object item)
    {
        var owner = CatalogOwner;
        if (owner == null) return;

        int oldCount = HGReflect.FindCatalog(owner.Catalogs, id)?.Items?.Count ?? 0;
        object before = model.CaptureCatalogs();
        owner.RemoveFromCatalog(id, item);

        if ((HGReflect.FindCatalog(owner.Catalogs, id)?.Items?.Count ?? 0) != oldCount) model.PushCatalogStep(before);
        MarkOwnerDirty();
    }

    /// <summary>目錄改完直接寫 Owner。不進工作副本，所以也不碰 model.Dirty 與存檔交易。</summary>
    // 同時記 catalogDirty：寫進記憶體中的 SO 不等於寫進檔案，視窗要看得出有東西沒落盤。
    private void MarkOwnerDirty()
    {
        if (model?.Owner != null) EditorUtility.SetDirty(model.Owner);
        catalogDirty = true;
        UpdateUnsavedState();
        Repaint();
    }

    private HGTokenLibraryView TokenLibraryView() => new()
    {
        Tokens = HGModel.ReadTokens(CurrentTokens()),
        FocusedToken = focus.Token,
    };

    private HGTokenLibraryCommands TokenLibraryCommands() => new()
    {
        Rename = RenameTokenFromLibrary,
        Activate = ActivateToken,
        Duplicate = DuplicateToken,
        Remove = RemoveToken,
        Create = ShowCreateTokenMenu,
        IssueOf = TokenIssue,
    };

    private bool RenameTokenFromLibrary(GraphToken endpoint, string name)
    {
        if (model.RenameToken(endpoint, name, CurrentTokens(), out string error))
        {
            MarkGraphChanged();
            return true;
        }
        ShowNotification(new GUIContent(error));
        return false;
    }

    /// <summary>Token庫選了一筆：再點一次目前這格＝退出，不必去找返回鈕。</summary>
    private void ActivateToken(GraphToken endpoint)
    {
        if (ReferenceEquals(focus.Token, endpoint)) ExitToken();
        else EnterToken(endpoint);
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
    /// <summary>進入這個Token自己的畫布。</summary>
    private void JumpToToken(HGToken token) => EnterToken(token?.Token);

    /// <summary>新增Token：先選結果型別，因為它決定端點的取值欄位，之後不再更動。</summary>
    private void ShowCreateTokenMenu()
    {
        var scope = CurrentTokens();
        if (scope == null) return;

        var menu = new GenericMenu();
        foreach (var (resultType, slotType) in model.FormulaKinds())
        {
            var captured = slotType;
            // 用族名而非結果型別名：同結果型別的多個族（String / Key）否則會列出兩個一模一樣的項目。
            menu.AddItem(new GUIContent(HGReflect.SlotKindName(slotType)), false, () =>
            {
                var endpoint = model.CreateToken(scope, captured, out string error);
                if (endpoint == null)
                {
                    ShowNotification(new GUIContent(error));
                    return;
                }
                MarkGraphChanged();
                EnterToken(endpoint);
            });
        }
        menu.ShowAsContext();
    }
    /// <summary>複製一個Token，並進去複本的畫布——複製完通常就是要改它。</summary>
    private void DuplicateToken(GraphToken source)
    {
        var scope = CurrentTokens();
        if (scope == null) return;

        BreakUndoMerge();                   // 複製自成一步
        var copy = model.DuplicateToken(source, scope, out string error);
        if (copy == null) { ShowNotification(new GUIContent(error)); return; }

        MarkGraphChanged();
        ShowNotification(new GUIContent($"已複製成 '{copy.Name}'"));
        EnterToken(copy);
    }

    /// <summary>
    /// 移除一個Token：指著它的節點會一起清空（`HGModel.DeleteToken`）。
    /// 不問確認——Owner 焦點 Ctrl+Z 復原得回來，資產焦點按「取消」可整批捨棄，提示裡直接寫出來。
    /// </summary>
    private void RemoveToken(GraphToken endpoint)
    {
        if (endpoint == null) return;
        // scope 與引用數都要在 ExitToken 之前取：退出Token焦點會換掉「現在在編誰」，清單也就跟著換了。
        var scope = CurrentTokens();
        if (scope == null) return;
        int used = HGModel.CountReferences(endpoint, SlotsInCurrentGraph());
        string name = string.IsNullOrEmpty(endpoint.Name) ? "（未命名）" : endpoint.Name;

        BreakUndoMerge();                   // 刪除自成一步，不跟前一個編輯合併成同一次復原
        if (ReferenceEquals(focus.Token, endpoint)) ExitToken();
        model.DeleteToken(endpoint, scope, CurrentCarrierScope());
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

    /// <summary>資產庫選了一筆：再點一次目前這格＝退出（在它的Token子畫布時先回到資產本體，由 EnterAsset 處理）。</summary>
    private void ActivateAsset(ScriptableObject asset, Type slotType)
    {
        bool isFocus = focus.Kind == HGFocusKind.Asset && focus.AssetObject == asset;
        if (isFocus && focus.Token == null) LeaveAsset();
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

        Type actionSlotType = model.RootItemSlotType;
        Type actionAssetType = actionSlotType != null ? HGReflect.ActionAssetType(actionSlotType) : null;
        if (actionAssetType != null) result.Add((actionAssetType, actionSlotType));
        return result;
    }


    /// <summary>這個標註有沒有問題。標註的問題掛在被標註節點的內容物件上（見 HGValidator）。</summary>
    private bool HasTokenIssue(HGToken token, out string reason, out bool isError)
    {
        reason = null; isError = false;
        object target = token?.Token;
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
        var groups = model.ReadRootGroups();

        foreach (var timing in model.AvailableRootKeys)
        {
            HGRootGroupView group = null;
            foreach (var candidate in groups)
                if (Equals(candidate.RootKey, timing)) { group = candidate; break; }

            var captured = timing;
            if (group == null)
            {
                menu.AddItem(new GUIContent($"{timing}（尚未建立）"), false, () => AddTimingGroup(captured, createPos));
                continue;
            }

            int actionCount = group.Items?.Count ?? 0;
            int errors = ErrorsOfGroup(group);
            string label = errors > 0
                ? $"{timing} ({actionCount})　{errors} 個錯誤"
                : $"{timing} ({actionCount})";
            menu.AddItem(new GUIContent(label), false, () => JumpToTiming(captured));
        }
        menu.ShowAsContext();
    }

    private int ErrorsOfGroup(HGRootGroupView group)
    {
        if (group?.Items == null) return 0;
        int errors = 0;
        for (int i = 0; i < group.Items.Count; i++)
        {
            var f = new HGFocus
            {
                Kind = HGFocusKind.Action, RootKey = group.RootKey,
                ActionList = group.Items, ActionIndex = i, ActionSlot = group.Items[i] as GraphSlotBase,
            };
            report.CountFor(f, out int e, out _);
            errors += e;
        }
        return errors;
    }

    /// <summary>時機節點的新增入口。已經存在的時機一律停用——一個時機只能有一顆節點。</summary>
    private void AddTimingMenuItems(GenericMenu menu, string prefix, Vector2 createPos)
    {
        foreach (var timing in model.AvailableRootKeys)
        {
            var content = new GUIContent(prefix + timing);
            if (model.HasRoot(timing)) { menu.AddDisabledItem(content); continue; }
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
        if (model.HasRoot(timing))
        {
            ShowNotification(new GUIContent($"{timing} 已經有節點了"));
            return;
        }

        BreakUndoMerge();
        var group = model.AddRoot(timing);
        if (group?.Root == null)
        {
            Debug.LogWarning($"[GraphKit] 建立{RootNoun}群組 '{timing}' 失敗：識別值型別與這張圖不符。");
            return;
        }

        // 建在使用者按下右鍵的位置，不要丟去自動排版的角落。
        HGReflect.SetHeadPos(group.Root, SnapToGrid(pos));
        if (focus.Kind != HGFocusKind.Root) SetFocus(AllRootsFocus());
        selectedIds.Clear();
        selectedIds.Add(HGGraph.GroupHeadId(model, group.Root));
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
        foreach (var g in model.ReadRootGroups())
        {
            if (!ReferenceEquals(g.Root, node.Obj)) continue;
            model.RemoveRoot(g);
            break;
        }
        selectedIds.Remove(node.Id);
        Invalidate();
        Repaint();
    }

    /// <summary>跳到某顆時機節點。同一張畫布，所以只是把視野移過去，不換焦點。</summary>
    private void JumpToTiming(object timing)
    {
        if (focus.Kind != HGFocusKind.Root) SetFocus(AllRootsFocus());
        foreach (var g in model.ReadRootGroups())
        {
            if (!Equals(g.RootKey, timing)) continue;
            pendingCenterTarget = g.Root;
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
        BindInSession(user);
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
            if (focus.Kind != HGFocusKind.Root) SetFocus(AllRootsFocus());
        }
        else if (!issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
