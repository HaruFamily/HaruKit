namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

/// <summary>
/// 視覺化編輯器唯一的反射入口：解析 Slot 內部欄位、節點的參數欄位、以及型別／欄位的顯示名稱。
/// </summary>
// Core 的 Slot 欄位是 private 且散在泛型 base，Editor 又是獨立 assembly；統一用反射比逐型別開 internal API 好維護。
public static class HGReflect
{
    private const BindingFlags Flags = BindingFlags.Instance
                                     | BindingFlags.Public
                                     | BindingFlags.NonPublic
                                     | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Type, List<FieldInfo>> fieldCache = new();
    private static readonly Dictionary<Type, string> nameCache = new();
    private static readonly Dictionary<Type, int> widthCache = new();
    private static readonly Dictionary<FieldInfo, (int Units, float Ratio)> labelWidthCache = new();
    private static readonly HashSet<string> showConditionErrors = new();

    // ===== 欄位走訪 =====

    /// <summary>沿繼承鏈往上收齊所有 instance 欄位（衍生型別的 GetFields 抓不到 base 的 private 欄位）。</summary>
    public static List<FieldInfo> Fields(Type type)
    {
        if (type == null) return new List<FieldInfo>();
        if (fieldCache.TryGetValue(type, out var cached)) return cached;

        var list = new List<FieldInfo>();
        var chain = new List<Type>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType) chain.Add(t);
        // base 在前、衍生在後：參數列顯示順序才符合「共通欄位在上」的直覺。
        for (int i = chain.Count - 1; i >= 0; i--)
            foreach (var f in chain[i].GetFields(Flags))
                list.Add(f);

