namespace PinPlugin.ActionSystem
{
using System.Collections.Generic;

/// <summary>
/// Owner 宣告「我會從節點圖外面、用字串 key 求值這些 token」。
///
/// Token 是 ActionSystem 唯一能被外部具名查詢的端點（`TokenTable.Has/Resolve`），
/// 這種引用在節點圖裡看不到任何連線，驗證器不問就會把它們全部報成「宣告後沒有任何欄位引用」。
/// 反過來，Owner 寫錯 key 也會靜默失效（runtime 只是 Has 回 false 然後跳過）。
/// 實作這個介面就能讓兩邊都被檢查到。
/// </summary>
public interface IExternalTokenKeys
{
    /// <summary>被外部引用的 token 名稱。null 與空字串由呼叫端忽略，不必自己過濾。</summary>
    IEnumerable<string> ExternalTokenKeys { get; }
}

}
