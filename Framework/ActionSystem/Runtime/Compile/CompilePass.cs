namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

// Description 編譯引擎的「一段處理」契約。具體語法由 Compile 呼叫端提供；
// Core 引擎只負責依序執行，不認得任何專案語法。
public interface ICompilePass<TPack>
{
    UniTask Run(CompileContext<TPack> ctx);
}

// 跑一次 Compile 的可變工作狀態 + 結果。每個 pass 讀改 Text、按需把專案產物丟進 Artifacts。
// Core 不認得任何專案產物型別（如 DetailInfo）；caller 端自行 OfType<T>() 取回。
public sealed class CompileContext<TPack>
{
    public string Text;
    public TPack Pack;
    public TokenTable<TPack> Tokens;
    public readonly List<object> Artifacts = new();
}

// 「找 token → 逐一替換」的 pass 樣板。絕大多數 token 語法屬此類，子類只給 regex + 單筆替換字串，
// StringBuilder cursor 走訪由 base 內建，免每個 pass 重抄。ReplaceMatch 回 null = 保留原 match 文字。
// 需要剝離區塊 / 自訂走訪（如 Block）的 pass 才直接實作 ICompilePass。
public abstract class RegexReplacePass<TPack> : ICompilePass<TPack>
{
    protected abstract System.Text.RegularExpressions.Regex Pattern { get; }
    protected abstract UniTask<string> ReplaceMatch(System.Text.RegularExpressions.Match m, CompileContext<TPack> ctx);

    public async UniTask Run(CompileContext<TPack> ctx)
    {
        var matches = Pattern.Matches(ctx.Text);
        if (matches.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        int cursor = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            sb.Append(ctx.Text, cursor, m.Index - cursor);
            string replaced = await ReplaceMatch(m, ctx);
            sb.Append(replaced ?? m.Value);
            cursor = m.Index + m.Length;
        }
        sb.Append(ctx.Text, cursor, ctx.Text.Length - cursor);
        ctx.Text = sb.ToString();
    }
}

}
