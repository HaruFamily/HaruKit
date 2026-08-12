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

    /// <summary>Slot 可接的 Formula Asset 型別（例如 IntAsset）。</summary>
    public static Type AssetType(Type slotType)
    {
        var args = FormulaSlotArgs(slotType);
        return args != null && args.Length == 4 ? args[1] : null;
    }

    /// <summary>ActionSlot&lt;TPack&gt; 可接的 Action base 型別 ActionBase&lt;TPack&gt;。</summary>
    public static Type ActionBaseType(Type actionSlotType)
    {
        var f = Find(actionSlotType, "_formula");
        return f?.FieldType;
    }

    /// <summary>ActionSlot&lt;TPack&gt; 的 Asset 型別 ActionAssetBase&lt;TPack&gt;。</summary>
    public static Type ActionAssetType(Type actionSlotType)
    {
        var f = Find(actionSlotType, "_asset");
        return f?.FieldType;
    }

    // ===== Slot 欄位存取（Formula 與 Action 共用同一組欄位名）=====

    /// <summary>FormulaSlot：0 常數 / 1 公式 / 2 資產 / 3 變數。ActionSlot：0 空槽 / 1 公式 / 2 資產。</summary>
    public static int UseType(object slot)
    {
        var v = Get(slot, "_useType");
        return v == null ? 0 : Convert.ToInt32(v);
    }

    public static void SetUseType(object slot, int value)
    {
        var f = Find(slot.GetType(), "_useType");
        if (f == null) return;
        f.SetValue(slot, Enum.ToObject(f.FieldType, value));
    }

    public static object GetFormula(object slot) => Get(slot, "_formula");

    public static void SetFormula(object slot, object formula) => Set(slot, "_formula", formula);

    public static UnityEngine.Object GetAsset(object slot) => Get(slot, "_asset") as UnityEngine.Object;

    public static void SetAsset(object slot, UnityEngine.Object asset) => Set(slot, "_asset", asset);

    public static string GetTokenKey(object slot) => Get(slot, "_tokenKey") as string;

    public static void SetTokenKey(object slot, string key) => Set(slot, "_tokenKey", key);

    public static object GetDefault(object slot) => Get(slot, "_default");

    public static void SetDefault(object slot, object value) => Set(slot, "_default", value);

    public static bool GetDisabled(object actionSlot) => Get(actionSlot, "_disabled") as bool? ?? false;

    public static void SetDisabled(object actionSlot, bool value) => Set(actionSlot, "_disabled", value);

    public static string GetLabel(object actionSlot) => Get(actionSlot, "_label") as string;

    public static void SetLabel(object actionSlot, string value) => Set(actionSlot, "_label", value);

    /// <summary>清掉非當前模式的殘留來源，避免 Verify 一直噴「殘留設定」警告。</summary>
    public static void ClearUnusedSources(object slot, int keepUseType)
    {
        if (keepUseType != 1) SetFormula(slot, null);
        if (keepUseType != 2) { SetAsset(slot, null); Set(slot, "_previousAsset", null); }
        if (Find(slot.GetType(), "_tokenKey") != null && keepUseType != 3) SetTokenKey(slot, null);
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

    // ===== 顯示名稱 =====

    // 名稱與分類只認 ActionSystem 自有屬性，避免 Graph 操作體驗受外部 Inspector 插件影響。

    private static ActionNodeAttribute NodeAttr(Type t)
        => t?.GetCustomAttribute<ActionNodeAttribute>(false);

    private static string Tooltip(MemberInfo member)
    {
        if (member == null) return "";
        var attributes = member.GetCustomAttributes(typeof(UnityEngine.TooltipAttribute), false);
        if (attributes == null || attributes.Length == 0) return "";
        var attr = attributes[0];
        var field = attr.GetType().GetField("tooltip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(attr) as string ?? "";
    }

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
        string cat = NodeAttr(t)?.Category;
        return string.IsNullOrEmpty(cat) ? "其他" : cat;
    }

    /// <summary>節點說明；沒寫就退回分類，不直接吐類別名。</summary>
    public static string TypeDescription(Type t)
    {
        if (t == null) return "尚未指定內容";

        string desc = NodeAttr(t)?.Description;
        if (!string.IsNullOrEmpty(desc)) return desc;

        string tip = Tooltip(t);
        if (!string.IsNullOrEmpty(tip)) return tip;

        return TypeCategory(t);
    }

    /// <summary>參數欄位顯示名。</summary>
    public static string FieldLabel(FieldInfo f)
    {
        if (f == null) return "?";

        string label = f.GetCustomAttribute<ActionParamAttribute>(false)?.Name;
        if (!string.IsNullOrEmpty(label)) return label;

        return Prettify(f.Name);
    }

    /// <summary>參數欄位說明（滑鼠停留顯示）；沒寫回空字串。</summary>
    public static string FieldDescription(FieldInfo f)
    {
        if (f == null) return "";
        string desc = f.GetCustomAttribute<ActionParamAttribute>(false)?.Description;
        if (!string.IsNullOrEmpty(desc)) return desc;
        return Tooltip(f);
    }

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
