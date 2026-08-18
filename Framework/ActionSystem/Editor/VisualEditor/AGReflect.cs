namespace PinPlugin.ActionSystem.Editor
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
public static class AGReflect
{
    private const BindingFlags Flags = BindingFlags.Instance
                                     | BindingFlags.Public
                                     | BindingFlags.NonPublic
                                     | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Type, List<FieldInfo>> fieldCache = new();
    private static readonly Dictionary<Type, string> nameCache = new();

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

    /// <summary>是否為 ActionSlot&lt;TPack&gt;（不知道 TPack，只能比泛型定義）。</summary>
    public static bool IsActionSlotType(Type t)
    {
        for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            if (cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(ActionSlot<>))
                return true;
        return false;
    }

    public static bool IsSlotType(Type t) => IsFormulaSlotType(t) || IsActionSlotType(t);

    /// <summary>取 FormulaSlot&lt;TResult, TAsset, TFormula, TPack&gt; 的泛型參數；找不到回 null。</summary>
    private static Type[] FormulaSlotArgs(Type slotType)
    {
        for (var cur = slotType; cur != null && cur != typeof(object); cur = cur.BaseType)
            if (cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(FormulaSlot<,,,>))
                return cur.GetGenericArguments();
        return null;
    }

    /// <summary>Slot 的結果型別（int / float / bool / string / EntityView…）。Action Slot 回 null。</summary>
    public static Type ResultType(Type slotType)
    {
        var args = FormulaSlotArgs(slotType);
        return args != null && args.Length == 4 ? args[0] : null;
    }

    /// <summary>Slot 可接的 Formula base 型別（例如 IntFormula）。</summary>
    public static Type FormulaBaseType(Type slotType)
    {
        var args = FormulaSlotArgs(slotType);
        return args != null && args.Length == 4 ? args[2] : null;
    }

    /// <summary>FormulaSlot 的 TPack；不是 FormulaSlot 回 null。列舉公式族時用它排除別的 pack。</summary>
    public static Type FormulaSlotPack(Type slotType)
    {
        var args = FormulaSlotArgs(slotType);
        return args != null && args.Length == 4 ? args[3] : null;
    }

    /// <summary>Slot 可接的 Formula Asset 型別（例如 IntAsset）。</summary>
    public static Type AssetType(Type slotType)
    {
        var args = FormulaSlotArgs(slotType);
        return args != null && args.Length == 4 ? args[1] : null;
    }

    /// <summary>ActionSlot&lt;TPack&gt; 的 TPack；不是 ActionSlot 回 null。</summary>
    private static Type ActionSlotPack(Type actionSlotType)
    {
        for (var cur = actionSlotType; cur != null && cur != typeof(object); cur = cur.BaseType)
            if (cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(ActionSlot<>))
                return cur.GetGenericArguments()[0];
        return null;
    }

    /// <summary>ActionSlot&lt;TPack&gt; 可接的 Action base 型別 ActionBase&lt;TPack&gt;。</summary>
    public static Type ActionBaseType(Type actionSlotType)
    {
        var pack = ActionSlotPack(actionSlotType);
        return pack == null ? null : typeof(ActionBase<>).MakeGenericType(pack);
    }

    /// <summary>ActionSlot&lt;TPack&gt; 的 Asset 型別 ActionAssetBase&lt;TPack&gt;。</summary>
    public static Type ActionAssetType(Type actionSlotType)
    {
        var pack = ActionSlotPack(actionSlotType);
        return pack == null ? null : typeof(ActionAssetBase<>).MakeGenericType(pack);
    }

    /// <summary>公式資產的結果型別；動作資產沒有結果型別，回 null。</summary>
    public static Type AssetResultType(UnityEngine.Object asset)
    {
        if (asset == null) return null;
        for (var t = asset.GetType(); t != null && t != typeof(object); t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(FormulaAsset<,>))
                return t.GetGenericArguments()[0];
        return null;
    }

    // ===== Slot 的節點存取 =====
    // Slot 只有「有沒有接節點」一種狀態；來源種類、內容與座標全在 GraphNode 上。
    // FormulaSlotBase 是非泛型，可直接呼叫；ActionSlot<TPack> 在 Editor 端拿不到 TPack，只能走成員反射。

    /// <summary>Slot 目前接的節點；null 代表常數（公式）或空槽（動作）。</summary>
    public static GraphNode GetNode(object slot)
    {
        if (slot is FormulaSlotBase fsb) return fsb.Node;
        return GetMember(slot, "Node") as GraphNode;
    }

    public static void SetNode(object slot, GraphNode node)
    {
        if (slot == null) return;
        if (slot is FormulaSlotBase fsb) { fsb.SetNode(node); return; }
        CallMethod(slot, "SetNode", node);
    }

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

