using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    /// <summary>LogicGraph 的測試 Owner：一個序列化欄位，不實作任何 Owner 介面。</summary>
    [CreateAssetMenu(menuName = "HaruKitEvi/Logic/Graph Definition", fileName = "EviGraphDefinition")]
    public sealed class EviGraphDefinition : ScriptableObject
    {
        [SerializeField] private LogicGraph<EviTiming, EviPack> graph = new LogicGraph<EviTiming, EviPack>();

        /// <summary>範本圖本身。只給建立範例的 Editor 程式使用；執行請用 <see cref="CreateRuntimeGraph"/>。</summary>
        public LogicGraph<EviTiming, EviPack> Graph => graph;

        public LogicGraph<EviTiming, EviPack> CreateRuntimeGraph() => graph.DeepCopy();

        /// <summary>換成一張空圖（重置範例用）。</summary>
        public void ResetGraph() => graph = new LogicGraph<EviTiming, EviPack>();
    }
}
