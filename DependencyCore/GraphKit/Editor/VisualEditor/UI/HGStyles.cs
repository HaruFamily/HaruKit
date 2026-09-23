namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 編輯器共用的顏色與 GUIStyle。顏色一律讀目前的 <see cref="HGTheme"/>；GUIStyle 只能在 OnGUI 期間建立，全部走 lazy，
/// 換主題時由 <see cref="ResetCache"/> 清掉重建。
/// </summary>
public static class HGStyles
{
    private static HGTheme T => HGTheme.Current;

    // 配色原則：**灰是結構，色只留給語意**。預設主題的畫布、面板、節點本體、線全部無彩，
    // 只有「節點身分」（Header）與「狀態」（選取、錯誤、警告）帶色相，色彩因此永遠等於資訊。
    // 自訂主題可以換整體色調，但同一個語意仍只用同一個鍵。
    public static Color Canvas => T.canvas;
    public static Color Grid => T.grid;
    public static Color GridBold => T.gridBold;
    public static Color Toolbar => T.toolbar;
    public static Color Panel => T.panel;
    public static Color PanelSection => T.panelSection;
    public static Color PanelList => T.panelList;
    public static Color Console => T.console;

    public static Color NodeBody => T.nodeBody;
    public static Color NodeBorder => T.nodeBorder;
    public static Color NodeBorderSelected => T.nodeBorderSelected;   // 選取＝狀態；預設暖金，全灰畫面裡一眼可見

    /// <summary>HEAD 專用外框：靠明度而不是色相和選取分開（預設純白灰）。</summary>
    public static Color HeadBorder => T.headBorder;

    /// <summary>
    /// HEAD 專用 Header 底色，代表流程入口，和 Action 以明度與色相分開：前者是從哪裡開始，後者是做什麼。
    /// 保留白外框與光暈，在任何縮放下都認得出起點。
    /// </summary>
    public static Color HeaderHead => T.headerHead;

    /// <summary>停用節點蓋在最上層的暗紗：停用是狀態不是身分，所以壓明度、不換色相。</summary>
    public static Color DisabledVeil => T.disabledVeil;

    /// <summary>接到停用節點的連線：同樣只壓明度，維持「灰是結構」的規則。</summary>
    public static Color LinkDisabled => T.linkDisabled;
    public static Color NodeNote => T.nodeNote;
    public static Color NodeNoteBorder => T.nodeNoteBorder;

    // Header 底色表示身分；下緣共用色帶另表達驗證與執行狀態。
    // 六種身分分開色相與明度，縮小或色弱時仍可辨識。主題調色時要維持：暖色（Action／Formula）是會執行的邏輯，
    // 冷色（Asset／Token）是可重用的引用；Property 要和 HEAD 分得開。
    public static Color HeaderAction => T.headerAction;
    public static Color HeaderFormula => T.headerFormula;
    public static Color HeaderAsset => T.headerAsset;
    public static Color HeaderToken => T.headerToken;
    public static Color HeaderProperty => T.headerProperty;

    /// <summary>Header 上的字與小圖示。Header 底色要深到讓這個顏色讀得出來。</summary>
    public static Color HeaderInk => T.headerInk;

    /// <summary>Header 上的疊層底色（chip、名稱區）。</summary>
    public static Color HeaderOverlay => T.headerOverlay;

    // 取值接點用明度分層：空槽暗灰、接上與提供值的接點亮灰白；相容提示使用獨立外圈。
    public static Color Link => T.link;
    public static Color InputPortLive => T.inputPortLive;
    public static Color OutputPortLive => T.outputPortLive;

    // 停用只壓暗，錯誤優先；選取連線仍使用暖金。
    public static Color OutputPortColor => T.outputPortColor;

    /// <summary>接點環內的底色：節點與畫布底色不同，空心處統一成同一色，環才讀得出來。</summary>
    public static Color PortHole => T.portHole;

    /// <summary>節點內與面板上的一般文字。原生樣式的字色跟 Unity 主題走，所以每個樣式都要明確指定。</summary>
    public static Color Text => T.text;

    public static Color Muted => T.muted;
    public static Color RowAlt => T.rowAlt;
    public static Color LibraryCellBorder => T.libraryCellBorder;

    // 清單是「一段」而不是「一堆長得一樣的列」：底帶、斑馬紋與縱線都是結構訊息，所以只用明度不用色相。
    // 底帶壓暗而不是提亮：節點本體已經是中灰，往下沉才分得出「這一段是凹進去的清單」。
    public static Color ListBand => T.listBand;
    // 斑馬紋做成雙向（一亮一暗）而不是單向疊一層淡白：Slot 元素右半被 HGValueField 的欄位框蓋住，
    // 只剩左半在比對，單向 5% 的差異等於看不見。
    public static Color ListStripeEven => T.listStripeEven;
    public static Color ListStripeOdd => T.listStripeOdd;
    public static Color ListRule => T.listRule;   // 新增列與整段清單的外框
    // 疊在底帶上的標題列：比斑馬紋暗一階，相鄰兩段清單靠「新的一段從這裡開始」分開。只用明度，不佔色相。
    public static Color ListHeader => T.listHeader;
    public static Color ListRowHover => T.listRowHover;
    public static Color ListRowDragging => T.listRowDragging;

