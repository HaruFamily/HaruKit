namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;

public partial class ActionSystem<TTiming, TPack, TTokenEntryPack>
where TTiming : Enum
where TTokenEntryPack : TokenEntryPack<TPack>, new()
{
    #region === Description Compile ===
    // 通用引擎：建 context → 依序跑 TokenEntry.CompilePasses → 回 context（Text + Artifacts）。
    // 不認得任何 token 語法或專案產物；Block / Detail / Times / Simple 全在 Project 端 ICompilePass 實作。
    public async UniTask<CompileContext<TPack>> Compile(string template, TPack pack)
    {
        var ctx = new CompileContext<TPack> { Text = template ?? "", Pack = pack };
        if (string.IsNullOrEmpty(template) || !HasContent()) return ctx;

        ctx.Tokens = CreateTokenCache();

        var passes = TokenEntry.CompilePasses;
        if (passes != null)
            foreach (var pass in passes)
                if (pass != null) await pass.Run(ctx);

        return ctx;
    }
    #endregion
}

}
