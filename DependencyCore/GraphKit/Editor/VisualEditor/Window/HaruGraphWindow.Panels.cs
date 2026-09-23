namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 庫區的左右停駐、排序、顯示偏好與內容命令，以及底部 Console。
/// </summary>
public partial class HaruGraphWindow
{
    // ===== 庫區版面 =====

    private bool LibraryAvailable(LibraryKind kind) => model?.Owner != null && (kind switch
    {
        LibraryKind.Property => HasPropertySection,
        LibraryKind.Token => HasTokenSection,
        LibraryKind.Asset => HasAssetSection,
        _ => false,
    });

    private static string LibraryTitle(LibraryKind kind) => kind switch
    {
        LibraryKind.Property => "變數庫",
        LibraryKind.Token => "Token 庫",
        _ => "資產庫",
    };

    private List<LibraryKind> LibrariesOnSide(bool right)
    {
        var result = new List<LibraryKind>();
        for (int i = 0; i < libraryVisible.Length; i++)
            if (libraryVisible[i] && libraryRight[i] == right && LibraryAvailable((LibraryKind)i))
                result.Add((LibraryKind)i);
        result.Sort((a, b) => libraryOrder[(int)a].CompareTo(libraryOrder[(int)b]));
        return result;
    }

    private bool HasLibraryOnSide(bool right)
    {
        for (int i = 0; i < libraryVisible.Length; i++)
            if (libraryVisible[i] && libraryRight[i] == right && LibraryAvailable((LibraryKind)i)) return true;
        return false;
    }

    private GenericMenu LibraryMenu(LibraryKind? target = null)
    {
        var menu = new GenericMenu();
        if (target.HasValue)
        {
            var kind = target.Value;
            int index = (int)kind;
            bool right = libraryRight[index];
            foreach (bool side in new[] { false, true })
            {
                var label = new GUIContent("停駐位置/" + (side ? "右側" : "左側"));
                if (side == right) menu.AddDisabledItem(label, true);
                else menu.AddItem(label, false, () =>
                {
                    libraryRight[index] = side;
                    int last = 0;
                    for (int i = 0; i < libraryOrder.Length; i++) last = Mathf.Max(last, libraryOrder[i]);
                    libraryOrder[index] = last + 1;
                    SaveLibraryLayout();
                });
            }
            menu.AddSeparator("");
            var peers = LibrariesOnSide(right);
            int position = peers.IndexOf(kind);
            AddLibraryMove(menu, "上移", kind, peers, position - 1);
            AddLibraryMove(menu, "下移", kind, peers, position + 1);
            menu.AddSeparator("");
        }
        for (int i = 0; i < libraryVisible.Length; i++)
        {
            int index = i;
            var kind = (LibraryKind)i;
            if (!LibraryAvailable(kind)) continue;
            menu.AddItem(new GUIContent((target.HasValue ? "顯示庫/" : "") + LibraryTitle(kind)),
                libraryVisible[i], () =>
                {
                    libraryVisible[index] = !libraryVisible[index];
                    SaveLibraryLayout();
                });
        }
        return menu;
    }

    private void AddLibraryMove(GenericMenu menu, string title, LibraryKind kind, List<LibraryKind> peers, int destination)
    {
        if (destination < 0 || destination >= peers.Count)
        {
            menu.AddDisabledItem(new GUIContent(title));
            return;
        }
        int index = (int)kind;
        int other = (int)peers[destination];
        menu.AddItem(new GUIContent(title), false, () =>
        {
            (libraryOrder[index], libraryOrder[other]) = (libraryOrder[other], libraryOrder[index]);
            SaveLibraryLayout();
        });
    }

