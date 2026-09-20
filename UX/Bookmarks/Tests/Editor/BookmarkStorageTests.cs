#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks.Tests
{
    public class BookmarkStorageTests
    {
        private string directory;
        private string path;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "HaruBookmarksTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "bookmarks.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void Flush_CreatesThenReplacesFile_AndRoundTripsFoldersAndHistory()
        {
            var storage = new BookmarkFileStorage(path);
            Assert.That(storage.Load(), Is.True);
            storage.Data.bookmarks.Add(new ObjectRef { guid = "asset-a", instanceId = 10, folder = "Art" });
            storage.Data.folders.Add(new FolderInfo { name = "Art", fold = false });
            storage.MarkDirty();
            Assert.That(storage.Flush(), Is.True, storage.LastError);

            storage.Data.history.Add(new ObjectRef { guid = "asset-b", instanceId = 20 });
            storage.Data.index = 0;
            storage.MarkDirty();
            Assert.That(storage.Flush(), Is.True, storage.LastError);
            Assert.That(storage.IsDirty, Is.False);
            Assert.That(File.Exists(path + ".tmp"), Is.False);

            var loaded = new BookmarkFileStorage(path);
            Assert.That(loaded.Load(), Is.True, loaded.LastError);
            Assert.That(loaded.Data.bookmarks[0].guid, Is.EqualTo("asset-a"));
            Assert.That(loaded.Data.bookmarks[0].folder, Is.EqualTo("Art"));
            Assert.That(loaded.Data.folders[0].fold, Is.False);
            Assert.That(loaded.Data.history[0].guid, Is.EqualTo("asset-b"));
            Assert.That(loaded.Data.index, Is.Zero);
        }

        [TestCase("")]
        [TestCase("not valid JSON")]
        public void Load_CorruptFile_BlocksWritesAndPreservesBytes(string original)
        {
            File.WriteAllText(path, original);
            var storage = new BookmarkFileStorage(path);
            Assert.That(storage.Load(), Is.False);
            Assert.That(storage.IsWriteBlocked, Is.True);
            StringAssert.Contains(path, storage.LastError);
            storage.Data.bookmarks.Add(new ObjectRef { guid = "must-not-save" });
            storage.MarkDirty();
            Assert.That(storage.Flush(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [Test]
        public void Flush_WriteFailure_PreservesOriginalAndDirtyState_ThenRetries()
        {
            string original = JsonUtility.ToJson(new InspectorJData());
            File.WriteAllText(path, original);
            var storage = new BookmarkFileStorage(path);
            Assert.That(storage.Load(), Is.True);
            storage.Data.bookmarks.Add(new ObjectRef { guid = "pending" });
            storage.MarkDirty();
            Directory.CreateDirectory(path + ".tmp");

            Assert.That(storage.Flush(), Is.False);
            Assert.That(storage.IsDirty, Is.True);
            Assert.That(storage.IsWriteBlocked, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            StringAssert.Contains(path, storage.LastError);

            Directory.Delete(path + ".tmp");
            Assert.That(storage.Flush(), Is.True, storage.LastError);
            Assert.That(storage.IsDirty, Is.False);
            Assert.That(storage.LastError, Is.Null);
            StringAssert.Contains("pending", File.ReadAllText(path));
        }

        [Test]
        public void Load_LegacyFile_CopiesValidatedDataWithoutRemovingSource()
        {
            string legacy = Path.Combine(directory, "legacy.json");
            var data = new InspectorJData();
            data.bookmarks.Add(new ObjectRef { guid = "legacy-bookmark" });
            string original = JsonUtility.ToJson(data);
            File.WriteAllText(legacy, original);

            var storage = new BookmarkFileStorage(path, legacy);
            Assert.That(storage.Load(), Is.True, storage.LastError);
            Assert.That(storage.Data.bookmarks[0].guid, Is.EqualTo("legacy-bookmark"));
            Assert.That(File.ReadAllText(legacy), Is.EqualTo(original));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [Test]
        public void Load_CorruptLegacyFile_DoesNotCreateReplacement()
        {
            string legacy = Path.Combine(directory, "legacy.json");
            File.WriteAllText(legacy, "corrupt legacy data");
            var storage = new BookmarkFileStorage(path, legacy);
            Assert.That(storage.Load(), Is.False);
            storage.MarkDirty();
            Assert.That(storage.Flush(), Is.False);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(File.ReadAllText(legacy), Is.EqualTo("corrupt legacy data"));
        }

        [Test]
        public void Load_MigrationFailure_BlocksWritesAndPreservesLegacy()
        {
            string legacy = Path.Combine(directory, "legacy.json");
            string original = JsonUtility.ToJson(new InspectorJData());
            File.WriteAllText(legacy, original);
            Directory.CreateDirectory(path);
            var storage = new BookmarkFileStorage(path, legacy);
            Assert.That(storage.Load(), Is.False);
            Assert.That(storage.IsWriteBlocked, Is.True);
            Assert.That(storage.Flush(), Is.False);
            Assert.That(File.ReadAllText(legacy), Is.EqualTo(original));
        }
    }
}
#endif
