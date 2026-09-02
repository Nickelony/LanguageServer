namespace Nickelony.IDEKit.Core.Comments;

// Internal. Incrementally walks a span of text and tracks whether each position is
// inside a quoted string literal, a line comment, or a block comment, so comment
// scanners can recognize delimiters only in code. Single- and double-quoted string
// styles reset at each LF line break; backtick and triple-quoted (raw) strings span lines; line
// comments end at an LF line break; block comments may span lines and nest when enabled.
internal ref struct CommentScanner
{
	private readonly ReadOnlySpan<char> _text;
	private readonly ReadOnlySpan<char> _lineDelimiter;
	private readonly ReadOnlySpan<char> _openBlockDelimiter;
	private readonly ReadOnlySpan<char> _closeBlockDelimiter;
	private readonly StringLiteralStyle _stringStyle;
	private readonly bool _allowNestedBlockComments;

	private int _position;
	private char _quoteChar;
	private char _rawStringQuote;
	private int _rawStringDelimiterLength;
	private int _longBracketEqualsCount;
	private bool _inLineComment;
	private bool _inCloserRemainder;
	private int _blockDepth;
	private int _closerRemaining;
	private int _lineCommentStart;
	private int _blockCommentStart;
	private int _blockCommentEnd;

	public CommentScanner(
		ReadOnlySpan<char> text,
		int startIndex,
		StringLiteralStyle stringStyle,
		ReadOnlySpan<char> lineDelimiter = default,
		ReadOnlySpan<char> openBlockDelimiter = default,
		ReadOnlySpan<char> closeBlockDelimiter = default,
		bool allowNestedBlockComments = false)
	{
		_text = text;
		_stringStyle = stringStyle;
		_lineDelimiter = lineDelimiter;
		_openBlockDelimiter = openBlockDelimiter;
		_closeBlockDelimiter = closeBlockDelimiter;
		_allowNestedBlockComments = allowNestedBlockComments;
		_position = startIndex;
		_quoteChar = '\0';
		_rawStringQuote = '\0';
		_rawStringDelimiterLength = 0;
		_longBracketEqualsCount = -1;
		_inLineComment = false;
		_inCloserRemainder = false;
		_blockDepth = 0;
		_closerRemaining = 0;
		_lineCommentStart = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
	}

	// The index of the character most recently consumed, or -1 before the first move.
	public readonly int CurrentIndex => _position - 1;

	// True when the most recently consumed character is in code (not inside a string or comment).
	public readonly bool IsInCode => _quoteChar == '\0' && !_inLineComment && _blockDepth == 0 && _closerRemaining == 0 && _rawStringQuote == '\0' && _longBracketEqualsCount < 0 && !_inCloserRemainder;

	// True when the most recently consumed character is inside a quoted string literal.
	public readonly bool IsInsideString => _quoteChar != '\0' || _rawStringQuote != '\0' || _longBracketEqualsCount >= 0;

	// True when the most recently consumed character is inside a line comment.
	public readonly bool IsInLineComment => _inLineComment;

	// True when the most recently consumed character is inside a block comment.
	public readonly bool IsInBlockComment => _blockDepth > 0;

	// The index where a line comment started during the most recent move, or -1.
	public readonly int LineCommentStartIndex => _lineCommentStart;

	// The index where the outermost block comment started during the most recent move, or -1.
	public readonly int BlockCommentStartIndex => _blockCommentStart;

	// The index just after a block comment closed during the most recent move, or -1.
	public readonly int BlockCommentEndIndex => _blockCommentEnd;

	// Consumes the next character and updates the scanning state. Returns false when
	// the text is exhausted.
	public bool MoveNext()
	{
		// Reset the one-shot transition markers for this move.
		_lineCommentStart = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
		_inCloserRemainder = false;

		if (_position >= _text.Length)
			return false;

		int index = _position;
		char c = _text[index];
		_position = index + 1;

		if (_inLineComment)
		{
			// A line comment ends at the LF line break.
			if (c == '\n')
				_inLineComment = false;

			return true;
		}

		if (_blockDepth > 0)
		{
			// Inside a block comment; only the closer (and nested openers) matter.
			if (IsAt(index, _closeBlockDelimiter))
			{
				_blockDepth--;

				if (_blockDepth == 0)
				{
					_blockCommentEnd = index + _closeBlockDelimiter.Length;

					// The detected character and the remaining closer characters are still
					// part of the comment and must not be reported as code. The detected
					// character was consumed by this move, so only Length - 1 remain.
					_closerRemaining = _closeBlockDelimiter.Length - 1;
				}
			}
			else if (_allowNestedBlockComments && IsAt(index, _openBlockDelimiter))
			{
				_blockDepth++;
			}

			return true;
		}

		if (_closerRemaining > 0)
		{
			// Consume the remainder of a just-closed block comment closer or raw-string closer.
			// These characters are still part of the closer, so the move is never reported as code.
			_inCloserRemainder = true;
			_closerRemaining--;

			return true;
		}

		if (_longBracketEqualsCount >= 0)
		{
			// Inside a long-bracket string; only a matching closer can end it. A closer is
			// a ']', the opener's equals count, and another ']' (with zero equals for [[ ]],
			// the closer is simply "]]").
			if (c == ']' && TryMatchLongBracketCloser(index, _longBracketEqualsCount, out int closerLength))
			{
				_longBracketEqualsCount = -1;

				// The detected character and the remaining closer characters are still part
				// of the string and must not be reported as code; Length - 1 remain after this move.
				_closerRemaining = closerLength - 1;
			}

			return true;
		}

		if (_rawStringQuote != '\0')
		{
			// Inside a raw (multi-line) string; only a matching quote run can close it.
			// Backslashes are content, not escapes. A run shorter than the delimiter is
			// content; a run of at least the delimiter length closes the string, and any
			// surplus quotes in a longer run are rescanned as code.
			if (c == _rawStringQuote && CountQuoteRun(index, _rawStringQuote) >= _rawStringDelimiterLength)
			{
				_rawStringQuote = '\0';

				// The detected character and the remaining closer characters are still part
				// of the string and must not be reported as code; Length - 1 remain after this move.
				_closerRemaining = _rawStringDelimiterLength - 1;
			}

			return true;
		}

		if (_quoteChar != '\0')
		{
			// Inside a single-line string; only the matching quote can close it. The
			// string cannot continue onto the next line, except for a backtick string
			// (JavaScript template literals, Go raw strings), which spans lines.
			if (c == '\n' && _quoteChar != '`')
				_quoteChar = '\0';
			else if (c == _quoteChar && !IsEscapedQuote(index))
				_quoteChar = '\0';

			return true;
		}

		// In code: a raw-string opener, a long-bracket opener, a quote, a block opener,
		// or a line delimiter. Raw strings are checked before single-line quotes because a
		// raw opener begins with the same quote character.
		if (c == '"' && (_stringStyle & StringLiteralStyle.TripleDoubleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '"');

			if (run >= 3)
			{
				_rawStringQuote = '"';
				_rawStringDelimiterLength = run;

				return true;
			}
		}

		if (c == '\'' && (_stringStyle & StringLiteralStyle.TripleSingleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '\'');

			if (run >= 3)
			{
				_rawStringQuote = '\'';
				_rawStringDelimiterLength = run;

				return true;
			}
		}

		if (c == '[' && (_stringStyle & StringLiteralStyle.LongBracketQuoted) != 0)
		{
			// A long-bracket opener is '[' followed by any number of '=' followed by '['.
			// The '[' is consumed by this move; inspect the following characters.
			if (TryMatchLongBracketOpener(index, out int equalsCount))
			{
				_longBracketEqualsCount = equalsCount;

				return true;
			}
		}

		if (IsQuoteCharacter(c))
		{
			if (!IsEscapedQuote(index))
				_quoteChar = c;

			return true;
		}

		if (!_openBlockDelimiter.IsEmpty && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth = 1;
			_blockCommentStart = index;

			return true;
		}

		if (!_lineDelimiter.IsEmpty && IsAt(index, _lineDelimiter))
		{
			_inLineComment = true;
			_lineCommentStart = index;

			return true;
		}

		return true;
	}

	// True when the marker begins at the given index. The marker must be non-empty.
	private readonly bool IsAt(int index, ReadOnlySpan<char> marker)
		=> index + marker.Length <= _text.Length && _text[index] == marker[0] && _text.Slice(index, marker.Length).SequenceEqual(marker);

	// True when the character at quoteIndex is escaped by an odd number of
	// immediately preceding backslashes.
	private readonly bool IsEscapedQuote(int quoteIndex)
	{
		int backslashCount = 0;

		for (int i = quoteIndex - 1; i >= 0 && _text[i] == '\\'; i--)
			backslashCount++;

		return backslashCount % 2 == 1;
	}

	// Counts the maximal run of the given quote character starting at index.
	private readonly int CountQuoteRun(int index, char quote)
	{
		int run = 0;

		while (index + run < _text.Length && _text[index + run] == quote)
			run++;

		return run;
	}

	// True when a long-bracket opener begins at the '[' at openerIndex. The opener is
	// '[' followed by any number of '=' followed by '[', e.g. "[[", "[=[", "[==[".
	// Reports the equals count (0 for "[[").
	private readonly bool TryMatchLongBracketOpener(int openerIndex, out int equalsCount)
	{
		equalsCount = 0;
		int index = openerIndex + 1;

		while (index < _text.Length && _text[index] == '=')
		{
			equalsCount++;
			index++;
		}

		return index < _text.Length && _text[index] == '[';
	}

	// True when a long-bracket closer matching the opener's equals count begins at the ']'
	// at closerIndex. The closer is ']' followed by the equals count of '=' followed by ']'.
	// Reports the full closer length.
	private readonly bool TryMatchLongBracketCloser(int closerIndex, int equalsCount, out int closerLength)
	{
		int index = closerIndex + 1;
		int actualEquals = 0;

		while (index < _text.Length && _text[index] == '=')
		{
			actualEquals++;
			index++;
		}

		if (actualEquals != equalsCount)
		{
			closerLength = 0;
			return false;
		}

		if (index < _text.Length && _text[index] == ']')
		{
			closerLength = 2 + equalsCount;
			return true;
		}

		closerLength = 0;
		return false;
	}

	// Bitwise checks avoid Enum.HasFlag boxing in the per-character scan.
	private readonly bool IsQuoteCharacter(char c)
		=> (c == '"' && (_stringStyle & StringLiteralStyle.DoubleQuoted) != 0)
		|| (c == '\'' && (_stringStyle & StringLiteralStyle.SingleQuoted) != 0)
		|| (c == '`' && (_stringStyle & StringLiteralStyle.BacktickQuoted) != 0);
}
