namespace HaruFamily.Framework.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 換來源：Asset 節點建立與拖放、型別替換、抽出資產、標註，以及節點右鍵選單。
/// </summary>
public partial class ActionGraphWindow
{
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

    /// <summary>變數落到畫布上：落在參數列就直接接上，空白處就建立候選節點。拖曳與「建立節點」共用。</summary>
    private void DropEndpointOn(GraphEndpoint endpoint, Vector2 graphMouse)
    {
        if (endpoint == null) return;
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddVariableReferenceNode(endpoint, graphMouse);
            return;
        }
        if (row.IsActionSlot || !AGReflect.AcceptsEndpoint(row.Slot, endpoint))
        {
            ShowNotification(new GUIContent("變數型別不符，無法接到這個欄位"));
            return;
        }
        BreakUndoMerge();
        AssignEndpoint(row.Slot, endpoint);
    }

    /// <summary>把變數拖到空白畫布：建立一個沒有連線的候選載體。</summary>
    private void AddVariableReferenceNode(GraphEndpoint endpoint, Vector2 graphMouse)
    {
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        if (endpoint == null) return;

        BreakUndoMerge();
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetEndpoint(endpoint);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>把 Project 的共用資產拖到空白畫布：建立一個沒有連線的候選載體。</summary>
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

    // 有頭端才有候選池可放；資產焦點的頭端是資產本身，時機畫布的頭端是整套 ActionSystem。
    private bool CanCreateReferenceNode() => focus.Head != null;

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

        // 同一族的 Formula／Asset／Token 一律可以互換，包含候選池裡沒有父欄位的節點：
        // 族靠「代表性的 Slot 型別」推導，推不出來才退回用目前內容的基底型別。
        Type slotType = slot?.GetType() ?? RepresentativeSlotType(node);
        bool isAction = slotType != null
            ? AGReflect.IsActionSlotType(slotType)
            : node.IsActionNode || (node.IsAssetNode && node.ResultType == null);
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

        // 族＝Slot 型別。同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
        // 拿結果型別當判準會把別族的變數一起列進來。
        Type slotKind = isAction ? null : slotType;

        // 完全推不出族的候選節點（沒有父欄位、沒有連入線、不是資產、也沒有建立當下的族提示）只剩結果型別
        // 可比。這是近似：真的接到欄位時 AcceptsEndpoint 仍會擋掉別族。
        Type resultType = isAction || slotKind != null ? null : node.ResultType;
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

        // 變數與 Formula／Asset 同一層：三者都能填同一族的欄位，所以換來源選單一律列在一起。
        // 判準用上面推出來的 slotKind，和 Formula／Asset 兩組同源；動作欄位沒有族，天然排除。
        if (slotKind != null || resultType != null)
        {
            foreach (var token in AGModel.ReadTokens(CurrentEndpoints()))
            {
                if (slotKind != null ? token.Kind != slotKind : token.ResultType != resultType) continue;
                var endpoint = token.Endpoint;
                options.Add(new AGSourceOption
                {
                    Group = "Token",
                    Name = token.Key,
                    IsCurrent = ReferenceEquals(node.Endpoint, endpoint),
                    Apply = () => ChangeNodeToVariable(node, endpoint),
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

        BreakUndoMerge();
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
    /// 父欄位 → 連入邊的欄位 → 目前資產對應的欄位 → 建立當下記下的族。
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

        if (!string.IsNullOrEmpty(node.Id) && orphanKindHints.TryGetValue(node.Id, out var hint)) return hint;
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

    private void ChangeNodeToAsset(AGNode node, ScriptableObject asset)
    {
        if (node?.Carrier == null || asset == null || node.Asset == asset) return;

        BreakUndoMerge();
        PreserveVisibleNodePositions();
        if (node.Carrier.Kind == NodeKind.Asset) ReconcileAssetBindings(node.Carrier, asset);
        else DetachChildSourcesForReplacement(node);
        node.Carrier.SetAsset(asset);
        model.ClearAssetParameterCache();
        model.EnsureAssetBindings(node.Carrier);
        Invalidate();
        Repaint();
    }

    /// <summary>切換資產只沿用同名、同族的綁定；其餘來源保留成候選。</summary>
    private void ReconcileAssetBindings(GraphNode carrier, ScriptableObject nextAsset)
    {
        if (carrier == null) return;
        var parameters = AssetGraphSchema.Read(nextAsset, out _);
        for (int i = carrier.Bindings.Count - 1; i >= 0; i--)
        {
            var binding = carrier.Bindings[i];
            // 配對鍵是（族, 名稱）：同結果型別的不同族（String / Key）是兩個參數，只比名字會沿用到錯的那筆。
            AssetParameterDefinition match = null;
            foreach (var parameter in parameters)
                if (parameter.Name == binding?.Name && parameter.Slot?.Kind == binding.Slot?.Kind) { match = parameter; break; }

            bool compatible = binding?.Slot != null && match != null;
            if (compatible) continue;

            var child = binding?.Slot?.Node;
            if (child != null)
            {
                binding.Slot.SetNode(null);
                model.AddOrphan(child);
            }
            carrier.Bindings.RemoveAt(i);
        }
    }

    /// <summary>換掉節點內容前，先把它的直接來源拆散：子載體原位變成候選，完整子樹與座標都留著。</summary>
    private void DetachChildSourcesForReplacement(AGNode node)
    {
        if (node?.Carrier == null) return;
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

    /// <summary>同 <see cref="CountCarrierUsers"/>，但結果快取到下次重建圖為止；每幀要用的地方走這個。</summary>
    private int CarrierUsers(GraphNode carrier)
    {
        if (carrier == null) return 0;
        if (carrierUsers.TryGetValue(carrier, out int cached)) return cached;
        int n = CountCarrierUsers(carrier);
        carrierUsers[carrier] = n;
        return n;
    }

    private int CountCarrierUsers(GraphNode carrier)
    {
        if (carrier == null) return 0;
        int n = 0;
        foreach (var slot in SlotsInCurrentGraph())
            if (ReferenceEquals(AGReflect.GetNode(slot), carrier)) n++;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), carrier)) n++;
        return n;
    }

    /// <summary>欄位長出一顆變數節點。與 <see cref="AssignAsset"/> 對稱：先長節點，選哪一個變數在節點本體那一列。</summary>
    private void AssignEndpoint(object slot, GraphEndpoint endpoint)
    {
        PreserveVisibleNodePositions();
        SoloSource(slot).SetEndpoint(endpoint);
        Invalidate();
    }

    /// <summary>放置模式落下：在點擊處長一顆空節點並接上欄位，內容由使用者在節點 Header 選。</summary>
    // 和「拉線到空白處」同一條路徑，只是起點是右鍵選單而不是接點。
    private void PlaceNewSource(object slot, Vector2 graphMouse)
    {
        if (slot == null) return;
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        NewSource(slot).Pos = SnapToGrid(graphMouse);
        Invalidate();
    }

    private void AssignAsset(object slot, UnityEngine.Object asset)
    {
        if (asset is not ScriptableObject so) return;
        PreserveVisibleNodePositions();
        var carrier = SoloSource(slot);
        if (carrier.Kind == NodeKind.Asset) ReconcileAssetBindings(carrier, so);
        carrier.SetAsset(so);
        model.ClearAssetParameterCache();
        model.EnsureAssetBindings(carrier);
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

    private void DeleteNode(AGNode node, bool pushUndo = true)
    {
        if (node == null) return;
        // 時機節點是使用者自己建出來的，就讓他自己刪掉；其餘 HEAD 是焦點本身，沒有「刪除」可言。
        if (node.IsTimingGroup) { RemoveTimingGroup(node); return; }
        if (node.IsRoot)
        {
            ShowNotification(new GUIContent("根節點不可刪除；要換內容請按右鍵"));
            return;
        }
        if (node.Carrier == null) return;
        if (pushUndo) BreakUndoMerge();
        PreserveVisibleNodePositions();

        // 刪節點＝斷開所有指著這個載體的欄位，並把它移出候選池。
        foreach (var slot in SlotsInCurrentGraph())
            if (ReferenceEquals(AGReflect.GetNode(slot), node.Carrier)) AGReflect.SetNode(slot, null);
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), node.Carrier))
            AGReflect.SetNode(focus.AssetHostSlot, null);
        model.RemoveOrphan(node.Carrier);

        selectedIds.Remove(node.Id);
        Invalidate();
    }

    // ===== 右鍵選單 =====

    /// <summary>
    /// 轉存為變數：新建一個端點，把這顆節點搬進它自己的畫布，**所有**指著它的欄位都改接變數節點。
    /// 和「轉存為資產」同一個手勢，差別是變數留在這張圖裡，不另外開檔。
    /// 空 Node 也收：它只有族、沒有內容，轉出來就是一個具名常數。
    /// </summary>
    // 共用載體在畫布上只畫一顆節點（AGNode.ParentSlot 只記走訪先到的那條邊），只改那一條的話，
    // 其餘欄位會繼續直接指著同一顆載體——那顆載體同時是變數的內容，變成一份資料兩種身分的別名。
    private void ExtractVariable(AGNode node)
    {
        if (node?.Carrier == null || node.ResultType == null) return;
        var scope = CurrentEndpoints();
        if (scope == null) return;

        // 族＝Slot 型別：同一個結果型別可能有多個族（例：string 同時有 String 與 Key），
        // 只比結果型別會抽出錯的族，變數之後就接不回原本那格。連入邊、資產、建立當下的族提示都算。
        Type slotType = RepresentativeSlotType(node);
        if (slotType == null)
            foreach (var kind in model.FormulaKinds())
                if (kind.resultType == node.ResultType) { slotType = kind.slotType; break; }

        BreakUndoMerge();
        var endpoint = model.CreateEndpoint(scope, slotType, out string error);
        if (endpoint == null)
        {
            ShowNotification(new GUIContent(error));
            return;
        }

        // 先收集再改接：改完之後端點自己的取值欄位也指著這顆載體，邊掃邊改會把它一起換成變數節點。
        var users = new List<object>();
        foreach (var slot in SlotsInCurrentGraph())
            if (slot != null && ReferenceEquals(AGReflect.GetNode(slot), node.Carrier)) users.Add(slot);

        // 端點的取值欄位接下這顆載體；它的子樹整棵跟著搬進變數畫布。
        // 空 Node 沒有內容可搬，而且搬進去會讓端點變成「來源接了一顆空節點」——那是存檔驗證會擋的狀態。
        // 留空＝具名常數，和左欄「＋ 新增變數」建出來的完全一樣。
        if (!node.IsPlaceholder) AGReflect.SetNode(endpoint.Slot, node.Carrier);

        // 每個欄位各給一顆變數節點：載體是座標與選取的單位，共用一顆會讓多個引用處黏在同一個位置。
        foreach (var slot in users)
        {
            var proxy = new GraphNode();
            proxy.EnsureId();
            proxy.SetEndpoint(endpoint);
            AGReflect.SetNode(slot, proxy);
        }

        if (node.ParentSlot == null) model.RemoveOrphan(node.Carrier);   // 原本是候選節點：搬走就不再掛在這張畫布上

        MarkGraphChanged();
    }

    /// <summary>只列這個節點收得下的變數。與 <see cref="ShowAssetPicker"/> 同一種版型。</summary>
    private void ShowVariablePicker(AGNode node, Rect anchor)
    {
        var options = new List<AGSourceOption>();
        foreach (var token in AGModel.ReadTokens(CurrentEndpoints()))
        {
            var endpoint = token.Endpoint;
            if (!CanReplaceVariableNode(node, endpoint)) continue;
            options.Add(new AGSourceOption
            {
                Name = $"{token.Key}　({token.TypeName})",
                IsCurrent = ReferenceEquals(endpoint, node.Endpoint),
                Apply = () => ChangeNodeToVariable(node, endpoint),
            });
        }

        if (options.Count == 0)
        {
            ShowNotification(new GUIContent("這張圖還沒有型別相容的變數"));
            return;
        }
        AGTypeCatalog.ShowSourcePicker(anchor, options, "選擇變數");
    }

    /// <summary>這個節點能不能換成這個變數。判定路徑與 <see cref="CanReplaceAssetNode"/> 一致。</summary>
    private bool CanReplaceVariableNode(AGNode node, GraphEndpoint endpoint)
    {
        if (endpoint?.Slot == null) return false;
        if (node?.ParentSlot != null) return AGReflect.AcceptsEndpoint(node.ParentSlot, endpoint);

        bool hasLink = false;
        if (graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.Target, node)) continue;
                hasLink = true;
                if (link.ParentRow?.Slot == null || !AGReflect.AcceptsEndpoint(link.ParentRow.Slot, endpoint)) return false;
            }
        }
        if (hasLink) return true;

        // 候選池裡沒有連入線的節點：拿代表性 Slot 的族比對；推不出族才退回結果型別（近似，接上去時仍會被擋）。
        Type slotType = RepresentativeSlotType(node);
        if (slotType != null) return slotType == endpoint.Kind;
        return node?.ResultType == null || node.ResultType == endpoint.ResultType;
    }

    private void ChangeNodeToVariable(AGNode node, GraphEndpoint endpoint)
    {
        if (node?.Carrier == null || endpoint == null || ReferenceEquals(node.Endpoint, endpoint)) return;

        BreakUndoMerge();
        PreserveVisibleNodePositions();
        if (node.Carrier.Kind != NodeKind.Token) DetachChildSourcesForReplacement(node);
        node.Carrier.SetEndpoint(endpoint);
        Invalidate();
        Repaint();
    }

    /// <summary>
    /// 節點右鍵。不論哪一種節點都是同四段、同順序：**轉存 → 刪除 → 畫布 → 原始碼**。
    /// 分隔線由 <c>Sep()</c> 依實際有沒有項目補，所以某一段缺席不會留下空隙。
    /// </summary>
    // 換來源走 Header 的 ▾、換引用對象走本體那列下拉、中斷連線雙擊連線，三者都不重複放進右鍵。
    private void ShowNodeMenu(AGNode node)
    {
        var menu = new GenericMenu();
        int section = 0;
        void Sep()
        {
            if (menu.GetItemCount() > section) menu.AddSeparator("");
            section = menu.GetItemCount();
        }

        // 時機節點沒有內容也沒有引用，只有「刪掉這個時機」與畫布操作。
        if (node.IsTimingGroup)
        {
            menu.AddItem(new GUIContent("刪除這個時機"), false, () => RemoveTimingGroup(node));
            AddCanvasMenuItems(menu, Sep);
            menu.ShowAsContext();
            return;
        }

        // 下鑽走雙擊、換引用對象走本體那列下拉、改名走左欄或變數畫布的標題，都不重複放進右鍵。

        // === 1. 轉存 ===
        // 資產根載體不會被資產格式保存；變數畫布的根載體同理，轉存後那張畫布就空了。
        bool assetRoot = focus.Kind == AGFocusKind.Asset && ReferenceEquals(node.ParentSlot, focus.AssetHostSlot);
        bool variableRoot = focus.Endpoint != null && ReferenceEquals(node.ParentSlot, focus.Endpoint.Slot);
        bool canExtract = !assetRoot && !variableRoot;

        // 變數節點自己就是變數，沒有「再轉存成變數」這回事。
        // 空 Node 收：族已知、沒有內容，轉出來就是具名常數（動作格沒有結果型別，自然被擋掉）。
        if (canExtract && !node.IsVariableNode && node.Carrier != null && node.ResultType != null)
            menu.AddItem(new GUIContent("轉存為變數"), false, () => ExtractVariable(node));

        // 資產要有本體才存得進 SetTarget，所以空 Node 只能轉變數：Obj 為 null 這裡就過不了。
        if (canExtract && (node.Obj != null || node.IsVariableNode))
            menu.AddItem(new GUIContent("轉存為資產"), false, () => ExtractAsset(node));
        Sep();

        // === 2. 刪除 ===
        if (!node.IsRoot && node.Carrier != null)
            menu.AddItem(new GUIContent(node.IsPlaceholder ? "清除空 Node" : "刪除"), false, () => DeleteNode(node));

        // === 3. 畫布 ===
        AddCanvasMenuItems(menu, Sep);

        // === 4. 原始碼 ===
        // 擺最後：改程式是離開這張圖的動作，跟編圖不同層級。
        // 空 Node、資產節點、變數節點沒有自己的程式本體，跳過去也沒東西可看。
        // 這裡不先查「找不找得到原始碼」：查一次要讀整批 .cs，右鍵當場會卡住。
        // 一律放這個項目，真的沒有原始碼（只在 DLL 裡）由 Open 印警告。
        var bodyType = node.Obj?.GetType();
        if (bodyType != null)
        {
            Sep();
            menu.AddItem(new GUIContent("編輯程式"), false, () => AGScriptLocator.Open(bodyType));
        }

        menu.ShowAsContext();
    }

    /// <summary>每個右鍵選單最後一段都一樣：整張畫布的操作。</summary>
    private void AddCanvasMenuItems(GenericMenu menu, Action separator)
    {
        separator();
        menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
        menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
    }

    /// <summary>目前這張圖的變數清單。資產焦點是資產的工作副本，其餘是 Owner 的工作副本。</summary>
    private List<GraphEndpoint> CurrentEndpoints()
        => focus.Kind == AGFocusKind.Asset ? focus.AssetEndpoints : model.OwnerEndpoints;

    /// <summary>目前這張圖的所有載體。刪變數要靠它把指著那個變數的節點一起清掉。</summary>
    private IEnumerable<GraphNode> CurrentCarrierScope()
    {
        if (focus.Kind != AGFocusKind.Asset) return model.AllCarriers();

        var roots = new List<object> { focus.AssetHostSlot };
        foreach (var endpoint in focus.AssetEndpoints ?? new List<GraphEndpoint>())
            if (endpoint?.Slot != null) roots.Add(endpoint.Slot);
        return model.CarriersOf(roots, AssetAllOrphans());
    }

    /// <summary>資產交易裡所有候選節點：資產本體那份，加上每個變數畫布自己那份。</summary>
    private IEnumerable<GraphNode> AssetAllOrphans()
    {
        foreach (var node in focus.AssetOrphans ?? new List<GraphNode>())
            if (node != null) yield return node;
        foreach (var endpoint in focus.AssetEndpoints ?? new List<GraphEndpoint>())
        {
            if (endpoint == null) continue;
            foreach (var node in endpoint.Orphans)
                if (node != null) yield return node;
        }
    }

    private IEnumerable<object> SlotsInCurrentGraph()
    {
        if (focus.Kind != AGFocusKind.Asset)
        {
            foreach (var slot in model.AllSlots()) yield return slot;
            yield break;
        }

        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var slot in AGModel.WalkSlots(focus.AssetHostSlot, visited)) yield return slot;
        // 變數的取值欄位也是這張圖的一部分：引用計數與拉線相容都要算進來。
        foreach (var endpoint in focus.AssetEndpoints ?? new List<GraphEndpoint>())
        {
            if (endpoint?.Slot == null) continue;
            foreach (var slot in AGModel.WalkSlots(endpoint.Slot, visited)) yield return slot;
        }
        foreach (var orphan in AssetAllOrphans())
            foreach (var slot in AGModel.WalkSlots(orphan, visited)) yield return slot;
    }

    private void ShowCanvasMenu(Vector2 graphMouse)
    {
        var menu = new GenericMenu();
        // 有頭端就有候選池可放，判準與拖曳放節點那條一致；變數與資產畫布也算。
        bool canEditFocus = CanCreateReferenceNode();

        // 時機節點由使用者自己建，位置就是按下右鍵的地方。
        if (focus.Kind == AGFocusKind.Timing)
        {
            AddTimingMenuItems(menu, "新增時機節點/", graphMouse);
            menu.AddSeparator("");
        }

        // 只選族（＝Slot 型別），不選具體 class：長出來的是「（選擇來源）」那種空節點，
        // 內容留到 Header 的 ▾ 再挑。族要先決定，否則空節點沒有型別關係，▾ 也列不出東西。
        // 沒有具體 inline 公式的族（例：Key 刻意不開放 inline 公式，鍵必須恆定）照列：
        // ▾ 仍然挑得到該族的資產與變數，那正是這種族唯一的來源。
        foreach (var (_, slotType) in model.FormulaKinds())
        {
            var captured = slotType;
            var content = new GUIContent($"建立公式/{AGReflect.SlotKindName(slotType)}");
            if (canEditFocus) menu.AddItem(content, false, () => CreateOrphan(graphMouse, captured));
            else menu.AddDisabledItem(content);
        }

        Type actionSlotType = ActionSlotTypeOfCurrentSystem();
        if (actionSlotType != null)
        {
            var content = new GUIContent("建立動作");
            if (canEditFocus) menu.AddItem(content, false, () => CreateOrphan(graphMouse, actionSlotType));
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

    /// <summary>
    /// 把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。
    /// 子樹跨出資產邊界的兩件事在這裡收斂：變數引用抬成資產參數、被子樹外共用的節點複製一份留給外部。
    /// </summary>
    private void ExtractAsset(AGNode node)
    {
        if (node?.Carrier == null) return;

        // 複製共用節點是語意改變（從此兩份各自獨立），不能默默做。
        int shared = FindBoundaryShared(SubtreeRootOf(node)).Count;
        if (shared == 0) { ExtractAssetConfirmed(node); return; }

        RequestConfirm(GraphToWindowRect(new Rect(node.Pos.x, node.Pos.y, node.Width, AGGraph.HeaderHeight)),
            $"這棵子樹裡有 {shared} 個節點還被子樹外的欄位使用，轉存時會各複製一份留給它們。"
            + "轉存後兩份各自獨立，改一邊不會影響另一邊。",
            "轉存", () => ExtractAssetConfirmed(node));
    }

    /// <summary>轉存的子樹根：變數節點轉存的是它指向的那個變數的內容，不是節點自己。</summary>
    private static GraphNode SubtreeRootOf(AGNode node)
        => node == null ? null : (node.IsVariableNode ? node.Endpoint?.Slot?.Node : node.Carrier);

    private void ExtractAssetConfirmed(AGNode node)
    {
        if (node?.Carrier == null) return;

        BreakUndoMerge();

        // 變數節點自己沒有內容：轉存的對象是它指向的那個變數的算式，變數本身留著。
        if (node.IsVariableNode) { ExtractVariableContentAsset(node); return; }

        if (node.Obj is not ActionSystemNode source) return;

        var assetType = AssetTypeFor(node);
        if (assetType == null)
        {
            ShowNotification(new GUIContent("找不到對應的資產型別"));
            return;
        }

        CreateExtractedAsset(node.Carrier, node.ParentSlot == null, source, assetType,
            AGReflect.TypeName(source.GetType()), node.ParentSlot?.GetType());
    }

    /// <summary>把變數的內容轉存成公式資產：變數與所有指著它的節點都不動，只是它的來源換成資產。</summary>
    private void ExtractVariableContentAsset(AGNode node)
    {
        var slot = node.Endpoint?.Slot;
        var inner = slot?.Node;
        if (inner?.BodyObject is not ActionSystemNode source)
        {
            ShowNotification(new GUIContent("這個變數的內容不是可轉存的公式"));
            return;
        }

        var assetType = AGReflect.AssetType(slot.GetType());
        if (assetType == null)
        {
            ShowNotification(new GUIContent("找不到對應的資產型別"));
            return;
        }

        CreateExtractedAsset(inner, false, source, assetType, AGReflect.TypeName(source.GetType()), slot.GetType());
    }

    /// hostSlotType：轉存後拿來驗新資產內容的欄位型別；未連接節點沒有父欄位，傳 null 就略過那次驗證。
    private void CreateExtractedAsset(GraphNode carrier, bool isOrphan, ActionSystemNode source, Type assetType,
        string assetName, Type hostSlotType)
    {
        if (!AGAssetStore.TryGetUniquePath(assetName, out string path))
        {
            ShowNotification(new GUIContent("尚未指定共用資產資料夾：左欄「資產庫」標題列的按鈕"));
            return;
        }

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

        // 這兩步都要在「原件還完整、且資產已確定會建出來」之間做：
        // 使用者在檔名對話框按取消時上面就 return 了，圖不會被動到。
        // 先複製共用點給外部（此時子樹裡的變數節點還指著本圖的變數，複本才會接對）。
        DetachBoundaryShared(carrier);
        // 抬參數要在寫檔之前：資產的變數清單就是它的參數介面，晚了就存不進 .asset。
        var lifted = LiftTokensToParameters(asset);

        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);

        AssetDatabase.SaveAssets();
        AGAssetIndex.Refresh();

        // 轉存的內容來自已驗證的圖，這一關正常一定過；沒過代表轉存本身把內容抄壞了，
        // 當場報出來，而不是等別人存檔時被 Core 擋在「Owner 未寫入」那個沒有細節的對話框。
        if (hostSlotType != null && AGValidator.AssetHasError(model, hostSlotType, asset))
            Debug.LogError($"[ActionGraph] 轉存出來的資產 '{asset.name}' 內部有錯誤，請雙擊它進入資產畫布查看驗證訊息。", asset);

        BreakUndoMerge();
        // 內容被「搬進資產」，所以是就地把載體換成資產引用，不留成候選。
        carrier?.SetAsset(asset);
        if (isOrphan) model.RemoveOrphan(carrier);

        // 參數列要現算：這個資產是剛剛才長出參數的，快取裡那份是空的。
        model.ClearAssetParameterCache();
        model.EnsureAssetBindings(carrier);
        BindLiftedParameters(carrier, lifted);

        Invalidate();
        EditorGUIUtility.PingObject(asset);
        ShowNotification(new GUIContent("已轉存為資產"));
    }

    // ===== 轉存邊界 =====
    // 資產是另一個序列化根，圖裡的共用跨不過去。子樹裡指向外面的兩種線都要在轉存當下處理掉，
    // 否則存檔時 Unity 會各抄一份，變成看不見的分家：變數引用查不到值、共用節點默默變兩份。

    /// <summary>
    /// 把子樹裡的變數引用抬成資產參數：資產內建同型參數、內部的變數節點改指它，
    /// 回傳「參數名 → 原本那個變數」讓呼叫點把線接回去。
    /// </summary>
    // 不抬的話：資產求值走的是自己的作用域（TokenTable.CreateAssetScope 只登記資產自己的參數），
    // Owner 的變數名查不到，FormulaSlot 直接回預設值，畫布上與驗證上都看不出來。
    private List<(GraphEndpoint Parameter, GraphEndpoint Source)> LiftTokensToParameters(ScriptableObject asset)
    {
        var lifted = new List<(GraphEndpoint, GraphEndpoint)>();
        var parameters = AGReflect.Endpoints(asset);
        if (parameters == null) return lifted;

        var map = new Dictionary<GraphEndpoint, GraphEndpoint>();
        foreach (var carrier in TokenCarriersIn(AGReflect.AssetRoot(asset)))
        {
            var source = carrier.Endpoint;
            if (source == null) continue;
            if (parameters.Contains(source)) continue;   // 已經是這個資產自己的參數，不必再抬一層

            if (!map.TryGetValue(source, out var parameter))
            {
                if (AGReflect.CreateInstance(source.Slot?.GetType()) is not FormulaSlotBase slot)
                {
                    Debug.LogWarning($"[ActionGraph] 變數 '{source.Name}' 建不出資產參數欄位，"
                        + "轉存後資產內這一格會取預設值，請手動改成常數或補上對應的 FormulaSlot 型別。");
                    continue;
                }
                parameter = new GraphEndpoint(UniqueParameterName(parameters, source.Name, slot.Kind), slot);
                parameter.EnsureId();
                parameters.Add(parameter);
                map[source] = parameter;
                lifted.Add((parameter, source));
            }
            carrier.SetEndpoint(parameter);
        }
        return lifted;
    }

    /// <summary>子樹裡所有變數引用節點。走訪在端點物件停住：變數的內容住在自己的畫布，不屬於這棵子樹。</summary>
    // 一顆載體只回一次：共用的變數節點會被多個欄位走到，重複回傳會把剛抬上去的參數再抬一層。
    private List<GraphNode> TokenCarriersIn(object root)
    {
        var result = new List<GraphNode>();
        if (root == null) return result;

        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var endpoint in CurrentEndpoints() ?? new List<GraphEndpoint>())
            if (endpoint != null) visited.Add(endpoint);

        var seen = new HashSet<GraphNode>();
        foreach (var slot in AGModel.WalkSlots(root, visited))
        {
            var carrier = AGReflect.GetNode(slot);
            if (carrier != null && carrier.Kind == NodeKind.Token && seen.Add(carrier)) result.Add(carrier);
        }
        return result;
    }

    /// <summary>資產參數名：沿用原變數名，同族撞名才加號碼。名稱是呼叫點綁定用的 key。</summary>
    private static string UniqueParameterName(List<GraphEndpoint> scope, string preferred, Type kind)
    {
        var used = new HashSet<string>();
        foreach (var other in scope)
            if (other != null && other.Kind == kind && !string.IsNullOrEmpty(other.Name))
                used.Add(other.Name);

        string root = string.IsNullOrEmpty(preferred) ? "Param" : preferred;
        if (!used.Contains(root)) return root;
        for (int i = 2; ; i++)
            if (!used.Contains($"{root}{i}")) return $"{root}{i}";
    }

    /// <summary>呼叫點的參數列接回原本那個變數。</summary>
    // 一定要打開覆蓋：抬上去的參數在資產內部沒有內容（等於具名常數），不覆蓋就是取那個空欄位的預設值，
    // 值會從「Owner 的變數」默默變成 0。
    private void BindLiftedParameters(GraphNode carrier, List<(GraphEndpoint Parameter, GraphEndpoint Source)> lifted)
    {
        if (carrier == null || lifted == null) return;
        foreach (var (parameter, source) in lifted)
        {
            // 配對鍵是（族, 名稱）：資產參數同名不同族時，只比名字會把線接到別族那一列。
            string name = parameter.Name;
            NamedFormulaSlot binding = null;
            foreach (var current in carrier.Bindings)
                if (current?.Name == name && current.Slot?.Kind == parameter.Kind) { binding = current; break; }

            if (binding?.Slot == null)
            {
                Debug.LogWarning($"[ActionGraph] 資產參數 '{name}' 沒有參數列，"
                    + $"請在這顆資產節點上手動把它接回變數 '{source?.Name}'。");
                continue;
            }
            binding.OverrideEnabled = true;
            AGReflect.SetEndpoint(binding.Slot, source);
        }
    }

    /// <summary>子樹裡被子樹外欄位指著的載體 → 那些外部欄位。根自己不算：指著根的線會跟著它一起變成資產引用。</summary>
    private Dictionary<GraphNode, List<object>> FindBoundaryShared(GraphNode root)
    {
        var result = new Dictionary<GraphNode, List<object>>();
        if (root == null) return result;

        // 端點先當成走過了：變數的內容不會跟著搬進資產，指著它的欄位也就不算跨邊界。
        var visited = new HashSet<object>(AGRefComparer.Instance);
        foreach (var endpoint in CurrentEndpoints() ?? new List<GraphEndpoint>())
            if (endpoint != null) visited.Add(endpoint);

        var innerSlots = new HashSet<object>(AGRefComparer.Instance);
        foreach (var slot in AGModel.WalkSlots(root, visited)) innerSlots.Add(slot);

        var innerCarriers = new HashSet<GraphNode>();
        foreach (var slot in innerSlots)
        {
            var carrier = AGReflect.GetNode(slot);
            if (carrier != null && !ReferenceEquals(carrier, root)) innerCarriers.Add(carrier);
        }
        if (innerCarriers.Count == 0) return result;

        foreach (var slot in SlotsInCurrentGraph())
        {
            if (slot == null || innerSlots.Contains(slot)) continue;
            var carrier = AGReflect.GetNode(slot);
            if (carrier == null || !innerCarriers.Contains(carrier)) continue;

            if (!result.TryGetValue(carrier, out var users)) result[carrier] = users = new List<object>();
            users.Add(slot);
        }
        return result;
    }

    /// <summary>把邊界共用點複製一份給子樹外的欄位；原件隨資產搬走，兩份從此各自獨立。</summary>
    // 一顆共用點只複製一份、外部所有欄位共指它：外部彼此之間原本的共用關係要留著。
    private void DetachBoundaryShared(GraphNode root)
    {
        var boundary = FindBoundaryShared(root);
        if (boundary.Count == 0) return;

        // 變數一律沿用不複製：複本裡的變數節點要繼續指向同一個變數，跟著抄會變成查不到值的孤兒端點。
        var shared = new List<object>();
        foreach (var endpoint in CurrentEndpoints() ?? new List<GraphEndpoint>())
            if (endpoint != null) shared.Add(endpoint);

        foreach (var pair in boundary)
        {
            var copy = ActionSystemDeepCopy.Copy(pair.Key, shared);
            if (copy == null)
            {
                Debug.LogError("[ActionGraph] 複製共用節點失敗，該欄位會跟著資產一起失去內容，詳見上一則訊息。");
                continue;
            }
            // 新舊載體不可共用識別碼：座標與選取狀態都掛在它身上。
            AGModel.ResetNodeIds(copy, shared);
            foreach (var slot in pair.Value) AGReflect.SetNode(slot, copy);
        }
    }

    /// <summary>這顆節點的內容該存成哪一種資產。動作與公式各走各的資產族，呼叫端不必自己分辨。</summary>
    private Type AssetTypeFor(AGNode node)
    {
        if (node.ParentSlot != null)
        {
            var slotType = node.ParentSlot.GetType();
            if (AGReflect.IsActionSlotType(slotType))
                return ConcreteAssetType(AGReflect.ActionAssetType(slotType));
            return AGReflect.AssetType(slotType);
        }

        // 未連接節點沒有父欄位，靠型別回推它屬於哪一族。
        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null && actionBase.IsInstanceOfType(node.Obj))
            return ConcreteAssetType(ActionAssetTypeOfCurrentSystem());

        foreach (var (_, slotType) in model.FormulaKinds())
        {
            var formulaBase = AGReflect.FormulaBaseType(slotType);
            if (formulaBase != null && formulaBase.IsInstanceOfType(node.Obj))
                return AGReflect.AssetType(slotType);
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

    /// <summary>
    /// 記住一顆空候選節點屬於哪一族（用代表性的 Slot 型別表示）。族決定它 Header 上的型別標籤與編輯期的
    /// 型別推導，**不進資料**；視窗關掉後那顆節點退回一般空節點，接上欄位一樣能選型別。
    /// </summary>
    private void RememberOrphanKind(GraphNode carrier, Type slotType)
    {
        if (carrier == null || slotType == null) return;
        // 有內容的載體型別看得出來，不需要族；資產與變數節點也各有自己的型別來源。
        if (carrier.BodyObject != null || carrier.AssetObject != null || carrier.Endpoint != null) return;

        string id = carrier.EnsureId();
        if (!string.IsNullOrEmpty(id)) orphanKindHints[id] = slotType;
    }

    // 候選池掛在焦點頭端上（資產有自己的一份），所以資產焦點也能建候選，不會污染 Owner。
    /// <summary>在畫布上放一顆空節點，並記住它屬於哪一族。</summary>
    private void CreateOrphan(Vector2 graphMouse, Type slotType)
    {
        BreakUndoMerge();
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        RememberOrphanKind(carrier, slotType);
        Invalidate();
        Repaint();
    }

    /// <summary>本系統的動作欄位型別。建立動作用它當代表性 Slot。</summary>
    private Type ActionSlotTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadGroups())
        {
            if (g.Actions == null) continue;
            return g.Actions.GetType().GetGenericArguments()[0];
        }
        return model.ActionSlotType;
    }
}

}
