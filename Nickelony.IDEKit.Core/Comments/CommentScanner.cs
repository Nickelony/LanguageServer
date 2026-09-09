using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Incrementally walks a span of text while tracking whether each position is inside a quoted
/// string literal, a line comment, or a block comment, so scanners recognize delimiters only in code.
/// </summary>
/// <remarks>
/// <para>
/// The state checks run in a fixed order: a pending delimiter remainder is consumed before
/// block-comment matching, so the characters of a detected opener can never pair with what follows
/// (the <c>*</c> of a <c>/*</c> opener cannot close the comment with a following <c>/</c>).
/// Single- and double-quoted string styles reset at each line terminator (CR, LF, or CRLF);
/// backtick, triple-quoted (raw), long-bracket, and verbatim strings span lines; line comments end
/// at a line terminator; block comments may span lines and nest when enabled.
/// </para>
/// <para>
/// Triple-double-quoted strings follow the C# raw-string rule (the opening quote run is the
/// delimiter); triple-single-quoted strings follow Python's fixed three-quote rule. A verbatim
/// string opener is recognized in both C# interpolation orders, <c>@"</c> and <c>@$"</c>; the
/// opener's characters are consumed together so the next move starts on the first content
/// character.
/// </para>
/// <para>
/// Interpolated string holes are not modeled in any style, so a quote inside a hole can end the
/// string early.
/// </para>
/// </remarks>
internal ref struct CommentScanner
{
	private readonly ReadOnlySpan<char> _text;
	private readonly ReadOnlySpan<char> _lineDelimiter;
	private readonly ReadOnlySpan<char> _openBlockDelimiter;
	private readonly ReadOnlySpan<char> _closeBlockDelimiter;
	private readonly StringLiteralStyle _stringStyle;
	private readonly bool _allowNestedBlockComments;

	private int _position;

	// Every field from _quoteChar through _delimiterRemaining marks a non-code state and is reflected
	// in IsInNonCodeState; a new state field must be added to that expression as well.
	private char _quoteChar;
	private char _rawStringQuote;
	private int _rawStringDelimiterLength;
	private int _longBracketEqualsCount;
	private bool _inVerbatimString;
	private bool _inLineComment;
	// Set for the move that consumed a delimiter character (an opener, a closer, or one of their
	// remaining characters); such a move is never reported as code, even when it closed a comment
	// and the position after it is code again.
	private bool _delimiterConsumed;
	private int _blockDepth;
	private int _delimiterRemaining;
	private int _lineCommentStart;
	private int _lineCommentEnd;
	private int _blockCommentStart;
	private int _blockCommentEnd;

	/// <summary>
	/// Initializes a scanner over <paramref name="text"/> using the delimiters and string styles of the
	/// supplied <see cref="CommentSyntax"/>.
	/// </summary>
	/// <param name="text">The text to scan.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	public CommentScanner(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		BlockCommentSyntax? blockComments = syntax.BlockComments;

		_text = text;
		_stringStyle = syntax.StringStyle;
		_lineDelimiter = syntax.LineCommentDelimiter;
		_openBlockDelimiter = blockComments?.Open ?? string.Empty;
		_closeBlockDelimiter = blockComments?.Close ?? string.Empty;
		_allowNestedBlockComments = blockComments?.AllowNesting == true;
		_position = 0;
		_quoteChar = '\0';
		_rawStringQuote = '\0';
		_rawStringDelimiterLength = 0;
		_longBracketEqualsCount = -1;
		_inVerbatimString = false;
		_inLineComment = false;
		_delimiterConsumed = false;
		_blockDepth = 0;
		_delimiterRemaining = 0;
		_lineCommentStart = -1;
		_lineCommentEnd = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
	}

	/// <summary>
	/// Gets the index of the last character consumed by the most recent move - for a move that consumes
	/// several characters at once (a line-comment body, a quote run inside a raw string, a doubled
	/// quote in a verbatim string, or an opener such as <c>@"</c> or <c>'''</c>), the last character
	/// of the consumed run - or <c>-1</c> before the first move.
	/// </summary>
	public readonly int CurrentIndex => _position - 1;

	/// <summary>
	/// Gets a value indicating whether the most recent move consumed only code characters, so the
	/// consumed character or run is outside strings and comments. A move that consumed a comment
	/// delimiter character - including the closing delimiter of a comment that ends with it - is not
	/// code; the closing quote of a single-line, backtick, or verbatim string is code (see
	/// <see cref="CommentOperations.GetCodeEnd"/>).
	/// </summary>
	public readonly bool IsInCode => !IsInNonCodeState && !_delimiterConsumed;

	/// <summary>
	/// Gets a value indicating whether the scanner sits inside a string or a comment. Every state
	/// field that marks non-code content must be reflected here; <see cref="IsInCode"/> adds the
	/// one-shot delimiter flag on top.
	/// </summary>
	private readonly bool IsInNonCodeState => _quoteChar != '\0' || _inLineComment || _blockDepth != 0 || _delimiterRemaining != 0 || _rawStringQuote != '\0' || _longBracketEqualsCount >= 0 || _inVerbatimString;

	/// <summary>
	/// Gets the index where a line comment started during the most recent move, or <c>-1</c> when none
	/// started.
	/// </summary>
	public readonly int LineCommentStartIndex => _lineCommentStart;

	/// <summary>
	/// Gets the exclusive end of a line comment started during the most recent move - the index of its
	/// line terminator, or the text length when the comment is not terminated - or <c>-1</c> when the
	/// most recent move started no line comment.
	/// </summary>
	public readonly int LineCommentEndIndex => _lineCommentEnd;

	/// <summary>
	/// Gets the index where the outermost block comment started during the most recent move, or
	/// <c>-1</c> when none started.
	/// </summary>
	public readonly int BlockCommentStartIndex => _blockCommentStart;

	/// <summary>
	/// Gets the index just after a block comment closed during the most recent move, or <c>-1</c> when
	/// none closed.
	/// </summary>
	public readonly int BlockCommentEndIndex => _blockCommentEnd;

	/// <summary>
	/// Advances the scanner and updates the scanning state.
	/// </summary>
	/// <remarks>
	/// A move that starts a line comment consumes the rest of its content in the same move, because
	/// nothing inside a line comment is recognized; the line terminator arrives with the following
	/// move. A move that enters a verbatim string consumes the at sign and the opening quote (three
	/// characters for the <c>@$"</c> order), a triple-single-quote opener consumes its three quotes,
	/// a quote run inside a raw string that cannot close the string consumes the whole run, and a
	/// doubled quote inside a verbatim string consumes both quotes. Every other move consumes exactly
	/// one character.
	/// </remarks>
	/// <returns>
	/// <see langword="true"/> when the scanner advanced; otherwise, <see langword="false"/> when
	/// the text is exhausted.
	/// </returns>
	public bool MoveNext()
	{
		// Reset the one-shot transition markers for this move.
		_lineCommentStart = -1;
		_lineCommentEnd = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
		_delimiterConsumed = false;

		if (_position >= _text.Length)
			return false;

		int index = _position;
		char c = _text[index];

		_position = index + 1;

		// Dispatch to the handler for the current scanning state. The order of the state checks is
		// significant: a pending delimiter remainder is consumed before block-comment matching, so
		// an opener's own characters can never pair with what follows.
		if (_inLineComment)
			ConsumeInLineComment(c);
		else if (_delimiterRemaining > 0)
			ConsumeDelimiterRemainder();
		else if (_blockDepth > 0)
			ConsumeInBlockComment(index);
		else if (_longBracketEqualsCount >= 0)
			ConsumeInLongBracket(index, c);
		else if (_rawStringQuote != '\0')
			ConsumeInRawString(index, c);
		else if (_inVerbatimString)
			ConsumeInVerbatimString(index, c);
		else if (_quoteChar != '\0')
			ConsumeInQuotedString(index, c);
		else
			ConsumeInCode(index, c);

		return true;
	}

	/// <summary>
	/// Consumes a character inside a line comment; a line terminator (CR, LF, or CRLF) ends the comment.
	/// </summary>
	private void ConsumeInLineComment(char c)
	{
		if (LineTerminators.IsTerminator(c))
			_inLineComment = false;
	}

	/// <summary>
	/// Consumes one of the remaining characters of a just-detected multi-character delimiter
	/// (opener or closer).
	/// </summary>
	private void ConsumeDelimiterRemainder()
	{
		// Delimiter characters are part of the delimiter, so the move is never reported as code.
		_delimiterConsumed = true;
		_delimiterRemaining--;
	}

	/// <summary>
	/// Consumes a character inside a block comment; only the closer and nested openers matter.
	/// </summary>
	private void ConsumeInBlockComment(int index)
	{
		if (IsAt(index, _closeBlockDelimiter))
		{
			_blockDepth--;

			if (_blockDepth == 0)
				_blockCommentEnd = index + _closeBlockDelimiter.Length;

			// The detected character and the remaining closer characters are still part of
			// the comment and must not be reported as code. They are consumed at every depth,
			// so an overlapping closer run (for example "]]]") cannot reuse a closer character
			// for the next nesting level. The detected character was consumed by this move,
			// so only Length - 1 remain; the move is marked as a delimiter move here so a
			// one-character closer behaves like the longer forms (no remainder move follows
			// that would set the mark).
			_delimiterConsumed = true;
			_delimiterRemaining = _closeBlockDelimiter.Length - 1;
		}
		else if (_allowNestedBlockComments && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth++;

			// Skip the nested opener's remaining characters before the next closer check; the
			// detected character is part of the opener, so the move is not code either.
			_delimiterConsumed = true;
			_delimiterRemaining = _openBlockDelimiter.Length - 1;
		}
	}

	/// <summary>
	/// Consumes a character inside a long-bracket string; only a matching closer can end it.
	/// </summary>
	private void ConsumeInLongBracket(int index, char c)
	{
		// A closer is a ']', the opener's equals count, and another ']'
		// (with zero equals, the closer is "]]").
		if (c == ']' && TryMatchLongBracketCloser(index, _longBracketEqualsCount, out int closerLength))
		{
			_longBracketEqualsCount = -1;

			// Consume the remainder of the detected closer as string content, not code.
			_delimiterRemaining = closerLength - 1;
		}
	}

	/// <summary>
	/// Consumes a character inside a raw (multi-line) string; only a matching quote run can close it.
	/// </summary>
	/// <remarks>
	/// The quote run is measured once from its first quote. A run shorter than the delimiter is
	/// skipped in one move (every quote is content), so a long run costs one scan instead of a
	/// rescan per quote. A run of at least the delimiter length closes the string, and any surplus
	/// quotes in a longer run are rescanned as code.
	/// </remarks>
	private void ConsumeInRawString(int index, char c)
	{
		// Backslashes are content, not escapes.
		if (c != _rawStringQuote)
			return;

		int run = CountQuoteRun(index, _rawStringQuote);

		if (run >= _rawStringDelimiterLength)
		{
			_rawStringQuote = '\0';

			// Consume the remainder of the detected closer as string content, not code.
			_delimiterRemaining = _rawStringDelimiterLength - 1;

			return;
		}

		// A run shorter than the delimiter is content; skip it without rescanning it from every
		// quote it contains.
		_position = index + run;
	}

	/// <summary>
	/// Consumes a character inside a single-line string; only the matching quote can close it.
	/// </summary>
	private void ConsumeInQuotedString(int index, char c)
	{
		// The string cannot continue onto the next line, except for a backtick string
		// (JavaScript template literals), which spans lines.
		if (LineTerminators.IsTerminator(c) && _quoteChar != '`')
			_quoteChar = '\0';
		else if (c == _quoteChar && !IsEscapedQuote(index))
			_quoteChar = '\0';
	}

	/// <summary>
	/// Consumes a character inside a verbatim string; a doubled quote is an escaped quote, and any
	/// other quote closes the string.
	/// </summary>
	private void ConsumeInVerbatimString(int index, char c)
	{
		if (c != '"')
			return;

		// A doubled quote is content; consume the second quote with the first so it cannot be
		// mistaken for a closer.
		if (index + 1 < _text.Length && _text[index + 1] == '"')
		{
			_position = index + 2;
			return;
		}

		_inVerbatimString = false;
	}

	/// <summary>
	/// Consumes a character in code and checks for openers in precedence order: raw strings,
	/// triple-single-quoted strings, long brackets, verbatim strings, single-line quotes, the block
	/// opener, and finally the line delimiter.
	/// </summary>
	private void ConsumeInCode(int index, char c)
	{
		// Raw strings are checked before single-line quotes because a raw opener begins with the
		// same quote character.
		if (c == '"' && !IsEscapedQuote(index) && (_stringStyle & StringLiteralStyle.TripleDoubleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '"');

			if (run >= 3)
			{
				// The C# raw-string rule: the whole opening quote run is the delimiter, and the string
				// closes on a run of at least that length. A closing run longer than the opener is
				// invalid (CS8998); the surplus quotes are re-scanned, so an odd surplus count leaves
				// the scan inside a string until quote parity realigns.
				_rawStringQuote = '"';
				_rawStringDelimiterLength = run;

				return;
			}
		}

		if (c == '\'' && !IsEscapedQuote(index) && (_stringStyle & StringLiteralStyle.TripleSingleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '\'');

			if (run >= 3)
			{
				// The Python rule: the delimiter is exactly three quotes. The opener consumes the
				// first three quotes, and surplus quotes in the opening run belong to the string
				// content, so a run such as '''' opens a string whose content begins with a quote.
				_rawStringQuote = '\'';
				_rawStringDelimiterLength = 3;
				_position = index + 3;

				return;
			}
		}

		if (c == '[' && (_stringStyle & StringLiteralStyle.LongBracketQuoted) != 0)
		{
			// A long-bracket opener is '[' followed by any number of '=' followed by '['.
			// The '[' is consumed by this move; inspect the following characters.
			if (TryMatchLongBracketOpener(index, out int equalsCount))
			{
				_longBracketEqualsCount = equalsCount;

				return;
			}
		}

		// A verbatim string opens with an at sign followed by '"': @"..." and the interpolated
		// $@"..." and @$"..." orders. The opener is consumed in one move so the next move starts
		// on the first content character; the dollar sign is part of the opener and has no other
		// effect because interpolation holes are not modeled. ($"@..." is not an interpolation
		// order: the at sign follows the opening quote and is string content.)
		if (c == '@' && (_stringStyle & StringLiteralStyle.VerbatimDoubleQuoted) != 0
			&& index + 1 < _text.Length
			&& (_text[index + 1] == '"'
				|| (_text[index + 1] == '$' && index + 2 < _text.Length && _text[index + 2] == '"')))
		{
			_inVerbatimString = true;
			_position = index + (_text[index + 1] == '$' ? 3 : 2);

			return;
		}

		if (IsQuoteCharacter(c))
		{
			if (!IsEscapedQuote(index))
				_quoteChar = c;

			return;
		}

		if (!_openBlockDelimiter.IsEmpty && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth = 1;
			_blockCommentStart = index;

			// Consume the opener's remaining characters before closer matching starts. Otherwise
			// the opener's own characters could pair with what follows: for example, the '*' in a
			// "/*" opener with a following '/' would close the comment immediately.
			_delimiterRemaining = _openBlockDelimiter.Length - 1;

			return;
		}

		if (!_lineDelimiter.IsEmpty && IsAt(index, _lineDelimiter))
		{
			_inLineComment = true;
			_lineCommentStart = index;

			// A line comment runs to the next line terminator and nothing inside it is recognized,
			// so both its end and the position after its content are known here. The terminator
			// itself is consumed by the following move, which also resets the comment state.
			int contentEnd = index + _lineDelimiter.Length;

			while (contentEnd < _text.Length && _text[contentEnd] != '\n' && _text[contentEnd] != '\r')
				contentEnd++;

			_lineCommentEnd = contentEnd;
			_position = contentEnd;
		}
	}

	/// <summary>
	/// Determines whether <paramref name="marker"/> begins at <paramref name="index"/>. An empty
	/// marker never matches.
	/// </summary>
	private readonly bool IsAt(int index, ReadOnlySpan<char> marker)
		=> !marker.IsEmpty && index + marker.Length <= _text.Length && _text[index] == marker[0] && _text.Slice(index, marker.Length).SequenceEqual(marker);

	/// <summary>
	/// Determines whether the character at <paramref name="quoteIndex"/> is escaped by an odd number
	/// of immediately preceding backslashes.
	/// </summary>
	private readonly bool IsEscapedQuote(int quoteIndex)
	{
		int backslashCount = 0;

		for (int i = quoteIndex - 1; i >= 0 && _text[i] == '\\'; i--)
			backslashCount++;

		return backslashCount % 2 == 1;
	}

	/// <summary>
	/// Counts the maximal run of <paramref name="quote"/> characters starting at <paramref name="index"/>.
	/// </summary>
	private readonly int CountQuoteRun(int index, char quote)
	{
		int run = 0;

		while (index + run < _text.Length && _text[index + run] == quote)
			run++;

		return run;
	}

	/// <summary>
	/// Determines whether a long-bracket opener begins at the <c>[</c> at <paramref name="openerIndex"/>.
	/// The opener is <c>[</c> followed by any number of <c>=</c> followed by <c>[</c>, for example
	/// <c>[[</c>, <c>[=[</c>, or <c>[==[</c>.
	/// </summary>
	/// <param name="openerIndex">The index of the <c>[</c> that may start a long-bracket opener.</param>
	/// <param name="equalsCount">Receives the number of equals signs (<c>0</c> for <c>[[</c>).</param>
	/// <returns><see langword="true"/> when a long-bracket opener begins at the index.</returns>
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

	/// <summary>
	/// Determines whether a long-bracket closer matching <paramref name="equalsCount"/> begins at the
	/// <c>]</c> at <paramref name="closerIndex"/>. The closer is <c>]</c> followed by the opener's
	/// number of equals signs followed by <c>]</c>.
	/// </summary>
	/// <param name="closerIndex">The index of the <c>]</c> that may start a long-bracket closer.</param>
	/// <param name="equalsCount">The opener's number of equals signs the closer must match.</param>
	/// <param name="closerLength">Receives the full closer length when the closer matches.</param>
	/// <returns><see langword="true"/> when a matching long-bracket closer begins at the index.</returns>
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

	/// <summary>
	/// Determines whether the character opens a single-line string under the configured styles.
	/// </summary>
	/// <remarks>Bitwise checks avoid an <c>Enum.HasFlag</c> call in the per-character scan.</remarks>
	private readonly bool IsQuoteCharacter(char c)
		=> (c == '"' && (_stringStyle & StringLiteralStyle.DoubleQuoted) != 0)
		|| (c == '\'' && (_stringStyle & StringLiteralStyle.SingleQuoted) != 0)
		|| (c == '`' && (_stringStyle & StringLiteralStyle.BacktickQuoted) != 0);
}
