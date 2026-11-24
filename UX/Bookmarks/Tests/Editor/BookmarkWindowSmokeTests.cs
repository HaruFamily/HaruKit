#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruFamily.UX.Bookmarks.Tests
{
    // 介面冒煙測試：開書籤視窗、送繪製／滑鼠／鍵盤事件，確認不噴例外（Test Runner 遇到 Console 錯誤會判定失敗）且列表內容正確。
    // 使用暫存資料檔與記憶體中的物件，不碰使用者的書籤資料，也不弄髒場景。
    public class BookmarkWindowSmokeTests
    {
        private string directory;
        private IDisposable storageScope;
        private Texture2D texture;
        private Mesh mesh;
        private BookmarksWindow window;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "HaruBookmarksSmoke", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var storage = new BookmarkFileStorage(Path.Combine(directory, "bookmarks.json"));
            Assert.That(storage.Load(), Is.True, storage.LastError);
            storageScope = BookmarkStorage.UseStorageForTests(storage);

            texture = new Texture2D(2, 2) { name = "SmokeTexture", hideFlags = HideFlags.DontSave };
            mesh = new Mesh { name = "SmokeMesh", hideFlags = HideFlags.DontSave };

            Assert.That(BookmarkService.AddFolder("Art"), Is.True);
            BookmarkService.AddBookmarks(new Object[] { texture }, "Art", null);
            BookmarkService.AddBookmarks(new Object[] { mesh }, string.Empty, null);
            // 指向不存在資產的書籤：應顯示為失效列。
            BookmarkStorage.Data.bookmarks.Add(new ObjectRef
            {
                guid = "00000000000000000000000000000001",
                globalId = "GlobalObjectId_V1-1-00000000000000000000000000000001-1-0",
                label = "Gone (Material)",
            });
            BookmarkStorage.Data.history.Add(new ObjectRef { instanceId = texture.GetInstanceID(), label = "SmokeTexture (Texture2D)", time = BookmarkService.NowMs() });
            BookmarkStorage.Data.index = 0;
            BookmarkStorage.Save();

            window = ScriptableObject.CreateInstance<BookmarksWindow>();
            window.Show();
            window.position = new Rect(80, 80, 420, 520);
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) window.Close();
            storageScope?.Dispose();
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(mesh);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        private void Send(EventType type, Vector2 mouse = default, KeyCode key = KeyCode.None)
            => window.SendEvent(new Event { type = type, mousePosition = mouse, keyCode = key });

        [Test]
        public void Draw_ShowsFolderRowsMissingRowAndHistory()
        {
            Send(EventType.Repaint);

            var gui = window.Gui;
            Assert.That(gui, Is.Not.Null);
            // Art 內 1 筆、未分類 2 筆（含失效列）、紀錄 1 筆。
            Assert.That(gui.VisibleRowCount, Is.EqualTo(4));
        }

        [Test]
        public void MouseMoveAndRepaint_OverEveryRow_DoNotThrow()
        {
            Send(EventType.Repaint);
            for (float y = 0; y < 300; y += 10)
            {
                Send(EventType.MouseMove, new Vector2(200, y));
                Send(EventType.Repaint, new Vector2(200, y));
            }
            Assert.That(window.Gui.VisibleRowCount, Is.EqualTo(4));
        }

        [Test]
        public void ArrowKeys_MoveCursorWithoutChangingBookmarks()
        {
            Send(EventType.Repaint);
            GUIUtility.keyboardControl = 0;
            Send(EventType.KeyDown, key: KeyCode.DownArrow);
            Send(EventType.KeyDown, key: KeyCode.DownArrow);
            Send(EventType.Repaint);

            var gui = window.Gui;
            Assert.That(gui.Cursor, Is.SameAs(gui.VisibleItems[1]));
            Assert.That(BookmarkStorage.Data.bookmarks.Count, Is.EqualTo(3));

            Send(EventType.KeyDown, key: KeyCode.UpArrow);
            Assert.That(gui.Cursor, Is.SameAs(gui.VisibleItems[0]));
        }

        [Test]
        public void RemoveAndUndo_RedrawsWithoutErrors()
        {
            Send(EventType.Repaint);
            BookmarkService.RemoveBookmark(BookmarkStorage.Data.bookmarks[0]);
            Assert.That(BookmarkService.CanUndo, Is.True);
            Send(EventType.Repaint); // 會畫出復原列
            Assert.That(window.Gui.VisibleRowCount, Is.EqualTo(3));

            BookmarkService.UndoLastRemoval();
            Send(EventType.Repaint);
            Assert.That(window.Gui.VisibleRowCount, Is.EqualTo(4));
            Assert.That(BookmarkService.CanUndo, Is.False);
        }
    }
}
#endif
