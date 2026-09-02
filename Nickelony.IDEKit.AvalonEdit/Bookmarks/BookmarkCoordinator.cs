using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Tracks bookmarks as document anchors and resolves adjacent bookmarked lines for navigation.
/// Persistence of the bookmark set is a host concern and is not performed by this coordinator.
/// </summary>
public sealed class BookmarkCoordinator
{
	private readonly Func<TextDocument> _documentProvider;
	private readonly Action? _onBookmarksChanged;
	private readonly List<TextAnchor> _bookmarkAnchors = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkCoordinator"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the document the bookmarks belong to.</param>
	/// <param name="onBookmarksChanged">
	/// The callback invoked when the bookmark set changes, or <see langword="null"/> for none.
	/// </param>
	public BookmarkCoordinator(Func<TextDocument> documentProvider, Action? onBookmarksChanged = null)
	{
		_documentProvider = documentProvider;
		_onBookmarksChanged = onBookmarksChanged;
	}

	/// <summary>
	/// Gets the bookmarked lines, sorted by line number.
	/// </summary>
	public IReadOnlyList<DocumentLine> GetBookmarkedLines()
		=> CollectBookmarkedLines(GetDocument());

	/// <summary>
	/// Toggles a bookmark on the line containing the supplied offset.
	/// </summary>
	/// <param name="caretOffset">
	/// The document offset whose line is toggled. Values outside the document are clamped.
	/// </param>
	public void ToggleBookmark(int caretOffset)
	{
		TextDocument document = GetDocument();

		if (document.LineCount == 0)
			return;

		DocumentLine currentLine = document.GetLineByOffset(document.ClampOffset(caretOffset));
		TextAnchor? bookmarkAnchor = FindBookmarkAnchor(document, currentLine);

		if (bookmarkAnchor is null)
			AddBookmark(document, currentLine);
		else
			_bookmarkAnchors.Remove(bookmarkAnchor);

		_onBookmarksChanged?.Invoke();
	}

	/// <summary>
	/// Gets the first bookmarked line after the line containing the supplied offset, wrapping to the first bookmark.
	/// </summary>
	/// <param name="caretOffset">The document offset of the current position.</param>
	public DocumentLine? GetNextBookmarkLine(int caretOffset)
		=> GetAdjacentBookmarkLine(caretOffset, findNext: true);

	/// <summary>
	/// Gets the last bookmarked line before the line containing the supplied offset, wrapping to the last bookmark.
	/// </summary>
	/// <param name="caretOffset">The document offset of the current position.</param>
	public DocumentLine? GetPreviousBookmarkLine(int caretOffset)
		=> GetAdjacentBookmarkLine(caretOffset, findNext: false);

	/// <summary>
	/// Removes all bookmarks.
	/// </summary>
	public void Clear()
	{
		_bookmarkAnchors.Clear();
		_onBookmarksChanged?.Invoke();
	}

	/// <summary>
	/// Replaces the current bookmarks with the bookmarks at the supplied one-based line numbers.
	/// Line numbers outside the document are ignored.
	/// </summary>
	/// <remarks>
	/// Restoring bookmarks does not invoke the <c>onBookmarksChanged</c> callback supplied to the constructor.
	/// </remarks>
	/// <param name="lineNumbers">The one-based line numbers to bookmark.</param>
	public void Restore(IEnumerable<int> lineNumbers)
	{
		_bookmarkAnchors.Clear();

		TextDocument document = GetDocument();

		if (document.LineCount == 0)
			return;

		foreach (int lineNumber in lineNumbers)
		{
			if (lineNumber >= 1 && lineNumber <= document.LineCount)
			{
				DocumentLine documentLine = document.GetLineByNumber(lineNumber);

				if (FindBookmarkAnchor(document, documentLine) is null)
					AddBookmark(document, documentLine);
			}
		}
	}

	private DocumentLine? GetAdjacentBookmarkLine(int caretOffset, bool findNext)
	{
		TextDocument document = GetDocument();
		List<DocumentLine> bookmarkedLines = CollectBookmarkedLines(document);

		if (document.LineCount == 0 || bookmarkedLines.Count == 0)
			return null;

		DocumentLine currentLine = document.GetLineByOffset(document.ClampOffset(caretOffset));

		return findNext
			? bookmarkedLines.FirstOrDefault(line => line.LineNumber > currentLine.LineNumber) ?? bookmarkedLines[0]
			: bookmarkedLines.LastOrDefault(line => line.LineNumber < currentLine.LineNumber) ?? bookmarkedLines[^1];
	}

	private TextDocument GetDocument()
		=> _documentProvider();

	private List<DocumentLine> CollectBookmarkedLines(TextDocument document)
	{
		var bookmarkedLines = new List<DocumentLine>();
		var invalidAnchors = new List<TextAnchor>();
		var seenLineNumbers = new HashSet<int>();

		foreach (TextAnchor anchor in _bookmarkAnchors)
		{
			DocumentLine? line = GetBookmarkedLine(document, anchor);

			if (line is null)
			{
				invalidAnchors.Add(anchor);
				continue;
			}

			if (seenLineNumbers.Add(line.LineNumber))
				bookmarkedLines.Add(line);
		}

		foreach (TextAnchor anchor in invalidAnchors)
			_bookmarkAnchors.Remove(anchor);

		bookmarkedLines.Sort((left, right) => left.LineNumber.CompareTo(right.LineNumber));
		return bookmarkedLines;
	}

	private void AddBookmark(TextDocument document, DocumentLine line)
	{
		TextAnchor anchor = document.CreateAnchor(line.Offset);
		anchor.MovementType = AnchorMovementType.BeforeInsertion;
		anchor.SurviveDeletion = true;

		_bookmarkAnchors.Add(anchor);
	}

	private TextAnchor? FindBookmarkAnchor(TextDocument document, DocumentLine line)
	{
		foreach (TextAnchor anchor in _bookmarkAnchors)
		{
			DocumentLine? bookmarkedLine = GetBookmarkedLine(document, anchor);

			if (bookmarkedLine is not null && bookmarkedLine.LineNumber == line.LineNumber)
				return anchor;
		}

		return null;
	}

	private static DocumentLine? GetBookmarkedLine(TextDocument document, TextAnchor anchor)
	{
		if (anchor.IsDeleted || document.LineCount == 0)
			return null;

		int offset = document.ClampOffset(anchor.Offset);
		return document.GetLineByOffset(offset);
	}
}
