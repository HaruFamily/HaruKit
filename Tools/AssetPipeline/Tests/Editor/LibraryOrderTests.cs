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
        public void CatalogMovesKeepIdentityAndHaveIndependentUndoRedoSteps()
        {
            var pipeline = ScriptableObject.CreateInstance<AssetPipeline>();
            var asset = new Texture2D(1, 1);
            try
            {
                var owner = (ICatalogOwner)pipeline;
                var order = (IReorderableCatalogOwner)pipeline;
                string first = owner.CreateCatalog().Id;
                string hidden = owner.CreateCatalog().Id;
                string last = owner.CreateCatalog().Id;
                pipeline.FindCatalogById(first).assets.AddRange(new Object[] { asset, null, asset });
                var model = new HGModel();
                Assert.That(model.Bind(pipeline), Is.True);
                var document = model.Data;

                object before = model.CaptureCatalogs();
                Assert.That(order.MoveCatalog(first, last), Is.True);
                model.PushCatalogStep(before, false);
                Assert.That(owner.Catalogs.Select(item => item.Id), Is.EqualTo(new[] { hidden, last, first }));
                Assert.That(pipeline.FindCatalogById(first).assets[0], Is.SameAs(asset));

                before = model.CaptureCatalogs();
                Assert.That(order.MoveCatalogItem(first, 1, 0), Is.True);
                model.PushCatalogStep(before, false);
                Assert.That(pipeline.FindCatalogById(first).assets, Is.EqualTo(new Object[] { null, asset, asset }));
                Assert.That(model.Undo(), Is.EqualTo(HGStepKind.Catalogs));
                Assert.That(owner.Catalogs.Select(item => item.Id), Is.EqualTo(new[] { hidden, last, first }));
                Assert.That(pipeline.FindCatalogById(first).assets, Is.EqualTo(new Object[] { asset, null, asset }));
                Assert.That(model.Undo(), Is.EqualTo(HGStepKind.Catalogs));
                Assert.That(owner.Catalogs.Select(item => item.Id), Is.EqualTo(new[] { first, hidden, last }));
                Assert.That(model.Redo(), Is.EqualTo(HGStepKind.Catalogs));
                Assert.That(model.Redo(), Is.EqualTo(HGStepKind.Catalogs));
                Assert.That(pipeline.FindCatalogById(first).assets, Is.EqualTo(new Object[] { null, asset, asset }));
                Assert.That(model.Data, Is.SameAs(document));
                Assert.That(model.Dirty, Is.False);

                Assert.That(order.MoveCatalog(first, first), Is.False);
                Assert.That(order.MoveCatalog("missing", first), Is.False);
                Assert.That(order.MoveCatalogItem(first, -1, 0), Is.False);
                Assert.That(order.MoveCatalogItem(first, 0, 3), Is.False);
                Assert.That(order.MoveCatalogItem(first, 1, 1), Is.False);
                Assert.That(order.MoveCatalogItem("missing", 0, 1), Is.False);
                Assert.That(pipeline.FindCatalogById(first).assets, Is.EqualTo(new Object[] { null, asset, asset }));
            }
            finally
            {
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AssetOrderSurvivesRefreshRenameAndNewAssetsAndUpdatesCatalogMetadata()
        {
            string folder = "Assets/GraphKitOrderTests_" + Guid.NewGuid().ToString("N");
            string folderKey = $"HaruGraph.AssetFolder.{Application.dataPath.GetHashCode():X8}";
            bool hadFolder = EditorPrefs.HasKey(folderKey);
            string previousFolder = EditorPrefs.GetString(folderKey, "");
            string key = "HaruGraph.AssetLib.Order." + Application.dataPath + ":" + folder;
            var pipeline = ScriptableObject.CreateInstance<AssetPipeline>();
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

                var owner = (ICatalogOwner)pipeline;
                string id = owner.CreateCatalog().Id;
                Assert.That(owner.AddToCatalog(id, new object[] { a, b, c }), Is.EqualTo(3));
                Assert.That(((IReorderableCatalogOwner)pipeline).MoveCatalogItem(id, 0, 2), Is.True);
                var group = pipeline.FindCatalogById(id);
                Assert.That(group.assets, Is.EqualTo(new Object[] { b, c, a }));
                Assert.That(group.assetInfos.Select(item => item.guid),
                    Is.EqualTo(group.assets.Select(item => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)))));
                Assert.That(group.typeGroups[0].assets, Is.EqualTo(group.assets));

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
                Object.DestroyImmediate(pipeline);
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
