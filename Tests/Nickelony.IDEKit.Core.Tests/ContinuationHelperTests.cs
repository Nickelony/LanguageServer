namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests continuation markers with whitespace, comments, and string content.
/// </summary>
[TestClass]
public sealed class ContinuationHelperTests
{
	private static readonly CommentSyntax s_semicolonSyntax = new(";", null, null, StringLiteralStyle.None);
	private static readonly CommentSyntax s_cStyleSyntax = new("//", "/*", "*/", StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);
	private static readonly CommentSyntax s_semicolonBlockSyntax = new(";", "/*", "*/", StringLiteralStyle.None);
	private const char ContinuationMarker = '>';

	// ---------------------------------------------------------------------------
	// IsValidContinuation
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void IsValidContinuation_WithMarker_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 >", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_WithMarkerAndTrailingWhitespace_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 > ", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_WithMarkerAndComment_ReturnsTrue()
	{
		// The marker remains after the comment is removed.
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 > ; comment", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_WithMarkerWhitespaceAndComment_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 >   ; comment", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_DuplicateMarker_ReturnsTrue()
	{
		// The final marker position determines the result.
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 >>", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_NoMarker_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerInComment_ReturnsFalse()
	{
		// A marker inside a comment does not count.
		bool result = ContinuationHelper.IsValidContinuation("Legend= 42 ; > not a marker", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_EmptyString_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation(string.Empty, s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_CommentOnlyLine_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("; just a comment", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_WhitespaceOnlyLine_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("   ", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerNotAtEndOfCode_ReturnsFalse()
	{
		// A marker in the middle of the code does not count.
		bool result = ContinuationHelper.IsValidContinuation("Legend= > 42", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_OnlyMarker_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation(">", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	// ---------------------------------------------------------------------------
	// IsValidContinuation - multi-character markers
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void IsValidContinuation_MultiCharMarker_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ...", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerWithTrailingWhitespace_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ...   ", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerWithComment_ReturnsTrue()
	{
		// The marker remains after the comment is removed.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ... ; comment", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerDuplicated_ReturnsTrue()
	{
		// The line still ends with the marker despite the extra period.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ....", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_PartialMultiCharMarker_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ..", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerLongerThanCode_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("..", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerInComment_ReturnsFalse()
	{
		// A marker inside a comment does not count.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 ; ...", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_EmptyMarker_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ...", s_semicolonSyntax, ReadOnlySpan<char>.Empty);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_CharOverloadMatchesSpanOverload()
	{
		bool charResult = ContinuationHelper.IsValidContinuation("Legend= 42 >", s_semicolonSyntax, ContinuationMarker);
		bool spanResult = ContinuationHelper.IsValidContinuation("Legend= 42 >", s_semicolonSyntax, ">");

		Assert.AreEqual(charResult, spanResult);
	}

	// ---------------------------------------------------------------------------
	// IsValidContinuation - block comment handling
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void IsValidContinuation_BlockCommentAfterMarker_ReturnsTrue()
	{
		// The marker remains after the comment is removed.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... /* c */", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_BlockCommentBeforeMarker_ReturnsTrue()
	{
		// A marker after the block comment counts.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 /* c */ ...", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_BlockAndLineComments_ReturnsTrue()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... /* c */ // trailing", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_BlockOpenerInsideLineComment_ReturnsTrue()
	{
		// Text inside a line comment does not affect the marker before it.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... // /* x */", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_UnclosedBlockCommentAfterMarker_ReturnsTrue()
	{
		// An unclosed comment after the marker does not affect the result.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... /* c", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_NoMarkerWithBlockComment_ReturnsFalse()
	{
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 /* c */", s_semicolonBlockSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_BlockMarkerInString_ReturnsFalse()
	{
		// The comment and marker inside the string do not count.
		bool result = ContinuationHelper.IsValidContinuation("\"value /* c */ ...\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	// ---------------------------------------------------------------------------
	// IsValidContinuation - multi-line (raw) string awareness
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void IsValidContinuation_MarkerInsideTripleQuotedString_ReturnsFalse()
	{
		// A marker inside the raw string does not count.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a ...\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerAfterTripleQuotedString_ReturnsTrue()
	{
		// A marker after the closed raw string counts.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a\"\"\" ...", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerInsideTripleQuotedMultiline_ReturnsFalse()
	{
		// A marker inside the multi-line raw string does not count.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a\n...\n\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_UnclosedTripleQuotedString_ReturnsFalse()
	{
		// A marker inside an unclosed raw string does not count.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a ...", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}
}
