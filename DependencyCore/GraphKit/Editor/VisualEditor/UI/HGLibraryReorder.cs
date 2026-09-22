namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 庫清單的拖曳重排：列左緣的把手、拖曳中的插入線，以及「放開才真的搬」這條規則。
/// </summary>
// 只回傳意圖（from／to），資料交易由呼叫端處理——與 <see cref="HGLibraryOrder"/> 同一個分工。
//
// **把手是唯一的抓取點。** 庫的列身已經有自己的拖曳語意（拖到畫布、拖到「－ 移除」上），
// 整列可抓會讓兩個手勢互搶，而且使用者分不出這一下會搬順序還是會刪掉東西。
//
// 一個面板可以有多份（例如外層清單與展開後的項目各一份）；同時只會有一份在拖，
// 因為按下把手時呼叫端要自己把另一份 Clear 掉。
public sealed class HGLibraryReorder
{
    /// <summary>把手欄寬。列的其餘部分從這裡之後開始。</summary>
    public const float HandleWidth = 14f;

    // 存 id 不存物件：Undo／Redo 會把整份資料換掉，留著舊參考會指到歷程裡的死物。
    private string activeId;
    private int fromIndex = -1;
    private int target = -1;

    // 這一輪每一列的中線 Y（與列同一個座標系）。比中線而不是比邊界——邊界會在兩格之間抖動。
    private readonly List<float> mids = new List<float>();

    public bool Active => activeId != null;

    /// <summary>每次 Draw 最前面呼叫，清掉上一輪的列位置。</summary>
    public void BeginFrame() => mids.Clear();

    public void Clear()
    {
        activeId = null;
        fromIndex = -1;
        target = -1;
    }

    /// <summary>畫一列的把手並收下按下。<paramref name="mid"/> 是這一列（或整塊）的中線 Y。</summary>
    /// <returns>這一列正在被拖。</returns>
    public bool Row(Rect handle, string id, int index, float mid)
    {
        mids.Add(mid);
        bool dragging = Active && activeId == id;
        GUI.Label(handle, new GUIContent("≡", "拖曳可調整順序"), dragging ? HGStyles.RowLabel : HGStyles.Tiny);

        // 沒有識別碼的項目不給拖：空字串會讓所有同樣沒有 Id 的列都被認成「正在拖的那一個」。
        if (string.IsNullOrEmpty(id)) return false;

        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !handle.Contains(e.mousePosition)) return dragging;

        activeId = id;
        fromIndex = index;
        target = index;
        // 吃掉這一下：同一個 MouseDown 否則會再被列身當成起拖或改名。
        e.Use();
        return true;
    }

    /// <summary>這一列現在是插入目標嗎。<paramref name="below"/> 為 true 時線畫在下緣。</summary>
    public bool IsTarget(int index, out bool below)
    {
        below = target > fromIndex;
        return Active && target == index;
    }

    /// <summary>插入位置的提示線。一條線就夠，不需要動到資料。</summary>
    public static void InsertLine(Rect block, bool below)
    {
        float y = below ? block.yMax : block.y;
        HGStyles.Fill(new Rect(block.x + 2f, y - 1f, block.width - 4f, 2f), HGStyles.Link);
    }

    /// <summary>
    /// 走完全部列之後呼叫：更新拖曳目標，並回報放開時要不要搬。
    /// **必須在列的同一個座標系裡呼叫**（在 ScrollView 內就要在 `EndScrollView` 之前），
    /// 否則滑鼠位置與記下來的中線不同一套。
    /// </summary>
    /// <returns>放開了而且位置有變。</returns>
    public bool EndFrame(out int from, out int to)
    {
        from = fromIndex;
        to = target;
        if (!Active) return false;

        var e = Event.current;
        if (e.type == EventType.MouseDrag)
        {
            target = IndexAt(e.mousePosition.y);
            to = target;
            e.Use();
        }

        // rawType：拖到視窗外放開時 type 會被吃掉，rawType 仍收得到，不然狀態會一直掛著。
        if (e.rawType != EventType.MouseUp) return false;

        bool changed = from >= 0 && to >= 0 && from != to;
        Clear();
        return changed;
    }

    private int IndexAt(float mouseY)
    {
        for (int i = 0; i < mids.Count; i++)
            if (mouseY < mids[i]) return i;
        return Mathf.Max(0, mids.Count - 1);
    }
}

}
