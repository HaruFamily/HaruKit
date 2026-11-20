using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    [CreateAssetMenu(menuName = "HaruKitEvi/Logic/Int Formula Asset", fileName = "EviIntAsset")]
    public sealed class EviIntAsset : FormulaAsset<int, EviIntSlot, EviPack> { }
}