        fieldCache[type] = list;
        return list;
    }

    public static FieldInfo Find(Type type, string name)
    {
        foreach (var f in Fields(type))
            if (f.Name == name) return f;
        return null;
    }

    public static object Get(object target, string fieldName)
    {
        if (target == null) return null;
        var f = Find(target.GetType(), fieldName);
        return f?.GetValue(target);
    }

    public static void Set(object target, string fieldName, object value)
    {
        if (target == null) return;
        var f = Find(target.GetType(), fieldName);
        if (f == null) return;
        f.SetValue(target, value);
    }

    // ===== Slot 判定 =====

    public static bool IsFormulaSlot(object o) => o is FormulaSlotBase;

    public static bool IsFormulaSlotType(Type t) => t != null && typeof(FormulaSlotBase).IsAssignableFrom(t);

    public static bool IsActionSlotType(Type t) => t != null && typeof(ActionSlotBase).IsAssignableFrom(t);

    public static bool IsSlotType(Type t) => t != null && typeof(GraphSlotBase).IsAssignableFrom(t);

    // Slot 的型別關係（結果／公式／資產／pack）住在 Slot 自己身上，但呼叫端手上多半只有 Type。
    // 所以建一顆該型別的實例去問：答案對同一個型別是常數，建完就快取。Slot 都是無參數的純資料類別，
    // 建立沒有副作用。走這條而不是反射泛型參數，編輯器才不必認得 FormulaSlot<,,,> / ActionSlot<>。
    private static readonly Dictionary<Type, object> slotProbes = new();

    private static object SlotProbe(Type slotType)
    {
        if (slotType == null || slotType.IsAbstract) return null;
        if (slotProbes.TryGetValue(slotType, out var cached)) return cached;

        var probe = CreateInstance(slotType);
        slotProbes[slotType] = probe;
        return probe;
    }

    private static FormulaSlotBase FormulaProbe(Type slotType) => SlotProbe(slotType) as FormulaSlotBase;

    private static ActionSlotBase ActionProbe(Type slotType) => SlotProbe(slotType) as ActionSlotBase;

    /// <summary>Slot 的結果型別（int / float / bool / string / EntityView…）。Action Slot 回 null。</summary>
    public static Type ResultType(Type slotType) => FormulaProbe(slotType)?.ResultType;

    /// <summary>Slot 可接的 Formula base 型別（例如 IntFormula）。</summary>
    public static Type FormulaBaseType(Type slotType) => FormulaProbe(slotType)?.BodyBaseType;

    /// <summary>FormulaSlot 的 TPack；不是 FormulaSlot 回 null。列舉公式族時用它排除別的 pack。</summary>
    public static Type FormulaSlotPack(Type slotType) => FormulaProbe(slotType)?.PackType;

    /// <summary>Slot 宣告的候選 pack 收窄條件；沒宣告或不是 FormulaSlot 回 null。</summary>
    public static Type CandidatePackType(Type slotType) => FormulaProbe(slotType)?.CandidatePackType;

    /// <summary>Slot 可接的 Formula Asset 型別（例如 IntAsset）。</summary>
    public static Type AssetType(Type slotType) => FormulaProbe(slotType)?.AssetBaseType;

    /// <summary>這個資產可以接進哪一種欄位。挑第一個型別相容的族，沒有就回 null（＝這張圖用不到它）。</summary>
    public static Type SlotTypeForAsset(UnityEngine.ScriptableObject asset,
        System.Collections.Generic.List<(Type acceptedAssetType, Type slotType)> slotTypes)
    {
        if (asset == null || slotTypes == null) return null;
        foreach (var candidate in slotTypes)
            if (candidate.acceptedAssetType.IsInstanceOfType(asset)) return candidate.slotType;
        return null;
    }

    /// <summary>動作欄位可接的 Action base 型別。</summary>
    public static Type ActionBaseType(Type actionSlotType) => ActionProbe(actionSlotType)?.BodyBaseType;

    /// <summary>動作欄位可接的 Action Asset 型別。</summary>
    public static Type ActionAssetType(Type actionSlotType) => ActionProbe(actionSlotType)?.AssetBaseType;

    /// <summary>公式資產的結果型別；動作資產沒有結果型別，回 null。</summary>
    public static Type AssetResultType(UnityEngine.Object asset) => (asset as IGraphAsset)?.ResultType;

    // ===== Slot 的節點存取 =====
    // Slot 只有「有沒有接節點」一種狀態；來源種類、內容與座標全在 GraphNode 上。
    // 兩種 Slot 各有非泛型基底（FormulaSlotBase / ActionSlotBase），所以這裡一律走型別，不走成員名。

    /// <summary>Slot 目前接的節點；null 代表常數（公式）、空槽（動作）或未接（目錄）。</summary>
    public static GraphNode GetNode(object slot) => (slot as GraphSlotBase)?.Node;

    public static void SetNode(object slot, GraphNode node) => (slot as GraphSlotBase)?.SetNode(node);

    /// <summary>沒接節點時就地建立一個空節點（＝使用者從接點拉線出來的編輯中狀態）。</summary>
    public static GraphNode EnsureNode(object slot)
    {
        var node = GetNode(slot);
        if (node != null) return node;

        node = new GraphNode();
        node.EnsureId();
        SetNode(slot, node);
        return node;
    }

    /// <summary>相容既有呼叫端的模式碼：0 常數／空槽、1 公式或動作（含編輯中空節點）、2 資產、3 具名Token。</summary>
    public static int UseType(object slot)
    {
        var node = GetNode(slot);
        if (node == null) return 0;
        return node.Kind switch
        {
            NodeKind.Asset => 2,
            NodeKind.Token => 3,
            NodeKind.Catalog => 5,
            _ => 1,   // Inline 與 Empty 都畫成來源節點，Empty 由驗證擋存檔
        };
    }

    public static object GetFormula(object slot)
    {
        var node = GetNode(slot);
        return node != null && node.Kind == NodeKind.Inline ? node.BodyObject : null;
    }

    public static UnityEngine.Object GetAsset(object slot)
    {
        var node = GetNode(slot);
        return node != null && node.Kind == NodeKind.Asset ? node.AssetObject : null;
    }

    public static void SetAsset(object slot, UnityEngine.Object asset)
        => EnsureNode(slot).SetAsset(asset as UnityEngine.ScriptableObject);

    /// <summary>這個欄位接的具名Token（沒接或不是Token節點回 null）。</summary>
    public static GraphToken GetToken(object slot) => GetNode(slot)?.Token;

    /// <summary>換成具名Token引用：節點 Id、座標、備註與連入邊全部保留，只換內容。</summary>
    public static void SetToken(object slot, GraphToken endpoint)
    {
        if (endpoint == null)
        {
            GetNode(slot)?.Clear();
            return;
        }
        EnsureNode(slot).SetToken(endpoint);
    }

    /// <summary>斷開來源：公式欄位回常數、動作欄位回空槽。</summary>
    public static void ClearNode(object slot) => SetNode(slot, null);

    /// <summary>這個欄位能不能接這個內嵌內容 / 資產。跨 pack 或跨結果型別在這裡擋下。</summary>
    public static bool AcceptsBody(object slot, object body)
        => body is GraphNodeContent node && (slot as GraphSlotBase)?.AcceptsBody(node) == true;

    public static bool AcceptsAsset(object slot, UnityEngine.Object asset)
        => (slot as GraphSlotBase)?.AcceptsAsset(asset as UnityEngine.ScriptableObject) == true;

    /// <summary>這個欄位能不能接這個具名Token。動作與目錄欄位一律不能。</summary>
    public static bool AcceptsToken(object slot, GraphToken endpoint)
        => endpoint != null && (slot as GraphSlotBase)?.AcceptsToken(endpoint) == true;

    public static object GetDefault(object slot) => (slot as FormulaSlotBase)?.DefaultObject;

    /// <summary>常數框該畫哪個型別。欄位沒特別宣告就是結果型別；fallback 給非 FormulaSlot 的呼叫端。</summary>
    public static Type DefaultEditType(object slot, Type fallback)
        => (slot as FormulaSlotBase)?.DefaultEditType ?? fallback;

    public static void SetDefault(object slot, object value)
    {
        if (slot is FormulaSlotBase fsb) fsb.DefaultObject = value;
    }

    /// <summary>
    /// 動作欄位自己的停用旗標（`ActionSlot._disabled`）。舊資產仍可能有這個值，執行期照樣擋，
    /// 但編輯器不再提供入口——要關掉一段行為改成停用它接的節點（`GraphNode.Disabled`），
    /// 那是共用單位，語意也更一致。因此這裡只有 Get。
    /// </summary>
    public static bool GetDisabled(object actionSlot) => actionSlot is ActionSlotBase asb && asb.Disabled;

    public static string GetLabel(object actionSlot) => (actionSlot as ActionSlotBase)?.Label;

    public static void SetLabel(object actionSlot, string value)
    {
        if (actionSlot is ActionSlotBase asb) asb.Label = value;
    }

    /// <summary>頭端目前的識別碼；還沒指派時為空字串。</summary>
    public static string SlotEditorId(object head) => (head as ActionSlotBase)?.Id;

    /// <summary>動作頭端的穩定識別碼（焦點 act:{id} 與 HEAD 節點座標都用它）。</summary>
    public static string EnsureSlotEditorId(object slot) => (slot as ActionSlotBase)?.EnsureId() ?? "?";

    /// <summary>複製頭端後換新識別碼，否則兩個頭端共用同一筆座標與焦點。</summary>
    public static void ResetSlotEditorId(object slot) => (slot as ActionSlotBase)?.ResetId();

    /// <summary>頭端座標。動作頭端、時機群組、Token端點與兩種資產都是頭端。</summary>
    public static bool GetHeadPos(object head, out UnityEngine.Vector2 pos)
    {
        pos = default;
        if (head is not IGraphHead h || !h.HasPos) return false;
        pos = h.Pos;
        return true;
    }

    public static void SetHeadPos(object head, UnityEngine.Vector2 pos)
    {
        if (head is IGraphHead h) h.Pos = pos;
    }

    public static void ClearHeadPos(object head) => (head as IGraphHead)?.ClearPos();

    /// <summary>畫布主人的候選節點池（LogicGraph、資產各一份；動作頭端上的那份只為讀回舊資料）。</summary>
    public static List<GraphNode> Orphans(object head) => (head as IOrphanPool)?.Orphans;

    /// <summary>資產根內容的載體。舊格式（只存裸內容）由資產自己就地補上載體，這裡一律拿得到 GraphNode。</summary>
    public static GraphNode AssetRoot(object asset) => (asset as IGraphAsset)?.Root;

    /// <summary>圖主人的具名Token清單（LogicGraph、公式／動作資產各一份）。</summary>
    public static List<GraphToken> Tokens(object owner) => (owner as ITokenOwner)?.Tokens;

    // ===== 清單欄位 =====

    /// <summary>欄位是否為 List&lt;T&gt; 或 T[]；是的話回傳元素型別。陣列是固定長度，呼叫端要擋增刪。</summary>
    public static bool IsList(Type t, out Type elementType)
    {
        elementType = null;
        if (t == null) return false;

        if (t.IsArray)
        {
            elementType = t.GetElementType();
            return elementType != null;
        }
        if (!t.IsGenericType) return false;
        if (t.GetGenericTypeDefinition() != typeof(List<>)) return false;
        elementType = t.GetGenericArguments()[0];
        return true;
    }

    /// <summary>取欄位上的清單；為 null 時就地建立一份寫回，讓編輯器可以直接新增項目。</summary>
    public static IList EnsureList(object owner, FieldInfo field)
    {
        var list = field.GetValue(owner) as IList;
        if (list != null) return list;

        // 陣列沒有無參數建構式，得用 Array.CreateInstance。
        list = field.FieldType.IsArray
            ? Array.CreateInstance(field.FieldType.GetElementType() ?? typeof(object), 0)
            : Activator.CreateInstance(field.FieldType) as IList;

        field.SetValue(owner, list);
        return list;
    }

    /// <summary>建立無參數建構的實例；失敗回 null（不丟例外，讓呼叫端記 Log 後續跑）。</summary>
    public static object CreateInstance(Type t)
    {
        if (t == null || t.IsAbstract) return null;
        try { return Activator.CreateInstance(t); }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[GraphKit] 建立 {t.Name} 失敗：{e.Message}");
            return null;
        }
    }

    /// <summary>判斷具體節點是不是動作。</summary>
    public static bool IsActionNodeType(Type type) => Closed(type, typeof(ActionNodeBase<>)) != null;

    /// <summary>從具體 Action／Formula 型別回推可供型別選單使用的封閉泛型 base。</summary>
    public static Type NodeBaseType(Type type)
        => Closed(type, typeof(ActionNodeBase<>)) ?? Closed(type, typeof(FormulaNodeBase<,>));

    /// <summary>從具體 Formula 型別取得結果型別；Action 回 null。</summary>
    public static Type FormulaResultType(Type type) => Closed(type, typeof(FormulaNodeBase<,>))?.GetGenericArguments()[0];

    /// <summary>從具體 Formula 型別取得 pack 型別；Action 回 null。</summary>
    // 與 FormulaSlotPack 不同：那個問的是 Slot，這個問的是公式本體。
    // 候選以 pack 收窄時比的是這一個——欄位不一定有 Slot（候選池裡的節點就沒有）。
    public static Type FormulaPackType(Type type) => Closed(type, typeof(FormulaNodeBase<,>))?.GetGenericArguments()[1];

    // 沿繼承鏈找出指定泛型定義的封閉型別；找不到回 null。
    private static Type Closed(Type type, Type definition)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == definition) return current;
        return null;
    }

    // ===== 顯示名稱 =====

    // 名稱與分類只認 LogicGraph 自有屬性，避免 Graph 操作體驗受外部 Inspector 插件影響。

    private static HGNodeAttribute NodeAttr(Type t)
        => t?.GetCustomAttribute<HGNodeAttribute>(false);

    /// <summary>節點顯示名。</summary>
    public static string TypeName(Type t)
    {
        if (t == null) return "（空）";
        if (nameCache.TryGetValue(t, out var cached)) return cached;

        string name = NodeAttr(t)?.Name;
        if (string.IsNullOrEmpty(name)) name = Prettify(t.Name);

        nameCache[t] = name;
        return name;
    }

    /// <summary>節點分類（建立選單的資料夾）。</summary>
    public static string TypeCategory(Type t)
    {
        string cat = NodeAttr(t)?.Group;
        return string.IsNullOrEmpty(cat) ? "其他" : cat;
    }

    /// <summary>
    /// 欄位指定的標籤欄寬度：`Units`＝`[HGLabel(Width = n)]` 的格數，`Ratio`＝`[HGLabel(WidthRatio = n)]` 的 0～1 比例。
    /// 都沒標回 (0, 0)＝走預設比例。換算成 px 在 `HGGraph.LabelWidthOf`。
    /// </summary>
    // 每列每次重繪都會問一次，反射結果進快取。FieldInfo 是 Type 快取出來的同一個實例，可以當 key。
    public static (int Units, float Ratio) LabelWidth(FieldInfo f)
    {
        if (f == null) return (0, 0f);
        if (labelWidthCache.TryGetValue(f, out var cached)) return cached;

        var attr = f.GetCustomAttribute<HGLabelAttribute>(false);
        var value = (Units: attr?.Width ?? 0, Ratio: attr?.WidthRatio ?? 0f);

        labelWidthCache[f] = value;
        return value;
    }

    /// <summary>
    /// 型別指定的節點寬度，單位＝格線格數（`[HGNodeView(Width = n)]`）。沒標回 0＝用預設寬。
    /// 夾範圍與換算成 px 在 `HGGraph.MeasureNode`，這裡只回原始宣告。
    /// </summary>
    // MeasureNode 每次重建整張圖都會逐節點問一次，反射結果一律進快取。
    public static int NodeWidthUnits(Type t)
    {
        if (t == null) return 0;
        if (widthCache.TryGetValue(t, out int cached)) return cached;

        int units = NodeAttr(t)?.Width ?? 0;
        widthCache[t] = units;
        return units;
    }

    /// <summary>節點說明；未標說明時不建立描述列。</summary>
    public static string TypeDescription(Type t)
    {
        if (t == null) return "尚未指定內容";

        return NodeAttr(t)?.Description ?? "";
    }

    /// <summary>參數欄位顯示名。</summary>
    public static string FieldLabel(FieldInfo f)
    {
        if (f == null) return "?";

        string label = f.GetCustomAttribute<HGLabelAttribute>(false)?.Name;
        if (!string.IsNullOrEmpty(label)) return label;

        return Prettify(f.Name);
    }

    /// <summary>參數欄位說明（滑鼠停留顯示）；沒寫回空字串。</summary>
    public static string FieldDescription(FieldInfo f)
    {
        if (f == null) return "";
        return f.GetCustomAttribute<HGDescriptionAttribute>(false)?.Text ?? "";
    }

    /// <summary>
    /// 這個欄位要不要畫出來。`[HGHide]` 是明講的；`[HideInInspector]` 也算——節點圖就是 Inspector 的替代品，
    /// 而 Core 用它標的都是編輯期內部欄位（座標、識別碼、候選池），出現在節點上只是雜訊。
    /// </summary>
    public static bool IsHidden(FieldInfo f)
        => f != null && (f.IsDefined(typeof(HGHideAttribute), false)
                      || f.IsDefined(typeof(UnityEngine.HideInInspector), false));

    /// <summary>取得 [HGShowIf] 的條件；設定錯誤時保持顯示，避免欄位被靜默隱藏。</summary>
    public static bool IsShown(object target, FieldInfo field)
    {
        var attr = field?.GetCustomAttribute<HGShowIfAttribute>(false);
        if (attr == null) return true;

        if (string.IsNullOrWhiteSpace(attr.ConditionName))
        {
            LogShowConditionError(target, field, "條件名稱不可為空。");
            return true;
        }

        var conditionField = Find(target?.GetType(), attr.ConditionName);
        if (conditionField?.FieldType == typeof(bool))
        {
            try { return (bool)conditionField.GetValue(target); }
            catch (Exception e)
            {
                LogShowConditionError(target, field, $"讀取 bool 欄位失敗：{e.Message}");
                return true;
            }
        }

        var conditionProperty = FindProperty(target?.GetType(), attr.ConditionName);
        var getter = conditionProperty?.GetGetMethod(true);
        if (conditionProperty?.PropertyType == typeof(bool)
            && conditionProperty.GetIndexParameters().Length == 0
            && getter != null)
        {
            try { return (bool)getter.Invoke(target, null); }
            catch (Exception e)
            {
                LogShowConditionError(target, field, $"讀取 bool 屬性失敗：{e.Message}");
                return true;
            }
        }

        LogShowConditionError(target, field, $"找不到 bool 欄位或無參數 bool 屬性「{attr.ConditionName}」。");
        return true;
    }

    private static PropertyInfo FindProperty(Type type, string name)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(name, Flags);
            if (property != null) return property;
        }

        return null;
    }

    private static void LogShowConditionError(object target, FieldInfo field, string message)
    {
        string typeName = target?.GetType().FullName ?? "（空）";
        string key = $"{typeName}.{field?.Name}:{message}";
        if (showConditionErrors.Add(key))
            UnityEngine.Debug.LogError($"[GraphKit] [HGShowIf] {typeName}.{field?.Name}：{message}");
    }

    /// <summary>這一列要不要畫左側標籤。清單子項是另一條路：`BuildListChildren` 直接設 `HGRow.HideLabel`，不經過欄位屬性。</summary>
    public static bool IsLabelHidden(FieldInfo f)
        => f?.IsDefined(typeof(HGHideLabelAttribute), false) ?? false;

    public static bool IsEnum(FieldInfo f)
        => f?.IsDefined(typeof(HGEnumAttribute), false) ?? false;

    /// <summary>節點在同分類內的排序權重。</summary>
    public static int TypePriority(Type t)
    {
        var attr = NodeAttr(t);
        if (attr != null) return attr.Priority;
        return 0;
    }

    /// <summary>去掉底線與型別前綴，切出可讀字串（Int_Math → Math、_tokenKey → Token Key）。</summary>
    public static string Prettify(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string s = raw.TrimStart('_');
        int cut = s.IndexOf('_');
        if (cut > 0 && cut < s.Length - 1) s = s.Substring(cut + 1);

        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString().Replace('_', ' ');
    }

    /// <summary>結果型別的短名，給節點 chip、Token 分頁與型別檢查提示用。族的 Slot 標了 [HGKind] 就用它。</summary>
    /// <summary>這個欄位要不要畫成「選一個目錄」的下拉（<see cref="HGCatalogAttribute"/>）。</summary>
    public static bool IsCatalogField(FieldInfo field)
        => field != null && field.FieldType == typeof(string) && field.IsDefined(typeof(HGCatalogAttribute), false);

    /// <summary>依 Id 找目錄。找不到回 null——目錄住在 Owner，隨時可能被刪掉，節點只留著 id。</summary>
    public static IGraphCatalogLibrary FindCatalog(IReadOnlyList<IGraphCatalogLibrary> catalogs, string id)
    {
        if (catalogs == null || string.IsNullOrEmpty(id)) return null;
        foreach (var catalog in catalogs)
            if (catalog != null && catalog.Id == id) return catalog;
        return null;
    }

    public static string ResultTypeName(Type t)
    {
        if (t == null) return "動作";

        string kind = KindName(t);
        if (!string.IsNullOrEmpty(kind)) return kind;

        if (t == typeof(int)) return "int";
        if (t == typeof(float)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        if (!t.IsGenericType) return t.Name;

        // 泛型的 Type.Name 是 CLR 內部寫法（List`1），對企劃無意義：拆回 List<int>。
        int tick = t.Name.IndexOf('`');
        var sb = new StringBuilder(tick < 0 ? t.Name : t.Name.Substring(0, tick)).Append('<');
        var args = t.GetGenericArguments();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(ResultTypeName(args[i]));
        }
        return sb.Append('>').ToString();
    }

    /// <summary>
    /// 這個 Slot 所屬族的 [HGKind] 顯示名。手上有 Slot 就用這支，才分得出同一結果型別的不同族
    /// （例：string 同時有 String 與 Key）。沒標 [HGKind] 時退回結果型別的短名。
    /// </summary>
    public static string SlotKindName(object slot) => SlotKindName(slot?.GetType());

    /// <inheritdoc cref="SlotKindName(object)"/>
    public static string SlotKindName(Type slotType)
    {
        if (slotType == null) return "動作";
        BuildKindNames();
        if (kindNamesBySlot.TryGetValue(slotType, out var name)) return name;
        return ResultTypeName(ResultType(slotType));
    }

    // [HGKind] 顯示名兩張表。掃一次全專案的 Slot 建表；domain reload 會自然重建。
    // bySlot 是權威：族的身份是 Slot 型別。byResult 只是給「手上只有結果型別」的呼叫端用的近似，
    // 所以同一結果型別有多個族時該項會留空，寧可退回 "string" 也不要顯示錯的族名。
    private static Dictionary<Type, string> kindNamesBySlot;
    private static Dictionary<Type, string> kindNamesByResult;
    private static Dictionary<Type, string> kindNamesByBody;

    /// <summary>
    /// 節點自己所屬族的 [HGKind] 名；問不出來回 null。候選節點沒有父欄位時用它。
    /// </summary>
    public static string NodeKindName(Type bodyType)
    {
        if (bodyType == null) return null;
        BuildKindNames();

        // 往上找最近的族基底：具體節點型別本身不會在表裡，表記的是 Slot 收的那個基底。
        for (Type t = bodyType; t != null && t != typeof(object); t = t.BaseType)
            if (kindNamesByBody.TryGetValue(t, out var name)) return name;
        return null;
    }

    private static string KindName(Type resultType)
    {
        BuildKindNames();
        return kindNamesByResult.TryGetValue(resultType, out var name) ? name : null;
    }

    private static void BuildKindNames()
    {
        if (kindNamesBySlot != null) return;

        kindNamesBySlot = new Dictionary<Type, string>();
        kindNamesByResult = new Dictionary<Type, string>();
        kindNamesByBody = new Dictionary<Type, string>();
        var familyCount = new Dictionary<Type, int>();

        // 先數每個結果型別有幾個族。判歧義要數族，不能數 [HGKind]：只有一個族標了名字的情況
        // （string 有 String 與 Key，但只有 Key 標了 HGKind）若不數族，String 的 chip 會被叫成 Key。
        foreach (var slotType in UnityEditor.TypeCache.GetTypesDerivedFrom<FormulaSlotBase>())
        {
            if (slotType.IsAbstract || slotType.ContainsGenericParameters) continue;
            var resultType = ResultType(slotType);
            if (resultType == null) continue;

            familyCount.TryGetValue(resultType, out int n);
            familyCount[resultType] = n + 1;

            var attr = slotType.GetCustomAttribute<HGKindAttribute>(false);
            if (attr == null || string.IsNullOrEmpty(attr.Name)) continue;

            kindNamesBySlot[slotType] = attr.Name;

            // 族基底 → 族名：候選節點沒有欄位可問，只剩節點型別問得出自己屬於哪一族。
            // 同一個基底被兩個族標成不同名字時記 null（例：產出端與讀取端若哪天名字不一致），
            // 寧可退回結果型別短名，也不要掛上另一族的名字。
            Type bodyBase = FormulaBaseType(slotType);
            if (bodyBase == null) continue;
            if (!kindNamesByBody.TryGetValue(bodyBase, out var existing)) kindNamesByBody[bodyBase] = attr.Name;
            else if (existing != attr.Name) kindNamesByBody[bodyBase] = null;
        }

        // byResult 只代表獨佔該結果型別的族；多族共用時留空，讓呼叫端退回結果型別短名。
        foreach (var pair in kindNamesBySlot)
        {
            var resultType = ResultType(pair.Key);
            if (resultType == null || familyCount[resultType] != 1) continue;
            kindNamesByResult[resultType] = pair.Value;
        }
    }
}

}
