using System;
using UnityEditor;
using UnityEngine;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_Folder<TPack> : FormulaBase<DefaultAsset, TPack>
    {
    }

    /// <summary>
    /// 資料夾欄位。固定值有兩層：先看指定的資料夾資產，沒有才退回 <see cref="defaultPath"/>。
    /// </summary>
    // 兩層固定值是既有行為：企劃常常只想打一段路徑，而路徑在專案搬動後會失效，
    // 所以保留「資產優先、路徑備援」而不是二選一。
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "HaruFamily.Tools.AssetPipeline", "HaruFamily.Tools.AssetPipeline.Editor", "FormulaAsset_Folder")]
    public class FolderSlot : FormulaSlot<DefaultAsset, Formula_Folder<NullPack>>
    {
        [HideInInspector]
        public string defaultPath = "Assets";

        public FolderSlot()
        {
            defaultPath = "Assets";
        }

        public FolderSlot(DefaultAsset defaultValue) : base(defaultValue)
        {
            defaultPath = string.Empty;
        }

        public FolderSlot(string defaultPath)
        {
            this.defaultPath = string.IsNullOrWhiteSpace(defaultPath) ? "Assets" : defaultPath;
        }

        protected override DefaultAsset Fallback()
        {
            DefaultAsset folder = ValidateFolder(_default, "FolderSlot 固定值");
            if (folder != null) return folder;

            return LoadFolderAtPath(defaultPath);
        }

        /// <summary>求值後直接取路徑字串。取不到資料夾時回空字串。</summary>
        public string EvaluatePath()
        {
            return GetFolderPath(Evaluate());
        }

        public static DefaultAsset LoadFolderAtPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string normalized = path.Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                AssetPipeline.ReportFormulaWarning($"FolderSlot 找不到資料夾：{path}");
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
            DefaultAsset validFolder = ValidateFolder(folder, "FolderSlot");
            if (validFolder == null) return string.Empty;

            return AssetDatabase.GetAssetPath(validFolder);
        }
    }
}
