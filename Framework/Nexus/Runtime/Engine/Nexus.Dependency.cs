using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace PinPlugin.Nexus
{
    // 依賴圖（診斷用，best-effort）：服務依賴邊（只記 OnInitialize 同步段內解析到的）+ owner→child 擁有邊。匯出 Graphviz DOT。
    public partial class Nexus
    {
        private void AddDep(int from, int to)
        {
            if (!_deps.TryGetValue(from, out var set)) { set = new HashSet<int>(); _deps[from] = set; }
            set.Add(to);
        }

        /// <summary>
        /// 目前依賴圖快照：服務依賴邊（OnInitialize 同步段內解析到的）+ owner→local child 擁有邊。
        /// best-effort：在真正 async（讓出 scheduler）之後才解析的依賴不會被記錄（與環偵測同源限制）。
        /// </summary>
        public IReadOnlyList<NexusEdge> GetDependencyGraph()
        {
            var edges = new List<NexusEdge>();
            foreach (var kv in _deps)
            {
                if (!_idToKey.TryGetValue(kv.Key, out var fk)) continue;
                foreach (var to in kv.Value)
                    if (_idToKey.TryGetValue(to, out var tk))
                        edges.Add(new NexusEdge(fk.ComponentType, tk.ComponentType, false));
            }
            foreach (var kv in _instances)
            {
                if (kv.Value is INexusContainer c && c.LocalChildren != null && _idToKey.TryGetValue(kv.Key, out var ok))
                    foreach (var childId in c.LocalChildren)
                        if (_idToKey.TryGetValue(childId, out var ck))
                            edges.Add(new NexusEdge(ok.ComponentType, ck.ComponentType, true));
            }
            return edges;
        }

        /// <summary>
        /// id 層級的服務節點快照（供 Runtime 視覺化視窗組 owner→child 樹）。
        /// 涵蓋活著的實例（_instances）與建立中的 pending（_idToKey 在 await 前已寫入，故撈得到）；
        /// 已釋放但保留 key 的 stale id（在 _idToKey 卻不在 _instances/_pending）會被略過。
        /// 回 copy、不持有實例參考。best-effort：反映呼叫當下狀態。
        /// </summary>
        public IReadOnlyList<NexusNode> GetNodeSnapshot()
        {
            var nodes = new List<NexusNode>(_instances.Count + _pending.Count);
            foreach (var kv in _idToKey)
            {
                var id = kv.Key;
                var key = kv.Value;
                var active = _instances.TryGetValue(id, out var inst);
                var pending = _pending.ContainsKey(id);
                if (!active && !pending) continue;   // stale 保留 key，無實例也非建立中

                nodes.Add(new NexusNode(
                    id,
                    key.ComponentType?.Name ?? "<null>",
                    key.IdentityKey,
                    key.ParentId,
                    isPending: pending && !active,
                    isContainer: inst is INexusContainer,
                    isPrefabMono: inst != null && _prefabOwned.Contains(inst),
                    isScriptable: inst != null && _scriptableOwned.Contains(inst),
                    isPoolable: inst is INexusPoolable));
            }
            return nodes;
        }

        /// <summary>把依賴圖匯出成 Graphviz DOT（貼到 dot 工具即可視覺化）。</summary>
        public string ToDot()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("digraph Nexus {");
            foreach (var e in GetDependencyGraph())
                sb.AppendLine($"  \"{e.From?.Name}\" -> \"{e.To?.Name}\"{(e.IsOwnership ? " [style=dashed,label=owns]" : "")};");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
