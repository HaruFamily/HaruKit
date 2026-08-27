namespace HaruFamily.Framework.ActionSystem.Editor
{
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>編輯器共用的顏色與 GUIStyle。GUIStyle 只能在 OnGUI 期間建立，全部走 lazy。</summary>
public static class AGStyles
{
    // 配色原則：**灰是結構，色只留給語意**。畫布、面板、節點本體、線全部無彩，
    // 只有「節點身分」（五種 Header）與「狀態」（選取、錯誤、警告）帶色相，色彩因此永遠等於資訊。
    public static readonly Color Canvas = new(0.145f, 0.145f, 0.145f);
    public static readonly Color Grid = new(0.19f, 0.19f, 0.19f);
    public static readonly Color GridBold = new(0.24f, 0.24f, 0.24f);
    public static readonly Color Toolbar = new(0.18f, 0.18f, 0.18f);
    public static readonly Color Panel = new(0.17f, 0.17f, 0.17f);
    public static readonly Color PanelSection = new(0.21f, 0.21f, 0.21f);
    public static readonly Color PanelList = new(0.13f, 0.13f, 0.13f);
    public static readonly Color Console = new(0.15f, 0.15f, 0.15f);

    public static readonly Color NodeBody = new(0.28f, 0.28f, 0.28f);
    public static readonly Color NodeBorder = new(0.11f, 0.11f, 0.11f);
    public static readonly Color NodeBorderSelected = new(1f, 0.80f, 0.38f);   // 選取＝狀態，用暖金；全灰畫面裡一眼可見

    /// <summary>HEAD 專用外框：純白灰，靠明度而不是色相和選取分開。</summary>
    public static readonly Color HeadBorder = new(0.93f, 0.93f, 0.93f);

    /// <summary>
    /// HEAD 專用 Header 底色。深紫紅代表流程入口，和 Action 的洋紅以明度與色相分開：前者是從哪裡開始，後者是做什麼。
    /// 保留白外框與光暈，在任何縮放下都認得出起點。
    /// </summary>
    public static readonly Color HeaderHead = new(0.447f, 0.227f, 0.408f);       // 深紫紅 #723A68

    /// <summary>停用節點蓋在最上層的暗紗：停用是狀態不是身分，所以壓明度、不換色相。</summary>
    public static readonly Color DisabledVeil = new(0.08f, 0.08f, 0.08f, 0.55f);

    /// <summary>接到停用節點的連線：同樣只壓明度，維持「灰是結構」的規則。</summary>
    public static readonly Color LinkDisabled = new(1f, 1f, 1f, 0.22f);
    public static readonly Color NodeNote = new(0.26f, 0.26f, 0.26f);
    public static readonly Color NodeNoteBorder = new(0.62f, 0.62f, 0.62f);

    // Header 是唯一帶色相的節點元素。暖色是會執行或求值的動態邏輯，冷色是可重用的靜態引用。
    // 五種身分分開色相與明度，縮小或色弱時仍可辨識。
    public static readonly Color HeaderAction = new(0.722f, 0.231f, 0.451f);  // 洋紅 #B83B73
    public static readonly Color HeaderFormula = new(0.750f, 0.520f, 0.200f); // 琥珀 #BF8533
    public static readonly Color HeaderAsset = new(0.270f, 0.450f, 0.770f);   // 靛藍 #4573C4
    public static readonly Color HeaderToken = new(0.160f, 0.420f, 0.310f);   // 深綠 #296B4F

    /// <summary>Header 是深色，上面的字與小圖示一律近白。</summary>
    public static readonly Color HeaderInk = new(0.97f, 0.93f, 0.95f);

    /// <summary>Header 上的疊層底色（chip、名稱區）。</summary>
    public static readonly Color HeaderOverlay = new(1f, 1f, 1f, 0.14f);

    // 線與接點用明度分層，不用色相：空槽是暗灰、接了東西是亮白、變數保留一點紫相當作唯一例外。
    public static readonly Color Link = new(0.80f, 0.80f, 0.82f);
    public static readonly Color PortEmpty = new(0.42f, 0.42f, 0.43f);
    public static readonly Color PortLive = new(0.80f, 0.80f, 0.82f);

    public static readonly Color Muted = new(0.74f, 0.74f, 0.75f);
    public static readonly Color RowAlt = new(1f, 1f, 1f, 0.04f);
    public static readonly Color LibraryCellBorder = new(0.11f, 0.11f, 0.11f);

    // 清單是「一段」而不是「一堆長得一樣的列」：底帶、斑馬紋與縱線都是結構訊息，所以只用明度不用色相。
    // 底帶壓暗而不是提亮：節點本體已經是中灰，往下沉才分得出「這一段是凹進去的清單」。
    public static readonly Color ListBand = new(0f, 0f, 0f, 0.24f);
    // 斑馬紋做成雙向（一亮一暗）而不是單向疊一層淡白：Slot 元素右半被 AGValueField 的欄位框蓋住，
    // 只剩左半在比對，單向 5% 的差異等於看不見。
    public static readonly Color ListStripeEven = new(1f, 1f, 1f, 0.07f);
    public static readonly Color ListStripeOdd = new(0f, 0f, 0f, 0.12f);
    public static readonly Color ListRule = new(1f, 1f, 1f, 0.13f);   // 新增列的外框
    public static readonly Color ListRowHover = new(1f, 1f, 1f, 0.07f);
    public static readonly Color ListRowDragging = new(1f, 1f, 1f, 0.13f);

    /// <summary>
    /// 左右欄清單格的底色：用節點 Header 的身分色沖淡，讓「清單上的一列」和「畫布上的那顆節點」是同一個顏色語彙。
    /// 交錯列只差一階濃度；聚焦中的那一列直接給滿色。
    /// </summary>
    public static Color CellTint(Color kind, bool altRow, bool focused)
        => focused ? kind : Color.Lerp(PanelList, kind, altRow ? 0.52f : 0.38f);

    // 語意色：錯誤永遠是紅、警告永遠是琥珀，不參與配色調整。
    public static readonly Color PortError = new(1f, 0.42f, 0.42f);
    public static readonly Color Error = new(1f, 0.42f, 0.42f);
    public static readonly Color Warning = new(1f, 0.78f, 0.34f);

    private static GUIStyle nodeTitle, nodeDesc, focusTitle, rowLabel, rowLabelError, chip, nodeChip, slotChip, portGlyph, headerButton, headerButtonDim, overlayTitle, panelHeader, consoleRow, tiny, listIndex, listAdd;

    public static GUIStyle NodeTitle => nodeTitle ??= new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 12,
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 6, 0, 0),
        normal = { textColor = HeaderInk },
    };

    public static GUIStyle NodeDesc => nodeDesc ??= new GUIStyle(EditorStyles.miniLabel)
    {
        padding = new RectOffset(6, 6, 0, 0),
        normal = { textColor = Muted },
        wordWrap = true,
    };

    public static GUIStyle FocusTitle => focusTitle ??= new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 15,
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(4, 4, 0, 0),
    };

    public static GUIStyle RowLabel => rowLabel ??= new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(4, 2, 0, 0),
    };

    public static GUIStyle RowLabelError => rowLabelError ??= new GUIStyle(RowLabel)
    {
        normal = { textColor = Error },
    };

    public static GUIStyle Chip => chip ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleLeft,
        padding = new RectOffset(6, 4, 0, 0),
        normal = { textColor = new Color(0.80f, 0.68f, 1f) },
    };

    /// <summary>畫布左上角說明面板的標題：底是深色，字要白，不能沿用 Header 的深色字。</summary>
    public static GUIStyle OverlayTitle => overlayTitle ??= new GUIStyle(NodeTitle)
    {
        normal = { textColor = Color.white },
    };

    /// <summary>
    /// 參數列最前面的型別 chip 底色。**中性色，不用色相**：洋紅／琥珀／藍／綠已經被「來源種類」用掉，
    /// 再開一套型別色相會讓整張圖只剩顏色在吵。型別靠字，不靠色。
    /// </summary>
    public static readonly Color SlotChipBody = new(1f, 1f, 1f, 0.10f);

    /// <summary>參數列型別 chip 的字：比標籤小一階、置中，讓它讀起來是標記而不是另一段文字。</summary>
    public static GUIStyle SlotChip => slotChip ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(2, 2, 0, 0),
        normal = { textColor = new Color(0.78f, 0.78f, 0.80f) },
    };

    /// <summary>Header 右側的結果型別標籤。</summary>
    public static GUIStyle NodeChip => nodeChip ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(4, 4, 0, 0),
        normal = { textColor = new Color(HeaderInk.r, HeaderInk.g, HeaderInk.b, 0.80f) },
    };

    /// <summary>
    /// 接點上的收合符號 `+`／`-`：字要壓在亮色的圓上，所以用深色而不是沿用 Header 的淺色圖示。
    /// </summary>
    public static GUIStyle PortGlyph => portGlyph ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 11,
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(0, 0, 0, 0),
        normal = { textColor = new Color(0.10f, 0.10f, 0.11f) },
    };

    /// <summary>Header 上的小圖示（換來源 ▾、註解 ✎）：無背景。</summary>
    public static GUIStyle HeaderButton => headerButton ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        padding = new RectOffset(0, 0, 0, 0),
        normal = { textColor = HeaderInk },
    };

    /// <summary>同上但半透明：表示「這個開關目前是關的」。</summary>
    public static GUIStyle HeaderButtonDim => headerButtonDim ??= new GUIStyle(HeaderButton)
    {
        normal = { textColor = new Color(HeaderInk.r, HeaderInk.g, HeaderInk.b, 0.45f) },
    };

    public static GUIStyle PanelHeader => panelHeader ??= new GUIStyle(EditorStyles.boldLabel)
    {
        padding = new RectOffset(6, 6, 2, 2),
    };

    public static GUIStyle ConsoleRow => consoleRow ??= new GUIStyle(EditorStyles.label)
    {
        fontSize = 11,
        padding = new RectOffset(6, 4, 1, 1),
    };

    public static GUIStyle Tiny => tiny ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        normal = { textColor = Muted },
    };

    /// <summary>清單元素的序號欄：右對齊才能對成一直排，掃視時才看得出順序。</summary>
    public static GUIStyle ListIndex => listIndex ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleRight,
        padding = new RectOffset(0, 2, 0, 0),
        normal = { textColor = new Color(0.62f, 0.62f, 0.63f) },
    };

    public static GUIStyle ListAdd => listAdd ??= new GUIStyle(EditorStyles.miniLabel)
    {
        fontSize = 10,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = Muted },
    };

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

    /// <summary>接點：以圓形區分資料流端點，外框保持在深色畫布上的辨識度。</summary>
    public static void Port(Rect r, Color c)
    {
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        RoundedFill(r, c, radius);
        RoundedFrame(r, new Color(0f, 0f, 0f, 0.6f), radius);
    }
}

}
