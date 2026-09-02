namespace Nickelony.IDEKit.Core.Comments.Tests;

/// <summary>
/// Tests for the <see cref="TextLineSyntaxService"/> comment-aware line helpers.
/// </summary>
[TestClass]
public sealed class TextLineSyntaxServiceTests
{
	private static readonly TextLineSyntaxService s_semicolonService = new(new CommentSyntax(";", null, null, StringLiteralStyle.None));
	private static readonly TextLineSyntaxService s_cStyleService = new(new CommentSyntax("//", null, null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted));

	[TestMethod]
	public void IsEmptyOrComments_NullOrWhitespace_ReturnsTrue()
	{
		Assert.IsTrue(s_semicolonService.IsEmptyOrComments(null));
		Assert.IsTrue(s_semicolonService.IsEmptyOrComments(string.Empty));
		Assert.IsTrue(s_semicolonService.IsEmptyOrComments("   \t "));
	}

	[TestMethod]
	public void IsEmptyOrComments_CommentOnlyLine_ReturnsTrue()
	{
		Assert.IsTrue(s_semicolonService.IsEmptyOrComments("; comment"));
		Assert.IsTrue(s_semicolonService.IsEmptyOrComments("   ; comment"));
	}

	[TestMethod]
	public void IsEmptyOrComments_CodeLine_ReturnsFalse()
	{
		Assert.IsFalse(s_semicolonService.IsEmptyOrComments("Legend= 42"));
		Assert.IsFalse(s_cStyleService.IsEmptyOrComments("var x = 1"));
	}

	[TestMethod]
	public void IsEmptyOrComments_CommentLine_ReturnsTrue()
	{
		Assert.IsTrue(s_cStyleService.IsEmptyOrComments("// not leading"));
	}

	[TestMethod]
	public void IsEmptyOrComments_NoLineCommentDelimiter_OnlyChecksWhitespace()
	{
		var service = new TextLineSyntaxService(new CommentSyntax(null, "/*", "*/", StringLiteralStyle.None));

		Assert.IsTrue(service.IsEmptyOrComments("   "));
		Assert.IsFalse(service.IsEmptyOrComments("/* block */"));
	}

	[TestMethod]
	public void RemoveComments_RemovesCommentAndPrecedingWhitespace()
	{
		// The comment span includes the leading whitespace, so no trailing space remains.
		Assert.AreEqual("Legend= 42", s_semicolonService.RemoveComments("Legend= 42 ; comment"));
		Assert.AreEqual("var x = 1", s_cStyleService.RemoveComments("var x = 1 // comment"));
	}

	[TestMethod]
	public void EscapeComments_MasksCommentPreservingLength()
	{
		Assert.AreEqual("Legend= 42          ", s_semicolonService.EscapeComments("Legend= 42 ; comment"));
	}

	[TestMethod]
	public void RemoveComments_StringLiteralContentIsPreserved()
	{
		Assert.AreEqual("var s = \"a//b\"", s_cStyleService.RemoveComments("var s = \"a//b\" // c"));
	}
}