    /// <summary>
    /// 左右欄清單格的底色：用節點 Header 的身分色沖淡，讓「清單上的一列」和「畫布上的那顆節點」是同一個顏色語彙。
    /// 交錯列只差一階濃度；聚焦中的那一列直接給滿色。
    /// </summary>
    public static Color CellTint(Color kind, bool altRow, bool focused)
        => focused ? kind : Color.Lerp(PanelList, kind, altRow ? 0.52f : 0.38f);

    /// <summary>
    /// 「一個群組連同它的內容」的底色：比清單格更淡，群組列鋪在它上面仍然是那一塊裡最顯眼的。
    /// </summary>
    // 濃度刻意壓到 CellTint 的三分之一左右：這一層的工作是圈出範圍，不是吸引視線。
    public static Color GroupTint(Color kind) => Color.Lerp(PanelList, kind, 0.14f);

    // 語意色：錯誤永遠是紅、警告永遠是琥珀，不參與配色調整。
    public static readonly Color InputPortError = new(1f, 0.42f, 0.42f);
    public static readonly Color Error = new(1f, 0.42f, 0.42f);
    public static readonly Color Warning = new(1f, 0.78f, 0.34f);
    public static Color ExecutionNotVisited => T.executionNotVisited;
    public static Color ExecutionRunning => T.executionRunning;
    public static Color ExecutionCompleted => T.executionCompleted;
    public static Color ExecutionCancelled => T.executionCancelled;

    public static Color OverlayPanel => T.overlayPanel;
    public static Color BoxSelect => T.boxSelect;
    public static Color ResizeGrip => T.resizeGrip;
    public static Color FocusBanner => T.focusBanner;

    /// <summary>拖到「新增」按鈕上的落點底色（複製）。</summary>
    public static Color DropCreate => T.dropCreate;

    /// <summary>拖到「移除」按鈕上的落點底色。</summary>
    public static Color DropRemove => T.dropRemove;

    /// <summary>工具列按鈕的染色，乘在按鈕底色上（<c>GUI.backgroundColor</c>）。</summary>
    public static Color ToolbarLocked => T.toolbarLocked;
    public static Color SaveHighlight => T.saveHighlight;

    /// <summary>換主題後丟掉已建立的 GUIStyle 與漸層貼圖，下次取用時依新主題重建。</summary>
    public static void ResetCache()
    {
        nodeTitle = nodeDesc = focusTitle = rowLabel = rowLabelError = chip = nodeChip = slotChip = null;
        inputPortGlyph = headerButton = headerButtonDim = overlayTitle = panelHeader = consoleRow = tiny = listIndex = listAdd = null;
        foreach (var tex in gradientCache.Values) if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
        gradientCache.Clear();
        elideCache.Clear();
    }

    /// <summary>
    /// 所有狀態的字色設成同一色。從 EditorStyles 複製出來的樣式帶著 Unity 主題的字色，只設 normal 的話
    /// hover／focused 等狀態仍會跟著 Unity 主題變。
    /// </summary>
    private static GUIStyle Ink(GUIStyle style, Color color)
    {
        style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
        style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = color;
        return style;
    }

    private static GUIStyle nodeTitle, nodeDesc, focusTitle, rowLabel, rowLabelError, chip, nodeChip, slotChip, inputPortGlyph, headerButton, headerButtonDim, overlayTitle, panelHeader, consoleRow, tiny, listIndex, listAdd;

