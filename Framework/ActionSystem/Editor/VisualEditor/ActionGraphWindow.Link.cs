namespace PinPlugin.ActionSystem.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 拉線：相容性快取、命中測試、連接與中斷。
/// </summary>
public partial class ActionGraphWindow
{
    private void BeginLinkFromRow(AGRow row)
    {
        linking = true;
        linkRow = row;
        linkNode = null;
        RebuildLinkCompatibility();
    }

    private void BeginLinkFromNode(AGNode node)
    {
        linking = true;
        linkRow = null;
        linkNode = node;
        RebuildLinkCompatibility();
    }

    private void EndLink()
    {
        linking = false;
        linkRow = null;
        linkNode = null;
        linkCompatibleNodeIds.Clear();
        linkCompatibleRows.Clear();
    }

    /// <summary>
    /// 拉線起手時把整張圖判定一次：從欄位拉出去就標出所有能當來源的 Node，從 Node 拉出去就標出所有收得下它的欄位。
    /// 判定結果整段拖曳期間不變（拖曳不改資料），所以算一次就夠，比原本每幀重算便宜。
    /// </summary>
    private void RebuildLinkCompatibility()
    {
        linkCompatibleNodeIds.Clear();
        linkCompatibleRows.Clear();
        if (graph == null) return;

        if (linkRow != null)
        {
            foreach (var node in graph.Nodes)
                if (!node.IsRoot && !node.Hidden && CanConnectLink(linkRow, node)) linkCompatibleNodeIds.Add(node.Id);
            return;
        }

        if (linkNode == null) return;
        foreach (var node in graph.Nodes)
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (row.IsLinkable && CanConnectLink(row, linkNode)) linkCompatibleRows.Add(row);
    }

    /// <summary>本次拉線中直接查快取；不在拉線中（例如放開瞬間的重算）才實算。</summary>
    private bool CanLinkTo(AGRow row, AGNode node)
        => linking && ReferenceEquals(row, linkRow)
            ? linkCompatibleNodeIds.Contains(node.Id)
            : CanConnectLink(row, node);

    private bool CanLinkFrom(AGRow row, AGNode node)
        => linking && ReferenceEquals(node, linkNode)
            ? linkCompatibleRows.Contains(row)
            : CanConnectLink(row, node);

    private AGNode LinkTargetNode(Vector2 graphMouse)
    {
        if (!linking) return null;
        if (linkRow != null) return SnappedOutputNode(graphMouse, linkRow);
        return linkNode != null ? OwnerOfRow(SnappedInputRow(graphMouse, linkNode)) : null;
    }

    private Vector2 LinkPreviewEnd(Vector2 graphMouse)
    {
        if (linkRow != null)
        {
            var target = SnappedOutputNode(graphMouse, linkRow);
            return target != null ? target.OutputPort : graphMouse;
        }

        var row = SnappedInputRow(graphMouse, linkNode);
        return row != null ? row.PortPos : graphMouse;
    }

