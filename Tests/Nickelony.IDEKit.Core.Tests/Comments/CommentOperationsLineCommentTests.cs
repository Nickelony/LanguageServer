using static Nickelony.IDEKit.Core.Tests.CommentTestSupport;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsLineCommentTests
{
	// Comment syntax used by the tests.
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxFixtures.SemicolonLine;
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyleLine;

	// The JSON-ish URL line reused by the string-awareness tests.
	private const string UrlLine = "\"url\": \"http://example.com\",";

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

		// The delimiter sits at index 14, so the span starts there.
		Assert.AreEqual(14, result);
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
	[DataRow(StringLiteralStyle.None, DisplayName = "WithoutStringAwareness")]
	[DataRow(StringLiteralStyle.DoubleQuoted, DisplayName = "WithStringAwareness")]
	public void FindCommentStart_EmptyDelimiter_ReturnsNegative(StringLiteralStyle stringStyle)
	{
		// An empty delimiter disables line-comment awareness regardless of the string style.
		var syntax = new CommentSyntax(string.Empty, null, stringStyle);

		Assert.AreEqual(-1, FindCommentStart("code ; comment", syntax));
	}

	[TestMethod]
	[DataRow("\"a\\\\\" // c", StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted, DisplayName = "DoubleQuoted")]
	[DataRow("`a\\\\` // c", StringLiteralStyle.BacktickQuoted, DisplayName = "BacktickQuoted")]
	public void FindCommentStart_EscapedBackslashBeforeClosingQuote_FindsTheComment(string text, StringLiteralStyle stringStyle)
	{
		// '\\' is an escaped backslash, so the closing quote still ends the string and the delimiter
		// after it starts a real comment.
		var syntax = new CommentSyntax("//", null, stringStyle);

		Assert.AreEqual(5, FindCommentStart(text, syntax));
	}

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotedString_ReturnsNegative()
	{
		int result = FindCommentStart(UrlLine, s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotesThenRealComment_FindsRealComment()
	{
		const string input = UrlLine + " // note";
		int result = FindCommentStart(input, s_cStyleSyntax);

		// The comment span starts at the space before the real delimiter.
		Assert.AreEqual(UrlLine.Length, result);
	}

	[TestMethod]
	public void FindCommentStart_WithoutStringAwareness_FindsHashInsideQuotes()
	{
		// Without string awareness, a `#` inside quotes starts a comment.
		int result = FindCommentStart("url = \"x#y\"", new CommentSyntax("#", null, StringLiteralStyle.None));

		Assert.AreEqual(8, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashInsideDoubleQuotes_ReturnsNegative()
	{
		int result = FindCommentStart("url = \"http://x/#y\"", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashInsideSingleQuotes_ReturnsNegative()
	{
		int result = FindCommentStart("url = 'http://x/#y'", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_PythonHashAfterString_FindsCommentStart()
	{
		int result = FindCommentStart("url = 'x' # note", CommentSyntaxFixtures.HashLine);

		// The comment (including leading whitespace) starts at index 9 (the space before '#').
		Assert.AreEqual(9, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithDoubleQuotedStyle_TreatsDelimiterAsComment()
	{
		// DoubleQuoted tracks only double quotes, so // inside single quotes is treated as a comment.
		int result = FindCommentStart("s = 'a//b'", new CommentSyntax("//", null, StringLiteralStyle.DoubleQuoted));

		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithCombinedStyle_ReturnsNegative()
	{
		int result = FindCommentStart("s = 'a//b'", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_InterleavedQuotes_TracksOuterString()
	{
		// A single quote inside a double-quoted string must not open a new string.
		int result = FindCommentStart("msg = \"it's a // test\" // real", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		// The comment (including leading whitespace) starts at index 22 (the space before the real '//').
		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithFlag_ReturnsNegative()
	{
		int result = FindCommentStart("s = `a//b`", CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithoutFlag_FindsDelimiter()
	{
		// Without the BacktickQuoted flag, // inside backticks is treated as a comment.
		int result = FindCommentStart("s = `a//b`", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		Assert.AreEqual(6, result);
	}

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
	public void FindCommentStart_TripleQuotedStringMultilineWithCrLf_IgnoresDelimiter()
	{
		// CRLF stays string content in a raw string, so the // on the second line is still inside it.
		int result = FindCommentStart("\"\"\"a\r\n// b\r\n\"\"\"", s_cStyleSyntax);

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
	public void FindCommentStart_SixQuoteClosingRun_RescansSurplusQuotesAsCode()
	{
		// The closer is the opener's three-quote length; the three surplus quotes are re-scanned as
		// code, where they open a new raw string that hides the //. A model that treated the surplus
		// quotes as string content would report a comment here.
		int result = FindCommentStart("\"\"\"a\"\"\"\"\"\"//b\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteClosingRun_OddSurplusHidesComment()
	{
		// The closer is the opener's three-quote length; the single surplus quote is re-scanned as
		// code, where it opens a new double-quoted string that hides the //. This is the documented
		// recovery for the compiler-invalid over-long closer.
		int result = FindCommentStart("\"\"\"a\"\"\"\" // note", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FiveQuoteClosingRun_EvenSurplusRealignsAndFindsComment()
	{
		// The two surplus quotes are re-scanned as code and pair up as an empty string, so the //
		// after them is a real comment. The comment (including leading whitespace) starts at
		// index 9 (the space before '//').
		int result = FindCommentStart("\"\"\"a\"\"\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual(9, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedString_IgnoresHash()
	{
		// Python docstring: the # inside ''' is content, not a comment.
		int result = FindCommentStart("'''a#b'''", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedStringThenRealComment_FindsComment()
	{
		int result = FindCommentStart("'''a''' # note", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 7 (the space before '#').
		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedSixQuotes_ClosesAtTheFirstThree()
	{
		// '''''' is an empty Python string (opener + closer), so the # after it starts a real comment.
		int result = FindCommentStart("'''''' # x", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 6 (the space before '#').
		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_IgnoresDelimiterInsideString()
	{
		// C# raw strings with a four-quote delimiter: the // inside the string is content.
		int result = FindCommentStart("\"\"\"\"a//b\"\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 12 (the space before '//').
		Assert.AreEqual(12, result);
	}

	[TestMethod]
	public void FindCommentStart_QuoteStateResetsAtCarriageReturn()
	{
		// The unterminated double-quoted string ends at the lone CR, so the semicolon is code.
		var doubleQuotedSyntax = new CommentSyntax(";", null, StringLiteralStyle.DoubleQuoted);

		int result = FindCommentStart("\"a\r; c", doubleQuotedSyntax);

		// The comment span starts at the CR that precedes the delimiter.
		Assert.AreEqual(2, result);
	}

	private static string RemoveLineComment(string text, CommentSyntax syntax)
		=> CommentOperations.RemoveComments(text, syntax);

	[TestMethod]
	public void FindComment_LoneCr_EndsCommentAtCarriageReturn()
	{
		CommentSpan? comment = CommentOperations.FindComment("a ; c\rb", s_semicolonSyntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(2, comment.Value.DelimiterStart);
		Assert.AreEqual(5, comment.Value.End);
	}

	[TestMethod]
	public void FindComment_LineCommentWithDelimitersInItsContent_EndsAtTheTerminator()
	{
		// Nothing inside a line comment is recognized, so the block opener, the closer, and the
		// second delimiter all stay content and the span still ends at the line terminator.
		var syntax = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.DoubleQuoted);
		CommentSpan? comment = CommentOperations.FindComment("code // /* x */ // y\nnext", syntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(4, comment.Value.SpanStart);
		Assert.AreEqual(5, comment.Value.DelimiterStart);
		Assert.AreEqual(20, comment.Value.End);
	}

	[TestMethod]
	public void RemoveLineComment_RemovesCommentAndPrecedingWhitespace()
	{
		string result = RemoveLineComment("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void RemoveLineComment_NoComment_ReturnsOriginal()
	{
		const string text = "Legend= 42";

		string result = RemoveLineComment(text, s_semicolonSyntax);

		// The documented fast path returns the original instance when no comment matches.
		Assert.AreSame(text, result);
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
	public void RemoveLineComment_LFLineEnding_PreservesNewline()
	{
		string result = RemoveLineComment("Legend= 42 ; comment\n", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42\n", result);
	}

	[TestMethod]
	public void RemoveLineComment_CRLFLineEnding_PreservesLineEnding()
	{
		// The line terminator is left outside the comment span.
		string result = RemoveLineComment("Legend= 42 ; comment\r\n", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42\r\n", result);
	}

	[TestMethod]
	public void RemoveLineComment_CRLineEnding_PreservesLineEnding()
	{
		string result = RemoveLineComment("Legend= 42 ; comment\r", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42\r", result);
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
		// A comment-only line also removes its preceding line terminator.
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

	[TestMethod]
	public void RemoveLineComment_CommentOnlyLineAfterBlankLine_PreservesBlankLine()
	{
		// The span absorbs only the line terminator directly before the comment line, so the blank line
		// above it stays.
		string result = RemoveLineComment("a\n\n; c\nb", s_semicolonSyntax);

		Assert.AreEqual("a\n\nb", result);
	}

	[TestMethod]
	[DataRow("; a\nb", "\nb", DisplayName = "Lf")]
	[DataRow("; a\r\nb", "\r\nb", DisplayName = "CrLf")]
	public void RemoveLineComment_CommentOnlyFirstLine_LeavesEmptyFirstLine(string text, string expected)
	{
		// A comment-only first line has no preceding line terminator to absorb, so only its own text is
		// removed and the following line keeps its content.
		Assert.AreEqual(expected, RemoveLineComment(text, s_semicolonSyntax));
	}

	[TestMethod]
	public void RemoveLineComment_SlashSlashInsideQuotedString_PreservesUrl()
	{
		string result = RemoveLineComment(UrlLine, s_cStyleSyntax);

		Assert.AreEqual(UrlLine, result);
	}

	[TestMethod]
	public void RemoveLineComment_UrlThenRealComment_RemovesOnlyComment()
	{
		string result = RemoveLineComment(UrlLine + " // note", s_cStyleSyntax);

		Assert.AreEqual(UrlLine, result);
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

	[TestMethod]
	public void RemoveLineComment_PythonHashInString_PreservesStringAndRemovesComment()
	{
		string result = RemoveLineComment("url = 'http://x/#y' # note", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual("url = 'http://x/#y'", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedSingleQuote_KeepsStringOpen()
	{
		// The \' is an escaped quote, so the # after it stays inside the string.
		string result = RemoveLineComment("s = 'a\\'b#c' # real", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual("s = 'a\\'b#c'", result);
	}

	[TestMethod]
	public void RemoveLineComment_BacktickTemplate_PreservesTemplateAndRemovesComment()
	{
		string result = RemoveLineComment(
			"let s = `a//b`; // real",
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a//b`;", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedBacktick_KeepsStringOpen()
	{
		// The \` is an escaped backtick, so the // after it stays inside the template.
		string result = RemoveLineComment(
			"let s = `a\\`b//c` // real",
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a\\`b//c`", result);
	}

	[TestMethod]
	public void RemoveLineComment_FiveQuoteClosingRun_RemovesRealComment()
	{
		string result = RemoveLineComment("\"\"\"a\"\"\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a\"\"\"\"\"", result);
	}

	[TestMethod]
	public void RemoveLineComment_TripleSingleQuotedFourQuoteOpener_TreatsSurplusQuoteAsContent()
	{
		// Python closes a ''' string at the first three quotes, so the fourth opening quote is
		// content and the trailing comment is a real comment.
		string result = RemoveLineComment("''''a''' # note", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		Assert.AreEqual("''''a'''", result);
	}

	[TestMethod]
	public void RemoveLineComment_EscapedFirstQuote_DoesNotOpenRawString()
	{
		// A backslash-escaped quote cannot open a raw string, so the // behind the quotes is a real
		// comment. (The prefix is invalid code; the scanner must still recover.)
		string result = RemoveLineComment("\\\"\"\" // x", s_cStyleSyntax);

		Assert.AreEqual("\\\"\"\"", result);
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
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a\n// not a comment\nb`;", result);
	}

	[TestMethod]
	public void RemoveLineComment_LoneCrLineEnding_PreservesFollowingLine()
	{
		string result = RemoveLineComment("a ; c\rb", s_semicolonSyntax);

		Assert.AreEqual("a\rb", result);
	}

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
	public void MaskLineComment_WithTrailingNewline_PreservesNewlineAndLength()
	{
		string input = "Legend= 42 ; comment\n";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Legend= 42          \n", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_CRLF_PreservesLineEnding()
	{
		string input = "A ; c\r\nB ; d\r\n";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("A    \r\nB    \r\n", result);
		Assert.AreEqual(input.Length, result.Length);
		Assert.IsFalse(result.Contains(";"));
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyLine_PreservesLineEndingAndLength()
	{
		// The comment-only line includes the preceding CRLF in its span; the line terminator is kept so
		// the line structure stays intact while the delimiter and the comment text become spaces.
		string input = "Line1\r\n; comment";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\r\n" + new string(' ', 9), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyLineWithLoneCr_PreservesLineEnding()
	{
		// A lone CR ending the preceding line is kept as well, so the line count does not change.
		string input = "Line1\r; comment";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\r" + new string(' ', 9), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyLineAfterBlankLine_PreservesLineStructure()
	{
		string input = "a\n\n; c\nb";

		string result = MaskLineComment(input, s_semicolonSyntax);

		// Only the line terminator directly before the comment is absorbed, so the blank line stays.
		Assert.AreEqual("a\n\n   \nb", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_SpacesMatchOriginalCommentLength()
	{
		// Every masked character becomes a space, including the whitespace and the delimiter.
		Assert.AreEqual("         ", MaskLineComment("; comment", s_semicolonSyntax));
		Assert.AreEqual("Line1      \nLine2        ", MaskLineComment("Line1 ; abc\nLine2 ; defgh", s_semicolonSyntax));
	}

	[TestMethod]
	public void MaskLineComment_CommentOnlyFirstLine_MasksTextAndKeepsLineStructure()
	{
		// The first line has no preceding line terminator to absorb, so the delimiter and text become
		// spaces while the line structure stays intact.
		string input = "; a\nb";
		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("   \nb", result);
		Assert.AreEqual(input.Length, result.Length);

		string crlfResult = MaskLineComment("; a\r\nb", s_semicolonSyntax);

		Assert.AreEqual("   \r\nb", crlfResult);
	}

	[TestMethod]
	public void MaskLineComment_PythonHashInString_PreservesLength()
	{
		string input = "x = 'a#b' # comment";
		string result = MaskLineComment(input, CommentSyntaxFixtures.HashLine);

		// Only the comment and its preceding whitespace are masked; the # inside the string is kept.
		Assert.AreEqual("x = 'a#b'" + new string(' ', 10), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskLineComment_LoneCrLineEnding_PreservesLineBreakAndLength()
	{
		string input = "a ; c\rb";

		string result = MaskLineComment(input, s_semicolonSyntax);

		Assert.AreEqual("a    \rb", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	private static string MaskLineComment(string text, CommentSyntax syntax)
		=> CommentOperations.MaskComments(text, syntax);
}
