using System;
using System.Collections.Generic;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.Tools.AssetPipeline.Tests
{
    public sealed class LibraryOrderTests
    {
        [Test]
        public void TokenViewPreservesStoredOrderAndReferencesAcrossRename()
        {
            var z = new GraphToken("Z", null);
            var a = new GraphToken("A", null);
            var source = new List<GraphToken> { z, null, a };
            Assert.That(HGModel.ReadTokens(source).Select(item => item.Token), Is.EqualTo(new[] { z, a }));
            z.Name = "0";
            a.Name = "-1";
            Assert.That(HGModel.ReadTokens(source).Select(item => item.Token), Is.EqualTo(new[] { z, a }));
            Assert.That(source, Is.EqualTo(new[] { z, null, a }));
        }

        [Test]
        public void AssetOrderSurvivesRefreshRenameAndNewAssets()
        {
            string folder = "Assets/GraphKitOrderTests_" + Guid.NewGuid().ToString("N");
            string folderKey = $"HaruGraph.AssetFolder.{Application.dataPath.GetHashCode():X8}";
            bool hadFolder = EditorPrefs.HasKey(folderKey);
            string previousFolder = EditorPrefs.GetString(folderKey, "");
            string key = "HaruGraph.AssetLib.Order." + Application.dataPath + ":" + folder;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                HGAssetStore.Folder = folder;
                var a = CreateAsset(folder, "A");
                var b = CreateAsset(folder, "B");
                var c = CreateAsset(folder, "C");
                HGAssetIndex.Refresh();
                Assert.That(HGAssetIndex.Move(a, c), Is.True);
                HGAssetIndex.Refresh();
                AssertAssets(b, c, a);
                Assert.That(AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(a), "00"), Is.Empty);
                HGAssetIndex.Refresh();
                AssertAssets(b, c, a);
                var added = CreateAsset(folder, "0-new");
                HGAssetIndex.Refresh();
                AssertAssets(b, c, a, added);
                Assert.That(HGAssetIndex.Move(a, b), Is.True);
                HGAssetIndex.Refresh();
                AssertAssets(a, b, c, added);

                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(c));
                HGAssetIndex.Refresh();
                AssertAssets(a, b, added);
            }
            finally
            {
                if (hadFolder) EditorPrefs.SetString(folderKey, previousFolder);
                else EditorPrefs.DeleteKey(folderKey);
                EditorPrefs.DeleteKey(key);
                AssetDatabase.DeleteAsset(folder);
                HGAssetIndex.Refresh();
            }
        }

        private static LibraryOrderTestAsset CreateAsset(string folder, string name)
        {
            var asset = ScriptableObject.CreateInstance<LibraryOrderTestAsset>();
            AssetDatabase.CreateAsset(asset, folder + "/" + name + ".asset");
            return asset;
        }

        private static void AssertAssets(params LibraryOrderTestAsset[] assets)
            => Assert.That(HGAssetIndex.Entries.Select(entry => entry.Asset), Is.EqualTo(assets));
    }
}
