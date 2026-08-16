namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 換來源：Token／Asset 節點建立與拖放、型別替換、抽出 Token／Asset，以及節點右鍵選單。
/// </summary>
public partial class ActionGraphWindow
{
    private void DropTokenOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddTokenReferenceNode(dragToken, graphMouse);
            return;
        }
        if (row.IsActionSlot)
        {
            ShowNotification(new GUIContent("只能拖到參數欄位上"));
            return;
        }
        if (row.ResultType != dragToken.ResultType)
        {
            ShowNotification(new GUIContent($"型別不符：這個欄位是 {AGReflect.ResultTypeName(row.ResultType)}"));
            return;
        }
        AssignToken(row.Slot, dragToken.Key);
    }

    private void DropAssetOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddAssetReferenceNode(dragAsset, graphMouse);
            return;
        }
        if (!CanAssignAsset(row, dragAsset))
        {
            ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
            return;
        }
        AssignAsset(row.Slot, dragAsset);
    }

    /// <summary>把左欄 Token 拖到空白畫布：建立一個沒有連線的變數載體，放進候選池等人來接。</summary>
    private void AddTokenReferenceNode(AGToken token, Vector2 graphMouse)
    {
        if (token == null) return;
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetToken(token.Key);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>把 Project 的共用資產拖到空白畫布：同樣建立一個候選載體。</summary>
    private void AddAssetReferenceNode(UnityEngine.Object asset, Vector2 graphMouse)
    {
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        if (asset is not ScriptableObject so)
        {
            ShowNotification(new GUIContent("資產尚未存入 Project，無法建立節點"));
            return;
        }
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetAsset(so);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>處理從 Project 拖進來的公式／動作資產；落在參數列就直接接上，空白處就建立來源節點。</summary>
    private bool HandleAssetDrag(Event e, Vector2 graphMouse)
    {
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return false;

        UnityEngine.Object asset = null;
        foreach (var candidate in DragAndDrop.objectReferences)
        {
            if (candidate is not ScriptableObject so || !IsSharedAsset(so)) continue;
            asset = so;
            break;
        }
        if (asset == null) return false;

        var row = RowAt(graphMouse, out _);
        bool canAssign = row != null && CanAssignAsset(row, asset);
        bool canCreate = row == null && CanCreateReferenceNode();
        DragAndDrop.visualMode = canAssign || canCreate ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        if (e.type == EventType.DragUpdated)
        {
            e.Use();
            return true;
        }

        if (canAssign) AssignAsset(row.Slot, asset);
        else if (canCreate) AddAssetReferenceNode(asset, graphMouse);
        else if (row == null) ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
        else ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
        DragAndDrop.AcceptDrag();
        e.Use();
        return true;
    }

    // 有頭端才有候選池可放；資產焦點的頭端是資產本身，同樣有一份。
    private bool CanCreateReferenceNode() => focus.Head != null && focus.RootSlot != null;

    private static bool CanAssignAsset(AGRow row, UnityEngine.Object asset)
    {
        if (row?.Slot == null || asset == null) return false;
        Type accepted = row.IsActionSlot
            ? AGReflect.ActionAssetType(row.Slot.GetType())
            : AGReflect.AssetType(row.Slot.GetType());
        return accepted != null && accepted.IsInstanceOfType(asset);
    }

    private void ShowNodeSourceSelector(AGNode node, Rect selector)
    {
        if (node == null) return;
        var options = new List<AGSourceOption>();
        object slot = SourceSlot(node);
        bool isAction = slot != null
            ? AGReflect.IsActionSlotType(slot.GetType())
            : node.IsActionNode || (node.IsAssetNode && node.ResultType == null);

        // 同一個結果型別的 Formula／Token／Asset 一律可以互換，包含候選池裡沒有父欄位的節點：
        // 型別關係靠「代表性的 Slot 型別」推導，推不出來才退回用目前內容的基底型別。
        Type slotType = slot?.GetType() ?? RepresentativeSlotType(node);
        Type baseType = slotType != null
            ? (isAction ? AGReflect.ActionBaseType(slotType) : AGReflect.FormulaBaseType(slotType))
            : AGReflect.NodeBaseType(node.Obj?.GetType());
        if (baseType != null)
        {
            string kind = isAction ? "Action" : "Formula";
            foreach (var type in AGTypeCatalog.Concrete(baseType))
            {
                Type captured = type;
                options.Add(new AGSourceOption
                {
                    Group = kind + "/" + AGReflect.TypeCategory(type),
                    Name = AGReflect.TypeName(type),
                    IsCurrent = node.Obj?.GetType() == type,
                    Apply = () => ReplaceNodeType(node, captured),
                });
            }
        }

        Type resultType = !isAction
            ? (slotType != null ? AGReflect.ResultType(slotType) : node.ResultType)
            : null;
        if (!isAction && resultType != null)
        {
            foreach (var token in model.ReadTokens())
            {
                if (token.ResultType != resultType || string.IsNullOrWhiteSpace(token.Key)) continue;
                string key = token.Key;
                options.Add(new AGSourceOption
                {
                    Group = "Token",
                    Name = key,
                    IsCurrent = node.TokenKey == key,
                    Apply = () => ChangeNodeToToken(node, key),
                });
            }
        }

        {
            foreach (var entry in AGAssetIndex.Entries)
            {
                if (entry.Asset == null || !CanReplaceAssetNode(node, entry.Asset)) continue;
                var asset = entry.Asset;
                options.Add(new AGSourceOption
                {
                    Group = "Asset",
                    Name = entry.Name,
                    IsCurrent = node.Asset == asset,
                    Apply = () => ChangeNodeToAsset(node, asset),
                });
            }
        }

        AGTypeCatalog.ShowSourcePicker(selector, options);
    }

    private object SourceSlot(AGNode node)
    {
        if (node?.ParentSlot != null) return node.ParentSlot;
        if (graph?.Links == null) return null;
        foreach (var link in graph.Links)
            if (ReferenceEquals(link.Target, node) && link.ParentRow?.Slot != null)
                return link.ParentRow.Slot;
        return null;
    }

    /// <summary>換節點型別＝換載體裡的內容。載體 Id、座標、備註與所有連入邊都不動，這才是真的「替換」。</summary>
    private void ReplaceNodeType(AGNode node, Type type)
    {
        if (node?.Carrier == null || type == null || node.Obj?.GetType() == type) return;
        if (AGReflect.CreateInstance(type) is not ActionSystemNode instance) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetBody(instance);
        Invalidate();
        Repaint();
    }

    private bool CanReplaceAssetNode(AGNode node, ScriptableObject asset)
    {
        if (node?.ParentSlot != null)
        {
            Type slotType = node.ParentSlot.GetType();
            Type accepted = AGReflect.IsActionSlotType(slotType)
                ? AGReflect.ActionAssetType(slotType)
                : AGReflect.AssetType(slotType);
            return accepted != null && accepted.IsInstanceOfType(asset);
        }

        bool hasLink = false;
        if (graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.Target, node)) continue;
                hasLink = true;
                if (!CanAssignAsset(link.ParentRow, asset)) return false;
            }
        }
        if (hasLink) return true;

        Type acceptedType = AcceptedAssetType(node);
        return acceptedType != null && acceptedType.IsInstanceOfType(asset);
    }

    /// <summary>
    /// 這個節點「相當於掛在哪一種 Slot 上」。候選池的節點沒有父欄位，型別關係只能這樣推：
    /// 父欄位 → 連入邊的欄位 → 目前資產對應的欄位 → 同結果型別的 Token 宣告。
    /// </summary>
    private Type RepresentativeSlotType(AGNode node)
    {
        if (node == null) return null;
        if (node.ParentSlot != null) return node.ParentSlot.GetType();

        if (graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.Target, node) || link.ParentRow?.Slot == null) continue;
                return link.ParentRow.Slot.GetType();
            }
        }

        if (node.Asset is ScriptableObject asset)
        {
            Type fromAsset = SlotTypeForAsset(asset, AssetSlotTypes());
            if (fromAsset != null) return fromAsset;
        }

        if (node.ResultType != null)
        {
            foreach (var (resultType, list) in model.TokenKinds())
            {
                if (resultType != node.ResultType) continue;
                Type entryType = list.GetType().GetGenericArguments()[0];
                return (AGReflect.CreateInstance(entryType) as ITokenEntry)?.Slot?.GetType();
            }
        }
        return null;
    }

    private Type AcceptedAssetType(AGNode node)
    {
        Type slotType = RepresentativeSlotType(node);
        if (slotType == null) return null;
        return AGReflect.IsActionSlotType(slotType)
            ? AGReflect.ActionAssetType(slotType)
            : AGReflect.AssetType(slotType);
    }

    /// <summary>節點改接變數。節點是共用來源時，所有指著它的欄位一起改——這正是共用的語意。</summary>
    private void ChangeNodeToToken(AGNode node, string key)
    {
        if (node?.Carrier == null || string.IsNullOrWhiteSpace(key) || node.TokenKey == key) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetToken(key);
        Invalidate();
        Repaint();
    }

    private void ChangeNodeToAsset(AGNode node, ScriptableObject asset)
    {
        if (node?.Carrier == null || asset == null || node.Asset == asset) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        DetachChildSourcesForReplacement(node);
        node.Carrier.SetAsset(asset);
        Invalidate();
        Repaint();
    }

    /// <summary>換掉節點內容前，先把它的直接來源拆散：子載體原位變成候選，完整子樹與座標都留著。</summary>
    private void DetachChildSourcesForReplacement(AGNode node)
    {
        if (node?.Obj == null) return;
        foreach (var row in AGGraph.AllRows(node.Rows))
        {
            if (row.Kind != AGRowKind.Slot || row.Slot == null) continue;
            var child = AGReflect.GetNode(row.Slot);
            if (child == null) continue;
            AGReflect.SetNode(row.Slot, null);
            model.AddOrphan(child);
        }
    }

    /// <summary>
    /// 取一個可以直接改內容的載體：欄位獨佔且不是內嵌內容時就地沿用；
    /// 共用中或還掛著內嵌子樹時另建一個，舊載體整棵留成候選。
    /// </summary>
    private GraphNode SoloSource(object slot)
    {
        var carrier = AGReflect.GetNode(slot);
        bool reusable = carrier != null && carrier.Kind != NodeKind.Inline && CountCarrierUsers(carrier) <= 1;
        return reusable ? carrier : NewSource(slot);
    }

    private int CountCarrierUsers(GraphNode carrier)
    {
        if (carrier == null) return 0;
        int n = 0;
        foreach (var slot in model.AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), carrier)) n++;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), carrier)) n++;
        return n;
    }

    /// <summary>把欄位接到共用變數；原本接著的公式節點留成候選。</summary>
    private void AssignToken(object slot, string key)
    {
        PreserveVisibleNodePositions();
        SoloSource(slot).SetToken(key);
        Invalidate();
    }

    private void AssignAsset(object slot, UnityEngine.Object asset)
    {
        if (asset is not ScriptableObject so) return;
        PreserveVisibleNodePositions();
        SoloSource(slot).SetAsset(so);
        Invalidate();
    }

    /// <summary>拓樸變動前固定目前畫面座標，避免 AutoLayout 因根節點順序改變而重排既有 Node。</summary>
    private void PreserveVisibleNodePositions()
    {
        if (graph?.Nodes == null) return;
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) continue;
            model.SetPosition(node.Id, node.Pos);
        }
    }

    /// <summary>切到這個變數節點指向的 Token 焦點。</summary>
    private void FocusToken(AGNode node)
    {
        foreach (var t in model.ReadTokens())
        {
            if (t.Key != node.TokenKey || t.ResultType != node.ResultType) continue;
            SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = t });
            Repaint();
            return;
        }
        ShowNotification(new GUIContent("找不到這個變數"));
    }

    private void DeleteNode(AGNode node, bool pushUndo = true)
    {
        if (node == null) return;
        if (node.IsRoot)
        {
            ShowNotification(new GUIContent("根節點不可刪除；要換內容請按右鍵"));
            return;
        }
        if (node.Carrier == null) return;
        if (pushUndo) model.BreakUndoMerge();
        PreserveVisibleNodePositions();

        // 刪節點＝斷開所有指著這個載體的欄位，並把它移出候選池。
        foreach (var slot in model.AllSlots())
            if (ReferenceEquals(AGReflect.GetNode(slot), node.Carrier)) AGReflect.SetNode(slot, null);
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), node.Carrier))
            AGReflect.SetNode(focus.AssetHostSlot, null);
        model.RemoveOrphan(node.Carrier);

        selectedIds.Remove(node.Id);
        Invalidate();
    }

    // ===== 右鍵選單 =====

    private void ShowSlotMenu(AGRow row)
    {
        var slot = row.Slot;
        var menu = new GenericMenu();
        int useType = AGReflect.UseType(slot);
        // GenericMenu 的回呼在 OnGUI 之外執行，Event.current 會是 null → 先把滑鼠位置抓下來。
        var menuPos = Event.current.mousePosition;

        if (!row.IsActionSlot)
        {
            menu.AddItem(new GUIContent("設為常數"), useType == 0, () => CutLink(slot));
        }

        menu.AddItem(new GUIContent(row.IsActionSlot ? "指定動作…" : "指定公式…"), false, () =>
        {
            var baseType = row.IsActionSlot
                ? AGReflect.ActionBaseType(slot.GetType())
                : AGReflect.FormulaBaseType(slot.GetType());
            AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), baseType,
                row.IsActionSlot ? "選擇動作" : "選擇公式", type =>
                {
                    var instance = AGReflect.CreateInstance(type);
                    if (instance != null) Connect(slot, instance);
                    Repaint();
                });
        });

        menu.AddItem(new GUIContent("接資產（長出資產節點）"), useType == 2, () => AssignAsset(slot, AGReflect.GetAsset(slot)));

        if (!row.IsActionSlot)
        {
            bool anyToken = false;
            foreach (var t in model.ReadTokens())
            {
                if (t.ResultType != row.ResultType || string.IsNullOrWhiteSpace(t.Key)) continue;
                anyToken = true;
                var captured = t.Key;
                menu.AddItem(new GUIContent($"接變數/{t.Key}"),
                    useType == 3 && AGReflect.GetTokenKey(slot) == captured,
                    () => { AssignToken(slot, captured); Repaint(); });
            }
            if (!anyToken) menu.AddDisabledItem(new GUIContent("接變數（此型別尚無變數）"));
        }

        if (!row.IsActionSlot && AGReflect.GetFormula(slot) != null)
        {
            menu.AddItem(new GUIContent("轉存為變數（Token）"), false, () =>
                AGPrompt.Show("轉存為變數", "輸入變數名稱", "", key => ExtractToken(slot, key)));
        }

        if (useType != 0)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清除這個欄位"), false, () => CutLink(slot));
        }
        menu.ShowAsContext();
    }

    /// <summary>把某個欄位的公式抽成共用變數，欄位本身改為變數狀態。</summary>
    private void ExtractToken(object slot, string key)
    {
        var resultType = AGReflect.ResultType(slot.GetType());
        var formula = AGReflect.GetFormula(slot);
        if (resultType == null || formula == null) return;

        if (!model.AddToken(resultType, key, out string error))
        {
            EditorUtility.DisplayDialog("無法轉存", error, "好");
            return;
        }

        foreach (var t in model.ReadTokens())
        {
            if (t.Key != key || t.ResultType != resultType) continue;
            AGReflect.SetFormula(t.Slot, formula);
            break;
        }

        // 內容已經搬進變數，這裡直接換一個乾淨的變數載體，不把舊載體留成候選（否則同一份公式會有兩個位置）。
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetToken(key);
        AGReflect.SetNode(slot, carrier);
        Invalidate();
        Repaint();
    }

    private void ShowNodeMenu(AGNode node)
    {
        // 換來源走 Header 名稱區、中斷連線走連線本身（雙擊），兩者都不重複放進右鍵選單。
        var menu = new GenericMenu();

        if (node.IsPlaceholder && node.ParentSlot != null)
        {
            if (!node.IsRoot) menu.AddItem(new GUIContent("清除空 Node"), false, () => DeleteNode(node));
            menu.ShowAsContext();
            return;
        }

        if (node.TokenKey != null)
        {
            menu.AddItem(new GUIContent("編輯這個變數"), false, () => FocusToken(node));
        }

        if (node.Obj != null)
        {
            bool isAction = node.ParentSlot != null
                ? AGReflect.IsActionSlotType(node.ParentSlot.GetType())
                : ActionBaseTypeOfCurrentSystem()?.IsInstanceOfType(node.Obj) ?? false;
            menu.AddItem(new GUIContent(isAction ? "轉存為動作資產" : "轉存為公式資產"), false, () => ExtractAsset(node));

            if (!isAction && node.ParentSlot != null && !AGReflect.IsActionSlotType(node.ParentSlot.GetType()))
            {
                var slot = node.ParentSlot;
                menu.AddItem(new GUIContent("轉存為變數（Token）…"), false, () =>
                    AGPrompt.Show("轉存為變數", "輸入變數名稱", "", key => ExtractToken(slot, key)));
            }
        }

        if (!node.IsRoot && (node.Obj != null || node.IsAssetNode || node.TokenKey != null))
            menu.AddItem(new GUIContent("刪除"), false, () => DeleteNode(node));

        if (node.IsAssetNode && node.Asset != null)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("編輯這個資產"), false, () => EnterAsset(node));
            menu.AddItem(new GUIContent("在 Project 中顯示"), false, () => EditorGUIUtility.PingObject(node.Asset));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
        menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
        menu.ShowAsContext();
    }

    private void ShowCanvasMenu(Vector2 graphMouse)
    {
        var menu = new GenericMenu();
        var menuPos = Event.current.mousePosition;
        bool canEditFocus = focus.Kind == AGFocusKind.Action || focus.Kind == AGFocusKind.Token;

        foreach (var (rt, list) in model.TokenKinds())
        {
            var elem = list.GetType().GetGenericArguments()[0];
            var probe = AGReflect.CreateInstance(elem) as ITokenEntry;
            var slotType = probe?.Slot?.GetType();
            var baseType = slotType != null ? AGReflect.FormulaBaseType(slotType) : null;
            if (baseType == null) continue;

            var capturedBase = baseType;
            var content = new GUIContent($"建立公式/{AGReflect.ResultTypeName(rt)}");
            if (canEditFocus)
                menu.AddItem(content, false, () =>
                    AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), capturedBase, "選擇公式",
                        type => CreateOrphan(type, graphMouse)));
            else menu.AddDisabledItem(content);
        }

        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null)
        {
            var content = new GUIContent("建立動作");
            if (canEditFocus)
                menu.AddItem(content, false, () =>
                    AGTypeCatalog.ShowPicker(new Rect(menuPos, Vector2.one), actionBase, "選擇動作",
                        type => CreateOrphan(type, graphMouse)));
            else menu.AddDisabledItem(content);
        }

        menu.AddSeparator("");
        if (canEditFocus)
        {
            menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
            menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("整理版面"));
            menu.AddDisabledItem(new GUIContent("聚焦全部節點"));
        }
        menu.ShowAsContext();
    }

    private static Vector2 SnapToGrid(Vector2 value)
    {
        return new Vector2(
            Mathf.Round(value.x / AGGraph.GridSize) * AGGraph.GridSize,
            Mathf.Round(value.y / AGGraph.GridSize) * AGGraph.GridSize);
    }


    // ===== 轉存為資產 =====

    /// <summary>把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。</summary>
    private void ExtractAsset(AGNode node)
    {
        if (node.Obj is not ActionSystemNode source) return;

        var assetType = AssetTypeFor(node, out _);
        if (assetType == null)
        {
            EditorUtility.DisplayDialog("無法轉存", "找不到對應的資產型別。", "好");
            return;
        }

        CreateExtractedAsset(node, source, assetType, AGReflect.TypeName(source.GetType()));
    }

    private void CreateExtractedAsset(AGNode node, ActionSystemNode source, Type assetType, string assetName)
    {
        if (!AGAssetStore.TryGetUniquePath(assetName, out string path)) return;

        var asset = ScriptableObject.CreateInstance(assetType);
        if (asset == null)
        {
            Debug.LogError($"[ActionGraph] 建立 {assetType.Name} 失敗。");
            return;
        }

        var setTarget = assetType.GetMethod("SetTarget");
        if (setTarget == null)
        {
            Debug.LogError($"[ActionGraph] {assetType.Name} 沒有 SetTarget。");
            return;
        }
        setTarget.Invoke(asset, new object[] { source });

        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);

        AssetDatabase.SaveAssets();
        AGAssetIndex.Refresh();

        model.BreakUndoMerge();
        // 內容被「搬進資產」，所以是就地把載體換成資產引用，不留成候選。
        if (node.Carrier != null) node.Carrier.SetAsset(asset);
        if (node.ParentSlot == null) model.RemoveOrphan(node.Carrier);

        Invalidate();
        EditorGUIUtility.PingObject(asset);
        ShowNotification(new GUIContent("已轉存為資產"));
    }

    private Type AssetTypeFor(AGNode node, out bool isAction)
    {
        isAction = false;
        if (node.ParentSlot != null)
        {
            var slotType = node.ParentSlot.GetType();
            if (AGReflect.IsActionSlotType(slotType))
            {
                isAction = true;
                return ConcreteAssetType(AGReflect.ActionAssetType(slotType));
            }
            return AGReflect.AssetType(slotType);
        }

        // 未連接節點沒有父欄位，靠型別回推它屬於哪一族。
        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null && actionBase.IsInstanceOfType(node.Obj))
        {
            isAction = true;
            return ConcreteAssetType(ActionAssetTypeOfCurrentSystem());
        }

        foreach (var (_, list) in model.TokenKinds())
        {
            var elem = list.GetType().GetGenericArguments()[0];
            var probeSlot = (AGReflect.CreateInstance(elem) as ITokenEntry)?.Slot;
            if (probeSlot == null) continue;
            var formulaBase = AGReflect.FormulaBaseType(probeSlot.GetType());
            if (formulaBase != null && formulaBase.IsInstanceOfType(node.Obj))
                return AGReflect.AssetType(probeSlot.GetType());
        }
        return null;
    }

    private static Type ConcreteAssetType(Type baseType)
    {
        if (baseType == null) return null;
        if (!baseType.IsAbstract) return baseType;
        foreach (var t in TypeCache.GetTypesDerivedFrom(baseType))
            if (!t.IsAbstract) return t;
        return null;
    }

    private Type ActionAssetTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            var slotType = g.Actions.GetType().GetGenericArguments()[0];
            return AGReflect.ActionAssetType(slotType);
        }
        return null;
    }

    private Type ActionBaseTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            var slotType = g.Actions.GetType().GetGenericArguments()[0];
            return AGReflect.ActionBaseType(slotType);
        }
        return null;
    }

    // 候選池掛在焦點頭端上（資產有自己的一份），所以資產焦點也能建候選，不會污染 Owner。
    private void CreateOrphan(Type type, Vector2 graphMouse)
    {
        var instance = AGReflect.CreateInstance(type);
        if (instance is not ActionSystemNode node) return;

        var carrier = new GraphNode(node);
        carrier.EnsureId();
        carrier.Pos = graphMouse;
        model.AddOrphan(carrier);
        Invalidate();
        Repaint();
    }
}

}
