namespace PinPlugin.ActionSystem
{
using System.Collections.Generic;

// Verify 走訪：concrete pack 知道 int↔IntTokenEntry 的型別配對，Core 知道怎麼驗一個 kind。
// 用 visitor 把「每種 kind 的 (TResult, TEntry) 泛型對」交回 Core 的泛型 walker，避免在 Core 寫死 6 種清單。
// 所有 token entry 的共同形狀：一個 Key + 一個 Slot。
// 抽出後 visitor / ForEachKind 不必再逐筆傳 e=>e.Key / e=>e.Slot 委派。
public interface ITokenEntry
{
    string Key { get; set; }
    FormulaSlotBase Slot { get; }
}

// owner 契約：editor 端 ConvertToToken 透過它拿到 token pack，再 pack.FindList<TEntry>() 取對應清單。
// 泛型回傳 Core base TokenEntryPack<TPack> → 不碰任何具體 entry 型別，故可留在 Core。
public interface ITokenEntryOwner<TPack>
{
    TokenEntryPack<TPack> GetTokenPack();
}

public interface ITokenKindVisitor<TPack>
{
    void Visit<TResult, TEntry>(string typeName, List<TEntry> entries)
        where TEntry : class, ITokenEntry;
}

public abstract class TokenEntryPack<TPack>
{
    // 唯一需要 concrete pack 實作的方法：列出本專案有哪些 token kind（type 配對 + list + getKey/getSlot）。
    // HasContent / AssignTokenKeys / BuildDict 全部由它衍生 → 新增一種 token 只改 ForEachKind + 欄位，不會漏改。
    public abstract void ForEachKind(ITokenKindVisitor<TPack> visitor);

    // Description 編譯 pass 清單：Core 引擎依序跑。具體 token 語法住 Project，從這裡供給。
    public abstract System.Collections.Generic.IReadOnlyList<ICompilePass<TPack>> CompilePasses { get; }

    public bool HasContent()
    {
        var v = new HasContentVisitor();
        ForEachKind(v);
        return v.Any;
    }

    public void AssignTokenKeys() => ForEachKind(AssignKeysVisitor.Instance);

    public void BuildDict(TokenCache<TPack> t) => ForEachKind(new BuildDictVisitor(t));

    // 依 entry 型別取回對應清單（給 editor 端 ConvertToToken 新增 token 用）。同樣由 ForEachKind 衍生，不必逐型寫 Get 方法。
    public List<TEntry> FindList<TEntry>() where TEntry : class, ITokenEntry
    {
        var v = new FindListVisitor<TEntry>();
        ForEachKind(v);
        return v.Found;
    }

    // ===== 衍生 visitor：每個就是「對一種 kind 做什麼」的單點定義 =====

    private sealed class HasContentVisitor : ITokenKindVisitor<TPack>
    {
        public bool Any;
        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries) where TEntry : class, ITokenEntry
        {
            if ((entries?.Count ?? 0) > 0) Any = true;
        }
    }

    // 無狀態 → 單例重用，避免每次 AssignTokenKeys / Verify 都 new。
    private sealed class AssignKeysVisitor : ITokenKindVisitor<TPack>
    {
        public static readonly AssignKeysVisitor Instance = new();
        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries) where TEntry : class, ITokenEntry
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (e.Slot != null) e.Slot.SetDictKey(e.Key);
            }
        }
    }

    private sealed class BuildDictVisitor : ITokenKindVisitor<TPack>
    {
        private readonly TokenCache<TPack> _t;
        public BuildDictVisitor(TokenCache<TPack> t) => _t = t;
        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries) where TEntry : class, ITokenEntry
        {
            var d = new Dictionary<string, IFormulaSlot<TResult, TPack>>();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    if (!string.IsNullOrEmpty(e.Key) && e.Slot is IFormulaSlot<TResult, TPack> typed) d[e.Key] = typed;
                }
            }
            _t.Register(d);
        }
    }

    private sealed class FindListVisitor<TWanted> : ITokenKindVisitor<TPack> where TWanted : class, ITokenEntry
    {
        public List<TWanted> Found;
        public void Visit<TResult, TEntry>(string typeName, List<TEntry> entries) where TEntry : class, ITokenEntry
        {
            if (entries is List<TWanted> match) Found = match;
        }
    }
}

}
