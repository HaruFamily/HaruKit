#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

namespace HaruFamily.UX.Bookmarks.Tests
{
    public class BookmarkLogicTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void Reorder_SameFolder_PreservesOtherFolderSlots(bool backwards)
        {
            var a = new ObjectRef { guid = "a", folder = "Art" };
            var b = new ObjectRef { guid = "b", folder = "Art" };
            var c = new ObjectRef { guid = "c", folder = "Art" };
            var x = new ObjectRef { guid = "x", folder = "Other" };
            var y = new ObjectRef { guid = "y", folder = "Other" };
            var list = new List<ObjectRef> { a, x, b, y, c };

            Assert.That(BookmarkService.ReorderBookmarks(list, backwards ? c : a, backwards ? a : c), Is.True);
            CollectionAssert.AreEqual(backwards ? new[] { c, x, a, y, b } : new[] { b, x, c, y, a }, list);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Reorder_CrossFolder_InsertsBeforeTargetInEitherDirection(bool backwards)
        {
            var source = new ObjectRef { guid = "source", folder = "Other" };
            var first = new ObjectRef { guid = "first", folder = "Art" };
            var target = new ObjectRef { guid = "target", folder = "Art" };
            var list = backwards ? new List<ObjectRef> { first, target, source } : new List<ObjectRef> { source, first, target };

            Assert.That(BookmarkService.ReorderBookmarks(list, source, target), Is.True);
            CollectionAssert.AreEqual(new[] { first, source, target }, list);
            Assert.That(source.folder, Is.EqualTo("Art"));
        }

        [Test]
        public void Reorder_FilteredTarget_UsesReferenceAndKeepsHiddenItems()
        {
            var source = new ObjectRef { guid = "match-1", folder = "Art" };
            var hidden = new ObjectRef { guid = "hidden", folder = "Art" };
            var target = new ObjectRef { guid = "match-2", folder = "Art" };
            var list = new List<ObjectRef> { source, hidden, target };

            Assert.That(BookmarkService.ReorderBookmarks(list, source, target), Is.True);
            CollectionAssert.AreEqual(new[] { hidden, target, source }, list);
        }

        [Test]
        public void Reorder_RemovedOrSameItem_DoesNotMutateList()
        {
            var a = new ObjectRef { guid = "a" };
            var b = new ObjectRef { guid = "b" };
            var removed = new ObjectRef { guid = "removed" };
            var list = new List<ObjectRef> { a, b };
            Assert.That(BookmarkService.ReorderBookmarks(list, removed, b), Is.False);
            Assert.That(BookmarkService.ReorderBookmarks(list, a, removed), Is.False);
            Assert.That(BookmarkService.ReorderBookmarks(list, a, a), Is.False);
            Assert.That(BookmarkService.ReorderBookmarks(list, null, b), Is.False);
            CollectionAssert.AreEqual(new[] { a, b }, list);
        }

        [Test]
        public void Reorder_NullFolderAndEmptyFolder_AreSameGroup()
        {
            var a = new ObjectRef { guid = "a", folder = null };
            var x = new ObjectRef { guid = "x", folder = "Other" };
            var b = new ObjectRef { guid = "b", folder = string.Empty };
            var list = new List<ObjectRef> { a, x, b };
            Assert.That(BookmarkService.ReorderBookmarks(list, a, b), Is.True);
            CollectionAssert.AreEqual(new[] { b, x, a }, list);
        }

        [TestCase("[Asset] Brick (Material)", "Art", "BRICK", true)]
        [TestCase("[Asset] Brick (Material)", "Art", "Art", true)]
        [TestCase("[Asset] Brick (Material)", "Art", "Ar", false)]
        [TestCase("[Asset] Brick (Material)", "Art", "art", false)]
        [TestCase("[Asset] Brick (Material)", null, "Art", false)]
        [TestCase(null, "Art", "Art", false)]
        [TestCase(null, "Art", "", false)]
        [TestCase("[Asset] Brick (Material)", "Art", "", true)]
        public void Search_UsesVisibleLabelOrExactFolder_AndExcludesMissingObjects(string label, string folder, string keyword, bool expected)
        {
            Assert.That(BookmarksGUI.MatchesSearch(label, folder, keyword), Is.EqualTo(expected));
        }

