namespace HaruFamily.Framework.ActionSystem.Editor
{
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 「誰引用了這個共用資產」的反向索引，從 <see cref="AGOwnerIndex"/> 現算。
///
/// 以前這份名單序列化在資產自己身上（`_subscribers`）：它會跟磁碟上的實際引用脫節，
/// 所以還得配一顆「重建引用清單」按鈕，而且每次註冊都要 SetDirty 資產。
/// 索引是衍生資料，不存檔就不會過期——這是拿掉那一整套維護手續的前提。
/// </summary>
[InitializeOnLoad]
public static class AGReferenceIndex
{
    private static Dictionary<ScriptableObject, List<ScriptableObject>> cache;
    private static readonly List<ScriptableObject> Empty = new();

    static AGReferenceIndex() => EditorApplication.projectChanged += Invalidate;

    /// <summary>引用這個資產的所有 Owner。只看磁碟上的內容，未存檔的修改不算。</summary>
    public static IReadOnlyList<ScriptableObject> Users(ScriptableObject asset)
    {
        if (asset == null) return Empty;
        cache ??= Build();
        return cache.TryGetValue(asset, out var list) ? list : Empty;
    }

    /// <summary>下次取用時重算。Owner 索引一起丟掉，否則新增的 Owner 永遠進不了反向索引。</summary>
    public static void Invalidate()
    {
        cache = null;
        AGOwnerIndex.Invalidate();
    }

    /// <summary>立刻重掃（使用者手動按「重新掃描」）。</summary>
    public static void Refresh()
    {
        AGOwnerIndex.Refresh();
        cache = Build();
    }

    private static Dictionary<ScriptableObject, List<ScriptableObject>> Build()
    {
        var map = new Dictionary<ScriptableObject, List<ScriptableObject>>();
        foreach (var entry in AGOwnerIndex.Entries)
        {
            var owner = entry.Owner;
            if (owner == null) continue;

            var system = AGModel.FindSystemField(owner)?.GetValue(owner);
            if (system == null) continue;

            foreach (var asset in AGModel.ReferencedAssetsOfSystem(system))
            {
                if (asset == null) continue;
                if (!map.TryGetValue(asset, out var users)) map[asset] = users = new List<ScriptableObject>();
                users.Add(owner);
            }
        }
        return map;
    }
}

}
