namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsBlockCommentTests
{
	// Comment syntax used by the tests.
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyle;
	private static readonly CommentSyntax s_luaSyntax = CommentSyntaxFixtures.Lua;

	[TestMethod]
	public void FindBlockCommentStart_LineWithComment_ReturnsOpenDelimiterIndex()
	{
		int result = FindBlockCommentStart("code /* comment */", s_cStyleSyntax);

		// The comment opener starts at index 5.
		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_NoComment_ReturnsNegative()
	{
		int result = FindBlockCommentStart("code", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_CommentOnly_ReturnsZero()
	{
		int result = FindBlockCommentStart("/* comment */", s_cStyleSyntax);

		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_UnclosedComment_ReturnsOpenDelimiterIndex()
	{
		int result = FindBlockCommentStart("code /* comment", s_cStyleSyntax);

		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_EmptyString_ReturnsNegative()
	{
		int result = FindBlockCommentStart(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_InsideQuotedString_IsIgnored()
	{
		// The configured string rules keep the `/*` inside the quoted string from being treated as an opener.
		int result = FindBlockCommentStart("\"code /* x */\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_CommentAfterString_FindsComment()
	{
		int result = FindBlockCommentStart("\"code\"/* x */", s_cStyleSyntax);

		// The opener follows the closing quote at index 6.
		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_LuaBlockDelimiter_WithoutStringAwareness_FindsDelimiter()
	{
		// Without long-bracket string awareness, `--[[ ... ]]` is treated as a block comment.
		int result = FindBlockCommentStart("'x --[[ c ]]", s_luaSyntax);

		Assert.AreEqual(3, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_LuaBlockDelimiter_InSingleQuotedString_ReturnsNegative()
	{
		int result = FindBlockCommentStart("'x --[[ c ]]", new CommentSyntax("--", new BlockCommentSyntax("--[[", "]]"), StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_OpenDelimiterInsideLineComment_ReturnsNegative()
	{
		// The `/*` is inside a `//` line comment, so it is not a comment opener.
		int result = FindBlockCommentStart("// not /* a comment */", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_BlockCommentAfterLineCommentOnNextLine_FindsComment()
	{
		// The line comment ends at the line terminator; the block comment on the next line is real.
		int result = FindBlockCommentStart("code // note\n/* real */", s_cStyleSyntax);

		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_LineCommentPreventsBlockDetection()
	{
		// The `/*` is inside the configured `#` line comment, so it is not an opener.
		int result = FindBlockCommentStart("# note /* x */", new CommentSyntax("#", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_HashIsNotLineDelimiter()
	{
		// The configured `//` line delimiter does not recognize `#`, so the opener is found.
		int result = FindBlockCommentStart("# note /* x */", s_cStyleSyntax);

		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_TripleQuotedString_IgnoresOpener()
	{
		// The /* inside the """ raw string is content, not a comment opener.
		int result = FindBlockCommentStart("\"\"\"a/* b */\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_TripleQuotedStringMultiline_IgnoresOpener()
	{
		// The opener on the second line is inside the multi-line raw string.
		int result = FindBlockCommentStart("\"\"\"a\n/* b */\n\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_FourQuoteRawString_OpenerInsideIsContent()
	{
		// The /* inside a four-quote raw string is content; only a four-quote run closes.
		int result = FindBlockCommentStart("\"\"\"\"a/* b */\"\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindComment_CloserOverlapsOpenerCharacters_DoesNotCloseEarly()
	{
		// The '*' of the opener must not pair with the following '/' to form a closer.
		CommentSpan? comment = CommentOperations.FindComment("/*/*/", s_cStyleSyntax);

		Assert.IsTrue(comment.HasValue);
		Assert.AreEqual(0, comment.Value.SpanStart);
		Assert.AreEqual(5, comment.Value.End);
	}

	[TestMethod]
	public void RemoveBlockComment_RemovesCommentSpan()
	{
		string result = RemoveBlockComment("a + /* c */ b", s_cStyleSyntax);

		// Whitespace around the comment is preserved.
		Assert.AreEqual("a +  b", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NoComment_ReturnsOriginal()
	{
		string result = RemoveBlockComment("a + b", s_cStyleSyntax);

		Assert.AreEqual("a + b", result);
	}

	[TestMethod]
	public void RemoveBlockComment_CommentOnly_ReturnsEmpty()
	{
		string result = RemoveBlockComment("/* comment */", s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComment_EmptyString_ReturnsEmpty()
	{
		string result = RemoveBlockComment(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComment_UnclosedComment_RemovesToEnd()
	{
		string result = RemoveBlockComment("a /* c", s_cStyleSyntax);

		Assert.AreEqual("a ", result);
	}

	[TestMethod]
	public void RemoveBlockComment_MultipleComments_RemovesAll()
	{
		string result = RemoveBlockComment("/* a */ x /* b */", s_cStyleSyntax);

		Assert.AreEqual(" x ", result);
	}

	[TestMethod]
	public void RemoveBlockComment_AdjacentComments_RemovesBoth()
	{
		// Both adjacent block comments should be removed.
		string result = RemoveBlockComment("a /* one *//* two */ b", s_cStyleSyntax);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComment_MultiLineComment_RemovesAcrossLines()
	{
		string result = RemoveBlockComment("/* a\nb */ x", s_cStyleSyntax);

		Assert.AreEqual(" x", result);
	}

	[TestMethod]
	public void RemoveBlockComment_CommentInsideString_IsPreserved()
	{
		// The `/* ... */` inside the string is not a comment; the real one is removed.
		string result = RemoveBlockComment("\"a /* x */\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"a /* x */\"", result);
	}

	[TestMethod]
	public void RemoveBlockComment_OpenerInsideLineComment_IsPreserved()
	{
		// The `/* ... */` is inside a `//` line comment, so it is not removed.
		string result = RemoveBlockComment("code // note /* x */", s_cStyleSyntax);

		Assert.AreEqual("code // note /* x */", result);
	}

	[TestMethod]
	public void RemoveBlockComment_BlockCommentAfterLineCommentOnNextLine_Removed()
	{
		string result = RemoveBlockComment("code // note\n/* real */", s_cStyleSyntax);

		Assert.AreEqual("code // note\n", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedNotAllowed_ClosesAtFirstCloser()
	{
		string result = RemoveBlockComment("/* a /* b */ c", s_cStyleSyntax);

		Assert.AreEqual(" c", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_ClosesAtFinalCloser()
	{
		string result = RemoveBlockComment("/* a /* b */ c */", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedWithCrLf_ClosesAtFinalCloser()
	{
		string result = RemoveBlockComment(
			"a /* one\r\n/* two */\r\nthree */ b",
			CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_PreservesOuterComment()
	{
		string result = RemoveBlockComment("x /* a /* b */ c */ y", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual("x  y", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_AdjacentClosers_KeepOuterOpenOnShortRun()
	{
		// Two closers close both levels; the third ']' is content, so the outer comment stays open
		// through the end of the text.
		string result = RemoveBlockComment(
			"a --[[ x --[[ y ]]] b",
			CommentSyntaxFixtures.LuaNested);

		Assert.AreEqual("a ", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_AdjacentClosers_CloseBothLevels()
	{
		// Four closers close the nested and the outer comment; the text after them stays.
		string result = RemoveBlockComment(
			"a --[[ x --[[ y ]]]] b",
			CommentSyntaxFixtures.LuaNested);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_AdjacentCStyleClosers_CloseBothLevels()
	{
		string result = RemoveBlockComment(
			"/* a /* b */*/ c",
			CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(" c", result);
	}

	[TestMethod]
	public void RemoveBlockComment_TripleQuotedString_PreservesInsideComment()
	{
		string result = RemoveBlockComment("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a/* b */\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComment_TripleQuotedString_CommentDirectlyAfterCloser()
	{
		// A comment immediately after the raw string is still removed.
		string result = RemoveBlockComment("\"\"\"a\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComment_TripleQuotedMultiline_PreservesInsideComment()
	{
		string result = RemoveBlockComment("\"\"\"\n/* c */\n\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"\n/* c */\n\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComment_CloserOverlapsOpenerCharacters_RemovesWholeComment()
	{
		// The '//' inside the still-open block comment must not start a line comment.
		string result = RemoveBlockComment("x/*/ y // z */ w", s_cStyleSyntax);

		Assert.AreEqual("x w", result);
	}

	[TestMethod]
	public void RemoveBlockComment_PascalDelimiters_CloserOverlapKeepsCommentOpen()
	{
		// '(*)' is an unclosed comment in Pascal; the ')' after '*' must not close it.
		var pascalSyntax = new CommentSyntax("//", new BlockCommentSyntax("(*", "*)"), StringLiteralStyle.None);

		string result = RemoveBlockComment("(*) x", pascalSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedCloserOverlapsOpener_KeepsOuterCommentOpen()
	{
		// The nested opener's '*' must not pair with the following '/' to close the nested comment.
		string result = RemoveBlockComment("/* a /*/ b */ x", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(string.Empty, result);
	}

	private static int FindBlockCommentStart(string text, CommentSyntax syntax)
	{
		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, syntax))
		{
			if (span.IsBlockComment)
				return span.DelimiterStart;
		}

		return -1;
	}

	private static string RemoveBlockComment(string text, CommentSyntax syntax)
		=> CommentOperations.RemoveBlockComments(text, syntax);

	[TestMethod]
	public void MaskBlockComment_ReplacesCommentWithSpaces_PreservesLength()
	{
		string result = MaskBlockComment("a /* c */ b", s_cStyleSyntax);

		// "/* c */" is 7 characters, replaced by spaces; surrounding whitespace is kept.
		Assert.AreEqual("a" + new string(' ', 9) + "b", result);
		Assert.AreEqual(11, result.Length);
	}

	[TestMethod]
	public void MaskBlockComment_NoComment_ReturnsOriginal()
	{
		string result = MaskBlockComment("a + b", s_cStyleSyntax);

		Assert.AreEqual("a + b", result);
	}

	[TestMethod]
	public void MaskBlockComment_EmptyString_ReturnsEmpty()
	{
		string result = MaskBlockComment(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void MaskBlockComment_MultiLine_PreservesNewlinesAndLength()
	{
		string result = MaskBlockComment("/* a\nb */", s_cStyleSyntax);

		// The line terminator inside the comment is preserved so line numbers stay stable.
		Assert.AreEqual("    \n    ", result);
		Assert.AreEqual(9, result.Length);
	}

	[TestMethod]
	public void MaskBlockComment_MultiLineWithCrLf_PreservesLineEndings()
	{
		string input = "/* a\r\nb */";

		string result = MaskBlockComment(input, s_cStyleSyntax);

		Assert.AreEqual("    \r\n    ", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskBlockComment_LoneCrLineEnding_PreservesLineBreak()
	{
		// A lone CR inside a block comment used to become a space, which merged lines.
		string input = "/* a\rb */";

		string result = MaskBlockComment(input, s_cStyleSyntax);

		Assert.AreEqual("    \r    ", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskBlockComment_UnclosedComment_MasksToEnd()
	{
		string result = MaskBlockComment("a /* c", s_cStyleSyntax);

		Assert.AreEqual("a" + new string(' ', 5), result);
		Assert.AreEqual(6, result.Length);
	}

	[TestMethod]
	public void MaskBlockComment_CommentInsideString_IsPreserved()
	{
		string result = MaskBlockComment("\"a /* x */\"", s_cStyleSyntax);

		Assert.AreEqual("\"a /* x */\"", result);
	}

	[TestMethod]
	public void MaskBlockComment_OpenerInsideLineComment_IsPreserved()
	{
		string result = MaskBlockComment("code // note /* x */", s_cStyleSyntax);

		Assert.AreEqual("code // note /* x */", result);
	}

	[TestMethod]
	public void MaskBlockComment_TripleQuotedString_PreservesInsideComment()
	{
		string result = MaskBlockComment("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		// Only the real comment is masked; the one inside the raw string is kept.
		Assert.AreEqual("\"\"\"a/* b */\"\"\"" + new string(' ', 10), result);
	}

	private static string MaskBlockComment(string text, CommentSyntax syntax)
		=> CommentOperations.MaskBlockComments(text, syntax);
}
