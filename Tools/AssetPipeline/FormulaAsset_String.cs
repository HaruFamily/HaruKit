using System;
using System.Collections.Generic;
using UnityEditor;

namespace HaruFamily.Tools.AssetPipeline
{
    [Serializable]
    public abstract class Formula_String : IFormula<string>
    {
        public abstract string Caculate();
    }

    [Serializable]
    public class Formula_String_FolderPath : Formula_String
    {
        public string folder = "Assets";

        public override string Caculate()
        {
            return folder ?? string.Empty;
        }
    }

    [Serializable]
    public class Formula_String_Folder : Formula_String
    {
        public FormulaAsset_Folder folder = new FormulaAsset_Folder("Assets");

        public override string Caculate()
        {
            DefaultAsset folderAsset = folder?.Caculate();
            return FormulaAsset_Folder.GetFolderPath(folderAsset);
        }
    }

    [Serializable]
    public class FormulaAsset_String : FormulaAsset<string, Formula_String>
    {
        public FormulaAsset_String()
        {
            @default = string.Empty;
        }

        public FormulaAsset_String(string @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_ListString : IFormula<List<string>>
    {
        public abstract List<string> Caculate();
    }

    [Serializable]
    public class FormulaAsset_ListString : FormulaAsset<List<string>, Formula_ListString>
    {
        public FormulaAsset_ListString()
        {
            @default = new List<string>();
        }

        public FormulaAsset_ListString(List<string> @default) : base(@default)
        {
        }
    }

    [Serializable]
    public abstract class Formula_StringList : Formula_ListString
    {
    }

    [Serializable]
    public class Formula_StringList_ObjectsName : Formula_StringList
    {
        public FormulaAsset_ObjectList objects = new FormulaAsset_ObjectList();

        public override List<string> Caculate()
        {
            var result = new List<string>();
            List<UnityEngine.Object> sourceObjects = objects?.Caculate();
            if (sourceObjects == null) return result;

            foreach (UnityEngine.Object obj in sourceObjects)
            {
                if (obj == null) continue;
                result.Add(obj.name);
            }

            return result;
        }
    }

    [Serializable]
    public class FormulaAsset_StringList : FormulaAsset<List<string>, Formula_StringList>
    {
        public FormulaAsset_StringList()
        {
            @default = new List<string>();
        }

        public FormulaAsset_StringList(List<string> @default) : base(@default)
        {
        }
    }
}
