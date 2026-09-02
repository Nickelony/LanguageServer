using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using System.IO;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// Tests for the <see cref="BookmarkSidecarStore"/> and the
/// <see cref="BookmarkStoreExtensions"/> coordinator round-trip.
/// </summary>
[TestClass]
public sealed class BookmarkSidecarStoreTests
{
	private static string CreateTempPath(out string directory)
	{
		directory = Path.Combine(Path.GetTempPath(), "BookmarkSidecarStoreTests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, "script.txt");
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), "BookmarkSidecarStoreTests-*"))
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch (IOException)
			{ }
			catch (UnauthorizedAccessException)
			{ }
		}
	}

	[TestMethod]
	public void Save_ThenRestore_RoundTripsLineNumbers()
	{
		string filePath = CreateTempPath(out _);
		var store = new BookmarkSidecarStore();

		bool saved = store.Save(filePath, [2, 7]);
		Assert.IsTrue(saved);

		IReadOnlyList<int> restored = store.Restore(filePath);

		CollectionAssert.AreEqual(new[] { 2, 7 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_EmptySet_DeletesSidecar()
	{
		string filePath = CreateTempPath(out _);
		var store = new BookmarkSidecarStore();
		store.Save(filePath, [1]);

		store.Save(filePath, []);

		Assert.IsFalse(File.Exists(filePath + ".bkmrk"));
		Assert.AreEqual(0, store.Restore(filePath).Count);
	}

	[TestMethod]
	public void CustomExtension_IsHonored()
	{
		string filePath = CreateTempPath(out _);
		var store = new BookmarkSidecarStore(".markers");

		store.Save(filePath, [3]);

		Assert.IsTrue(File.Exists(filePath + ".markers"));
		Assert.IsFalse(File.Exists(filePath + ".bkmrk"));
	}

	[TestMethod]
	public void Coordinator_SaveBookmarks_ThenRestoreBookmarks_RoundTrips()
	{
		string filePath = CreateTempPath(out _);
		var document = new TextDocument("one\r\ntwo\r\nthree\r\nfour");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new BookmarkSidecarStore();

		coordinator.ToggleBookmark(GetLineOffset(document, 2));
		coordinator.ToggleBookmark(GetLineOffset(document, 4));

		coordinator.SaveBookmarks(store, filePath);
		coordinator.Clear();
		Assert.AreEqual(0, coordinator.GetBookmarkedLines().Count);

		coordinator.RestoreBookmarks(store, filePath);

		Assert.AreEqual(2, coordinator.GetBookmarkedLines().Count);
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
		Assert.AreEqual(4, coordinator.GetBookmarkedLines()[1].LineNumber);
	}

	[TestMethod]
	public void Coordinator_Restore_OutOfRangeLines_AreIgnored()
	{
		string filePath = CreateTempPath(out _);
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new BookmarkSidecarStore();

		store.Save(filePath, [2, 99]);

		coordinator.RestoreBookmarks(store, filePath);

		Assert.AreEqual(1, coordinator.GetBookmarkedLines().Count);
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
	}

	private static int GetLineOffset(TextDocument document, int lineNumber)
		=> document.GetLineByNumber(lineNumber).Offset;
}
