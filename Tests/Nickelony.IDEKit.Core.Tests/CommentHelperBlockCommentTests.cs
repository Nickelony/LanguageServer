using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests block-comment detection, masking, and removal by <see cref="CommentHelper"/>
/// using explicit block-comment syntax.
/// </summary>
[TestClass]
public sealed class CommentHelperBlockCommentTests
{
	// Each test supplies the comment syntax explicitly.
	private static readonly StringLiteralStyle s_cStyle = StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted;
	private static readonly CommentSyntax s_cStyleSyntax = new("//", "/*", "*/", s_cStyle);
	private static readonly CommentSyntax s_luaSyntax = new("--", "--[[", "]]", StringLiteralStyle.None);

	// ---------------------------------------------------------------------------
	// FindBlockCommentStart
	// ---------------------------------------------------------------------------

	private static int FindBlockCommentStart(string text, CommentSyntax syntax)
		=> CommentHelper.FindBlockComment(text, syntax) is { } comment ? comment.DelimiterStart : -1;

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
	public void FindBlockCommentStart_LuaDelimiter_WithoutStringAwarenessFindsDelimiter()
	{
		// Without long-bracket string awareness, `--[[ ... ]]` is treated as a block comment.
		int result = FindBlockCommentStart("'x --[[ c ]]", s_luaSyntax);

		Assert.AreEqual(3, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_LuaDelimiterInSingleQuotedString_ReturnsNegative()
	{
		int result = FindBlockCommentStart("'x --[[ c ]]", new CommentSyntax("--", "--[[", "]]", StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_EmptyOpenDelimiter_ReturnsNegative()
	{
		int result = FindBlockCommentStart("code /* c */", new CommentSyntax("//", "", "*/", s_cStyle));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_EmptyCloseDelimiter_ReturnsNegative()
	{
		int result = FindBlockCommentStart("code /* c */", new CommentSyntax("//", "/*", "", s_cStyle));

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
		// The line comment ends at the line break; the block comment on the next line is real.
		int result = FindBlockCommentStart("code // note\n/* real */", s_cStyleSyntax);

		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_LineCommentPreventsBlockDetection()
	{
		// The `/*` is inside the configured `#` line comment, so it is not an opener.
		int result = FindBlockCommentStart("# note /* x */", new CommentSyntax("#", "/*", "*/", StringLiteralStyle.None));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindBlockCommentStart_HashIsNotLineDelimiter()
	{
		// The configured `//` line delimiter does not recognize `#`, so the opener is found.
		int result = FindBlockCommentStart("# note /* x */", s_cStyleSyntax);

		Assert.AreEqual(7, result);
	}

	// ---------------------------------------------------------------------------
	// GetCodeRange
	// ---------------------------------------------------------------------------

	private static TextRange GetCodeRange(string text, CommentSyntax syntax)
		=> CommentHelper.GetCodeRange(text, syntax);

	[TestMethod]
	public void GetCodeRange_LineWithComment_ReturnsCodeBeforeOpener()
	{
		TextRange range = GetCodeRange("code /* comment */", s_cStyleSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(5, range.Length); // The code range ends where the comment opener begins.

		Assert.AreEqual("code ", range.GetText("code /* comment */"));
	}

	[TestMethod]
	public void GetCodeRange_NoComment_ReturnsFullText()
	{
		TextRange range = GetCodeRange("code", s_cStyleSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(4, range.Length);
		Assert.AreEqual("code", range.GetText("code"));
	}

	[TestMethod]
	public void GetCodeRange_CommentOnly_ReturnsEmptyRange()
	{
		TextRange range = GetCodeRange("/* comment */", s_cStyleSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
	}

	[TestMethod]
	public void GetCodeRange_EmptyString_ReturnsEmptyRange()
	{
		TextRange range = GetCodeRange(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
	}

	// ---------------------------------------------------------------------------
	// RemoveBlockComments
	// ---------------------------------------------------------------------------

	private static string RemoveBlockComment(string text, CommentSyntax syntax)
		=> CommentHelper.RemoveBlockComments(text, syntax);

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
	public void RemoveBlockComment_NullString_ReturnsEmpty()
	{
		string result = RemoveBlockComment(null!, s_cStyleSyntax);

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
		// The second opener immediately follows the first closer, so the closer residue
		// must not hide it.
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
		string result = RemoveBlockComment("/* a /* b */ c */", new CommentSyntax("//", "/*", "*/", s_cStyle, allowNestedBlockComments: true));

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComment_NestedAllowed_PreservesOuterComment()
	{
		string result = RemoveBlockComment("x /* a /* b */ c */ y", new CommentSyntax("//", "/*", "*/", s_cStyle, allowNestedBlockComments: true));

		Assert.AreEqual("x  y", result);
	}

	// ---------------------------------------------------------------------------
	// MaskBlockComments
	// ---------------------------------------------------------------------------

	private static string MaskBlockComment(string text, CommentSyntax syntax)
		=> CommentHelper.MaskBlockComments(text, syntax);

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
	public void MaskBlockComment_NullString_ReturnsEmpty()
	{
		string result = MaskBlockComment(null!, s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void MaskBlockComment_MultiLine_PreservesNewlinesAndLength()
	{
		string result = MaskBlockComment("/* a\nb */", s_cStyleSyntax);

		// The line break inside the comment is preserved so line numbers stay stable.
		Assert.AreEqual("    \n    ", result);
		Assert.AreEqual(9, result.Length);
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

	// ---------------------------------------------------------------------------
	// Multi-line (raw) string-aware scanning
	// ---------------------------------------------------------------------------

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
	public void RemoveBlockComment_TripleQuotedString_PreservesInsideComment()
	{
		string result = RemoveBlockComment("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a/* b */\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComment_TripleQuotedString_CommentDirectlyAfterCloser()
	{
		// The real comment begins immediately after the raw-string closer with no space,
		// so the closer residue must not hide its opener.
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
	public void MaskBlockComment_TripleQuotedString_PreservesInsideComment()
	{
		string result = MaskBlockComment("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		// Only the real comment is masked; the one inside the raw string is kept.
		Assert.AreEqual("\"\"\"a/* b */\"\"\"" + new string(' ', 10), result);
	}
}
