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
        bool isAction = slot != null
            ? AGReflect.IsActionSlotType(slot.GetType())
            : node.IsActionNode || (node.IsAssetNode && node.ResultType == null);

        // 同一個結果型別的 Formula／Asset 一律可以互換，包含候選池裡沒有父欄位的節點：
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
    /// 父欄位 → 連入邊的欄位 → 目前資產對應的欄位。
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
        else
        {
            // 動作欄位的標籤：清單列上只顯示不編輯，改名的入口放這裡。
            menu.AddItem(new GUIContent("設定標籤…"), false, () =>
                AGPrompt.Show("動作標籤", "用來區分同型別的動作（例如：主傷害 / 濺射）；留空就顯示型別名",
                    AGReflect.GetLabel(slot) ?? "", text =>
                    {
                        model.BreakUndoMerge();
                        AGReflect.SetLabel(slot, text.Trim());
                        Invalidate();
                        Repaint();
                    }));
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

        if (useType != 0)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清除這個欄位"), false, () => CutLink(slot));
        }
        if (row.AssetBinding != null && model.Carrier(row.OwnerNodeId) is GraphNode assetCarrier
            && IsStaleBinding(assetCarrier, row.AssetBinding))
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("移除失效的資產參數綁定"), false, () =>
            {
                var child = row.AssetBinding.Slot?.Node;
                if (child != null)
                {
                    row.AssetBinding.Slot.SetNode(null);
                    model.AddOrphan(child);
                }
                assetCarrier.Bindings.Remove(row.AssetBinding);
                Invalidate();
                Repaint();
            });
        }
        menu.ShowAsContext();
    }

    private bool IsStaleBinding(GraphNode carrier, NamedFormulaSlot binding)
    {
        if (carrier?.AssetObject == null || binding?.Slot == null) return true;
        foreach (var parameter in model.AssetParameters(carrier.AssetObject))
            if (parameter.Name == binding.Name
                && parameter.ResultType == binding.Slot.ResultType
                && parameter.PackType == binding.Slot.PackType) return false;
        return true;
    }

    /// <summary>
    /// 標註一顆節點：從此它是這張圖的對外端點，Inspector 可以用這個名字查它的值。
    /// 內容不搬、載體不換、連線不動——標註只是在載體上加一個名字。
    /// </summary>
    private void RegisterToken(AGNode node, string key)
    {
        if (node?.Carrier == null) return;
        if (!model.SetTokenName(node.Carrier, key, CurrentTokenScope(), out string error))
        {
            EditorUtility.DisplayDialog("無法標註", error, "好");
            return;
        }
        Invalidate();
        Repaint();
    }

    private void UnregisterToken(AGNode node)
    {
        if (node?.Carrier == null) return;
        model.BreakUndoMerge();
        model.ClearTokenName(node.Carrier);
        Invalidate();
        Repaint();
    }

    private void ShowNodeMenu(AGNode node)
    {
        // 換來源走 Header 名稱區、中斷連線走連線本身（雙擊），兩者都不重複放進右鍵選單。
        var menu = new GenericMenu();

        if (node.IsTimingGroup)
        {
            menu.AddItem(new GUIContent("刪除這個時機"), false, () => RemoveTimingGroup(node));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("聚焦全部節點"), false, FrameAll);
            menu.AddItem(new GUIContent("整理版面"), false, ResetLayout);
            menu.ShowAsContext();
            return;
        }

        if (node.IsPlaceholder && node.ParentSlot != null)
        {
            if (!node.IsRoot) menu.AddItem(new GUIContent("清除空 Node"), false, () => DeleteNode(node));
            menu.ShowAsContext();
            return;
        }

        // 標註只適用可求值節點；資產根載體不會被資產格式保存，因此禁止標註。
        bool assetRoot = focus.Kind == AGFocusKind.Asset && ReferenceEquals(node.ParentSlot, focus.AssetHostSlot);
        if (node.Carrier != null && node.ResultType != null && !assetRoot)
        {
            if (node.TokenName != null)
            {
                menu.AddItem(new GUIContent($"重新命名標註（目前 @{node.TokenName}）…"), false, () =>
                    AGPrompt.Show("標註名稱", "外部（Inspector）用這個名字查這顆節點的值",
                        node.TokenName, key => RegisterToken(node, key)));
                menu.AddItem(new GUIContent("取消標註"), false, () => UnregisterToken(node));
            }
            else
            {
                menu.AddItem(new GUIContent("註冊為 Token…"), false, () =>
                    AGPrompt.Show("標註名稱", "外部（Inspector）用這個名字查這顆節點的值",
                        "", key => RegisterToken(node, key)));
            }
            menu.AddSeparator("");
        }

        if (node.Obj != null)
        {
            bool isAction = node.ParentSlot != null
                ? AGReflect.IsActionSlotType(node.ParentSlot.GetType())
                : ActionBaseTypeOfCurrentSystem()?.IsInstanceOfType(node.Obj) ?? false;
            menu.AddItem(new GUIContent(isAction ? "轉存為動作資產" : "轉存為公式資產"), false, () => ExtractAsset(node));
        }

        if (!node.IsRoot && (node.Obj != null || node.IsAssetNode))
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

    private IEnumerable<GraphNode> CurrentTokenScope()
    {
        if (focus.Kind != AGFocusKind.Asset) return model.AllCarriers();
        return model.CarriersOf(focus.Roots, focus.AssetOrphans);
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
        if (focus.AssetOrphans == null) yield break;
        foreach (var orphan in focus.AssetOrphans)
            foreach (var slot in AGModel.WalkSlots(orphan, visited)) yield return slot;
    }

    private void ShowCanvasMenu(Vector2 graphMouse)
    {
        var menu = new GenericMenu();
        var menuPos = Event.current.mousePosition;
        bool canEditFocus = focus.Kind == AGFocusKind.Timing || focus.Kind == AGFocusKind.Action;

        // 時機節點由使用者自己建，位置就是按下右鍵的地方。
        if (focus.Kind == AGFocusKind.Timing)
        {
            AddTimingMenuItems(menu, "新增時機節點/", graphMouse);
            menu.AddSeparator("");
        }

        foreach (var (rt, slotType) in model.FormulaKinds())
        {
            var baseType = AGReflect.FormulaBaseType(slotType);
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
