namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 換來源：Asset 節點建立與拖放、型別替換、抽出資產、標註，以及節點右鍵選單。
/// </summary>
public partial class HaruGraphWindow
{
    private void DropAssetOn(Vector2 graphMouse)
    {
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddAssetReferenceNode(drag.Asset, graphMouse);
            return;
        }
        if (!CanAssignAsset(row, drag.Asset))
        {
            ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
            return;
        }
        AssignAsset(row.InputSlot, drag.Asset);
    }

    /// <summary>Token落到畫布上：落在參數列就直接接上，空白處就建立候選節點。拖曳與「建立節點」共用。</summary>
    private void DropTokenOn(GraphToken endpoint, Vector2 graphMouse)
    {
        if (endpoint == null) return;
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddTokenReferenceNode(endpoint, graphMouse);
            return;
        }
        var source = TokenDropSource(endpoint);
        if (row.IsActionSlot || !CanAcceptExternal(row, source))
        {
            ShowNotification(new GUIContent("Token 型別不符，無法接到這個欄位"));
            return;
        }
        BreakUndoMerge();
        AssignToken(row.InputSlot, endpoint);
    }

    /// <summary>
    /// Property 落到畫布上：落在讀寫得下它的欄位就直接接上，空白處就建立引用節點。與Token那條同一種形狀。
    /// </summary>
    // 拉進來的是對既有定義的引用，不是複製一份新的儲存位置——多個節點指同一顆定義是正常狀態。
    private void DropPropertyOn(GraphProperty property, Vector2 graphMouse)
    {
        if (property == null) return;
        var row = RowAt(graphMouse, out _);
        if (row == null)
        {
            AddPropertyReferenceNode(property, graphMouse);
            return;
        }
        var source = PropertyDropSource(property);
        if (row.IsActionSlot || !CanAcceptExternal(row, source))
        {
            ShowNotification(new GUIContent("Property 型別不符，無法接到這個欄位"));
            return;
        }
        BreakUndoMerge();
        AssignProperty(row.InputSlot, property);
    }

    /// <summary>把Token拖到空白畫布：建立一個沒有連線的候選載體。</summary>
    private void AddTokenReferenceNode(GraphToken endpoint, Vector2 graphMouse)
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
        carrier.SetToken(endpoint);
        carrier.Pos = SnapToGrid(graphMouse);
        model.AddOrphan(carrier);
        Invalidate();
    }

    /// <summary>把 Property 拖到空白畫布：建立一個沒有連線的候選載體，指向同一顆定義。</summary>
    private void AddPropertyReferenceNode(GraphProperty property, Vector2 graphMouse)
    {
        if (!CanCreateReferenceNode())
        {
            ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
            return;
        }
        if (property == null) return;

        BreakUndoMerge();
        var carrier = new GraphNode();
        carrier.EnsureId();
        carrier.SetProperty(property);
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

        if (canAssign) AssignAsset(row.InputSlot, asset);
        else if (canCreate) AddAssetReferenceNode(asset, graphMouse);
        else if (row == null) ShowNotification(new GUIContent("先指定根公式或動作，才能放入參照節點"));
        else ShowNotification(new GUIContent("資產型別不符，無法接到這個欄位"));
        DragAndDrop.AcceptDrag();
        e.Use();
        return true;
    }

    // 有頭端才有候選池可放；資產焦點的頭端是資產本身，時機畫布的頭端是整套 LogicGraph。
    private bool CanCreateReferenceNode() => focus.Head != null;

    private bool CanAssignAsset(HGRow row, UnityEngine.Object asset)
    {
        if (row?.InputSlot == null || asset == null) return false;
        return CanAcceptExternal(row, AssetDropSource(asset));
    }

    private static bool CanAssignAsset(GraphSlotBase slot, UnityEngine.Object asset)
        => slot != null && asset is ScriptableObject scriptable && slot.AcceptsAsset(scriptable);

    private static IHGPortSource AssetDropSource(UnityEngine.Object asset)
        => new HGDelegatePortSource(null, null, input => CanAssignAsset(input, asset), input =>
            asset is ScriptableObject scriptable && input.AcceptsAsset(scriptable)
                ? HGPortConnectionResult.Allowed
                : asset == null ? HGPortConnectionResult.MissingBinding : HGPortConnectionResult.IncompatibleType);

    private static IHGPortSource TokenDropSource(GraphToken endpoint)
        => new HGDelegatePortSource(null, endpoint?.Slot, input => input.AcceptsToken(endpoint), input =>
            endpoint == null ? HGPortConnectionResult.MissingBinding
            : input.AcceptsToken(endpoint) ? HGPortConnectionResult.Allowed : HGPortConnectionResult.IncompatibleFamily);

    // 第二個參數刻意給 null 而不是 property.Slot：那顆 Slot 只宣告型別與初始常數，不是求值來源，
    // 當成來源會讓「讀 Property → 算 → 寫回同一顆」被算進求值依賴。
    private static IHGPortSource PropertyDropSource(GraphProperty property)
        => new HGDelegatePortSource(null, null, input => input.AcceptsProperty(property), input =>
            property == null ? HGPortConnectionResult.MissingBinding
            : input.AcceptsProperty(property) ? HGPortConnectionResult.Allowed
            : HGPortConnectionResult.IncompatibleFamily);

    private void ShowNodeSourceSelector(HGNodeView node, Rect selector)
    {
        if (node == null) return;
        HGTypeCatalog.ShowSourcePicker(selector, NodeSourceOptions(node));
    }

    internal List<HGSourceOption> NodeSourceOptions(HGNodeView node)
    {
        var options = new List<HGSourceOption>();
        if (node == null) return options;
        // 來源由自身的族決定；接線只在替換後決定保留或斷開，不控制候選可用性。
        Type slotType = ReplacementSlotType(node);
        bool isAction = slotType != null
            ? HGReflect.IsActionSlotType(slotType)
            : node.IsActionNode || (node.IsAssetNode && node.ResultType == null);
        Type baseType = slotType != null
            ? (isAction ? HGReflect.ActionBaseType(slotType) : HGReflect.FormulaBaseType(slotType))
            : HGReflect.NodeBaseType(node.Obj?.GetType());
        // 有些欄位的族是「pack 固定、結果型別任意」，
        // 那個條件 BodyBaseType 表達不出來，由 Slot 另外宣告 CandidatePackType 收窄。
        Type packFilter = !isAction && slotType != null ? HGReflect.CandidatePackType(slotType) : null;
        var bodyTypes = new HashSet<Type>();
        if (!isAction && slotType != null && HGReflect.CreateInstance(slotType) is FormulaSlotBase formulaSlot)
            foreach (var type in HGTypeCatalog.FormulasFor(formulaSlot)) bodyTypes.Add(type);
        else if (baseType != null)
            foreach (var type in HGTypeCatalog.Concrete(baseType, packFilter)) bodyTypes.Add(type);
        else if (node.IsPropertyNode)
            foreach (var family in model.FormulaKinds())
                foreach (var type in HGTypeCatalog.Concrete(HGReflect.FormulaBaseType(family.slotType),
                    HGReflect.CandidatePackType(family.slotType))) bodyTypes.Add(type);
        if (bodyTypes.Count > 0)
        {
            string kind = isAction ? "Action" : "Formula";
            foreach (var type in bodyTypes)
            {
                Type captured = type;
                options.Add(new HGSourceOption
                {
                    Group = kind + "/" + HGReflect.TypeCategory(type),
                    Name = HGReflect.TypeName(type),
                    IsCurrent = node.Obj?.GetType() == type,
                    Apply = () => ReplaceNodeType(node, captured),
                });
            }
        }

        // 族＝Slot 型別。同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
        // 拿結果型別當判準會把別族的Token一起列進來。
        Type slotKind = isAction ? null : slotType;

        // 完全推不出族的候選節點（沒有父欄位、沒有連入線、不是資產、也沒有建立當下的族提示）只剩結果型別
        // 可比。這是近似：真的接到欄位時 AcceptsToken 仍會擋掉別族。
        Type resultType = isAction || slotKind != null ? null : node.ResultType;


        // 不支援共用資產的圖直接跳過，避免無用的資產掃描。
        if (HasAssetSection)
        {
            foreach (var entry in HGAssetIndex.Entries)
            {
                if (entry.Asset == null || !CanReplaceAssetNode(node, entry.Asset)) continue;
                var asset = entry.Asset;
                options.Add(new HGSourceOption
                {
                    Group = "Asset",
                    Name = entry.Name,
                    IsCurrent = node.Asset == asset,
                    Apply = () => ChangeNodeToAsset(node, asset),
                });
            }
        }

        // Token 與其他讀值來源同層；動作不列 Token。
        // 判準用上面推出來的 slotKind，和 Formula／Asset 兩組同源；動作欄位沒有族，天然排除。
        // 沒宣告 Token 能力的圖直接跳過：那張圖的Token清單永遠是空的，列出來也只有標題。
        if (HasTokenSection && !isAction && (slotKind != null || resultType != null || node.IsPropertyNode))
        {
            foreach (var token in HGModel.ReadTokens(CurrentTokens()))
            {
                if (slotKind != null ? token.FamilyType != slotKind : resultType != null && token.ResultType != resultType) continue;
                var endpoint = token.Token;
                options.Add(new HGSourceOption
                {
                    Group = "Token",
                    Name = token.Key,
                    IsCurrent = ReferenceEquals(node.Token, endpoint),
                    Apply = () => ChangeNodeToToken(node, endpoint),
                });
            }
        }

        if (!isAction && HasPropertySection)
        {
            options.Add(new HGSourceOption
            {
                Group = "Property",
                Name = "LocalProperty",
                IsCurrent = node.IsPropertyNode && node.Carrier?.IsProtoProperty == false,
                Apply = () => ChangePropertyMode(node, false),
            });
            options.Add(new HGSourceOption
            {
                Group = "Property",
                Name = "ProtoProperty",
                IsCurrent = node.IsPropertyNode && node.Carrier?.IsProtoProperty == true,
                Apply = () => ChangePropertyMode(node, true),
            });
        }
        return options;
    }

    internal void ChangePropertyMode(HGNodeView node, bool proto)
    {
        if (node?.Carrier == null || (node.IsPropertyNode && node.Carrier.IsProtoProperty == proto)) return;

        Type family = ReplacementSlotType(node);
        GraphProperty next = proto ? FirstCompatibleProtoProperty(node) : model.CreateLocalProperty(family, out _);
        if (!proto && next == null) return;

        ReplaceNodeSource(node, family, () =>
        {
            DetachChildSourcesForReplacement(node);
            if (proto) node.Carrier.SetProtoProperty(next);
            else node.Carrier.SetLocalProperty(next);
        });
    }

    private GraphProperty FirstCompatibleProtoProperty(HGNodeView node)
    {
        var properties = CurrentProperties();
        if (properties == null) return null;

        Type family = ReplacementSlotType(node);
        foreach (var property in properties)
            if (property?.Proto == true && property.FamilyType == family) return property;

        foreach (var property in properties)
            if (property?.Proto == true) return property;
        return null;
    }

    /// <summary>斷開接不上新定義的讀寫連線。載體留在畫布上，沒有任何欄位再接它時才回到候選池。</summary>
    // 必須走 AttachSource：直接 SetNode(null) 會讓失去最後一個引用的 Property 節點從整張圖失聯，
    // 操作起來就是「換個 Header 模式，節點連同座標一起不見」。要斷的是線，不是節點。
    private void BreakIncompatiblePropertyLinks(GraphNode carrier, GraphProperty property)
    {
        // 先收成清單：AttachSource 會把失去引用的載體加進候選池，邊走邊改會中斷走訪。
        var linked = new List<GraphSlotBase>(SlotsInCurrentGraph());
        foreach (var linkedSlot in linked)
            if (ReferenceEquals(linkedSlot?.Node, carrier) && !linkedSlot.AcceptsProperty(property)) AttachSource(linkedSlot, null);
    }

    /// <summary>換來源後只斷不相容的線；不刪兩端節點，整次操作保持單一步復原。</summary>
    private void ReplaceNodeSource(HGNodeView node, Type family, Action replace)
    {
        var carrier = node.Carrier;
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        int disconnected = 0;
        bool trackChanges = model.TrackChanges;
        model.TrackChanges = false;
        try
        {
            replace();
            foreach (var slot in new List<GraphSlotBase>(SlotsInCurrentGraph()))
            {
                if (slot is GraphPropertyInputSlot || !ReferenceEquals(slot?.Node, carrier)) continue;
                bool compatible = carrier.Kind switch
                {
                    NodeKind.Inline => slot.AcceptsBody(carrier.BodyObject),
                    NodeKind.Asset => slot.AcceptsAsset(carrier.AssetObject),
                    NodeKind.Token => slot.AcceptsToken(carrier.Token),
                    NodeKind.Property => slot.AcceptsProperty(carrier.Property),
                    _ => false,
                };
                if (!compatible && AttachSource(slot, null)) disconnected++;
            }
        }
        finally { model.TrackChanges = trackChanges; }
        // AttachSource 可能記到寫入端的 PropertySlot 型別；來源的族提示必須維持讀值族。
        if (family != null) orphanKindHints[carrier.EnsureId()] = family;
        MarkGraphChanged();
        BreakUndoMerge();
        if (disconnected > 0) ShowNotification(new GUIContent($"已變更來源，斷開 {disconnected} 條不相容連線"));
    }

    private Type ReplacementSlotType(HGNodeView node)
    {
        if (node == null) return null;
        if (node.IsPropertyNode) return node.Property?.FamilyType;
        if (node.Token != null) return node.Token.FamilyType;
        if (node.Asset is ScriptableObject asset)
        {
            Type family = HGReflect.SlotTypeForAsset(asset, AssetSlotTypes());
            if (family != null) return family;
        }
        if (!string.IsNullOrEmpty(node.Id) && orphanKindHints.TryGetValue(node.Id, out var hint)
            && !typeof(PropertySlotBase).IsAssignableFrom(hint)) return hint;
        if (node.IsActionNode) return ActionSlotTypeOfCurrentSystem();
        if (node.Obj is GraphNodeContent body)
            foreach (var family in model.FormulaKinds())
                if (HGReflect.CreateInstance(family.slotType) is FormulaSlotBase probe
                    && probe.ResultType == node.ResultType && probe.AcceptsBody(body))
                    return family.slotType;
        return RepresentativeSlotType(node);
    }

    /// <summary>換節點內容，保留身分與相容的連線。</summary>
    internal void ReplaceNodeType(HGNodeView node, Type type)
    {
        if (node?.Carrier == null || type == null || node.Obj?.GetType() == type) return;
        if (HGReflect.CreateInstance(type) is not GraphNodeContent instance) return;
        Type family = ReplacementSlotType(node);

        ReplaceNodeSource(node, family, () =>
        {
            DetachChildSourcesForReplacement(node);
            node.Carrier.SetBody(instance);
        });
    }

    private bool CanReplaceAssetNode(HGNodeView node, ScriptableObject asset)
    {
        Type slotType = ReplacementSlotType(node);
        Type acceptedType = slotType == null ? null : HGReflect.IsActionSlotType(slotType)
            ? HGReflect.ActionAssetType(slotType) : HGReflect.AssetType(slotType);
        if (slotType == null && node?.IsPropertyNode == true)
            foreach (var family in model.FormulaKinds())
                if (HGReflect.CreateInstance(family.slotType) is FormulaSlotBase probe && probe.AcceptsAsset(asset)) return true;
        return acceptedType != null && acceptedType.IsInstanceOfType(asset);
    }

    /// <summary>
    /// 這個節點「相當於掛在哪一種 Slot 上」。候選池的節點沒有父欄位，型別關係只能這樣推：
    /// 父欄位 → 連入邊的欄位 → 目前資產對應的欄位 → 建立當下記下的族。
    /// </summary>
    private Type RepresentativeSlotType(HGNodeView node)
    {
        if (node == null) return null;
        if (node.ParentSlot != null) return node.ParentSlot.GetType();

        if (graph?.Links != null)
        {
            foreach (var link in graph.Links)
            {
                if (!ReferenceEquals(link.OutputOwner, node) || link.ParentRow?.InputSlot == null) continue;
                return link.ParentRow.InputSlot.GetType();
            }
        }

        if (node.Asset is ScriptableObject asset)
        {
            Type fromAsset = HGReflect.SlotTypeForAsset(asset, AssetSlotTypes());
            if (fromAsset != null) return fromAsset;
        }

        if (!string.IsNullOrEmpty(node.Id) && orphanKindHints.TryGetValue(node.Id, out var hint)) return hint;
        return null;
    }

    private void ChangeNodeToAsset(HGNodeView node, ScriptableObject asset)
    {
        if (node?.Carrier == null || asset == null || node.Asset == asset) return;
        Type family = ReplacementSlotType(node);

        ReplaceNodeSource(node, family, () =>
        {
            if (node.Carrier.Kind == NodeKind.Asset) ReconcileAssetBindings(node.Carrier, asset);
            else DetachChildSourcesForReplacement(node);
            node.Carrier.SetAsset(asset);
            model.ClearAssetParameterCache();
            model.EnsureAssetBindings(node.Carrier);
        });
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
                if (parameter.Name == binding?.Name && parameter.Slot?.FamilyType == binding.Slot?.FamilyType) { match = parameter; break; }

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
    private void DetachChildSourcesForReplacement(HGNodeView node)
    {
        if (node?.Carrier == null || node.IsPropertyNode) return;
        foreach (var row in HGGraph.AllRows(node.Rows))
        {
            if (!row.HasSlot) continue;
            var child = row.InputSlot.Node;
            if (child == null) continue;
            row.InputSlot.SetNode(null);
            model.AddOrphan(child);
        }
    }

    /// <summary>
    /// 取一個可以直接改內容的載體：欄位獨佔且不是內嵌內容時就地沿用；
    /// 共用中或還掛著內嵌子樹時另建一個，舊載體整棵留成候選。
    /// </summary>
    private GraphNode SoloSource(GraphSlotBase slot)
    {
        var carrier = HGReflect.GetNode(slot);
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
            if (ReferenceEquals(HGReflect.GetNode(slot), carrier)) n++;
        if (focus.Kind == HGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(HGReflect.GetNode(focus.AssetHostSlot), carrier)) n++;
        return n;
    }

    /// <summary>欄位長出一顆Token節點。與 <see cref="AssignAsset"/> 對稱：先長節點，選哪一個Token在節點本體那一列。</summary>
    private void AssignToken(GraphSlotBase slot, GraphToken endpoint)
    {
        PreserveVisibleNodePositions();
        SoloSource(slot).SetToken(endpoint);
        Invalidate();
    }

    private void AssignProperty(GraphSlotBase slot, GraphProperty property)
    {
        PreserveVisibleNodePositions();
        SoloSource(slot).SetProperty(property);
        Invalidate();
    }

    /// <summary>放置模式落下：在點擊處長一顆空節點並接上欄位，內容由使用者在節點 Header 選。</summary>
    // 和「拉線到空白處」同一條路徑，只是起點是右鍵選單而不是接點。
    private void PlaceNewSource(GraphSlotBase slot, Vector2 graphMouse)
    {
        if (slot == null) return;
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        NewSource(slot).Pos = SnapToGrid(graphMouse);
        Invalidate();
    }

    private void AssignAsset(GraphSlotBase slot, UnityEngine.Object asset)
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

    private void DeleteNode(HGNodeView node, bool pushUndo = true)
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
        DeleteCarrier(node.Carrier, node.Id, pushUndo);
    }

    /// <summary>
    /// 刪一顆載體：斷開所有指著它的欄位，並把它移出候選池與任何容器。
    /// </summary>
    // 節點與容器內的一格共用這一條：兩者的差別只有畫法，載體的拆除步驟完全一樣。
    private void DeleteCarrier(GraphNode carrier, string nodeId = null, bool pushUndo = true)
    {
        if (carrier == null) return;
        GraphProperty property = carrier.Property;
        if (pushUndo) BreakUndoMerge();
        PreserveVisibleNodePositions();

        foreach (var slot in SlotsInCurrentGraph())
            if (ReferenceEquals(HGReflect.GetNode(slot), carrier)) HGReflect.SetNode(slot, null);
        if (focus.Kind == HGFocusKind.Asset && focus.AssetHostSlot != null
            && ReferenceEquals(HGReflect.GetNode(focus.AssetHostSlot), carrier))
            HGReflect.SetNode(focus.AssetHostSlot, null);
        model.RemoveOrphan(carrier);
        RemoveFromNodeOwners(carrier);

        // ProtoProperty 是庫定義，刪掉畫布引用不可連帶刪庫；一般 Property 才跟最後一個引用一起回收。
        if (property != null && !property.Proto && !HasPropertyCarrier(property)) CurrentProperties()?.Remove(property);

        if (!string.IsNullOrEmpty(nodeId)) selectedIds.Remove(nodeId);
        Invalidate();
    }

    private bool HasPropertyCarrier(GraphProperty property)
    {
        foreach (GraphNode other in model.AllCarriers())
            if (ReferenceEquals(other?.Property, property)) return true;
        return false;
    }

    /// <summary>把載體從任何「帶子節點」的擁有者身上摘掉。</summary>
    // 子節點不在候選池裡，RemoveOrphan 摘不到它；漏掉這一步，刪過的格子下次重建圖時又會冒出來。
    private void RemoveFromNodeOwners(GraphNode carrier)
    {
        if (carrier == null || graph == null) return;
        foreach (var other in graph.Nodes)
            if (other?.Obj is IGraphNodeOwner owner) owner.RemoveChild(carrier);
    }

    // ===== 右鍵選單 =====

    /// <summary>
    /// 轉存為Token：新建一個端點，把這顆節點搬進它自己的畫布，**所有**指著它的欄位都改接Token節點。
    /// 和「轉存為資產」同一個手勢，差別是Token留在這張圖裡，不另外開檔。
    /// 空 Node 也收：它只有族、沒有內容，轉出來就是一個具名常數。
    /// </summary>
    // 共用載體在畫布上只畫一顆節點（HGNodeView.ParentSlot 只記走訪先到的那條邊），只改那一條的話，
    // 其餘欄位會繼續直接指著同一顆載體——那顆載體同時是Token的內容，變成一份資料兩種身分的別名。
    private void ExtractToken(HGNodeView node)
    {
        if (node?.Carrier == null || node.ResultType == null) return;
        var scope = CurrentTokens();
        if (scope == null) return;

        // 族＝Slot 型別：同一個結果型別可能有多個族（例：string 同時有 String 與 Key），
        // 只比結果型別會抽出錯的族，Token之後就接不回原本那格。連入邊、資產、建立當下的族提示都算。
        Type slotType = RepresentativeSlotType(node);
        if (slotType == null)
            foreach (var kind in model.FormulaKinds())
                if (kind.resultType == node.ResultType) { slotType = kind.slotType; break; }

        BreakUndoMerge();
        var endpoint = model.CreateToken(scope, slotType, out string error);
        if (endpoint == null)
        {
            ShowNotification(new GUIContent(error));
            return;
        }

        // 先收集再改接：改完之後端點自己的取值欄位也指著這顆載體，邊掃邊改會把它一起換成Token節點。
        var users = new List<GraphSlotBase>();
        foreach (var slot in SlotsInCurrentGraph())
            if (slot != null && ReferenceEquals(slot.Node, node.Carrier)) users.Add(slot);

        // 端點的取值欄位接下這顆載體；它的子樹整棵跟著搬進Token畫布。
        // 空 Node 沒有內容可搬，而且搬進去會讓端點變成「來源接了一顆空節點」——那是存檔驗證會擋的狀態。
        // 留空＝具名常數，和左欄「＋ 新增Token」建出來的完全一樣。
        if (!node.IsPlaceholder) HGReflect.SetNode(endpoint.Slot, node.Carrier);

        // 每個欄位各給一顆Token節點：載體是座標與選取的單位，共用一顆會讓多個引用處黏在同一個位置。
        foreach (var slot in users)
        {
            var proxy = new GraphNode();
            proxy.EnsureId();
            proxy.SetToken(endpoint);
            HGReflect.SetNode(slot, proxy);
        }

        if (node.ParentSlot == null) model.RemoveOrphan(node.Carrier);   // 原本是候選節點：搬走就不再掛在這張畫布上

        MarkGraphChanged();
    }

    /// <summary>只列這個節點收得下的Token。與 <see cref="ShowAssetPicker"/> 同一種版型。</summary>
    private void ShowTokenPicker(HGNodeView node, Rect anchor)
    {
        var options = new List<HGSourceOption>();
        foreach (var token in HGModel.ReadTokens(CurrentTokens()))
        {
            var endpoint = token.Token;
            if (!CanReplaceTokenNode(node, endpoint)) continue;
            options.Add(new HGSourceOption
            {
                Name = $"{token.Key}　({token.TypeName})",
                IsCurrent = ReferenceEquals(endpoint, node.Token),
                Apply = () => ChangeNodeToToken(node, endpoint),
            });
        }

        if (options.Count == 0)
        {
            ShowNotification(new GUIContent("這張圖還沒有型別相容的 Token"));
            return;
        }
        HGTypeCatalog.ShowSourcePicker(anchor, options, "選擇 Token");
    }

    /// <summary>這個節點能不能換成這個Token。判定路徑與 <see cref="CanReplaceAssetNode"/> 一致。</summary>
    private bool CanReplaceTokenNode(HGNodeView node, GraphToken endpoint)
    {
        if (endpoint?.Slot == null) return false;
        Type slotType = ReplacementSlotType(node);
        if (slotType != null) return slotType == endpoint.FamilyType;
        return node?.ResultType == null || node.ResultType == endpoint.ResultType;
    }

    private void ChangeNodeToToken(HGNodeView node, GraphToken endpoint)
    {
        if (node?.Carrier == null || endpoint == null || ReferenceEquals(node.Token, endpoint)) return;

        ReplaceNodeSource(node, endpoint.FamilyType, () =>
        {
            if (node.Carrier.Kind != NodeKind.Token) DetachChildSourcesForReplacement(node);
            node.Carrier.SetToken(endpoint);
        });
    }

    /// <summary>
    /// 節點右鍵。不論哪一種節點都是同四段、同順序：**轉存 → 刪除 → 畫布 → 原始碼**。
    /// 分隔線由 <c>Sep()</c> 依實際有沒有項目補，所以某一段缺席不會留下空隙。
    /// </summary>
    // 換來源走 Header 的 ▾、換引用對象走本體那列下拉、中斷連線雙擊連線，三者都不重複放進右鍵。
    private void ShowNodeMenu(HGNodeView node)
    {
        var menu = new GenericMenu();
        int section = 0;
        void Sep()
        {
            if (menu.GetItemCount() > section) menu.AddSeparator("");
            section = menu.GetItemCount();
        }

        // root 節點沒有內容也沒有引用，只有「刪掉它」與畫布操作。
        if (node.IsTimingGroup)
        {
            menu.AddItem(new GUIContent($"刪除這個{RootNoun}"), false, () => RemoveTimingGroup(node));
            AddCanvasMenuItems(menu, Sep);
            menu.ShowAsContext();
            return;
        }

        // 下鑽走雙擊、換引用對象走本體那列下拉、改名走左欄或Token畫布的標題，都不重複放進右鍵。

        // === 1. 轉存 ===
        // 資產根載體不會被資產格式保存；Token畫布的根載體同理，轉存後那張畫布就空了。
        bool assetRoot = focus.Kind == HGFocusKind.Asset && ReferenceEquals(node.ParentSlot, focus.AssetHostSlot);
        bool variableRoot = focus.Token != null && ReferenceEquals(node.ParentSlot, focus.Token.Slot);
        bool canExtract = !assetRoot && !variableRoot;

        // Token節點自己就是Token，沒有「再轉存成Token」這回事。
        // 空 Node 收：族已知、沒有內容，轉出來就是具名常數（動作格沒有結果型別，自然被擋掉）。
        // 沒宣告 Token 能力的圖連這一項都不出現——轉出來的東西沒有地方可列。
        // Property 節點也收不了：它沒有可轉出的內容，值是執行期由動作寫進去的。
        if (canExtract && HasTokenSection && !node.IsTokenNode && !node.IsPropertyNode
            && node.Carrier != null && node.ResultType != null)
            menu.AddItem(new GUIContent("轉存為 Token"), false, () => ExtractToken(node));

        // 資產要有本體才存得進 SetTarget，所以空 Node 只能轉Token：Obj 為 null 這裡就過不了。
        // 不支援共用資產的圖連這一項都不出現——點得到卻永遠失敗的選單項比沒有更糟。
        if (canExtract && HasAssetSection && (node.Obj != null || node.IsTokenNode))
            menu.AddItem(new GUIContent("轉存為資產"), false, () => ExtractAsset(node));
        Sep();

        // === 2. 刪除 ===
        if (!node.IsRoot && node.Carrier != null)
            menu.AddItem(new GUIContent(node.IsPlaceholder ? "清除空 Node" : "刪除"), false, () => DeleteNode(node));

        // === 3. 畫布 ===
        AddCanvasMenuItems(menu, Sep);

        // === 4. 原始碼 ===
        // 擺最後：改程式是離開這張圖的動作，跟編圖不同層級。
        // 空 Node、資產節點、Token節點沒有自己的程式本體，跳過去也沒東西可看。
        // 這裡不先查「找不找得到原始碼」：查一次要讀整批 .cs，右鍵當場會卡住。
        // 一律放這個項目，真的沒有原始碼（只在 DLL 裡）由 Open 印警告。
        var bodyType = node.Obj?.GetType();
        if (bodyType != null)
        {
            Sep();
            menu.AddItem(new GUIContent("編輯程式"), false, () => HGScriptLocator.Open(bodyType));
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

    /// <summary>目前這張圖的Token清單。資產焦點是資產的工作副本，其餘是 Owner 的工作副本。</summary>
    private List<GraphToken> CurrentTokens()
        => focus.Kind == HGFocusKind.Asset ? focus.AssetTokens : model.OwnerTokens;

    /// <summary>目前這張圖的所有載體。刪Token要靠它把指著那個Token的節點一起清掉。</summary>
    private IEnumerable<GraphNode> CurrentCarrierScope()
    {
        if (focus.Kind != HGFocusKind.Asset) return model.AllCarriers();

        var roots = new List<object> { focus.AssetHostSlot };
        foreach (var endpoint in focus.AssetTokens ?? new List<GraphToken>())
            if (endpoint?.Slot != null) roots.Add(endpoint.Slot);
        return model.CarriersOf(roots, AssetAllOrphans());
    }

    /// <summary>資產交易裡所有候選節點：資產本體那份，加上每個Token畫布自己那份。</summary>
    private IEnumerable<GraphNode> AssetAllOrphans()
    {
        foreach (var node in focus.AssetOrphans ?? new List<GraphNode>())
            if (node != null) yield return node;
        foreach (var endpoint in focus.AssetTokens ?? new List<GraphToken>())
        {
            if (endpoint == null) continue;
            foreach (var node in endpoint.Orphans)
                if (node != null) yield return node;
        }
    }

    private IEnumerable<GraphSlotBase> SlotsInCurrentGraph()
    {
        if (focus.Kind != HGFocusKind.Asset)
        {
            foreach (var slot in model.AllSlots()) yield return slot;
            yield break;
        }

        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var slot in HGModel.WalkSlots(focus.AssetHostSlot, visited)) yield return slot;
        // Token的取值欄位也是這張圖的一部分：引用計數與拉線相容都要算進來。
        foreach (var endpoint in focus.AssetTokens ?? new List<GraphToken>())
        {
            if (endpoint?.Slot == null) continue;
            foreach (var slot in HGModel.WalkSlots(endpoint.Slot, visited)) yield return slot;
        }
        foreach (var orphan in AssetAllOrphans())
            foreach (var slot in HGModel.WalkSlots(orphan, visited)) yield return slot;
    }

    private void ShowCanvasMenu(Vector2 graphMouse)
    {
        var menu = new GenericMenu();
        // 有頭端就有候選池可放，判準與拖曳放節點那條一致；Token與資產畫布也算。
        bool canEditFocus = CanCreateReferenceNode();

        // root 節點由使用者自己建，位置就是按下右鍵的地方。
        if (focus.Kind == HGFocusKind.Root)
        {
            AddTimingMenuItems(menu, $"新增{RootNoun}節點/", graphMouse);
            menu.AddSeparator("");
        }

        // 只選族（＝Slot 型別），不選具體 class：長出來的是「（選擇來源）」那種空節點，
        // 內容留到 Header 的 ▾ 再挑。族要先決定，否則空節點沒有型別關係，▾ 也列不出東西。
        // 沒有具體 inline 公式的族（例：Key 刻意不開放 inline 公式，鍵必須恆定）照列：
        // ▾ 仍然挑得到該族的資產與Token，那正是這種族唯一的來源。
        foreach (var (slotType, path) in HGTypeCatalog.FormulaKindOptions(model.FormulaKinds()))
        {
            var captured = slotType;
            var content = new GUIContent($"建立公式/{path}");
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
            Mathf.Round(value.x / HGGraph.GridSize) * HGGraph.GridSize,
            Mathf.Round(value.y / HGGraph.GridSize) * HGGraph.GridSize);
    }


    // ===== 轉存為資產 =====

    /// <summary>
    /// 把節點抽成獨立資產，原欄位改指向它。未連接節點則只建立資產。
    /// 子樹跨出資產邊界的兩件事在這裡收斂：Token引用抬成資產參數、被子樹外共用的節點複製一份留給外部。
    /// </summary>
    private void ExtractAsset(HGNodeView node)
    {
        if (node?.Carrier == null) return;

        // 複製共用節點是語意改變（從此兩份各自獨立），不能默默做。
        int shared = FindBoundaryShared(SubtreeRootOf(node)).Count;
        if (shared == 0) { ExtractAssetConfirmed(node); return; }

        RequestConfirm(GraphToWindowRect(new Rect(node.Pos.x, node.Pos.y, node.Width, HGGraph.HeaderHeight)),
            $"這棵子樹裡有 {shared} 個節點還被子樹外的欄位使用，轉存時會各複製一份留給它們。"
            + "轉存後兩份各自獨立，改一邊不會影響另一邊。",
            "轉存", () => ExtractAssetConfirmed(node));
    }

    /// <summary>轉存的子樹根：Token節點轉存的是它指向的那個Token的內容，不是節點自己。</summary>
    private static GraphNode SubtreeRootOf(HGNodeView node)
        => node == null ? null : (node.IsTokenNode ? node.Token?.Slot?.Node : node.Carrier);

    private void ExtractAssetConfirmed(HGNodeView node)
    {
        if (node?.Carrier == null) return;

        BreakUndoMerge();

        // Token節點自己沒有內容：轉存的對象是它指向的那個Token的算式，Token本身留著。
        if (node.IsTokenNode) { ExtractTokenContentAsset(node); return; }

        if (node.Obj is not GraphNodeContent source) return;

        var assetType = AssetTypeFor(node);
        if (assetType == null)
        {
            ShowNotification(new GUIContent("找不到對應的資產型別"));
            return;
        }

        CreateExtractedAsset(node.Carrier, node.ParentSlot == null, source, assetType,
            HGReflect.TypeName(source.GetType()), node.ParentSlot?.GetType());
    }

    /// <summary>把Token的內容轉存成公式資產：Token與所有指著它的節點都不動，只是它的來源換成資產。</summary>
    private void ExtractTokenContentAsset(HGNodeView node)
    {
        var slot = node.Token?.Slot;
        var inner = slot?.Node;
        if (inner?.BodyObject is not GraphNodeContent source)
        {
            ShowNotification(new GUIContent("這個 Token 的內容不是可轉存的公式"));
            return;
        }

        var assetType = HGReflect.AssetType(slot.GetType());
        if (assetType == null)
        {
            ShowNotification(new GUIContent("找不到對應的資產型別"));
            return;
        }

        CreateExtractedAsset(inner, false, source, assetType, HGReflect.TypeName(source.GetType()), slot.GetType());
    }

    /// hostSlotType：轉存後拿來驗新資產內容的欄位型別；未連接節點沒有父欄位，傳 null 就略過那次驗證。
    private void CreateExtractedAsset(GraphNode carrier, bool isOrphan, GraphNodeContent source, Type assetType,
        string assetName, Type hostSlotType)
    {
        if (!HGAssetStore.TryGetUniquePath(assetName, out string path))
        {
            ShowNotification(new GUIContent("尚未指定共用資產資料夾：左欄「資產庫」標題列的按鈕"));
            return;
        }

        var asset = ScriptableObject.CreateInstance(assetType);
        if (asset == null)
        {
            Debug.LogError($"[GraphKit] 建立 {assetType.Name} 失敗。");
            return;
        }

        var setTarget = assetType.GetMethod("SetTarget");
        if (setTarget == null)
        {
            Debug.LogError($"[GraphKit] {assetType.Name} 沒有 SetTarget。");
            return;
        }
        setTarget.Invoke(asset, new object[] { source });

        // 這兩步都要在「原件還完整、且資產已確定會建出來」之間做：
        // 使用者在檔名對話框按取消時上面就 return 了，圖不會被動到。
        // 先複製共用點給外部（此時子樹裡的Token節點還指著本圖的Token，複本才會接對）。
        DetachBoundaryShared(carrier);
        // 抬參數要在寫檔之前：資產的Token清單就是它的參數介面，晚了就存不進 .asset。
        var lifted = LiftTokensToParameters(asset);

        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);

        AssetDatabase.SaveAssets();
        HGAssetIndex.Refresh();

        // 轉存的內容來自已驗證的圖，這一關正常一定過；沒過代表轉存本身把內容抄壞了，
        // 當場報出來，而不是等別人存檔時被 Core 擋在「Owner 未寫入」那個沒有細節的對話框。
        if (hostSlotType != null && HGValidator.AssetHasError(model, hostSlotType, asset))
            Debug.LogError($"[GraphKit] 轉存出來的資產 '{asset.name}' 內部有錯誤，請雙擊它進入資產畫布查看驗證訊息。", asset);

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
    // 否則存檔時 Unity 會各抄一份，變成看不見的分家：Token引用查不到值、共用節點默默變兩份。

    /// <summary>
    /// 把子樹裡的Token引用抬成資產參數：資產內建同型參數、內部的Token節點改指它，
    /// 回傳「參數名 → 原本那個Token」讓呼叫點把線接回去。
    /// </summary>
    // 不抬的話：資產求值走的是自己的作用域（TokenTable.CreateAssetScope 只登記資產自己的參數），
    // Owner 的Token名查不到，FormulaSlot 直接回預設值，畫布上與驗證上都看不出來。
    private List<(GraphToken Parameter, GraphToken Source)> LiftTokensToParameters(ScriptableObject asset)
    {
        var lifted = new List<(GraphToken, GraphToken)>();
        var parameters = HGReflect.Tokens(asset);
        if (parameters == null) return lifted;

        var map = new Dictionary<GraphToken, GraphToken>();
        foreach (var carrier in TokenCarriersIn(HGReflect.AssetRoot(asset)))
        {
            var source = carrier.Token;
            if (source == null) continue;
            if (parameters.Contains(source)) continue;   // 已經是這個資產自己的參數，不必再抬一層

            if (!map.TryGetValue(source, out var parameter))
            {
                if (HGReflect.CreateInstance(source.Slot?.GetType()) is not FormulaSlotBase slot)
                {
                    Debug.LogWarning($"[GraphKit] Token '{source.Name}' 建不出資產參數欄位，"
                        + "轉存後資產內這一格會取預設值，請手動改成常數或補上對應的 FormulaSlot 型別。");
                    continue;
                }
                parameter = new GraphToken(UniqueParameterName(parameters, source.Name, slot.FamilyType), slot);
                parameter.EnsureId();
                parameters.Add(parameter);
                map[source] = parameter;
                lifted.Add((parameter, source));
            }
            carrier.SetToken(parameter);
        }
        return lifted;
    }

    /// <summary>子樹裡所有Token引用節點。走訪在端點物件停住：Token的內容住在自己的畫布，不屬於這棵子樹。</summary>
    // 一顆載體只回一次：共用的Token節點會被多個欄位走到，重複回傳會把剛抬上去的參數再抬一層。
    private List<GraphNode> TokenCarriersIn(object root)
    {
        var result = new List<GraphNode>();
        if (root == null) return result;

        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var endpoint in CurrentTokens() ?? new List<GraphToken>())
            if (endpoint != null) visited.Add(endpoint);

        var seen = new HashSet<GraphNode>();
        foreach (var slot in HGModel.WalkSlots(root, visited))
        {
            var carrier = HGReflect.GetNode(slot);
            if (carrier != null && carrier.Kind == NodeKind.Token && seen.Add(carrier)) result.Add(carrier);
        }
        return result;
    }

    /// <summary>資產參數名：沿用原Token名，同族撞名才加號碼。名稱是呼叫點綁定用的 key。</summary>
    private static string UniqueParameterName(List<GraphToken> scope, string preferred, Type kind)
    {
        var used = new HashSet<string>();
        foreach (var other in scope)
            if (other != null && other.FamilyType == kind && !string.IsNullOrEmpty(other.Name))
                used.Add(other.Name);

        string root = string.IsNullOrEmpty(preferred) ? "Param" : preferred;
        if (!used.Contains(root)) return root;
        for (int i = 2; ; i++)
            if (!used.Contains($"{root}{i}")) return $"{root}{i}";
    }

    /// <summary>呼叫點的參數列接回原本那個Token。</summary>
    // 一定要打開覆蓋：抬上去的參數在資產內部沒有內容（等於具名常數），不覆蓋就是取那個空欄位的預設值，
    // 值會從「Owner 的Token」默默變成 0。
    private void BindLiftedParameters(GraphNode carrier, List<(GraphToken Parameter, GraphToken Source)> lifted)
    {
        if (carrier == null || lifted == null) return;
        foreach (var (parameter, source) in lifted)
        {
            // 配對鍵是（族, 名稱）：資產參數同名不同族時，只比名字會把線接到別族那一列。
            string name = parameter.Name;
            NamedFormulaSlot binding = null;
            foreach (var current in carrier.Bindings)
                if (current?.Name == name && current.Slot?.FamilyType == parameter.FamilyType) { binding = current; break; }

            if (binding?.Slot == null)
            {
                Debug.LogWarning($"[GraphKit] 資產參數 '{name}' 沒有參數列，"
                    + $"請在這顆資產節點上手動把它接回 Token '{source?.Name}'。");
                continue;
            }
            binding.OverrideEnabled = true;
            HGReflect.SetToken(binding.Slot, source);
        }
    }

    /// <summary>子樹裡被子樹外欄位指著的載體 → 那些外部欄位。根自己不算：指著根的線會跟著它一起變成資產引用。</summary>
    private Dictionary<GraphNode, List<GraphSlotBase>> FindBoundaryShared(GraphNode root)
    {
        var result = new Dictionary<GraphNode, List<GraphSlotBase>>();
        if (root == null) return result;

        // 端點先當成走過了：Token的內容不會跟著搬進資產，指著它的欄位也就不算跨邊界。
        var visited = new HashSet<object>(HGRefComparer.Instance);
        foreach (var endpoint in CurrentTokens() ?? new List<GraphToken>())
            if (endpoint != null) visited.Add(endpoint);

        // GraphSlotBase 沒有覆寫 Equals，預設就是參考比對，不必再給 HGRefComparer。
        var innerSlots = new HashSet<GraphSlotBase>();
        foreach (var slot in HGModel.WalkSlots(root, visited)) innerSlots.Add(slot);

        var innerCarriers = new HashSet<GraphNode>();
        foreach (var slot in innerSlots)
        {
            var carrier = slot.Node;
            if (carrier != null && !ReferenceEquals(carrier, root)) innerCarriers.Add(carrier);
        }
        if (innerCarriers.Count == 0) return result;

        foreach (var slot in SlotsInCurrentGraph())
        {
            if (slot == null || innerSlots.Contains(slot)) continue;
            var carrier = slot.Node;
            if (carrier == null || !innerCarriers.Contains(carrier)) continue;

            if (!result.TryGetValue(carrier, out var users)) result[carrier] = users = new List<GraphSlotBase>();
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

        // Token一律沿用不複製：複本裡的Token節點要繼續指向同一個Token，跟著抄會變成查不到值的孤兒端點。
        var shared = new List<object>();
        foreach (var endpoint in CurrentTokens() ?? new List<GraphToken>())
            if (endpoint != null) shared.Add(endpoint);

        foreach (var pair in boundary)
        {
            var copy = GraphDeepCopy.Copy(pair.Key, shared);
            if (copy == null)
            {
                Debug.LogError("[GraphKit] 複製共用節點失敗，該欄位會跟著資產一起失去內容，詳見上一則訊息。");
                continue;
            }
            // 新舊載體不可共用識別碼：座標與選取狀態都掛在它身上。
            HGModel.ResetNodeIds(copy, shared);
            foreach (var slot in pair.Value) HGReflect.SetNode(slot, copy);
        }
    }

    /// <summary>這顆節點的內容該存成哪一種資產。動作與公式各走各的資產族，呼叫端不必自己分辨。</summary>
    private Type AssetTypeFor(HGNodeView node)
    {
        if (node.ParentSlot != null)
        {
            var slotType = node.ParentSlot.GetType();
            if (HGReflect.IsActionSlotType(slotType))
                return ConcreteAssetType(HGReflect.ActionAssetType(slotType));
            return HGReflect.AssetType(slotType);
        }

        // 未連接節點沒有父欄位，靠型別回推它屬於哪一族。
        var actionBase = ActionBaseTypeOfCurrentSystem();
        if (actionBase != null && actionBase.IsInstanceOfType(node.Obj))
            return ConcreteAssetType(ActionAssetTypeOfCurrentSystem());

        foreach (var (_, slotType) in model.FormulaKinds())
        {
            var formulaBase = HGReflect.FormulaBaseType(slotType);
            if (formulaBase != null && formulaBase.IsInstanceOfType(node.Obj))
                return HGReflect.AssetType(slotType);
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
        foreach (var g in model.ReadRootGroups())
        {
            if (g.Items == null) continue;
            var slotType = g.Items.GetType().GetGenericArguments()[0];
            return HGReflect.ActionAssetType(slotType);
        }
        return null;
    }

    private Type ActionBaseTypeOfCurrentSystem()
    {
        foreach (var g in model.ReadRootGroups())
        {
            if (g.Items == null) continue;
            var slotType = g.Items.GetType().GetGenericArguments()[0];
            return HGReflect.ActionBaseType(slotType);
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
        // 有內容的載體型別看得出來，不需要族；資產與Token節點也各有自己的型別來源。
        if (carrier.BodyObject != null || carrier.AssetObject != null || carrier.Token != null) return;

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
        foreach (var g in model.ReadRootGroups())
        {
            if (g.Items == null) continue;
            return g.Items.GetType().GetGenericArguments()[0];
        }
        return model.RootItemSlotType;
    }
}

}
