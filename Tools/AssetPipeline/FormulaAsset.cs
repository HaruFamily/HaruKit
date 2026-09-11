using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline
{
    public interface IFormula<T>
    {
        T Caculate();
    }

    [Serializable]
    public abstract class FormulaAssetBase<T, TFormula> where TFormula : IFormula<T>
    {
        [FormerlySerializedAs("value")]
        public T @default;

        [SerializeReference]
        public TFormula formula;

        protected FormulaAssetBase()
        {
        }

        protected FormulaAssetBase(T @default)
        {
            this.@default = @default;
        }

        public abstract T Caculate();

        protected T CaculateFormulaOrDefault()
        {
            if (formula == null)
            {
                ReportFallback("formula 為空");
                return @default;
            }

            try
            {
                return formula.Caculate();
            }
            catch (Exception ex)
            {
                ReportFallback(ex.Message);
                return @default;
            }
        }

        protected void ReportFallback(string reason)
        {
            AssetPipeline.ReportFormulaWarning($"{GetType().Name} 使用 fallback：{reason}");
        }

        protected virtual bool IsFormulaEnabled()
        {
            return true;
        }
    }

    public enum FormulaAssetData
    {
        None,
        Formula
    }

    [Serializable]
    public class FormulaAsset<T, TFormula> : FormulaAssetBase<T, TFormula> where TFormula : IFormula<T>
    {
        public FormulaAssetData data = FormulaAssetData.Formula;

        protected FormulaAsset()
        {
        }

        protected FormulaAsset(T @default) : base(@default)
        {
            data = FormulaAssetData.None;
        }

        public override T Caculate()
        {
            if (data == FormulaAssetData.None) return @default;
            return CaculateFormulaOrDefault();
        }

        protected override bool IsFormulaEnabled()
        {
            return data == FormulaAssetData.Formula;
        }
    }

    [Serializable]
    public class FormulaAsset_Asset<T, TFormula> : FormulaAssetBase<T, TFormula> where T : Object where TFormula : IFormula<T>
    {
        public enum AssetData
        {
            None,
            Formula,
            AssetSource
        }

        public AssetData assetData = AssetData.Formula;

        public AssetPipelineSource assetSource = new AssetPipelineSource();

        protected FormulaAsset_Asset()
        {
        }

        protected FormulaAsset_Asset(T @default) : base(@default)
        {
            assetData = AssetData.None;
        }

        public override T Caculate()
        {
            if (assetData == AssetData.None) return @default;
            if (assetData == AssetData.Formula) return CaculateFormulaOrDefault();

            try
            {
                List<T> assets = assetSource?.GetAssets<T>();
                if (assets == null || assets.Count == 0) return @default;
                return assets[0];
            }
            catch (Exception ex)
            {
                ReportFallback(ex.Message);
                return @default;
            }
        }

        protected override bool IsFormulaEnabled()
        {
            return assetData == AssetData.Formula;
        }

        private bool IsAssetSourceMode()
        {
            return assetData == AssetData.AssetSource;
        }
    }

    [Serializable]
    public class FormulaAsset_AssetList<TAsset, TFormula> : FormulaAssetBase<List<TAsset>, TFormula> where TAsset : Object where TFormula : IFormula<List<TAsset>>
    {
        public enum AssetData
        {
            None,
            Formula,
            AssetSource
        }

        public AssetData assetData = AssetData.Formula;

        public AssetPipelineSource assetSource = new AssetPipelineSource();

        protected FormulaAsset_AssetList()
        {
            @default = new List<TAsset>();
        }

        protected FormulaAsset_AssetList(List<TAsset> @default) : base(@default)
        {
            assetData = AssetData.None;
        }

        public override List<TAsset> Caculate()
        {
            if (assetData == AssetData.None) return @default;
            if (assetData == AssetData.Formula) return CaculateFormulaOrDefault();

            try
            {
                return assetSource?.GetAssets<TAsset>() ?? @default;
            }
            catch (Exception ex)
            {
                ReportFallback(ex.Message);
                return @default;
            }
        }

        protected override bool IsFormulaEnabled()
        {
            return assetData == AssetData.Formula;
        }

        private bool IsAssetSourceMode()
        {
            return assetData == AssetData.AssetSource;
        }
    }
}