    /// <summary>相容既有呼叫端的模式碼：0 常數／空槽、1 公式或動作（含編輯中空節點）、2 資產、3 具名變數。</summary>
    public static int UseType(object slot)
    {
        var node = GetNode(slot);
        if (node == null) return 0;
        return node.Kind switch
        {
            NodeKind.Asset => 2,
            NodeKind.Token => 3,
            _ => 1,   // Inline 與 Empty 都畫成來源節點，Empty 由驗證擋存檔
        };
    }

    public static object GetFormula(object slot)
    {
        var node = GetNode(slot);
        return node != null && node.Kind == NodeKind.Inline ? node.BodyObject : null;
    }

    /// <summary>換內嵌來源：節點 Id、座標、備註與連入邊全部保留，只換內容。</summary>
    public static void SetFormula(object slot, object formula)
    {
        if (formula == null)
        {
            GetNode(slot)?.Clear();
            return;
        }
        EnsureNode(slot).SetBody(formula as ActionSystemNode);
    }

    public static UnityEngine.Object GetAsset(object slot)
    {
        var node = GetNode(slot);
        return node != null && node.Kind == NodeKind.Asset ? node.AssetObject : null;
    }

    public static void SetAsset(object slot, UnityEngine.Object asset)
        => EnsureNode(slot).SetAsset(asset as UnityEngine.ScriptableObject);

    /// <summary>這個欄位接的具名變數（沒接或不是變數節點回 null）。</summary>
    public static GraphEndpoint GetEndpoint(object slot) => GetNode(slot)?.Endpoint;

    /// <summary>換成具名變數引用：節點 Id、座標、備註與連入邊全部保留，只換內容。</summary>
    public static void SetEndpoint(object slot, GraphEndpoint endpoint)
    {
        if (endpoint == null)
        {
            GetNode(slot)?.Clear();
            return;
        }
        EnsureNode(slot).SetEndpoint(endpoint);
    }

    /// <summary>斷開來源：公式欄位回常數、動作欄位回空槽。</summary>
    public static void ClearNode(object slot) => SetNode(slot, null);

    /// <summary>這個欄位能不能接這個內嵌內容 / 資產。跨 pack 或跨結果型別在這裡擋下。</summary>
    public static bool AcceptsBody(object slot, object body)
    {
        if (body is not ActionSystemNode node) return false;
        if (slot is FormulaSlotBase fsb) return fsb.AcceptsBody(node);
        return CallMethod(slot, "AcceptsBody", node) as bool? ?? false;
    }

    public static bool AcceptsAsset(object slot, UnityEngine.Object asset)
    {
        var so = asset as UnityEngine.ScriptableObject;
        if (slot is FormulaSlotBase fsb) return fsb.AcceptsAsset(so);
        return CallMethod(slot, "AcceptsAsset", so) as bool? ?? false;
    }

    /// <summary>這個欄位能不能接這個具名變數。動作欄位一律不能。</summary>
    public static bool AcceptsEndpoint(object slot, GraphEndpoint endpoint)
    {
        if (endpoint == null) return false;
        if (slot is FormulaSlotBase fsb) return fsb.AcceptsEndpoint(endpoint);
        return CallMethod(slot, "AcceptsEndpoint", endpoint) as bool? ?? false;
    }

    public static object GetDefault(object slot) => (slot as FormulaSlotBase)?.DefaultObject;

    public static void SetDefault(object slot, object value)
    {
        if (slot is FormulaSlotBase fsb) fsb.DefaultObject = value;
    }

    /// <summary>
    /// 動作欄位自己的停用旗標（`ActionSlot._disabled`）。舊資產仍可能有這個值，執行期照樣擋，
    /// 但編輯器不再提供入口——要關掉一段行為改成停用它接的節點（`GraphNode.Disabled`），
    /// 那是共用單位，語意也更一致。因此這裡只有 Get。
    /// </summary>
    public static bool GetDisabled(object actionSlot) => GetMember(actionSlot, "Disabled") as bool? ?? false;

    public static string GetLabel(object actionSlot) => GetMember(actionSlot, "Label") as string;

    public static void SetLabel(object actionSlot, string value) => SetMember(actionSlot, "Label", value);

    /// <summary>頭端目前的識別碼；還沒指派時為空字串。</summary>
    public static string SlotEditorId(object head) => GetMember(head, "Id") as string;

    /// <summary>動作頭端的穩定識別碼（焦點 act:{id} 與 HEAD 節點座標都用它）。</summary>
    public static string EnsureSlotEditorId(object slot)
    {
        if (slot == null) return "?";
        return CallMethod(slot, "EnsureId") as string ?? "?";
    }

    /// <summary>複製頭端後換新識別碼，否則兩個頭端共用同一筆座標與焦點。</summary>
    public static void ResetSlotEditorId(object slot) => CallMethod(slot, "ResetId");

