namespace PinPlugin.ActionSystem
{
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

public partial class ActionSystem<TTiming, TPack>
where TTiming : Enum
{
    #region === Description Compile ===
    // 通用引擎：建 context → 依序跑 passes → 回 context（Text + Artifacts）。
    // 不認得任何 token 語法或專案產物；Block / Detail / Times / Simple 全在 Project 端 ICompilePass 實作。
    //
    // passes 由呼叫端供給；Description 語法真的出現時再由使用端組合。
    public async UniTask<CompileContext<TPack>> Compile(string template, TPack pack,
        IReadOnlyList<ICompilePass<TPack>> passes)
    {
        var ctx = new CompileContext<TPack> { Text = template ?? "", Pack = pack };
        if (string.IsNullOrEmpty(template) || passes == null || passes.Count == 0) return ctx;

        ctx.Tokens = CreateTokenTable();
        foreach (var pass in passes)
            if (pass != null) await pass.Run(ctx);

        return ctx;
    }
    #endregion
}

}
