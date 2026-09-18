namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>公式與動作資產共同提供的內部圖。資產的參數就是它自己的具名端點清單。</summary>
public interface IGraphAsset : IOrphanPool, ITokenOwner, IGraphHead
{
    object ContentObject { get; }

    /// <summary>根內容的載體。根節點的 Id／座標／備註都住在它身上，和圖上其他節點同一套。</summary>
    GraphNode Root { get; }

    /// <summary>公式資產的結果型別；動作資產沒有結果型別，回 null。「是不是動作資產」一律問這個。</summary>
    Type ResultType { get; }
}

/// <summary>動作資產的標記。</summary>
// 非泛型才能在「型別層」分辨動作資產：資產索引刻意先看型別再決定要不要載入，
// 全專案每個 SO 都載一次太慢，所以這個判斷不能依賴實例。
public interface IActionGraphAsset : IGraphAsset
{
}

public sealed class AssetParameterDefinition
{
    public string Name;
    public Type ResultType;
    public Type PackType;
    public FormulaSlotBase Slot;
    public GraphToken Token;
}

/// <summary>把資產的端點清單讀成參數 schema。清單即介面，不必掃圖。</summary>
public static class AssetGraphSchema
{
    // 求值期每次都重建參數清單太貴（拖曳預覽會逐格逐詞綴呼叫）。schema 只在資產端點變動時才會變，
    // 所以求值路徑走這份快取；編輯器與 Verify 一律走未快取的 Read，看到的永遠是當下資料。
    private static readonly Dictionary<ScriptableObject, List<AssetParameterDefinition>> Cache = new();

    /// <summary>求值路徑專用：同一個資產只讀一次。編輯期改完圖由 <see cref="InvalidateCache"/> 清掉。</summary>
    public static List<AssetParameterDefinition> ReadCached(ScriptableObject asset)
    {
        if (asset == null) return new List<AssetParameterDefinition>();
        if (Cache.TryGetValue(asset, out var cached)) return cached;
        cached = Read(asset, out _);
        Cache[asset] = cached;
        return cached;
    }

    /// <summary>清掉求值快取。編輯器每次重建圖都會呼叫；runtime 資產不會變，不必呼叫。</summary>
    public static void InvalidateCache() => Cache.Clear();

#if UNITY_EDITOR
    // 關掉 Domain Reload 時 static 會跨 Play 存活，進 Play 一定要丟掉編輯期留下的舊 schema。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCacheOnPlay() => Cache.Clear();
#endif

    /// <summary>讀出參數清單。duplicates 收「族＋名稱」撞號的名字，由呼叫端報錯。</summary>
    public static List<AssetParameterDefinition> Read(ScriptableObject asset, out List<string> duplicates)
    {
        duplicates = new List<string>();
        var result = new List<AssetParameterDefinition>();
        if (asset is not IGraphAsset graph || graph.Tokens == null) return result;

        var seen = new HashSet<(Type, string)>();
        foreach (var endpoint in graph.Tokens)
        {
            if (endpoint == null) continue;
            string name = endpoint.Name;
            var resultType = endpoint.ResultType;
            // 名字或 Slot 沒填完的端點對外不成立參數；Verify 會另外報，這裡直接略過。
            if (string.IsNullOrEmpty(name) || resultType == null) continue;
            // 撞號看族不看結果型別：同一個結果型別的不同族（String / Key）是兩個參數，不算重複。
            if (!seen.Add((endpoint.Slot.FamilyType, name))) { duplicates.Add(name); continue; }

            result.Add(new AssetParameterDefinition
            {
                Name = name,
                ResultType = resultType,
                PackType = endpoint.PackType,
                Slot = endpoint.Slot,
                Token = endpoint,
            });
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }
}
}
