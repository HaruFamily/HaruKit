using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    [CreateAssetMenu(menuName = "HaruKitEvi/Logic/Bool Formula Asset", fileName = "EviBoolAsset")]
    public sealed class EviBoolAsset : FormulaAsset<bool, EviBoolSlot, EviPack> { }
}
