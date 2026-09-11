using System;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_AddressableGroup : IFormula<AddressableAssetGroup>
    {
        public abstract AddressableAssetGroup Caculate();
    }

    [Serializable]
    public class Formula_AddressableGroup_ByName : Formula_AddressableGroup
    {
        public enum MissingGroupMode
        {
            Stop,
            Create
        }

        public FormulaAsset_String groupName = new FormulaAsset_String("Default");

        public MissingGroupMode missingMode = MissingGroupMode.Create;

        public override AddressableAssetGroup Caculate()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AssetPipeline.ReportFormulaWarning("Formula_AddressableGroup_ByName 找不到 Addressable Settings");
                return null;
            }

            string name = groupName?.Caculate();
            name = string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim();

            AddressableAssetGroup group = settings.FindGroup(name);
            if (group != null) return group;
            if (missingMode == MissingGroupMode.Stop)
            {
                AssetPipeline.ReportFormulaWarning($"Formula_AddressableGroup_ByName 找不到 Addressable Group [{name}]");
                return null;
            }

            return settings.CreateGroup(name, false, false, false, null,
                typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
        }
    }

    [Serializable]
    public class FormulaAsset_AddressableGroup : FormulaAsset_Asset<AddressableAssetGroup, Formula_AddressableGroup>
    {
        public FormulaAsset_AddressableGroup()
        {
            formula = new Formula_AddressableGroup_ByName();
        }

        public FormulaAsset_AddressableGroup(AddressableAssetGroup @default) : base(@default)
        {
        }
    }
}