        [Test]
        public void EqualsItem_SubAssetsOfSameFile_AreDistinguishedByGlobalId()
        {
            var spriteA = new ObjectRef { guid = "texture", globalId = "GlobalObjectId_V1-1-texture-111-0", instanceId = 1 };
            var spriteB = new ObjectRef { guid = "texture", globalId = "GlobalObjectId_V1-1-texture-222-0", instanceId = 2 };
            var spriteAAgain = new ObjectRef { guid = "texture", globalId = spriteA.globalId };
            Assert.That(BookmarkService.EqualsItem(spriteA, spriteB), Is.False);
            Assert.That(BookmarkService.EqualsItem(spriteA, spriteAAgain), Is.True);
        }

        [Test]
        public void EqualsItem_SessionOnlyRefs_UseNonZeroInstanceId()
        {
            Assert.That(BookmarkService.EqualsItem(new ObjectRef { instanceId = 5 }, new ObjectRef { instanceId = 5 }), Is.True);
            Assert.That(BookmarkService.EqualsItem(new ObjectRef { instanceId = 5 }, new ObjectRef { instanceId = 6 }), Is.False);
            Assert.That(BookmarkService.EqualsItem(new ObjectRef(), new ObjectRef()), Is.False);
        }

        [Test]
        public void EqualsItem_LegacyRefWithoutGlobalId_FallsBackToGuid()
        {
            var legacy = new ObjectRef { guid = "asset" };
            var current = new ObjectRef { guid = "asset", globalId = "GlobalObjectId_V1-1-asset-1-0", instanceId = 9 };
            Assert.That(BookmarkService.EqualsItem(legacy, current), Is.True);
            Assert.That(BookmarkService.EqualsItem(legacy, new ObjectRef { guid = "other" }), Is.False);
        }

        [Test]
        public void IsSessionOnly_OnlyWhenNoGlobalIdAndNoGuid()
        {
            Assert.That(BookmarkService.IsSessionOnly(new ObjectRef { instanceId = 3 }), Is.True);
            Assert.That(BookmarkService.IsSessionOnly(new ObjectRef { guid = "asset" }), Is.False);
            Assert.That(BookmarkService.IsSessionOnly(new ObjectRef { globalId = "GlobalObjectId_V1-2-scene-1-0" }), Is.False);
        }

        // 游標指向 index 位置的項目；移除後應仍指向同一筆，該筆被移除則退到前一筆仍存在的項目。
        [TestCase(new[] { "a", "b", "c", "d" }, 2, "a", 1, "c")]
        [TestCase(new[] { "a", "b", "c", "d" }, 2, "d", 2, "c")]
        [TestCase(new[] { "a", "b", "c", "d" }, 2, "c", 1, "b")]
        [TestCase(new[] { "a", "b", "c", "d" }, 0, "a", 0, "b")]
        [TestCase(new[] { "a", "b", "c" }, 1, "abc", -1, null)]
        public void RemoveAllKeepCursor_KeepsCursorOnSameItem(string[] names, int index, string removeNames, int expectedIndex, string expectedCurrent)
        {
            var list = new List<ObjectRef>();
            foreach (var name in names) list.Add(new ObjectRef { guid = name });

            int removed = BookmarkService.RemoveAllKeepCursor(list, ref index, x => removeNames.Contains(x.guid));

            Assert.That(removed, Is.EqualTo(names.Length - list.Count));
            Assert.That(index, Is.EqualTo(expectedIndex));
            if (expectedCurrent != null) Assert.That(list[index].guid, Is.EqualTo(expectedCurrent));
        }

        private static List<ObjectRef> Refs(params string[] guids)
        {
            var list = new List<ObjectRef>();
            foreach (var guid in guids) list.Add(new ObjectRef { guid = guid });
            return list;
        }

        // 改小上限時游標要跟著原本那筆，不能直接跳到最後一筆。
        [TestCase(5, 3, 3, 1, "d")]
        [TestCase(5, 1, 3, 0, "c")]
        [TestCase(5, 4, 5, 4, "e")]
        public void TrimToLimit_RemovesOldestAndKeepsCursor(int count, int index, int max, int expectedIndex, string expectedCurrent)
        {
            var list = Refs("a", "b", "c", "d", "e").GetRange(0, count);
            int removed = BookmarkService.TrimToLimit(list, ref index, max);
            Assert.That(removed, Is.EqualTo(System.Math.Max(0, count - max)));
            Assert.That(list.Count, Is.EqualTo(System.Math.Min(count, max)));
            Assert.That(index, Is.EqualTo(expectedIndex));
            Assert.That(list[index].guid, Is.EqualTo(expectedCurrent));
        }

