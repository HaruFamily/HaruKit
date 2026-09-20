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

            Assert.That(Inspector.ReorderBookmarks(list, backwards ? c : a, backwards ? a : c), Is.True);
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

            Assert.That(Inspector.ReorderBookmarks(list, source, target), Is.True);
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

            Assert.That(Inspector.ReorderBookmarks(list, source, target), Is.True);
            CollectionAssert.AreEqual(new[] { hidden, target, source }, list);
        }

        [Test]
        public void Reorder_RemovedOrSameItem_DoesNotMutateList()
        {
            var a = new ObjectRef { guid = "a" };
            var b = new ObjectRef { guid = "b" };
            var removed = new ObjectRef { guid = "removed" };
            var list = new List<ObjectRef> { a, b };
            Assert.That(Inspector.ReorderBookmarks(list, removed, b), Is.False);
            Assert.That(Inspector.ReorderBookmarks(list, a, removed), Is.False);
            Assert.That(Inspector.ReorderBookmarks(list, a, a), Is.False);
            Assert.That(Inspector.ReorderBookmarks(list, null, b), Is.False);
            CollectionAssert.AreEqual(new[] { a, b }, list);
        }

        [Test]
        public void Reorder_NullFolderAndEmptyFolder_AreSameGroup()
        {
            var a = new ObjectRef { guid = "a", folder = null };
            var x = new ObjectRef { guid = "x", folder = "Other" };
            var b = new ObjectRef { guid = "b", folder = string.Empty };
            var list = new List<ObjectRef> { a, x, b };
            Assert.That(Inspector.ReorderBookmarks(list, a, b), Is.True);
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
    }
}
#endif