    private AGNode SnappedOutputNode(Vector2 graphMouse, AGRow row)
    {
        if (graph == null || row == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (node.IsRoot || node.Hidden || !node.Rect.Contains(graphMouse) || !CanLinkTo(row, node)) continue;
            return node;
        }

        float maxDistanceSqr = LinkSnapDistance * LinkSnapDistance / (zoom * zoom);
        float nearestDistanceSqr = maxDistanceSqr;
        AGNode nearest = null;
        foreach (var node in graph.Nodes)
        {
            if (node.IsRoot || node.Hidden || !CanLinkTo(row, node)) continue;
            float distanceSqr = (node.OutputPort - graphMouse).sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr) continue;
            nearestDistanceSqr = distanceSqr;
            nearest = node;
        }
        return nearest;
    }

    private AGRow SnappedInputRow(Vector2 graphMouse, AGNode node)
    {
        if (graph == null || node == null) return null;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var owner = graph.Nodes[i];
            if (!owner.Rect.Contains(graphMouse)) continue;

            AGRow nearestInNode = null;
            float nearestY = float.MaxValue;
            foreach (var row in AGGraph.AllRows(owner.Rows))
            {
                if (!row.IsLinkable || !CanLinkFrom(row, node)) continue;
                float distanceY = Mathf.Abs(row.ScreenRect.center.y - graphMouse.y);
                if (distanceY >= nearestY) continue;
                nearestY = distanceY;
                nearestInNode = row;
            }
            if (nearestInNode != null) return nearestInNode;
        }

        float maxDistanceSqr = LinkSnapDistance * LinkSnapDistance / (zoom * zoom);
        float nearestDistanceSqr = maxDistanceSqr;
        AGRow nearest = null;
        foreach (var owner in graph.Nodes)
        {
            foreach (var row in AGGraph.AllRows(owner.Rows))
            {
                if (!row.IsLinkable || !CanLinkFrom(row, node)) continue;
                float distanceSqr = (row.PortPos - graphMouse).sqrMagnitude;
                if (distanceSqr > nearestDistanceSqr) continue;
                nearestDistanceSqr = distanceSqr;
                nearest = row;
            }
        }
        return nearest;
    }

    private AGNode OwnerOfRow(AGRow target)
    {
        if (graph == null || target == null) return null;
        foreach (var node in graph.Nodes)
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (ReferenceEquals(row, target)) return node;
        return null;
    }

    private AGRow RowAt(Vector2 graphPoint, out AGNode owner)
    {
        owner = null;
        if (graph == null) return null;
        foreach (var node in graph.Nodes)
        {
            if (!node.Rect.Contains(graphPoint)) continue;
            foreach (var row in AGGraph.AllRows(node.Rows))
                if (row.IsLinkable && row.ScreenRect.Contains(graphPoint)) { owner = node; return row; }
        }
        return null;
    }

    /// <summary>找滑鼠附近的直線連線。</summary>
    private AGLink LinkAt(Vector2 graphPoint)
    {
        if (graph == null) return null;
        foreach (var link in graph.Links)
        {
            if (!IsLinkVisible(link)) continue;       // 沒畫出來的線不該點得到
            Vector2 a = link.ParentRow.PortPos;
            Vector2 b = link.Target.OutputPort;
            if (PointToSegmentSqrDistance(graphPoint, a, b) < 36f) return link;
        }
        return null;
    }

    private static float PointToSegmentSqrDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.01f) return (point - from).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSqr);
        return (point - (from + segment * t)).sqrMagnitude;
    }

    /// <summary>切線：父欄位改回常數／空槽，被切下來的來源留成候選。</summary>
    private void CutLink(AGLink link)
    {
        CutLink(link?.ParentRow?.Slot);
    }

    private void CutLink(object slot)
    {
        if (slot == null) return;
        PreserveVisibleNodePositions();
        AttachSource(slot, null);
        Invalidate();
    }

    /// <summary>
    /// 換掉欄位接的來源載體。舊載體若沒有其他欄位在用，原地留成候選——完整子樹與座標都跟著載體走。
    /// </summary>
    private void AttachSource(object slot, GraphNode next)
    {
        if (slot == null) return;
        var old = AGReflect.GetNode(slot);
        if (ReferenceEquals(old, next)) return;

        AGReflect.SetNode(slot, next);
        if (old != null && !IsCarrierUsed(old))
        {
            model.AddOrphan(old);
            // 切下來就失去父欄位。空節點的型別線索只剩這個欄位，記成族，候選池裡才還畫得出型別。
            RememberOrphanKind(old, slot.GetType());
        }
        if (next != null)
        {
            next.EnsureId();
            model.RemoveOrphan(next);
        }
    }

    /// <summary>還有沒有別的欄位指著這個載體（共用來源）。</summary>
    private bool IsCarrierUsed(GraphNode carrier)
    {
        if (carrier == null) return false;
        foreach (var slot in SlotsInCurrentGraph())
            if (ReferenceEquals(AGReflect.GetNode(slot), carrier)) return true;
        if (focus.Kind == AGFocusKind.Asset && focus.AssetHostSlot != null)
            return ReferenceEquals(AGReflect.GetNode(focus.AssetHostSlot), carrier);
        return false;
    }

    /// <summary>建立新的空載體並接上欄位；使用者接著在節點上選具體來源。</summary>
    private GraphNode NewSource(object slot)
    {
        var carrier = new GraphNode();
        carrier.EnsureId();
        AttachSource(slot, carrier);
        return carrier;
    }

    private void ResolveLink(Vector2 graphMouse)
    {
        if (linkRow?.Slot == null) return;
        var target = SnappedOutputNode(graphMouse, linkRow);
        if (TryConnectLink(linkRow, target)) return;

        // 落在既有 Node 但沒有相容來源時取消，不能把它誤判成空白而疊一顆空 Node 上去。
        if (NodeAt(graphMouse) != null)
        {
            ShowNotification(new GUIContent("請拖到相容的節點或畫布空白處"));
            return;
        }

        // 真正的空白處放開：先建立空 Node，讓使用者在 Node 上決定具體型別。
        BreakUndoMerge();
        PreserveVisibleNodePositions();
        NewSource(linkRow.Slot).Pos = SnapToGrid(graphMouse);
        Invalidate();
        Repaint();
    }

    private void ResolveLinkFromOutput(Vector2 graphMouse)
    {
        var row = SnappedInputRow(graphMouse, linkNode);
        if (row == null || !TryConnectLink(row, linkNode))
            ShowNotification(new GUIContent("請拖到相容的參數接點"));
    }

    /// <summary>接線＝欄位指到那個節點的載體。Token／資產節點因此天然可以被多個欄位共用。</summary>
    private bool TryConnectLink(AGRow row, AGNode target)
    {
        if (row?.Slot == null || target?.Carrier == null) return false;

        if (!CanConnectLink(row, target))
        {
            ShowNotification(new GUIContent("型別或連線關係不符"));
            return true;
        }

        BreakUndoMerge();
        PreserveVisibleNodePositions();
        AttachSource(row.Slot, target.Carrier);
        Invalidate();
        return true;
    }

    private static bool CanConnectLink(AGRow row, AGNode target)
    {
        if (row?.Slot == null || target?.Carrier == null) return false;
        if (row.Locked) return false;             // 沒勾覆蓋的參數不收來源：接上去也不會被採用
        if (target.IsAssetNode)
            return CanAssignAsset(row, target.Asset) && !WouldCreateCycle(row.Slot, target.Carrier);
        // 變數節點沒有內容，型別由端點的取值欄位決定；環偵測要走進端點的子樹。
        if (target.IsVariableNode)
            return AGReflect.AcceptsEndpoint(row.Slot, target.Endpoint)
                && !WouldCreateCycle(row.Slot, target.Endpoint?.Slot);

        // 空節點還沒有身分，接上去才由父欄位決定它能變成哪一族；沒有內容就沒有型別可牴觸。
        if (target.IsPlaceholder) return !WouldCreateCycle(row.Slot, target.Carrier);

        if (target.Obj == null) return false;

        object slot = row.Slot;
        Type accepted = row.IsActionSlot
            ? AGReflect.ActionBaseType(slot.GetType())
            : AGReflect.FormulaBaseType(slot.GetType());
        if (accepted == null || !accepted.IsInstanceOfType(target.Obj)) return false;
        return !WouldCreateCycle(slot, target.Carrier);
    }

    private static bool WouldCreateCycle(object slot, object node)
    {
        foreach (var childSlot in AGModel.WalkSlots(node, new HashSet<object>(AGRefComparer.Instance)))
            if (ReferenceEquals(childSlot, slot)) return true;
        return false;
    }

    /// <summary>把一個新建立的具體 Action／Formula 接到欄位（右鍵「指定公式」等入口）。</summary>
    private void Connect(object slot, object node)
    {
        if (node is not ActionSystemNode body) return;
        PreserveVisibleNodePositions();
        NewSource(slot).SetBody(body);
        Invalidate();
    }
}

}
