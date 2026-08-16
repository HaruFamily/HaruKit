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

    private void DrawTokenLibrary(Rect r, float top, bool inAsset)
    {
        GUI.Label(new Rect(r.x + 6f, top, r.width - 12f, 16f),
            new GUIContent(inAsset ? "呼叫端變數" : "共用變數",
                inAsset ? "資產目前以名稱對應呼叫端的變數，沒有自己的參數宣告" : ""), AGStyles.Tiny);

        var createRect = new Rect(r.x + 2f, top + 18f, r.width - 4f, 28f);
        AGStyles.Fill(createRect, AGStyles.PanelSection);
        AGStyles.Frame(createRect, AGStyles.NodeBorder);

        var kinds = model.TokenKinds();
        GUI.enabled = !inAsset && kinds.Count > 0;
        if (GUI.Button(new Rect(r.x + 4f, top + 21f, r.width - 8f, 22f), "新增變數")) ShowAddTokenMenu();
        GUI.enabled = true;

        var removeRect = new Rect(r.x + 4f, top + 50f, r.width - 8f, 20f);
        bool canRemoveToken = !inAsset && focus.Kind == AGFocusKind.Token && focus.Token != null;
        GUI.enabled = canRemoveToken;
        if (GUI.Button(removeRect, "移除變數")) RemoveToken(focus.Token);
        GUI.enabled = true;

        var searchRect = new Rect(r.x + 4f, top + 74f, r.width - 8f, 20f);
        GUI.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, 16f, 16f),
            EditorGUIUtility.IconContent("Search Icon", "搜尋變數"));
        tokenSearch = EditorGUI.TextField(new Rect(searchRect.x + 20f, searchRect.y, searchRect.width - 20f, searchRect.height), tokenSearch);

        var listRect = new Rect(r.x + 2f, top + 98f, r.width - 4f, r.yMax - top - 100f);
        var tokens = model.ReadTokens();
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
            bool isFocus = focus.Kind == AGFocusKind.Token && focus.Token != null
                && focus.Token.Key == token.Key && focus.Token.ResultType == token.ResultType;
            // 變數＝橘→綠，和畫布上的變數節點同一條漸層。
            DrawCellBackground(row, AGStyles.HeaderToken, AGStyles.HeaderFormula, i % 2 == 1, isFocus);

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 70f, 18f),
                string.IsNullOrEmpty(token.Key) ? "（未命名）" : token.Key, AGStyles.RowLabel);
            var typeRect = new Rect(row.xMax - 58f, row.y + 6f, 42f, 15f);
            AGStyles.RoundedFill(typeRect, AGStyles.HeaderFormula, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(token.TypeName, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);

            if (HasTokenIssue(token, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 1) { ShowTokenMenu(token); e.Use(); }
                else
                {
                    dragToken = token;
                    pendingTokenFocus = token;
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDrag && dragToken == token) dragTokenActive = true;
            if (e.type == EventType.MouseUp && pendingTokenFocus != null
                && pendingTokenFocus.Key == token.Key && pendingTokenFocus.ResultType == token.ResultType
                && !dragTokenActive && row.Contains(e.mousePosition))
            {
                dragToken = null;
                pendingTokenFocus = null;
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
                e.Use();
            }
        }
        GUI.EndScrollView();
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

            GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 64f, 17f), asset.name, AGStyles.RowLabel);
            string kind = entry.IsAction ? "ACT" : AGReflect.ResultTypeName(entry.ResultType);
            var typeRect = new Rect(row.xMax - 54f, row.y + 5f, 46f, 15f);
            AGStyles.RoundedFill(typeRect, payload, CellCornerRadius);
            GUI.Label(typeRect, AGStyles.Elide(kind, AGStyles.NodeChip, typeRect.width), AGStyles.NodeChip);
            GUI.Label(new Rect(row.x + 8f, row.y + 18f, row.width - 70f, 13f), entry.TypeName, AGStyles.Tiny);

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
                EnterAsset(asset, shown[i].slotType);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>找目前 ActionSystem 中能承載此資產內容的 Slot；找不到代表本 Owner 不相容。</summary>
    private List<(Type acceptedAssetType, Type slotType)> AssetSlotTypes()
    {
        var result = new List<(Type acceptedAssetType, Type slotType)>();
        foreach (var (_, list) in model.TokenKinds())
        {
            Type entryType = list.GetType().GetGenericArguments()[0];
            if (AGReflect.CreateInstance(entryType) is not ITokenEntry entry || entry.Slot == null) continue;
            Type slotType = entry.Slot.GetType();
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

    private bool HasTokenIssue(AGToken token, out string reason, out bool isError)
    {
        reason = null; isError = false;
        foreach (var issue in report.Issues)
        {
            if (issue.Focus == null || issue.Focus.Kind != AGFocusKind.Token) continue;
            if (issue.Focus.Token == null || issue.Focus.Token.Key != token.Key) continue;
            reason = issue.Line;
            isError = issue.IsError;
            if (isError) return true;
        }
        return reason != null;
    }

    private bool HasActionIssue(AGFocus action, out string reason, out bool isError)
    {
        reason = null; isError = false;
        foreach (var issue in report.Issues)
        {
            if (issue.Focus == null || !issue.Focus.SameAs(action)) continue;
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

    private void ShowAddTokenMenu()
    {
        var menu = new GenericMenu();
        foreach (var (resultType, _) in model.TokenKinds())
        {
            var capturedType = resultType;
            menu.AddItem(new GUIContent(AGReflect.ResultTypeName(capturedType)), false, () => AddToken(capturedType));
        }
        menu.ShowAsContext();
    }

    private void AddToken(Type resultType)
    {
        string typeName = AGReflect.ResultTypeName(resultType).ToLowerInvariant();
        int index = 0;
        string key;
        do { key = $"t_{typeName}_{index++}"; }
        while (TokenKeyExists(key));

        if (!model.AddToken(resultType, key, out string error))
        {
            ShowNotification(new GUIContent(error));
            return;
        }
        foreach (var token in model.ReadTokens())
        {
            if (token.Key != key || token.ResultType != resultType) continue;
            SetFocus(new AGFocus { Kind = AGFocusKind.Token, Token = token });
            break;
        }
        Invalidate();
        Repaint();
    }

    private bool TokenKeyExists(string key)
    {
        foreach (var token in model.ReadTokens())
            if (token.Key == key) return true;
        return false;
    }

    private void RemoveToken(AGToken token)
    {
        if (token == null) return;
        int refs = model.CountReferences(token);
        string msg = refs > 0
            ? $"'{token.Key}' 還有 {refs} 個欄位在引用，刪除後那些欄位會指向不存在的變數。"
            : $"確定刪除 '{token.Key}'？";
        if (!EditorUtility.DisplayDialog("刪除變數", msg, "刪除", "取消")) return;
        model.RemoveToken(token);
        SetFocus(new AGFocus());
        Invalidate();
        DoVerify(true);
        Repaint();
    }

    private void ShowTokenMenu(AGToken token)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("改名"), false, () =>
            AGPrompt.Show("變數改名", "輸入新名稱（所有引用處會同步更新）", token.Key, key =>
            {
                if (!model.RenameToken(token, key, out string error)) EditorUtility.DisplayDialog("無法改名", error, "好");
                Invalidate();
                Repaint();
            }));
        menu.AddItem(new GUIContent("刪除"), false, () =>
        {
            RemoveToken(token);
        });
        menu.ShowAsContext();
    }

    private void DrawDragTokenGhost()
    {
        if (dragToken == null) return;
        var p = Event.current.mousePosition;
        var r = new Rect(p.x + 8f, p.y + 8f, 140f, 18f);
        AGStyles.GradientFill(r, AGStyles.HeaderToken, AGStyles.HeaderFormula, CellCornerRadius);
        GUI.Label(r, $"@{dragToken.Key}", AGStyles.Chip);
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

    // ===== 右欄：時機與動作清單 =====

    private void DrawTimingPanel(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Panel);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var groups = model.ReadGroups();
        var timingSection = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 50f);
        AGStyles.Fill(timingSection, AGStyles.PanelSection);
        AGStyles.Frame(timingSection, AGStyles.NodeBorder);
        GUI.Label(new Rect(r.x + 4f, r.y + 4f, 120f, 18f), "時機", AGStyles.PanelHeader);

        string timingLabel = currentTiming != null ? currentTiming.ToString() : "（選擇時機）";
        var dropRect = new Rect(r.x + 4f, r.y + 26f, r.width - 8f, 24f);
        if (EditorGUI.DropdownButton(dropRect, new GUIContent(timingLabel), FocusType.Keyboard))
            ShowTimingMenu(groups);

        AGTimingGroup current = null;
        foreach (var g in groups)
            if (currentTiming != null && Equals(g.Timing, currentTiming)) current = g;

        GUI.enabled = currentTiming != null;
        if (GUI.Button(new Rect(r.x + 4f, r.y + 56f, r.width - 8f, 20f), "新增動作"))
            AddEmptyAction(currentTiming);
        GUI.enabled = true;

        var removeRect = new Rect(r.x + 4f, r.y + 78f, r.width - 8f, 20f);
        bool canRemoveAction = focus.Kind == AGFocusKind.Action && focus.ActionSlot != null
            && Equals(focus.Timing, currentTiming);
        GUI.enabled = canRemoveAction;
        if (GUI.Button(removeRect, "移除動作")) RemoveAction(focus);
        GUI.enabled = true;

        var listRect = new Rect(r.x + 2f, r.y + 102f, r.width - 4f, r.height - 104f);
        AGStyles.Fill(listRect, AGStyles.PanelList);

        if (current?.Actions != null) DrawActionList(listRect, current);

    }

    private void DrawActionList(Rect listRect, AGTimingGroup group)
    {
        var actions = group.Actions;
        var content = new Rect(0f, 0f, listRect.width - 16f, actions.Count * ActionCellHeight + 4f);
        actionScroll = GUI.BeginScrollView(listRect, actionScroll, content);

        for (int i = 0; i < actions.Count; i++)
        {
            var slot = actions[i];
            if (slot == null) continue;
            var row = new Rect(2f, i * ActionCellHeight + 2f, content.width - 4f, ActionCellHeight - 3f);
            bool isFocus = focus.Kind == AGFocusKind.Action && ReferenceEquals(focus.ActionSlot, slot);
            // 動作沒有容器語意，單色。
            DrawCellBackground(row, AGStyles.HeaderAction, AGStyles.HeaderAction, i % 2 == 1, isFocus);

            GUI.Label(new Rect(row.x + 5f, row.y + 5f, 12f, 18f), "≡", AGStyles.Tiny);

            bool disabled = AGReflect.GetDisabled(slot);
            bool enabled = !disabled;
            bool newEnabled = GUI.Toggle(new Rect(row.x + 20f, row.y + 6f, 16f, 16f), enabled, GUIContent.none);
            if (newEnabled != enabled) { AGReflect.SetDisabled(slot, !newEnabled); Invalidate(); }

            var focusOfRow = new AGFocus
            {
                Kind = AGFocusKind.Action,
                Timing = group.Timing,
                ActionList = actions,
                ActionIndex = i,
                ActionSlot = slot,
            };

            string typeName = AGFocus.ActionName(slot);
            string label = AGReflect.GetLabel(slot);
            string name = string.IsNullOrEmpty(label) ? typeName : label;
            GUI.Label(new Rect(row.x + 42f, row.y + 2f, row.width - 60f, 18f), name, AGStyles.RowLabel);

            if (HasActionIssue(focusOfRow, out string reason, out bool isError))
            {
                var dot = new Rect(row.xMax - 10f, row.y + 10f, 7f, 7f);
                AGStyles.Fill(dot, isError ? AGStyles.Error : AGStyles.Warning);
                GUI.Label(dot, new GUIContent("", reason));
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 1) { ShowActionMenu(group, i); e.Use(); }
                else
                {
                    pendingActionFocus = focusOfRow;
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDrag && dragActionIndex < 0
                && pendingActionFocus != null && ReferenceEquals(pendingActionFocus.ActionSlot, slot))
                dragActionIndex = i;
            if (e.type == EventType.MouseDrag && dragActionIndex >= 0 && dragActionIndex < actions.Count)
            {
                int target = Mathf.Clamp(Mathf.FloorToInt(e.mousePosition.y / ActionCellHeight), 0, actions.Count - 1);
                if (target != dragActionIndex)
                {
                    var moved = actions[dragActionIndex];
                    actions.RemoveAt(dragActionIndex);
                    actions.Insert(target, moved);
                    dragActionIndex = target;
                    RefreshActionIndices(actions);
                    Invalidate();
                }
            }
            if (e.type == EventType.MouseUp && pendingActionFocus != null
                && ReferenceEquals(pendingActionFocus.ActionSlot, slot) && dragActionIndex < 0 && row.Contains(e.mousePosition))
            {
                var nextFocus = pendingActionFocus;
                pendingActionFocus = null;
                if (isFocus) SetFocus(new AGFocus());
                else SetFocus(nextFocus);
                e.Use();
            }
        }
        GUI.EndScrollView();
    }

    private void ShowTimingMenu(List<AGTimingGroup> groups)
    {
        var menu = new GenericMenu();
        foreach (Enum timing in Enum.GetValues(model.TimingType))
        {
            AGTimingGroup group = null;
            foreach (var candidate in groups)
                if (Equals(candidate.Timing, timing)) { group = candidate; break; }

            int actionCount = group?.Actions?.Count ?? 0;
            int errors = 0;
            if (group?.Actions != null)
            {
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
            }
            string countLabel = actionCount > 0 ? actionCount.ToString() : "+";
            string label = errors > 0
                ? $"{timing} ({countLabel})　{errors} 個錯誤"
                : $"{timing} ({countLabel})";
            var captured = timing;
            menu.AddItem(new GUIContent(label), Equals(currentTiming, timing), () =>
            {
                currentTiming = captured;
                SaveCurrentTiming();
                Repaint();
            });
        }
        menu.ShowAsContext();
    }

    private void AddEmptyAction(Enum timing)
    {
        var slotType = model.ActionSlotType;
        if (slotType == null || timing == null) return;
        var slot = AGReflect.CreateInstance(slotType);
        if (slot == null) return;

        model.BreakUndoMerge();
        var group = model.AddGroup(timing);
        if (group?.Actions == null) return;
        group.Actions.Add(slot);

        SetFocus(new AGFocus
        {
            Kind = AGFocusKind.Action, Timing = group.Timing,
            ActionList = group.Actions, ActionIndex = group.Actions.Count - 1, ActionSlot = slot,
        });
        Invalidate();
        Repaint();
    }

    private void ShowActionMenu(AGTimingGroup group, int index)
    {
        var menu = new GenericMenu();
        var slot = group.Actions[index];

        menu.AddItem(new GUIContent("設定標籤"), false, () =>
            AGPrompt.Show("動作標籤", "用來區分同名動作（例如：主傷害 / 濺射）", AGReflect.GetLabel(slot) ?? "", text =>
            {
                AGReflect.SetLabel(slot, text);
                Invalidate();
                Repaint();
            }));

        menu.AddItem(new GUIContent(AGReflect.GetDisabled(slot) ? "啟用" : "停用"), false, () =>
        {
            AGReflect.SetDisabled(slot, !AGReflect.GetDisabled(slot));
            Invalidate();
            Repaint();
        });

        menu.AddItem(new GUIContent("刪除"), false, () =>
        {
            RemoveAction(new AGFocus
            {
                Kind = AGFocusKind.Action, Timing = group.Timing,
                ActionList = group.Actions, ActionIndex = index, ActionSlot = slot,
            });
        });
        menu.ShowAsContext();
    }

    private void RemoveAction(AGFocus action)
    {
        if (action?.ActionList == null || action.ActionSlot == null) return;
        int index = IndexOfReference(action.ActionList, action.ActionSlot);
        if (index < 0) return;
        if (!EditorUtility.DisplayDialog("刪除動作", "確定刪除這個動作？", "刪除", "取消")) return;
        action.ActionList.RemoveAt(index);
        foreach (var group in model.ReadGroups())
        {
            if (!ReferenceEquals(group.Actions, action.ActionList)) continue;
            if (group.Actions.Count == 0) model.RemoveGroup(group);
            break;
        }
        SetFocus(new AGFocus());
        Invalidate();
        DoVerify(true);
        Repaint();
    }

    private void RefreshActionIndices(IList actions)
    {
        if (focus.Kind == AGFocusKind.Action && ReferenceEquals(focus.ActionList, actions))
            focus.ActionIndex = IndexOfReference(actions, focus.ActionSlot);
        if (pendingActionFocus != null && ReferenceEquals(pendingActionFocus.ActionList, actions))
            pendingActionFocus.ActionIndex = IndexOfReference(actions, pendingActionFocus.ActionSlot);
    }

    private static int IndexOfReference(IList list, object item)
    {
        if (list == null || item == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    // ===== 右欄（資產焦點）：引用清單 =====

    private void DrawReferencePanel(Rect r)
    {
        AGStyles.Fill(r, AGStyles.Panel);
        AGStyles.Frame(r, AGStyles.NodeBorder);

        var asset = focus.AssetObject;
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), "引用此資產的對象", AGStyles.PanelHeader);

        var subscribers = AGReflect.Get(asset, "_subscribers") as IList;
        int count = subscribers?.Count ?? 0;
        GUI.Label(new Rect(r.x + 6f, r.y + 22f, r.width - 12f, 16f),
            count > 0 ? $"共 {count} 個對象（清單可能不完整，可重建）" : "清單是空的，按下方重建掃描整個專案", AGStyles.Tiny);

        var listRect = new Rect(r.x + 2f, r.y + 40f, r.width - 4f, r.height - 92f);
        AGStyles.Fill(listRect, AGStyles.PanelList);

        var content = new Rect(0f, 0f, listRect.width - 16f, count * 24f + 4f);
        referenceScroll = GUI.BeginScrollView(listRect, referenceScroll, content);
        for (int i = 0; i < count; i++)
        {
            var so = subscribers[i] as ScriptableObject;
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

        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 50f, r.width - 8f, 20f), "重建引用清單（掃描整個專案）"))
            RebuildReferences(asset);
        if (GUI.Button(new Rect(r.x + 4f, r.yMax - 26f, r.width - 8f, 20f), "全部重新驗證"))
            VerifyAllSubscribers(asset);
    }

    /// <summary>掃描專案裡所有 Owner，重建這個資產的引用清單。只看磁碟上的內容，未存檔的修改不算。</summary>
    private void RebuildReferences(UnityEngine.Object asset)
    {
        var found = new List<ScriptableObject>();
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("重建引用清單", $"{i + 1}/{guids.Length}", (float)i / guids.Length))
                    break;

                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not IActionSystemOwner) continue;

                var field = AGModel.FindSystemField(so);
                var system = field?.GetValue(so);
                if (system == null) continue;

                foreach (var slot in AGModel.SlotsOfSystem(system))
                {
                    if (AGReflect.GetAsset(slot) != asset) continue;
                    found.Add(so);
                    break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        asset.GetType().GetMethod("ClearSubscribers")?.Invoke(asset, null);
        var register = asset.GetType().GetMethod("RegisterSubscriber");
        foreach (var so in found) register?.Invoke(asset, new object[] { so });

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"找到 {found.Count} 個引用"));
    }

    private void VerifyAllSubscribers(UnityEngine.Object asset)
    {
        if (AGReflect.Get(asset, "_subscribers") is not IList subscribers) return;
        int ok = 0, fail = 0;
        foreach (var s in subscribers)
        {
            if (s is not IActionSystemOwner owner) continue;
            owner.VerifyActionSystem();
            if (owner.IsActionSystemValidated()) ok++; else fail++;
            if (s is UnityEngine.Object so) EditorUtility.SetDirty(so);
        }
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
        if (issue.Focus != null && !issue.Focus.SameAs(focus)) SetFocus(issue.Focus);
        pendingCenterTarget = issue.Slot ?? issue.Node;
        graphDirty = true;
        Repaint();
    }
}

}
