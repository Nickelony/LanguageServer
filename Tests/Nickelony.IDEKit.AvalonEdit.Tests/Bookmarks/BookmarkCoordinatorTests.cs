using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class BookmarkCoordinatorTests
{
	[TestMethod]
	public void ToggleBookmark_AddsBookmarkForContainingLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.HasCount(1, coordinator.GetBookmarkedLines());
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
	}

	[TestMethod]
	public void ToggleBookmark_ToggleAgain_RemovesBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.IsEmpty(coordinator.GetBookmarkedLines());
	}

	[TestMethod]
	public void GetBookmarkedLines_ReturnsLinesSortedByLineNumber()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 3));
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.AreEqual(1, coordinator.GetBookmarkedLines()[0].LineNumber);
		Assert.AreEqual(3, coordinator.GetBookmarkedLines()[1].LineNumber);
	}

	[TestMethod]
	public void GetNextBookmarkLine_WrapsToFirstBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		DocumentLine? next = coordinator.GetNextBookmarkLine(GetLineOffset(document, 3));

		Assert.IsNotNull(next);
		Assert.AreEqual(1, next.LineNumber);
	}

	[TestMethod]
	public void GetPreviousBookmarkLine_WrapsToLastBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		DocumentLine? previous = coordinator.GetPreviousBookmarkLine(GetLineOffset(document, 1));

		Assert.IsNotNull(previous);
		Assert.AreEqual(3, previous.LineNumber);
	}

	[TestMethod]
	public void GetAdjacentBookmarkLine_NoBookmarks_ReturnsNull()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		Assert.IsNull(coordinator.GetNextBookmarkLine(0));
		Assert.IsNull(coordinator.GetPreviousBookmarkLine(0));
	}

	[TestMethod]
	public void Clear_RemovesAllBookmarksAndRaisesChanged()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");

		int changedCalls = 0;
		var coordinator = new BookmarkCoordinator(() => document, () => changedCalls++);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		coordinator.Clear();

		Assert.IsEmpty(coordinator.GetBookmarkedLines());
		Assert.AreEqual(3, changedCalls);
	}

	[TestMethod]
	public void ToggleBookmark_RaisesChangedCallback()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");

		int changedCalls = 0;
		var coordinator = new BookmarkCoordinator(() => document, () => changedCalls++);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.AreEqual(2, changedCalls);
	}

	[TestMethod]
	public void Restore_AddsBookmarksForValidLineNumbers()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.Restore([2, 99, -1]);

		Assert.HasCount(1, coordinator.GetBookmarkedLines());
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
	}

	[TestMethod]
	public void Restore_ReplacesExistingBookmarks()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.Restore([2]);

		Assert.HasCount(1, coordinator.GetBookmarkedLines());
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
	}

	[TestMethod]
	public void Restore_DoesNotRaiseChangedCallback()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");

		int changedCalls = 0;
		var coordinator = new BookmarkCoordinator(() => document, () => changedCalls++);

		coordinator.Restore([1, 2]);

		Assert.AreEqual(0, changedCalls);
	}

	[TestMethod]
	public void BookmarkFollowsTextInsertedBeforeIt()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		document.Insert(0, "X");

		Assert.HasCount(1, coordinator.GetBookmarkedLines());
		Assert.AreEqual(2, coordinator.GetBookmarkedLines()[0].LineNumber);
	}

	private static int GetLineOffset(TextDocument document, int lineNumber)
		=> document.GetLineByNumber(lineNumber).Offset;
}
