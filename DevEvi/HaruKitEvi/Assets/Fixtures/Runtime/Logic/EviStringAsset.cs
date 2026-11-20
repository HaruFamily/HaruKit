using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    [CreateAssetMenu(menuName = "HaruKitEvi/Logic/Text Formula Asset", fileName = "EviStringAsset")]
    public sealed class EviStringAsset : FormulaAsset<string, EviStringSlot, EviPack> { }
}
