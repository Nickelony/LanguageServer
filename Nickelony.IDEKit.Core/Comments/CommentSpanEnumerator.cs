namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Enumerates the comments in a span of text in a single forward pass, recognizing
/// line comments and block comments together and ignoring delimiters inside string
/// literals and comments. Create one with
/// <see cref="CommentHelper.EnumerateComments(ReadOnlySpan{char}, CommentSyntax)"/>.
/// </summary>
public ref struct CommentSpanEnumerator
{
	private readonly ReadOnlySpan<char> _text;
	private CommentScanner _scanner;
	private int _floor;
	private CommentSpan _current;

	/// <summary>
	/// Gets the current comment span. Valid only after <see cref="MoveNext"/> has
	/// returned <see langword="true"/>.
	/// </summary>
	public readonly CommentSpan Current => _current;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSpanEnumerator"/> struct.
	/// </summary>
	/// <param name="text">The text to scan. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	public CommentSpanEnumerator(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		_text = text;
		_scanner = new CommentScanner(text, 0, syntax.StringStyle, syntax.LineCommentDelimiter, syntax.BlockCommentOpen, syntax.BlockCommentClose, syntax.AllowNestedBlockComments);
		_floor = 0;
		_current = default;
	}

	/// <summary>
	/// Advances to the next comment span in the text.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if a comment span is available in <see cref="Current"/>;
	/// otherwise <see langword="false"/>.
	/// </returns>
	public bool MoveNext()
	{
		// Phase 1: locate the next comment opener in code (outside strings and comments).
		int lineStart = -1;
		int blockStart = -1;

		while (_scanner.MoveNext())
		{
			if (_scanner.LineCommentStartIndex >= 0)
			{
				lineStart = _scanner.LineCommentStartIndex;
				break;
			}

			if (_scanner.BlockCommentStartIndex >= 0)
			{
				blockStart = _scanner.BlockCommentStartIndex;
				break;
			}
		}

		if (lineStart < 0 && blockStart < 0)
			return false;

		if (lineStart >= 0)
		{
			// The span starts at the delimiter and includes preceding whitespace, but
			// never before the previous comment's end, so a comment-only line consumes
			// its preceding line ending.
			int start = lineStart;

			while (start > _floor && char.IsWhiteSpace(_text[start - 1]))
				start--;

			// A line comment runs to the line break (exclusive) or the end of the text.
			int end = lineStart;

			while (end < _text.Length && _text[end] != '\n')
				end++;

			_current = new CommentSpan(start, lineStart, end, isLineComment: true);
			_floor = end;

			return true;
		}

		// Phase 2: continue to the matching block closer; the scanner tracks nesting.
		int blockEnd = -1;

		while (_scanner.MoveNext())
		{
			if (_scanner.BlockCommentEndIndex >= 0)
			{
				blockEnd = _scanner.BlockCommentEndIndex;
				break;
			}
		}

		// An unclosed block comment extends to the end of the text.
		if (blockEnd < 0)
			blockEnd = _text.Length;

		_current = new CommentSpan(blockStart, blockStart, blockEnd, isLineComment: false);
		_floor = blockEnd;

		return true;
	}
}
