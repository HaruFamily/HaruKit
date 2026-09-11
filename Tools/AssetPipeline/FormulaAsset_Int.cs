using System;
using System.Collections.Generic;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Int : IFormula<int>
    {
        public abstract int Caculate();
    }

    [Serializable]
    public class Formula_Int_ObjectsCount : Formula_Int
    {
        public FormulaAsset_ObjectList objects = new FormulaAsset_ObjectList();

        public override int Caculate()
        {
            List<UnityEngine.Object> sourceObjects = objects?.Caculate();
            return sourceObjects?.Count ?? 0;
        }
    }

    [Serializable]
    public class FormulaAsset_Int : FormulaAsset<int, Formula_Int>
    {
        public FormulaAsset_Int()
        {
        }

        public FormulaAsset_Int(int @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListInt : IFormula<List<int>>
    {
        public abstract List<int> Caculate();
    }

    [Serializable]
    public class FormulaAsset_ListInt : FormulaAsset<List<int>, Formula_ListInt>
    {
        public FormulaAsset_ListInt()
        {
            @default = new List<int>();
        }

        public FormulaAsset_ListInt(List<int> @default) : base(@default)
        {
        }
    }
}
