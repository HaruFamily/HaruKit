using UnityEngine;

namespace HaruKitEvi.Fixtures.Plain
{
    /// <summary>
    /// 不經 LogicGraph／AssetPipeline 的最小 GraphKit Owner：一個 IGraphDocument 欄位。
    /// 用來單獨測編輯器本身，不排程、不求值。
    /// </summary>
    [CreateAssetMenu(menuName = "HaruKitEvi/Plain/Graph Owner", fileName = "PlainGraphOwner")]
    public sealed class PlainGraphOwner : ScriptableObject
    {
        [SerializeField] private PlainDocument document = new PlainDocument();

        public PlainDocument Document => document;

        /// <summary>換成一張空圖（重置範例用）。</summary>
        public void ResetDocument() => document = new PlainDocument();
    }
}
