using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

namespace HaruFamily.Framework.Nexus
{
    /// <summary>
    /// <c>await using (Nexus.CreateScope()) { ... }</c> 的 RAII 封裝。離開 using 區塊（含例外中斷）自動 <c>Pop</c>，
    /// 避免忘記 Pop 造成 stack 洩漏。Dispose 冪等。
    /// <para>
    /// <b>刻意只實作 <see cref="IAsyncDisposable"/>、不實作 <see cref="IDisposable"/></b>：因 <c>Pop</c> 是 async（內含 ClearAll），
    /// 同步 dispose 無法 await 拆除。只有 IAsyncDisposable 時，同步 <c>using var scope = Nexus.CreateScope();</c> 會**編譯失敗**
    /// （CS1674：型別需可轉成 <c>System.IDisposable</c>），強制呼叫端寫 <c>await using</c>。<b>切勿補上 IDisposable</b>——
    /// 那會讓同步 using 編譯通過卻無法正確 await Pop，破壞此契約。
    /// </para>
    /// </summary>
    public sealed class NexusScope : IAsyncDisposable
    {
        private bool _disposed;

        public async System.Threading.Tasks.ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GC.SuppressFinalize(this);
#endif
            await Nexus.Pop();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // debug 守衛：抓「忘了 await using」——scope 被 GC 卻沒 DisposeAsync，代表 Push 沒有對應 Pop，context stack 已洩漏。
        // 同步 using 已被編譯擋掉；這條補上「整個忘了寫 using / await」的 fire-and-forget 漏洞。正式 build 編譯掉、零成本。
        ~NexusScope()
        {
            if (!_disposed)
                UnityEngine.Debug.LogError(
                    "[Nexus] NexusScope 未被 dispose：必須以 `await using (Nexus.CreateScope()) { ... }` 使用。" +
                    "偵測到忘了 await using（Push 沒有對應的 Pop，context stack 已洩漏）。");
        }
#endif
    }
}