        [Test]
        public void CollectLatestDistinct_KeepsLatestOccurrenceInChronologicalOrder()
        {
            var list = Refs("a", "b", "a", "c", "b");
            var result = new List<int>();
            BookmarkService.CollectLatestDistinct(list, result);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, result);
        }

        [TestCase("Player Controller (MonoScript)", "player mono", true)]
        [TestCase("Player Controller (MonoScript)", "player enemy", false)]
        [TestCase("Player Controller (MonoScript)", "  ", true)]
        public void Search_EveryWordMustMatch(string label, string keyword, bool expected)
        {
            Assert.That(BookmarksGUI.MatchesSearch(label, null, keyword), Is.EqualTo(expected));
        }

        [Test]
        public void MoveBookmarks_BeforeTarget_KeepsListOrderAndTakesTargetFolder()
        {
            var a = new ObjectRef { guid = "a", folder = "" };
            var b = new ObjectRef { guid = "b", folder = "Art" };
            var c = new ObjectRef { guid = "c", folder = "" };
            var t = new ObjectRef { guid = "t", folder = "Art" };
            var list = new List<ObjectRef> { a, b, c, t };

            // 選取順序 c、a，但插入時依清單原順序 a、c。
            int moved = BookmarkService.MoveBookmarks(list, new[] { c, a }, "Art", t);

            Assert.That(moved, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { b, a, c, t }, list);
            Assert.That(a.folder, Is.EqualTo("Art"));
            Assert.That(c.folder, Is.EqualTo("Art"));
        }

        [Test]
        public void MoveBookmarks_WithoutTarget_OnlyChangesFolder()
        {
            var a = new ObjectRef { guid = "a", folder = "" };
            var b = new ObjectRef { guid = "b", folder = "" };
            var list = new List<ObjectRef> { a, b };
            Assert.That(BookmarkService.MoveBookmarks(list, new[] { b }, "Art", null), Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { a, b }, list);
            Assert.That(b.folder, Is.EqualTo("Art"));
            Assert.That(a.folder, Is.EqualTo(""));
        }

        [TestCase("C", "A", new[] { "C", "A", "B" })]
        [TestCase("A", "C", new[] { "B", "A", "C" })]
        [TestCase("A", "", new[] { "B", "C", "A" })]
        public void ReorderFolders_MovesBeforeTargetOrToEnd(string source, string before, string[] expected)
        {
            var folders = new List<FolderInfo> { new() { name = "A" }, new() { name = "B" }, new() { name = "C" } };
            Assert.That(BookmarkService.ReorderFolders(folders, source, before), Is.True);
            CollectionAssert.AreEqual(expected, folders.ConvertAll(f => f.name));
        }

        [Test]
        public void ReorderFolders_SameOrMissing_ReturnsFalse()
        {
            var folders = new List<FolderInfo> { new() { name = "A" } };
            Assert.That(BookmarkService.ReorderFolders(folders, "A", "A"), Is.False);
            Assert.That(BookmarkService.ReorderFolders(folders, "X", ""), Is.False);
            Assert.That(BookmarkService.ReorderFolders(folders, "", "A"), Is.False);
        }

        [TestCase(0L, "")]
        [TestCase(30_000L, "剛剛")]
        [TestCase(5 * 60_000L, "5 分鐘前")]
        [TestCase(3 * 3_600_000L, "3 小時前")]
        [TestCase(2 * 86_400_000L, "2 天前")]
        public void FormatRelativeTime_UsesLargestUnit(long ago, string expected)
        {
            const long now = 1_800_000_000_000L;
            long time = ago == 0 ? 0 : now - ago;
            Assert.That(BookmarkService.FormatRelativeTime(time, now), Is.EqualTo(expected));
        }

        [Test]
        public void CollectLatestDistinct_SubAssetsAndSessionRefs_AreNotMerged()
        {
            var list = new List<ObjectRef>
            {
                new ObjectRef { guid = "tex", globalId = "GlobalObjectId_V1-1-tex-1-0" },
                new ObjectRef { guid = "tex", globalId = "GlobalObjectId_V1-1-tex-2-0" },
                new ObjectRef { instanceId = 7 },
                new ObjectRef { instanceId = 8 },
                new ObjectRef { instanceId = 7 },
            };
            var result = new List<int>();
            BookmarkService.CollectLatestDistinct(list, result);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, result);
        }
    }
}
#endif
