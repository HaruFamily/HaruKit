namespace PinPlugin.ActionSystem.Editor
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
        model.BreakUndoMerge();
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

        model.BreakUndoMerge();
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

        // 同一個結果型別的 Formula／Asset／Token 一律可以互換，包含候選池裡沒有父欄位的節點：
        // 型別關係靠「代表性的 Slot 型別」推導，推不出來才退回用目前內容的基底型別。
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

        Type resultType = !isAction
            ? (slotType != null ? AGReflect.ResultType(slotType) : node.ResultType)
            : null;
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

        // 變數與 Formula／Asset 同一層：三者都能填同一個結果型別的欄位，所以換來源選單一律列在一起。
        // 判準用上面推出來的 resultType，和 Formula／Asset 兩組同源；動作欄位沒有結果型別，天然排除。
        if (resultType != null)
        {
            foreach (var token in AGModel.ReadTokens(CurrentEndpoints()))
            {
                if (token.ResultType != resultType) continue;
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

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        if (node.Carrier.Kind == NodeKind.Asset) ReconcileAssetBindings(node.Carrier, asset);
        else DetachChildSourcesForReplacement(node);
        node.Carrier.SetAsset(asset);
        model.ClearAssetParameterCache();
        model.EnsureAssetBindings(node.Carrier);
        Invalidate();
        Repaint();
    }

    /// <summary>切換資產只沿用同名、同結果型別、同 Pack 的綁定；其餘來源保留成候選。</summary>
    private void ReconcileAssetBindings(GraphNode carrier, ScriptableObject nextAsset)
    {
        if (carrier == null) return;
        var parameters = AssetGraphSchema.Read(nextAsset, out _);
        for (int i = carrier.Bindings.Count - 1; i >= 0; i--)
        {
            var binding = carrier.Bindings[i];
            AssetParameterDefinition match = null;
            foreach (var parameter in parameters)
                if (parameter.Name == binding?.Name) { match = parameter; break; }

            bool compatible = binding?.Slot != null && match != null
                && binding.Slot.ResultType == match.ResultType
                && binding.Slot.PackType == match.PackType;
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
        model.BreakUndoMerge();
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
        if (pushUndo) model.BreakUndoMerge();
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
    /// 轉存為變數：新建一個端點，把這顆節點搬進它自己的畫布，原欄位改接一顆變數節點。
    /// 和「轉存為資產」同一個手勢，差別是變數留在這張圖裡，不另外開檔。
    /// </summary>
    private void ExtractVariable(AGNode node)
    {
        if (node?.Carrier == null || node.ResultType == null) return;
        var scope = CurrentEndpoints();
        if (scope == null) return;

        Type slotType = null;
        foreach (var kind in model.FormulaKinds())
            if (kind.resultType == node.ResultType) { slotType = kind.slotType; break; }

        model.BreakUndoMerge();
        var endpoint = model.CreateEndpoint(scope, slotType, out string error);
        if (endpoint == null)
        {
            ShowNotification(new GUIContent(error));
            return;
        }

        // 端點的取值欄位接下這顆載體；它的子樹整棵跟著搬進變數畫布。
        AGReflect.SetNode(endpoint.Slot, node.Carrier);

        if (node.ParentSlot != null)
        {
            var proxy = new GraphNode();
            proxy.EnsureId();
            proxy.SetEndpoint(endpoint);
            AGReflect.SetNode(node.ParentSlot, proxy);
        }
        else model.RemoveOrphan(node.Carrier);   // 原本是候選節點：搬走就不再掛在這張畫布上

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

        // 候選池裡沒有連入線的節點：拿代表性 Slot 的結果型別比對。
        Type slotType = RepresentativeSlotType(node);
        Type resultType = slotType != null ? AGReflect.ResultType(slotType) : node?.ResultType;
        return resultType == null || resultType == endpoint.ResultType;
    }

    private void ChangeNodeToVariable(AGNode node, GraphEndpoint endpoint)
    {
        if (node?.Carrier == null || endpoint == null || ReferenceEquals(node.Endpoint, endpoint)) return;

        model.BreakUndoMerge();
        PreserveVisibleNodePositions();
        if (node.Carrier.Kind != NodeKind.Token) DetachChildSourcesForReplacement(node);
        node.Carrier.SetEndpoint(endpoint);
        Invalidate();
        Repaint();
    }

    /// <summary>
    /// 節點右鍵。不論哪一種節點都是同四段、同順序：**下鑽 → 編輯這顆 → 刪除 → 畫布**。
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
        bool canExtract = !node.IsPlaceholder && !assetRoot && !variableRoot;

        // 變數節點自己就是變數，沒有「再轉存成變數」這回事。
        if (canExtract && !node.IsVariableNode && node.Carrier != null && node.ResultType != null)
            menu.AddItem(new GUIContent("轉存為變數"), false, () => ExtractVariable(node));

        if (canExtract && (node.Obj != null || node.IsVariableNode))
            menu.AddItem(new GUIContent("轉存為資產"), false, () => ExtractAsset(node));
        Sep();

        // === 2. 刪除 ===
        if (!node.IsRoot && node.Carrier != null)
            menu.AddItem(new GUIContent(node.IsPlaceholder ? "清除空 Node" : "刪除"), false, () => DeleteNode(node));

        // === 3. 畫布 ===
        AddCanvasMenuItems(menu, Sep);
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

        // 只選族（結果型別），不選具體 class：長出來的是「（選擇 Formula）」那種空節點，
        // 型別留到 Header 的 ▾ 再挑。族要先決定，否則空節點沒有型別關係，▾ 也列不出東西。
        foreach (var (rt, slotType) in model.FormulaKinds())
        {
            var captured = slotType;
            var content = new GUIContent($"建立公式/{AGReflect.ResultTypeName(rt)}");
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

    /// <summary>把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。</summary>
    private void ExtractAsset(AGNode node)
    {
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

        // 轉存的內容來自已驗證的圖，這一關正常一定過；沒過代表轉存本身把內容抄壞了，
        // 當場報出來，而不是等別人存檔時被 Core 擋在「Owner 未寫入」那個沒有細節的對話框。
        if (hostSlotType != null && AGValidator.AssetHasError(model, hostSlotType, asset))
            Debug.LogError($"[ActionGraph] 轉存出來的資產 '{asset.name}' 內部有錯誤，請雙擊它進入資產畫布查看驗證訊息。", asset);

        model.BreakUndoMerge();
        // 內容被「搬進資產」，所以是就地把載體換成資產引用，不留成候選。
        carrier?.SetAsset(asset);
        if (isOrphan) model.RemoveOrphan(carrier);

        Invalidate();
        EditorGUIUtility.PingObject(asset);
        ShowNotification(new GUIContent("已轉存為資產"));
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
        model.BreakUndoMerge();
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
