using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    /// <summary>可重用的動作資產，用來測共用資產的下鑽、參數綁定與引用清單。</summary>
    [CreateAssetMenu(menuName = "HaruKitEvi/Logic/Action Asset", fileName = "EviActionAsset")]
    public sealed class EviActionAsset : ActionAssetBase<EviPack> { }
}