    public static GUIStyle NodeTitle => nodeTitle ??= Ink(new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 12,
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 6, 0, 0),
    }, HeaderInk);

    public static GUIStyle NodeDesc => nodeDesc ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        padding = new RectOffset(6, 6, 0, 0),
        wordWrap = true,
    }, Muted);

    public static GUIStyle FocusTitle => focusTitle ??= Ink(new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 15,
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(4, 4, 0, 0),
    }, Text);

    public static GUIStyle RowLabel => rowLabel ??= Ink(new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(4, 2, 0, 0),
    }, Text);

    public static GUIStyle RowLabelError => rowLabelError ??= Ink(new GUIStyle(RowLabel), Error);

    public static GUIStyle Chip => chip ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 4, 0, 0),
    }, T.chipText);

    /// <summary>畫布左上角說明面板的標題：底色與 Header 不同，字色另設，不沿用 Header 的字。</summary>
    public static GUIStyle OverlayTitle => overlayTitle ??= Ink(new GUIStyle(NodeTitle), T.overlayTitle);

    /// <summary>
    /// 參數列最前面的型別 chip 底色。**中性色，不用色相**：Header 的身分色已經被「來源種類」用掉，
    /// 再開一套型別色相會讓整張圖只剩顏色在吵。型別靠字，不靠色。
    /// </summary>
    public static Color SlotChipBody => T.slotChipBody;

    /// <summary>參數列型別 chip 的字：比標籤小一階、置中，讓它讀起來是標記而不是另一段文字。</summary>
    public static GUIStyle SlotChip => slotChip ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(2, 2, 0, 0),
    }, T.slotChipText);

    /// <summary>Header 右側的結果型別標籤。</summary>
    public static GUIStyle NodeChip => nodeChip ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(4, 4, 0, 0),
    }, new Color(HeaderInk.r, HeaderInk.g, HeaderInk.b, 0.80f));

    /// <summary>
    /// 接點上的收合符號 `+`／`-`：字壓在接點的實心圓上，所以另設字色，不沿用 Header 的圖示色。
    /// </summary>
    public static GUIStyle InputPortGlyph => inputPortGlyph ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 11,
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(0, 0, 0, 0),
    }, T.portGlyph);

    /// <summary>Header 上的小圖示（換來源 ▾、註解 ✎）：無背景。</summary>
    public static GUIStyle HeaderButton => headerButton ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(0, 0, 0, 0),
    }, HeaderInk);

    /// <summary>同上但半透明：表示「這個開關目前是關的」。</summary>
    public static GUIStyle HeaderButtonDim => headerButtonDim ??= Ink(new GUIStyle(HeaderButton), new Color(HeaderInk.r, HeaderInk.g, HeaderInk.b, 0.45f));

    public static GUIStyle PanelHeader => panelHeader ??= Ink(new GUIStyle(EditorStyles.boldLabel)
    {
        padding = new RectOffset(6, 6, 2, 2),
    }, Text);

    public static GUIStyle ConsoleRow => consoleRow ??= Ink(new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(6, 4, 1, 1),
    }, Text);

    public static GUIStyle Tiny => tiny ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
    }, Muted);

    /// <summary>清單元素的序號欄：右對齊才能對成一直排，掃視時才看得出順序。</summary>
    public static GUIStyle ListIndex => listIndex ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleRight,
        padding = new RectOffset(0, 2, 0, 0),
    }, T.listIndex);

    public static GUIStyle ListAdd => listAdd ??= Ink(new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleCenter,
    }, Muted);

    // 節點同寬後，過長的文字沒有把節點撐開的機會，必須自己截字；截掉的部分靠 tooltip 補回來。
    private static readonly Dictionary<string, string> elideCache = new();

    /// <summary>把 text 截到 width 以內並補上省略號；有截字時 tooltip 顯示完整內容。</summary>
    public static GUIContent Elide(string text, GUIStyle style, float width, string tooltip = null)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f) return new GUIContent(text, tooltip);

        var content = new GUIContent(text);
        if (style.CalcSize(content).x <= width) return new GUIContent(text, tooltip);

        string key = text + "" + style.name + "" + Mathf.RoundToInt(width);
        if (elideCache.TryGetValue(key, out var cached))
            return new GUIContent(cached, string.IsNullOrEmpty(tooltip) ? text : text + "\n" + tooltip);

        // 二分找最長可容納的前綴，避免逐字量測。
        int low = 0, high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            content.text = text.Substring(0, mid) + "…";
            if (style.CalcSize(content).x <= width) low = mid;
            else high = mid - 1;
        }

        string elided = low <= 0 ? "…" : text.Substring(0, low) + "…";
        if (elideCache.Count > 512) elideCache.Clear();
        elideCache[key] = elided;
        return new GUIContent(elided, string.IsNullOrEmpty(tooltip) ? text : text + "\n" + tooltip);
    }

    public static void Fill(Rect r, Color c) => EditorGUI.DrawRect(r, c);

    /// <summary>
    /// 清單格底：和節點 Header 同一套語彙——身分色 + 「容器→內容」漸層，只是沖淡。
    /// payload 傳同一個顏色就是單色（動作沒有容器語意）。
    /// </summary>
    public static void CellBackground(Rect row, Color kind, Color payload, bool altRow, bool focused,
        float radius = 3f)
    {
        GradientFill(row, CellTint(kind, altRow, focused), CellTint(payload, altRow, focused), radius);
        RoundedFrame(row, focused ? Link : LibraryCellBorder, radius);
    }

    public static void RoundedFill(Rect r, Color c, float radius)
    {
        GUI.DrawTexture(r, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
    }

    public static void RoundedTopFill(Rect r, Color c, float radius)
    {
        RoundedFill(r, c, radius);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - radius, r.width, radius), c);
    }

    /// <summary>
    /// 貼在節點頂緣的狀態色條。厚度通常比圓角還薄，<see cref="RoundedTopFill"/> 的補方角會反過來
    /// 畫到節點外面，所以這裡逐列依圓的方程式內縮，左右上角剛好貼合節點輪廓。
    /// </summary>
    // 不開 GUI.BeginClip：畫布本身帶縮放矩陣，巢狀 clip 會被矩陣一起變換；色條只有幾列，直接算還比較便宜。
    public static void TopStripeFill(Rect r, Color c, float radius)
    {
        if (r.height <= 0f || r.width <= 0f) return;
        if (radius <= 0f) { Fill(r, c); return; }

        for (float y = 0f; y < r.height; y += 1f)
        {
            float rowHeight = Mathf.Min(1f, r.height - y);
            float dy = radius - (y + rowHeight * 0.5f);      // 這一列的中線離圓心多遠
            float inset = dy <= 0f ? 0f : radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float width = r.width - inset * 2f;
            if (width <= 0f) continue;
            Fill(new Rect(r.x + inset, r.y + y, width, rowHeight), c);
        }
    }

    // IMGUI 沒有漸層繪製，只能貼圖：一組顏色做一張 64x1 的水平漸層，之後重複使用。
    private static readonly Dictionary<(Color, Color), Texture2D> gradientCache = new();

    /// <summary>漸層前段維持原色的比例，過了才開始過渡（0.6＝60% 之後才漸層）。</summary>
    private const float GradientHold = 0.6f;

    private static Texture2D GradientTexture(Color from, Color to)
    {
        if (gradientCache.TryGetValue((from, to), out var cached) && cached != null) return cached;

        const int width = 64;
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        for (int x = 0; x < width; x++)
        {
            float t = x / (float)(width - 1);
            // 前段維持容器色不動，只有尾段才過渡到內容色：身分要一眼認得，漸層只是補充資訊。
            float ramp = t <= GradientHold ? 0f : (t - GradientHold) / (1f - GradientHold);
            tex.SetPixel(x, 0, Color.Lerp(from, to, ramp));
        }
        tex.Apply();
        gradientCache[(from, to)] = tex;
        return tex;
    }

    /// <summary>四角圓角的水平漸層。from == to 時退回單色，呼叫端不必自己判斷。</summary>
    public static void GradientFill(Rect r, Color from, Color to, float radius)
    {
        if (from == to) { RoundedFill(r, from, radius); return; }
        GUI.DrawTexture(r, GradientTexture(from, to), ScaleMode.StretchToFill, true, 0f, Color.white, 0f, radius);
    }

    /// <summary>Header 底：單色或左右漸層，只有上緣圓角。</summary>
    public static void HeaderFill(Rect r, Color from, Color to, float radius)
    {
        if (from == to) { RoundedTopFill(r, from, radius); return; }

        GradientFill(r, from, to, radius);
        // 下緣要方角：漸層是水平的，同一張貼圖再鋪一次底部條帶就能對齊。
        GUI.DrawTexture(new Rect(r.x, r.yMax - radius, r.width, radius), GradientTexture(from, to),
            ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
    }

    public static void RoundedFrame(Rect r, Color c, float radius, float thickness = 1f)
    {
        GUI.DrawTexture(r, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, thickness, radius);
    }

    /// <summary>畫外框（四條線，避免額外貼圖）。</summary>
    public static void Frame(Rect r, Color c, float thickness = 1f)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
    }

    /// <summary>
    /// 接點：外環永遠在（一眼認得出是接點），中心實心點表示已接線——沒接 ○、有接 ◎。
    /// 顏色只講用途（取值／寫入／錯誤），接不接交給中心點，兩件事不共用同一個通道。
    /// </summary>
    public static void DrawInputPort(Rect r, Color c, bool connected) => DrawPort(r, c, connected);

    public static void DrawOutputPort(Rect r, Color c, bool connected) => DrawPort(r, c, connected);

    /// <summary>中心點佔接點直徑的比例。要容得下收合用的 -／+，又要在 0.45 倍縮放下還看得到。</summary>
    private const float PortCoreRatio = 0.6f;

    private static void DrawPort(Rect r, Color c, bool connected)
    {
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        RoundedFill(r, PortHole, radius);
        RoundedFrame(r, c, radius, 2f);
        if (!connected) return;

        float core = radius * 2f * PortCoreRatio;
        var coreRect = new Rect(r.center.x - core * 0.5f, r.center.y - core * 0.5f, core, core);
        RoundedFill(coreRect, c, core * 0.5f);
    }
}

}
