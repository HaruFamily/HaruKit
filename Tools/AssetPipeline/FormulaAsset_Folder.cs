using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Folder : IFormula<DefaultAsset>
    {
        public abstract DefaultAsset Caculate();
    }

    [Serializable]
    public class Formula_FolderPath : Formula_Folder
    {
        public string folder = "Assets";

        public override DefaultAsset Caculate()
        {
            return FormulaAsset_Folder.LoadFolderAtPath(folder);
        }
    }

    [Serializable]
    public class Formula_FolderAsset : Formula_Folder
    {
        public DefaultAsset folder;

        public override DefaultAsset Caculate()
        {
            return FormulaAsset_Folder.ValidateFolder(folder, "Formula_FolderAsset");
        }
    }

    [Serializable]
    public class FormulaAsset_Folder
    {
        public enum FolderData
        {
            None,
            Formula,
            AssetSource
        }

        [FormerlySerializedAs("data")]
        public FolderData assetData = FolderData.Formula;

        public DefaultAsset @default;

        [HideInInspector]
        public string defaultPath = "Assets";

        [SerializeReference]
        public Formula_Folder formula = new Formula_FolderPath();

        public AssetPipelineSource assetSource = new AssetPipelineSource();

        public FormulaAsset_Folder()
        {
            defaultPath = "Assets";
        }

        public FormulaAsset_Folder(DefaultAsset @default)
        {
            this.@default = @default;
            defaultPath = string.Empty;
            assetData = FolderData.None;
        }

        public FormulaAsset_Folder(string defaultPath)
        {
            this.defaultPath = string.IsNullOrWhiteSpace(defaultPath) ? "Assets" : defaultPath;
            assetData = FolderData.None;
        }

        public DefaultAsset Caculate()
        {
            if (assetData == FolderData.None) return GetDefaultFolder();

            if (assetData == FolderData.AssetSource) return CaculateAssetSource();

            if (formula == null)
            {
                AssetPipeline.ReportFormulaWarning("FormulaAsset_Folder 使用 fallback：formula 為空");
                return GetDefaultFolder();
            }

            try
            {
                DefaultAsset folder = ValidateFolder(formula.Caculate(), "FormulaAsset_Folder 公式結果");
                return folder != null ? folder : GetDefaultFolder();
            }
            catch (Exception ex)
            {
                AssetPipeline.ReportFormulaWarning($"FormulaAsset_Folder 使用 fallback：{ex.Message}");
                return GetDefaultFolder();
            }
        }

        public string CaculatePath()
        {
            return GetFolderPath(Caculate());
        }

        private DefaultAsset CaculateAssetSource()
        {
            try
            {
                List<DefaultAsset> folders = assetSource?.GetAssets<DefaultAsset>();
                if (folders == null || folders.Count == 0)
                {
                    AssetPipeline.ReportFormulaWarning("FormulaAsset_Folder 資產來源沒有資料夾資產");
                    return GetDefaultFolder();
                }

                foreach (DefaultAsset folder in folders)
                {
                    DefaultAsset validFolder = ValidateFolder(folder, "FormulaAsset_Folder 資產來源");
                    if (validFolder != null) return validFolder;
                }
            }
            catch (Exception ex)
            {
                AssetPipeline.ReportFormulaWarning($"FormulaAsset_Folder 資產來源使用 fallback：{ex.Message}");
            }

            return GetDefaultFolder();
        }

        private DefaultAsset GetDefaultFolder()
        {
            DefaultAsset folder = ValidateFolder(@default, "FormulaAsset_Folder 固定值");
            if (folder != null) return folder;

            return LoadFolderAtPath(defaultPath);
        }

        public static DefaultAsset LoadFolderAtPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string normalized = path.Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                AssetPipeline.ReportFormulaWarning($"FormulaAsset_Folder 找不到資料夾：{path}");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<DefaultAsset>(normalized);
        }

        public static DefaultAsset ValidateFolder(DefaultAsset folder, string source)
        {
            if (folder == null) return null;

            string path = AssetDatabase.GetAssetPath(folder);
            if (AssetDatabase.IsValidFolder(path)) return folder;

            AssetPipeline.ReportFormulaWarning($"{source} 不是有效資料夾：{path}");
            return null;
        }

        public static string GetFolderPath(DefaultAsset folder)
        {
            DefaultAsset validFolder = ValidateFolder(folder, "FormulaAsset_Folder");
            if (validFolder == null) return string.Empty;

            return AssetDatabase.GetAssetPath(validFolder);
        }

        private bool IsFormulaEnabled()
        {
            return assetData == FolderData.Formula;
        }

        private bool IsAssetSourceMode()
        {
            return assetData == FolderData.AssetSource;
        }
    }
}
