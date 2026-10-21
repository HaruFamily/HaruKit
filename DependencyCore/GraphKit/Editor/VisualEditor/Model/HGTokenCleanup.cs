using System.Collections.Generic;
using UnityEngine;

namespace HaruFamily.DependencyCore.GraphKit.Editor
{
    /// <summary>只整理工作副本；移除不再符合宣告的 Token，保留其來源與候選節點。</summary>
    internal static class HGTokenCleanup
    {
        internal static int Clean(IGraphDocument document)
        {
            int removed = Clean(document, (document as ITokenOwner)?.Tokens, document.Orphans);
            if (removed > 0) document.InvalidateValidation();
            return removed;
        }

        internal static int Clean(object content, List<GraphToken> tokens, List<GraphNode> orphans)
        {
            if (tokens == null || orphans == null) return 0;
            var obsolete = new HashSet<GraphToken>(HGRefComparer.Instance);
            foreach (var token in tokens)
                if (token != null && token.IsObsolete) obsolete.Add(token);
            if (obsolete.Count == 0) return 0;

            var visited = new HashSet<object>(HGRefComparer.Instance);
            var slots = new List<GraphSlotBase>();
            slots.AddRange(HGModel.WalkSlots(content, visited));
            slots.AddRange(HGModel.WalkSlots(tokens, visited));
            slots.AddRange(HGModel.WalkSlots(orphans, visited));
            var pools = new List<List<GraphNode>> { orphans };
            foreach (var token in tokens) if (token != null) pools.Add(token.Orphans);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is not IOrphanPool pool || pool.Orphans == null || pools.Contains(pool.Orphans)) continue;
                pools.Add(pool.Orphans);
                slots.AddRange(HGModel.WalkSlots(pool.Orphans, visited));
            }
            bool IsRemovedProxy(GraphNode node) => node?.Kind == NodeKind.Token && obsolete.Contains(node.Token);
            var proxies = new HashSet<GraphNode>(HGRefComparer.Instance);
            var carrierVisits = new HashSet<object>(HGRefComparer.Instance);
            foreach (var node in HGModel.WalkCarriers(new object[] { content, tokens, pools }, carrierVisits))
                if (IsRemovedProxy(node)) proxies.Add(node);
            int disconnected = 0;
            foreach (var slot in slots)
            {
                if (!IsRemovedProxy(slot.Node)) continue;
                proxies.Add(slot.Node);
                slot.SetNode(null);
                disconnected++;
            }
            foreach (var pool in pools)
                foreach (var node in pool) if (IsRemovedProxy(node)) proxies.Add(node);

            foreach (var token in obsolete)
            {
                var source = token.Slot?.Node;
                if (source != null && !proxies.Contains(source) && !orphans.Contains(source)) orphans.Add(source);
                foreach (var candidate in token.Orphans)
                    if (candidate != null && !proxies.Contains(candidate) && !orphans.Contains(candidate)) orphans.Add(candidate);
            }
            foreach (var pool in pools) pool.RemoveAll(node => proxies.Contains(node));
            foreach (var proxy in proxies) proxy.Clear();
            tokens.RemoveAll(token => token != null && obsolete.Contains(token));
            Debug.LogWarning($"[GraphKit] 工作副本已移除 {obsolete.Count} 個過時 Token、斷開 {disconnected} 個引用；來源節點已保留到候選池。請檢查後存檔。");
            return obsolete.Count;
        }
    }
}