    /// <summary>頭端座標。動作頭端與時機群組都有。</summary>
    public static bool GetHeadPos(object head, out UnityEngine.Vector2 pos)
    {
        pos = default;
        if (head == null) return false;
        if ((GetMember(head, "HasPos") as bool? ?? false) == false) return false;
        pos = GetMember(head, "Pos") as UnityEngine.Vector2? ?? default;
        return true;
    }

    public static void SetHeadPos(object head, UnityEngine.Vector2 pos) => SetMember(head, "Pos", pos);

    public static void ClearHeadPos(object head) => CallMethod(head, "ClearPos");

    /// <summary>畫布主人的候選節點池（ActionSystem、資產各一份；動作頭端上的那份只為讀回舊資料）。</summary>
    public static List<GraphNode> Orphans(object head) => GetMember(head, "Orphans") as List<GraphNode>;

    /// <summary>圖主人的具名變數清單（ActionSystem、公式／動作資產各一份）。</summary>
    // 走 GetMember 而不是 Get：Endpoints 是屬性，Get 只找欄位，拿到的會是 null。
    public static List<GraphEndpoint> Endpoints(object owner) => GetMember(owner, "Endpoints") as List<GraphEndpoint>;

    // 泛型成員只能靠名稱呼叫：ActionSlot<TPack> 的 TPack 在 Editor 端是未知的。
    private static object GetMember(object target, string name)
        => target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static void SetMember(object target, string name, object value)
        => target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);

    private static object CallMethod(object target, string name, params object[] args)
    {
        var m = target?.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
        return m?.Invoke(target, args);
    }

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
            UnityEngine.Debug.LogError($"[ActionGraph] 建立 {t.Name} 失敗：{e.Message}");
            return null;
        }
    }

    /// <summary>判斷具體節點是否繼承 ActionBase&lt;&gt;；避免 Graph 引用專案端型別。</summary>
    public static bool IsActionNodeType(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ActionBase<>)) return true;
        return false;
    }

    /// <summary>從具體 Action／Formula 型別回推可供型別選單使用的封閉泛型 base。</summary>
    public static Type NodeBaseType(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            if (!current.IsGenericType) continue;
            Type definition = current.GetGenericTypeDefinition();
            if (definition == typeof(ActionBase<>) || definition == typeof(FormulaBase<,>)) return current;
        }
        return null;
    }

    /// <summary>從具體 Formula 型別取得 TResult；Action 回 null。</summary>
    public static Type FormulaResultType(Type type)
    {
        Type nodeBase = NodeBaseType(type);
        return nodeBase != null && nodeBase.GetGenericTypeDefinition() == typeof(FormulaBase<,>)
            ? nodeBase.GetGenericArguments()[0]
            : null;
    }

    // ===== 顯示名稱 =====

    // 名稱與分類只認 ActionSystem 自有屬性，避免 Graph 操作體驗受外部 Inspector 插件影響。

    private static ASNodeAttribute NodeAttr(Type t)
        => t?.GetCustomAttribute<ASNodeAttribute>(false);

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

        string label = f.GetCustomAttribute<ASLabelAttribute>(false)?.Name;
        if (!string.IsNullOrEmpty(label)) return label;

        return Prettify(f.Name);
    }

    /// <summary>參數欄位說明（滑鼠停留顯示）；沒寫回空字串。</summary>
    public static string FieldDescription(FieldInfo f)
    {
        if (f == null) return "";
        return f.GetCustomAttribute<ASDescriptionAttribute>(false)?.Text ?? "";
    }

    /// <summary>
    /// 這個欄位要不要畫出來。`[ASHide]` 是明講的；`[HideInInspector]` 也算——節點圖就是 Inspector 的替代品，
    /// 而 Core 用它標的都是編輯期內部欄位（座標、識別碼、候選池），出現在節點上只是雜訊。
    /// </summary>
    public static bool IsHidden(FieldInfo f)
        => f != null && (f.IsDefined(typeof(ASHideAttribute), false)
                      || f.IsDefined(typeof(UnityEngine.HideInInspector), false));

    public static bool IsLabelHidden(FieldInfo f)
        => f?.GetCustomAttribute<ASLabelAttribute>(false)?.Mode == ASLabelMode.Hide;

    public static bool IsEnum(FieldInfo f)
        => f?.IsDefined(typeof(ASEnumAttribute), false) ?? false;

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

    /// <summary>結果型別的短名，給 Token 分頁與型別檢查提示用。</summary>
    public static string ResultTypeName(Type t)
    {
        if (t == null) return "動作";
        if (t == typeof(int)) return "int";
        if (t == typeof(float)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        return t.Name;
    }
}

}