    private void LoadLibraryLayout()
    {
        rightWidth = EditorPrefs.GetFloat("HaruGraph.RightWidth", DefaultLeftWidth);
        for (int i = 0; i < libraryVisible.Length; i++)
        {
            string key = "HaruGraph.Library." + (LibraryKind)i;
            libraryVisible[i] = EditorPrefs.GetBool(key + ".Visible", true);
            libraryRight[i] = EditorPrefs.GetBool(key + ".Right", false);
            libraryOrder[i] = EditorPrefs.GetInt(key + ".Order", i);
            float defaultHeight = i == 0 ? EditorPrefs.GetFloat(PrefPropertySection, DefaultPropertySection)
                : i == 1 ? EditorPrefs.GetFloat(PrefTokenSection, DefaultTokenSection) : 240f;
            libraryHeights[i] = EditorPrefs.GetFloat(key + ".Height", defaultHeight);
        }
    }

    private void SaveLibraryLayout()
    {
        // 視圖偏好與文件交易無關，不標 Dirty、不建立 Undo。
        var order = new List<int> { 0, 1, 2 };
        order.Sort((a, b) => libraryOrder[a] != libraryOrder[b]
            ? libraryOrder[a].CompareTo(libraryOrder[b]) : a.CompareTo(b));
        for (int i = 0; i < order.Count; i++) libraryOrder[order[i]] = i;
        EditorPrefs.SetFloat("HaruGraph.RightWidth", rightWidth);
        for (int i = 0; i < libraryVisible.Length; i++)
        {
            string key = "HaruGraph.Library." + (LibraryKind)i;
            EditorPrefs.SetBool(key + ".Visible", libraryVisible[i]);
            EditorPrefs.SetBool(key + ".Right", libraryRight[i]);
            EditorPrefs.SetInt(key + ".Order", libraryOrder[i]);
            EditorPrefs.SetFloat(key + ".Height", libraryHeights[i]);
        }
        resizingLibrary = -1;
        Repaint();
    }

    private float LibraryMinimum(LibraryKind kind) => kind switch
    {
        LibraryKind.Property => 22f + MinPropertySection,
        LibraryKind.Token => 22f + MinTokenSection,
        _ => MinAssetSection + (HasReferenceSection ? MinRefSection + ResizeHandleWidth : 0f),
    };

