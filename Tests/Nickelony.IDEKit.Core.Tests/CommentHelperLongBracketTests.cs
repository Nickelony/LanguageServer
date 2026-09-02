namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests for Lua-style long-bracket string awareness in
/// <see cref="CommentHelper"/> through <see cref="StringLiteralStyle.LongBracketQuoted"/>.
/// </summary>
[TestClass]
public sealed class CommentHelperLongBracketTests
{
	private static readonly CommentSyntax s_luaSyntax = new("--", null, null, StringLiteralStyle.LongBracketQuoted);

	// ---------------------------------------------------------------------------
	// FindCommentStart
	// ---------------------------------------------------------------------------

	private static int FindCommentStart(string text, CommentSyntax syntax)
		=> CommentHelper.FindComment(text, syntax) is { } comment ? comment.Start : -1;

	[TestMethod]
	public void FindCommentStart_NoLongBrackets_DetectsLineComment()
	{
		int result = FindCommentStart("local x = 1 -- comment", s_luaSyntax);

		// The comment span includes the leading whitespace before '--'.
		Assert.AreEqual(11, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentDelimiterInsideDoubleBracket_IsNotAComment()
	{
		// The -- inside [[ ]] is string content, not a comment.
		int result = FindCommentStart("local s = [[a -- b]] -- after", s_luaSyntax);

		// The real comment starts at the "-- after" suffix.
		Assert.AreEqual(20, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentDelimiterInsideEqualsBracket_IsNotAComment()
	{
		int result = FindCommentStart("local s = [=[a -- b]=] -- after", s_luaSyntax);

		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void FindCommentStart_UnclosedLongBracket_ConsumesRestOfText()
	{
		// An unterminated long bracket runs to the end of the text, so no comment is found.
		int result = FindCommentStart("local s = [[unterminated -- not a comment", s_luaSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_ClosingBracketWithMismatchedEquals_DoesNotClose()
	{
		// [=[ requires ]=]; the plain ]] inside is content, so the string stays open.
		int result = FindCommentStart("local s = [=[a ]] b]=] -- after", s_luaSyntax);

		Assert.IsTrue(result >= 0);
	}

	[TestMethod]
	public void FindCommentStart_MultiLineLongString_SpansLines()
	{
		int result = FindCommentStart("local s = [[line1\n-- not a comment\nline2]] -- after", s_luaSyntax);

		Assert.IsTrue(result >= 0);
		Assert.IsTrue(FindCommentStart("local s = [[line1\nline2]]", s_luaSyntax) == -1);
	}

	[TestMethod]
	public void FindCommentStart_WithoutLongBracketFlag_TreatsBracketsAsCode()
	{
		// Without the LongBracketQuoted flag, the -- inside the brackets is a comment.
		var plainSyntax = new CommentSyntax("--", null, null, StringLiteralStyle.None);
		int result = FindCommentStart("local s = [[a -- b]]", plainSyntax);

		Assert.IsTrue(result >= 0);
	}

	// ---------------------------------------------------------------------------
	// RemoveComments / MaskComments
	// ---------------------------------------------------------------------------

	[TestMethod]
	public void RemoveComments_CommentAfterLongString_KeepsString()
	{
		string result = CommentHelper.RemoveComments("local s = [[a -- b]] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [[a -- b]]", result);
	}

	[TestMethod]
	public void RemoveComments_EqualsBracketString_KeepsString()
	{
		string result = CommentHelper.RemoveComments("local s = [=[a -- b]=] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [=[a -- b]=]", result);
	}

	[TestMethod]
	public void MaskComments_CommentAfterLongString_MasksCommentNotString()
	{
		string result = CommentHelper.MaskComments("local s = [[a -- b]] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [[a -- b]]         ", result);
	}
}
