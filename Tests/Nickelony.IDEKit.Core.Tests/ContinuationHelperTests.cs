namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests for <see cref="ContinuationHelper"/> with configured comments,
/// trailing whitespace, and continuation markers.
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
		// The comment is stripped before checking the marker.
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
		// A marker at the end is sufficient, so >> also ends with >.
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
		// The > is inside the comment, so it should not count.
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
		// The marker is in the middle of the code, not at the end.
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
		// The comment is stripped before checking the marker.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + ... ; comment", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MultiCharMarkerDuplicated_ReturnsTrue()
	{
		// A line ending in ... followed by another . still ends with ...,
		// so it continues.
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
		// The ... is inside the comment, so it should not count.
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
		// The block comment is stripped before checking the marker.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... /* c */", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_BlockCommentBeforeMarker_ReturnsTrue()
	{
		// The marker after the block comment still counts.
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
		// The block opener inside the line comment is not a comment; the marker before it still counts.
		bool result = ContinuationHelper.IsValidContinuation("value = 1 + 2 ... // /* x */", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_UnclosedBlockCommentAfterMarker_ReturnsTrue()
	{
		// The unclosed block comment runs to the end; the marker before it still counts.
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
		// The block comment and marker are inside the string, so neither counts.
		bool result = ContinuationHelper.IsValidContinuation("\"value /* c */ ...\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	// ---------------------------------------------------------------------------
	// IsValidContinuation - multi-line (raw) string awareness
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void IsValidContinuation_MarkerInsideTripleQuotedString_ReturnsFalse()
	{
		// The ... is inside the """ raw string, so it is not a code marker.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a ...\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerAfterTripleQuotedString_ReturnsTrue()
	{
		// The marker after the closed raw string counts; the closing quotes are not code.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a\"\"\" ...", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsValidContinuation_MarkerInsideTripleQuotedMultiline_ReturnsFalse()
	{
		// The ... on the second line is inside the multi-line raw string.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a\n...\n\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void IsValidContinuation_UnclosedTripleQuotedString_ReturnsFalse()
	{
		// The unclosed raw string runs to the end, so the marker is not code.
		bool result = ContinuationHelper.IsValidContinuation("\"\"\"a ...", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}
}