    /// <summary>同側依偏好排序，最後一庫填滿剩餘高度。</summary>
    private void DrawLibraryPanel(Rect r, bool right)
    {
        HGStyles.Fill(r, HGStyles.Panel);
        HGStyles.Frame(r, HGStyles.NodeBorder);

        var libraries = LibrariesOnSide(right);
        float remaining = Mathf.Max(0f, r.height - ResizeHandleWidth * (libraries.Count - 1));
        float y = r.y;
        for (int i = 0; i < libraries.Count; i++)
        {
            var kind = libraries[i];
            int index = (int)kind;
            bool last = i == libraries.Count - 1;
            float share = remaining / (libraries.Count - i);
            float minimum = Mathf.Min(LibraryMinimum(kind), share);
            float reserved = 0f;
            for (int j = i + 1; j < libraries.Count; j++) reserved += Mathf.Min(LibraryMinimum(libraries[j]), share);
            float maximum = Mathf.Max(minimum, remaining - reserved);
            float height = last ? remaining : Mathf.Clamp(libraryHeights[index], minimum, maximum);
            var rect = new Rect(r.x, y, r.width, height);
            var handle = new Rect(r.x, rect.yMax, r.width, ResizeHandleWidth);
            if (!last) HandleLibraryResize(handle, index, height, minimum, maximum);
            DrawLibrarySection(rect, kind);
            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                LibraryMenu(kind).ShowAsContext();
                Event.current.Use();
            }
            if (!last) DrawResizeGrip(handle, false, resizingLibrary == index);
            remaining -= height;
            y = handle.yMax;
        }
    }

    private void DrawLibrarySection(Rect r, LibraryKind kind)
    {
        if (kind == LibraryKind.Asset)
        {
            DrawAssetSection(r);
            return;
        }
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, Mathf.Max(0f, r.width - 8f), 18f),
            LibraryTitle(kind), HGStyles.PanelHeader);
        if (kind == LibraryKind.Property)
            propertyLibrary.Draw(r, r.y + 22f, PropertyLibraryView(), PropertyLibraryCommands(), inlineName, drag);
        else
            tokenLibrary.Draw(new Rect(r.x, r.y + 22f, r.width, Mathf.Max(0f, r.height - 22f)), r.y + 24f,
                TokenLibraryView(), TokenLibraryCommands(), inlineName, drag);
    }

    /// <summary>引用清單隸屬資產庫，跟著資產庫停駐與顯示。</summary>
    private void DrawAssetSection(Rect r)
    {
        bool showRef = HasReferenceSection;
        float handleCount = showRef ? 1f : 0f;
        float avail = Mathf.Max(0f, r.height - ResizeHandleWidth * handleCount);
        // 視窗太矮時連各區的最小高度都放不下，這時平均分；寧可擠也不要出現負高度的 Rect。
        float share = avail / (1f + handleCount);
        float minAsset = Mathf.Min(MinAssetSection, share);
        float minRef = showRef ? Mathf.Min(MinRefSection, share) : 0f;

        // 夾限後寫回欄位：拖曳是累加 delta，記著的值若跟畫面上的高度不同步，下一次拖會整段跳。
        // 引用區在資產庫底部，其餘高度給資產清單。
        float maxRef = Mathf.Max(minRef, avail - minAsset);
        if (showRef) refSectionHeight = Mathf.Clamp(refSectionHeight, minRef, maxRef);
        float refHeight = showRef ? refSectionHeight : 0f;
        var assetRect = new Rect(r.x, r.y, r.width, Mathf.Max(0f, avail - refHeight));

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

        assetLibrary.Draw(assetRect, assetRect.y + 24f, AssetLibraryView(), inlineName, drag, AssetLibraryCommands());

        if (!showRef) return;

        var refHandle = new Rect(r.x, assetRect.yMax, r.width, ResizeHandleWidth);
        var refRect = new Rect(r.x, refHandle.yMax, r.width, r.yMax - refHandle.yMax);
        HandleRefSplitResize(refHandle, minRef, maxRef);
        referenceList.Draw(refRect, focus.AssetObject as ScriptableObject, ReferenceListCommands());
        DrawResizeGrip(refHandle, false, resizingRefSplit);
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
        Move = MoveToken,
    };

    private void MoveToken(GraphToken token, GraphToken target)
    {
        var scope = CurrentTokens();
        if (scope == null) return;
        int from = scope.IndexOf(token);
        int to = scope.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        BreakUndoMerge();
        scope.RemoveAt(from);
        scope.Insert(to, token);
        MarkGraphChanged();
        BreakUndoMerge();
    }

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

    // ===== Property 庫 =====
    // 定義住圖的工作副本，所以每個命令都走 MarkGraphChanged，跟著存檔交易與圖的 Undo 堆疊；
    // 這和目錄庫（內容住 Owner、立即寫檔）不同，不要照抄那一邊的收尾。

    // 庫只列 ProtoProperty：庫的用途是編「作者填的初始內容」，一般 Property 沒有那種內容，
    // 列在這裡只會是一排點不出東西的格子。一般 Property 在畫布上就地建立與選取。
    private HGPropertyLibraryView PropertyLibraryView() => new()
    {
        Properties = HGModel.ReadProperties(CurrentProperties(), proto: true),
    };

    private HGPropertyLibraryCommands PropertyLibraryCommands() => new()
    {
        Rename = RenamePropertyFromLibrary,
        Remove = RemoveProperty,
        Create = ShowCreatePropertyMenu,
        IssueOf = PropertyIssue,
        Move = MoveProperty,
        SetInitialValue = SetPropertyInitialValue,
        AddInitialItems = AddPropertyInitialItems,
        RemoveInitialItem = RemovePropertyInitialItem,
        MoveInitialItem = MovePropertyInitialItem,
    };

    /// <summary>目前這張圖的 Property 定義。沒有這個能力的文件回 null，面板不會被畫出來。</summary>
    private List<GraphProperty> CurrentProperties()
        => focus.Kind == HGFocusKind.Asset ? focus.AssetProperties : HGReflect.Properties(model?.Data);

    private bool RenamePropertyFromLibrary(GraphProperty property, string name)
    {
        if (model.RenameProperty(property, name, CurrentProperties(), out string error))
        {
            MarkGraphChanged();
            return true;
        }
        ShowNotification(new GUIContent(error));
        return false;
    }

    private void MoveProperty(GraphProperty property, GraphProperty target)
    {
        var scope = CurrentProperties();
        if (scope == null) return;
        int from = scope.IndexOf(property);
        int to = scope.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        BreakUndoMerge();
        scope.RemoveAt(from);
        scope.Insert(to, property);
        MarkGraphChanged();
        BreakUndoMerge();
    }

    /// <summary>移除一顆 Property：指著它的讀取與寫入節點一起清空，變成空節點由驗證擋住。</summary>
    private void RemoveProperty(GraphProperty property)
    {
        var scope = CurrentProperties();
        if (property == null || scope == null) return;

        BreakUndoMerge();                   // 刪除自成一個復原步驟
        int references = HGModel.CountReferences(property, SlotsInCurrentGraph());
        model.DeleteProperty(property, scope, CurrentCarrierScope());
        MarkGraphChanged();
        BreakUndoMerge();

        ShowNotification(new GUIContent(references > 0
            ? $"已移除 '{property.Name}'，清空了 {references} 處引用；Ctrl+Z 可復原"
            : $"已移除 '{property.Name}'；Ctrl+Z 可復原"));
    }

    /// <summary>庫的「＋ 新增」只建 ProtoProperty：型別決定族，建立後不再更動。</summary>
    // 一般 Property 不從這裡建——它沒有可編的初始內容，建在庫裡等於在庫裡放一格點不開的東西。
    private void ShowCreatePropertyMenu()
    {
        var scope = CurrentProperties();
        if (scope == null) return;

        var menu = new GenericMenu();
        foreach (var (slotType, path) in HGTypeCatalog.FormulaKindOptions(model.FormulaKinds()))
        {
            var captured = slotType;
            menu.AddItem(new GUIContent(path), false,
                () => CreateProperty(scope, captured, true));
        }
        menu.ShowAsContext();
    }

    private GraphProperty CreateProperty(List<GraphProperty> scope, Type slotType, bool proto)
    {
        var property = model.CreateProperty(scope, slotType, proto, out string error);
        if (property == null)
        {
            ShowNotification(new GUIContent(error));
            return null;
        }
        MarkGraphChanged();
        return property;
    }

    /// <summary>從畫布建一顆節點私有的 LocalProperty 並讓這個節點指向它。</summary>
    private void CreatePropertyForNode(HGNodeView node, Type slotType)
    {
        if (node?.Carrier == null) return;

        BreakUndoMerge();
        GraphProperty property = model.CreateLocalProperty(slotType, out string error);
        if (property == null) { ShowNotification(new GUIContent(error)); return; }

        node.Carrier.SetLocalProperty(property);
        Invalidate();
        MarkGraphChanged();
        BreakUndoMerge();
    }

    /// <summary>改 ProtoProperty 的初始內容。寫的是型別宣告欄位的常數，不是任何執行期的目前值。</summary>
    private void SetPropertyInitialValue(GraphProperty property, object value)
    {
        if (property?.Slot == null) return;
        property.Slot.DefaultObject = value;
        MarkGraphChanged();
    }

    /// <summary>清單型初始內容的可寫容器。還沒有清單時就地建一個並寫回欄位。</summary>
    // 就地改容器而不是每次換一份新的：目前值在初始化時與它共用引用，換掉容器會讓已經取得引用的
    // 執行期讀取者指到舊清單（見 §2.2 的直接共用語意）。
    private static IList PropertyInitialItems(GraphProperty property, bool create)
    {
        FormulaSlotBase slot = property?.Slot;
        if (slot == null) return null;
        if (slot.DefaultObject is IList existing) return existing;
        if (!create) return null;

        Type resultType = slot.ResultType;
        if (resultType == null || !HGReflect.IsList(resultType, out _)) return null;
        if (HGReflect.CreateInstance(resultType) is not IList created) return null;

        slot.DefaultObject = created;
        return created;
    }

    private void AddPropertyInitialItems(GraphProperty property, IReadOnlyList<UnityEngine.Object> items)
    {
        if (items == null || items.Count == 0) return;
        IList list = PropertyInitialItems(property, true);
        if (list == null) { ShowNotification(new GUIContent("這個型別的初始內容不是清單")); return; }

        BreakUndoMerge();
        int added = 0;
        foreach (UnityEngine.Object item in items)
        {
            if (item == null || list.Contains(item)) continue;   // 去重比參照：同一顆資產加兩次沒有意義
            list.Add(item);
            added++;
        }
        if (added == 0) return;                                  // 全都已經在裡面，不留一個退回去看不出差別的 Undo 步
        MarkGraphChanged();
        BreakUndoMerge();
    }

    private void RemovePropertyInitialItem(GraphProperty property, int index)
    {
        IList list = PropertyInitialItems(property, false);
        if (list == null || index < 0 || index >= list.Count) return;

        BreakUndoMerge();
        list.RemoveAt(index);
        MarkGraphChanged();
        BreakUndoMerge();
    }

    private void MovePropertyInitialItem(GraphProperty property, int from, int to)
    {
        IList list = PropertyInitialItems(property, false);
        if (list == null || from == to) return;
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count) return;

        BreakUndoMerge();
        object item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        MarkGraphChanged();
        BreakUndoMerge();
    }

    private (string reason, bool isError) PropertyIssue(HGProperty property)
        => HasPropertyIssue(property, out string reason, out bool isError) ? (reason, isError) : (null, false);

    private bool HasPropertyIssue(HGProperty property, out string reason, out bool isError)
    {
        reason = null; isError = false;
        object target = property?.Property;
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

    /// <summary>Token庫選了一筆：再點一次目前這格＝退出，不必去找返回鈕。</summary>
    private void ActivateToken(GraphToken endpoint)
    {
        if (ReferenceEquals(focus.Token, endpoint)) ExitToken();
        else EnterToken(endpoint);
    }

    private (string reason, bool isError) TokenIssue(HGToken token)
        => HasTokenIssue(token, out string reason, out bool isError) ? (reason, isError) : (null, false);

    private void HandleLibraryResize(Rect handle, int index, float height, float min, float max)
    {
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition))
        {
            resizingLibrary = index;
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag && resizingLibrary == index)
        {
            libraryHeights[index] = Mathf.Clamp(height + e.delta.y, min, max);
            e.Use();
            Repaint();
            return;
        }
        if (e.type == EventType.MouseUp && resizingLibrary == index)
        {
            SaveLibraryLayout();
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
        foreach (var (slotType, path) in HGTypeCatalog.FormulaKindOptions(model.FormulaKinds()))
        {
            var captured = slotType;
            menu.AddItem(new GUIContent(path), false, () =>
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

    private HGAssetLibraryCommands AssetLibraryCommands() => new()
    {
        Rename = RenameAssetFile,
        Activate = ActivateAsset,
        Move = (asset, target) => { if (HGAssetIndex.Move(asset, target)) Repaint(); },
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

        int count = 0;
        foreach (var group in model.ReadRootGroups())
            if (Equals(group.Root, node.Obj))
            {
                count = group.Items?.Count ?? 0;
                break;
            }
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
            if (!HGOwnerValidation.CanVerify(so)) continue;

            bool now = HGOwnerValidation.Verify(so, out bool changed);

            if (now) ok++; else fail++;
            if (!changed) continue;

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

        // 查已儲存文件的驗證旗標；不把驗證、未存修改與執行混為同一個狀態。
        string warning = !isAsset && model != null && !model.IsStoredDocumentValidated
            ? "已儲存圖尚未通過驗證"
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
