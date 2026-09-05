using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests line-comment detection, removal, and masking by <see cref="CommentHelper"/>.
/// </summary>
[TestClass]
public sealed class CommentHelperLineCommentTests
{
	// Comment syntax used by the tests.
	private static readonly CommentSyntax s_semicolonSyntax = new(";", null, null, StringLiteralStyle.None);
	private static readonly CommentSyntax s_cStyleSyntax = new("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

	// ---------------------------------------------------------------------------
	// FindCommentStart
	// ---------------------------------------------------------------------------

	private static int FindCommentStart(string text, CommentSyntax syntax)
		=> CommentHelper.FindComment(text, syntax) is { } comment ? comment.Start : -1;

	[TestMethod]
	public void FindCommentStart_LineWithComment_ReturnsWhitespaceBeforeSemicolon()
	{
		int result = FindCommentStart("Legend= 42 ; comment", s_semicolonSyntax);

		// The comment (including leading whitespace) starts at index 10 (the space before ';').
		Assert.AreEqual(10, result);
	}

	[TestMethod]
	public void FindCommentStart_NoComment_ReturnsNegative()
	{
		int result = FindCommentStart("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentOnlyLine_ReturnsZero()
	{
		int result = FindCommentStart("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public void FindCommentStart_WithoutStringAwareness_TreatsSemicolonAsComment()
	{
		// String awareness is disabled, so a semicolon inside quotes is treated as a delimiter.
		int result = FindCommentStart("Legend= \"hello;world\"", s_semicolonSyntax);

		Assert.IsTrue(result >= 0);
	}

	[TestMethod]
	public void FindCommentStart_NonBreakingSpaceBeforeComment_IncludesWhitespace()
	{
		int result = FindCommentStart("Legend= 42\u00A0; comment", s_semicolonSyntax);

		Assert.AreEqual(10, result);
	}

	[TestMethod]
	public void FindCommentStart_EmptyString_ReturnsNegative()
	{
		int result = FindCommentStart(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_MultiCharacterDelimiter_FindsDelimiter()
	{
		int result = FindCommentStart("code // comment", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 4 (the space before '//').
		Assert.AreEqual(4, result);
	}

	[TestMethod]
	public void FindCommentStart_DelimiterAtEnd_ReturnsCorrectPosition()
	{
		int result = FindCommentStart("code ;", s_semicolonSyntax);

		// The comment (including leading whitespace) starts at index 4 (the space before ';').
		Assert.AreEqual(4, result);
	}

	[TestMethod]
	public void FindCommentStart_EmptyDelimiter_ReturnsNegative()
	{
		int result = FindCommentStart("code ; comment", new CommentSyntax("", null, null, StringLiteralStyle.None));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_EmptyDelimiterWithStringStyle_ReturnsNegative()
	{
		int result = FindCommentStart("code ; comment", new CommentSyntax("", null, null, StringLiteralStyle.DoubleQuoted));

		Assert.AreEqual(-1, result);
	}

	// ---------------------------------------------------------------------------
	// GetCodeRange
	// ---------------------------------------------------------------------------

	private static TextRange GetCodeRange(string text, CommentSyntax syntax)
		=> CommentHelper.GetCodeRange(text, syntax);

	[TestMethod]
	public void GetCodeRange_LineWithComment_ReturnsCodeBeforeWhitespace()
	{
		TextRange range = GetCodeRange("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		// Code ends at index 10 (the space before ';').
		Assert.AreEqual(10, range.Length);
		Assert.AreEqual("Legend= 42", range.GetText("Legend= 42 ; comment"));
	}

	[TestMethod]
	public void GetCodeRange_NoComment_ReturnsFullText()
	{
		TextRange range = GetCodeRange("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(10, range.Length);
		Assert.AreEqual("Legend= 42", range.GetText("Legend= 42"));
	}

	[TestMethod]
	public void GetCodeRange_CommentOnlyLine_ReturnsEmptyRange()
	{
		TextRange range = GetCodeRange("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
		Assert.IsTrue(range.IsEmpty);
	}

	[TestMethod]
	public void GetCodeRange_EmptyString_ReturnsEmptyRange()
	{
		TextRange range = GetCodeRange(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
	}

	// ---------------------------------------------------------------------------
	// RemoveComments
	// ---------------------------------------------------------------------------

	private static string RemoveLineComment(string text, CommentSyntax syntax)
		=> CommentHelper.RemoveComments(text, syntax);

	[TestMethod]
	public void RemoveLineComment_RemovesCommentAndPrecedingWhitespace()
	{
		string result = RemoveLineComment("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void RemoveLineComment_NoComment_ReturnsOriginal()
	{
		string result = RemoveLineComment("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void RemoveLineComment_CommentOnlyLine_ReturnsEmpty()
	{
		string result = RemoveLineComment("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveLineComment_EmptyString_ReturnsEmpty()
	{
		string result = RemoveLineComment(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveLineComment_NullString_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => RemoveLineComment(null!, s_semicolonSyntax));
	}

	[TestMethod]
	public void RemoveLineComment_LFLineEnding_PreservesNewline()
	{
		string result = RemoveLineComment("Legend= 42 ; comment\n", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42\n", result);
	}

	[TestMethod]
	public void RemoveLineComment_CRLFLineEnding_PreservesLFOnly()
	{
		// The carriage return is removed; the line feed is preserved.
		string result = RemoveLineComment("Legend= 42 ; comment\r\n", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42\n", result);
	}

	[TestMethod]
	public void RemoveLineComment_CRLineEnding_RemovesCarriageReturn()
	{
		string result = RemoveLineComment("Legend= 42 ; comment\r", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void RemoveLineComment_MultipleLines_RemovesCommentsFromAllLines()
	{
		string input = "Line1 ; comment1\nLine2 ; comment2\nLine3";

		string result = RemoveLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\nLine2\nLine3", result);
	}

	[TestMethod]
	public void RemoveLineComment_AllCommentLines_RemovesInterveningNewlines()
	{
		string input = "; a\n; b\n; c";

		string result = RemoveLineComment(input, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveLineComment_TrailingCommentOnlyLine_RemovesPrecedingLineEnding()
	{
		// A comment-only line also removes its preceding line ending.
		string input = "Line1 ; comment\n; comment";

		string result = RemoveLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Line1", result);
	}

	[TestMethod]
	public void RemoveLineComment_CommentOnlyLine_RemovesPrecedingLineEnding()
	{
		string result = RemoveLineComment("Line1\r\n; comment", s_semicolonSyntax);

		Assert.AreEqual("Line1", result);
	}

	// ---------------------------------------------------------------------------
	// MaskComments
	// ---------------------------------------------------------------------------

	private static string MaskLineComment(string text, CommentSyntax syntax)
		=> CommentHelper.MaskComments(text, syntax);

	[TestMethod]
	public void MaskLineComment_ReplacesCommentWithSpaces_PreservesLength()
	{
		string result = MaskLineComment("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42          ", result);
		Assert.AreEqual(20, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_NoComment_ReturnsOriginal()
	{
		string result = MaskLineComment("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void MaskLineComment_EmptyString_ReturnsEmpty()
	{
		string result = MaskLineComment(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void MaskLineComment_NullString_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => MaskLineComment(null!, s_semicolonSyntax));
	}

	[TestMethod]
	public void MaskLineComment_WithTrailingNewline_PreservesNewlineAndLength()
	{
		string input = "Legend= 42 ; comment\n";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.IsTrue(result.EndsWith("\n"));
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_CRLF_MasksCarriageReturnBeforeLineFeed()
	{
		string input = "A ; c\r\nB ; d\r\n";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.IsTrue(result.EndsWith(" \n"));
		Assert.AreEqual(input.Length, result.Length);
		Assert.IsFalse(result.Contains(";"));
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyLine_MasksPrecedingLineEnding()
	{
		// The comment-only line includes the preceding CRLF in its span, so all 11
		// characters are replaced with spaces.
		string input = "Line1\r\n; comment";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Line1" + new string(' ', 11), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_MultipleLines_SpacesMatchOriginalCommentLength()
	{
		string input = "Line1 ; abc\nLine2 ; defgh";

		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual(input.Length, result.Length);
		Assert.IsFalse(result.Contains(";"));
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyLine_BecomesSpacesOfSameLength()
	{
		string input = "; comment";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual(input.Length, result.Length);
		Assert.IsFalse(result.Contains(";"));
	}

	// ---------------------------------------------------------------------------
	// // delimiter - string-aware behavior (C family)
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotedString_ReturnsNegative()
	{
		int result = FindCommentStart("\"url\": \"http://example.com\",", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotesThenRealComment_FindsRealComment()
	{
		const string input = "\"url\": \"http://example.com\", // note";
		int result = FindCommentStart(input, s_cStyleSyntax);

		int realCommentIndex = input.IndexOf("//", 20, StringComparison.Ordinal);
		Assert.AreEqual(realCommentIndex - 1, result);
	}

	[TestMethod]
	public void RemoveLineComment_SlashSlashInsideQuotedString_PreservesUrl()
	{
		string result = RemoveLineComment("\"url\": \"http://example.com\",", s_cStyleSyntax);

		Assert.AreEqual("\"url\": \"http://example.com\",", result);
	}

	[TestMethod]
	public void RemoveLineComment_UrlThenRealComment_RemovesOnlyComment()
	{
		string result = RemoveLineComment("\"url\": \"http://example.com\", // note", s_cStyleSyntax);

		Assert.AreEqual("\"url\": \"http://example.com\",", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedQuote_PreservesSlashSlashInsideString()
	{
		// The \" is an escaped quote, so the // before the closing quote stays inside the string.
		string result = RemoveLineComment("\"path\": \"a\\\"b//c\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"path\": \"a\\\"b//c\"", result);
	}

	[TestMethod]
	public void RemoveLineComment_QuotedSlashSlashAcrossLines_ResetsQuoteStatePerLine()
	{
		string input = "\"path\": \"a//b\"\n\"title\": \"Caves\" // only this is a comment";

		string result = RemoveLineComment(input, s_cStyleSyntax);

		Assert.AreEqual("\"path\": \"a//b\"\n\"title\": \"Caves\"", result);
	}

	// ---------------------------------------------------------------------------
	// StringLiteralStyle - string-aware scanning
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void FindCommentStart_WithoutStringAwareness_FindsHashInsideQuotes()
	{
		// Without string awareness, a `#` inside quotes starts a comment.
		int result = FindCommentStart("url = \"x#y\"", new CommentSyntax("#", null, null, StringLiteralStyle.None));

		Assert.AreEqual(8, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashInsideDoubleQuotes_ReturnsNegative()
	{
		int result = FindCommentStart("url = \"http://x/#y\"", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashInsideSingleQuotes_ReturnsNegative()
	{
		int result = FindCommentStart("url = 'http://x/#y'", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashAfterString_FindsCommentStart()
	{
		int result = FindCommentStart("url = 'x' # note", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		// The comment (including leading whitespace) starts at index 9 (the space before '#').
		Assert.AreEqual(9, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithDoubleQuotedStyle_TreatsDelimiterAsComment()
	{
		// DoubleQuoted tracks only double quotes, so // inside single quotes is treated as a comment.
		int result = FindCommentStart("s = 'a//b'", new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted));

		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithCombinedStyle_ReturnsNegative()
	{
		int result = FindCommentStart("s = 'a//b'", new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_InterleavedQuotes_TracksOuterString()
	{
		// A single quote inside a double-quoted string must not open a new string.
		int result = FindCommentStart("msg = \"it's a // test\" // real", new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		// The comment (including leading whitespace) starts at index 22 (the space before the real '//').
		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void GetCodeRange_PythonHashInSingleQuotes_ReturnsFullText()
	{
		TextRange range = GetCodeRange("url = 'http://x/#y'", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(19, range.Length);
	}

	[TestMethod]
	public void RemoveLineComment_PythonHashInString_PreservesStringAndRemovesComment()
	{
		string result = RemoveLineComment("url = 'http://x/#y' # note", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual("url = 'http://x/#y'", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedSingleQuote_KeepsStringOpen()
	{
		// The \' is an escaped quote, so the # after it stays inside the string.
		string result = RemoveLineComment("s = 'a\\'b#c' # real", new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual("s = 'a\\'b#c'", result);
	}

	[TestMethod]
	public void MaskLineComment_PythonHashInString_PreservesLength()
	{
		string input = "x = 'a#b' # comment";
		string result = MaskLineComment(input, new CommentSyntax("#", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		// Only the comment and its preceding whitespace are masked; the # inside the string is kept.
		Assert.AreEqual("x = 'a#b'" + new string(' ', 10), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithFlag_ReturnsNegative()
	{
		int result = FindCommentStart("s = `a//b`", new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted | StringLiteralStyle.BacktickQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithoutFlag_FindsDelimiter()
	{
		// Without the BacktickQuoted flag, // inside backticks is treated as a comment.
		int result = FindCommentStart("s = `a//b`", new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void RemoveLineComment_BacktickTemplate_PreservesTemplateAndRemovesComment()
	{
		string result = RemoveLineComment(
			"let s = `a//b`; // real",
			new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted | StringLiteralStyle.BacktickQuoted));

		Assert.AreEqual("let s = `a//b`;", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedBacktick_KeepsStringOpen()
	{
		// The \` is an escaped backtick, so the // after it stays inside the template.
		string result = RemoveLineComment(
			"let s = `a\\`b//c` // real",
			new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted | StringLiteralStyle.BacktickQuoted));

		Assert.AreEqual("let s = `a\\`b//c`", result);
	}

	// ---------------------------------------------------------------------------
	// StringLiteralStyle - multi-line (raw) string-aware scanning
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void FindCommentStart_TripleQuotedString_IgnoresDelimiter()
	{
		// The // inside the """ raw string is content, not a comment.
		int result = FindCommentStart("s = \"\"\"a//b\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleQuotedStringThenRealComment_FindsComment()
	{
		int result = FindCommentStart("\"\"\"a\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 7 (the space before '//').
		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleQuotedStringMultiline_IgnoresDelimiter()
	{
		// The // on the second line is inside the multi-line raw string.
		int result = FindCommentStart("\"\"\"a\n// b\n\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_TripleSequenceInsideIsContent()
	{
		// The // inside a four-quote raw string is content; only a four-quote run closes.
		int result = FindCommentStart("\"\"\"\"a\"\"\"b//c\"\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_CommentAfterClose_FindsComment()
	{
		int result = FindCommentStart("\"\"\"\"a\"\"\"b\"\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 13 (the space before '//').
		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedString_IgnoresHash()
	{
		// Python docstring: the # inside ''' is content, not a comment.
		int result = FindCommentStart("'''a#b'''", new CommentSyntax("#", null, null, StringLiteralStyle.TripleSingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedStringThenRealComment_FindsComment()
	{
		int result = FindCommentStart("'''a''' # note", new CommentSyntax("#", null, null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 7 (the space before '#').
		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void RemoveLineComment_TripleQuotedMultiline_PreservesStringAndRemovesRealComment()
	{
		string result = RemoveLineComment("\"\"\"\n// not a comment\n\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"\n// not a comment\n\"\"\"", result);
	}

	[TestMethod]
	public void RemoveLineComment_BacktickTemplateMultiline_PreservesCommentInsideTemplate()
	{
		// The // on the second line is inside the multi-line backtick template.
		string result = RemoveLineComment(
			"let s = `a\n// not a comment\nb`; // real",
			new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted | StringLiteralStyle.BacktickQuoted));

		Assert.AreEqual("let s = `a\n// not a comment\nb`;", result);
	}
}
