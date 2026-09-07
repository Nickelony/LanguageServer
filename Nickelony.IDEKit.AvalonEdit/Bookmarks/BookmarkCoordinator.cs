using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Manages bookmarks within an AvalonEdit <see cref="TextDocument"/>,
/// allowing toggling, navigation, and restoration of bookmarks.
/// </summary>
/// <remarks>Bookmark persistence is the host's responsibility.</remarks>
public sealed class BookmarkCoordinator
{
	private readonly Func<TextDocument> _documentProvider;
	private readonly Action? _onBookmarksChanged;
	private readonly List<TextAnchor> _bookmarkAnchors = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkCoordinator"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the AvalonEdit <see cref="TextDocument"/> used for bookmark operations.</param>
	/// <param name="onBookmarksChanged">
	/// Invoked after <see cref="ToggleBookmark"/> changes a bookmark or <see cref="Clear"/> is called.
	/// Passing <see langword="null"/> disables the callback.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public BookmarkCoordinator(Func<TextDocument> documentProvider, Action? onBookmarksChanged = null)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);

		_documentProvider = documentProvider;
		_onBookmarksChanged = onBookmarksChanged;
	}

	/// <summary>
	/// Gets the bookmarked document lines in ascending line-number order.
	/// </summary>
	public IReadOnlyList<DocumentLine> GetBookmarkedLines()
		=> CollectBookmarkedLines(GetDocument());

	/// <summary>
	/// Adds or removes the bookmark on the line containing the specified offset.
	/// </summary>
	/// <param name="caretOffset">
	/// The zero-based document offset used to locate the line. Values outside the document are clamped.
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
	/// Gets the first bookmarked line after the line containing the offset,
	/// wrapping to the first bookmark in the document when necessary.
	/// </summary>
	/// <param name="caretOffset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The next bookmarked line, or <see langword="null"/> if none exist.</returns>
	public DocumentLine? GetNextBookmarkLine(int caretOffset)
		=> GetAdjacentBookmarkLine(caretOffset, findNext: true);

	/// <summary>
	/// Gets the last bookmarked line before the line containing the offset,
	/// wrapping to the last bookmark in the document when necessary.
	/// </summary>
	/// <param name="caretOffset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The previous bookmarked line, or <see langword="null"/> if none exist.</returns>
	public DocumentLine? GetPreviousBookmarkLine(int caretOffset)
		=> GetAdjacentBookmarkLine(caretOffset, findNext: false);

	/// <summary>
	/// Removes all bookmarks.
	/// </summary>
	/// <remarks>Invokes the change callback when supplied, even when no bookmarks exist.</remarks>
	public void Clear()
	{
		_bookmarkAnchors.Clear();
		_onBookmarksChanged?.Invoke();
	}

	/// <summary>
	/// Replaces the current bookmarks with bookmarks for the supplied one-based line numbers.
	/// Line numbers outside the document and duplicate entries are ignored.
	/// </summary>
	/// <remarks>Does not invoke the change callback.</remarks>
	/// <param name="lineNumbers">The one-based line numbers to bookmark.</param>
	/// <exception cref="ArgumentNullException"><paramref name="lineNumbers"/> is <see langword="null"/>.</exception>
	public void Restore(IEnumerable<int> lineNumbers)
	{
		ArgumentNullException.ThrowIfNull(lineNumbers);

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
	{
		TextDocument document = _documentProvider();
		ArgumentNullException.ThrowIfNull(document);

		return document;
	}

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
