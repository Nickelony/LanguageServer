namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins behavior gaps found by the second fresh review: the code-range stop rule, the Unicode
/// blankness of the renamed predicate, the multi-character continuation marker with its whitespace
/// flag, and the planner's configurable delimiter spacing.
/// </summary>
[TestClass]
public sealed class CommentOperationsAdditionalTests
{
	[TestMethod]
	public void GetCodeRange_CodeAfterTheFirstComment_StopsBeforeThatComment()
	{
		var syntax = new CommentSyntax(";", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None);
		var snapshot = new StringTextSnapshot("code /* c */ more ; x");

		TextRange range = CommentOperations.GetCodeRange("code /* c */ more ; x", syntax);

		Assert.AreEqual(new TextRange(0, 5), range);
		Assert.AreEqual("code ", range.GetText(snapshot));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_UnicodeWhitespaceOnly_IsBlank()
	{
		var syntax = new CommentSyntax(";", null, StringLiteralStyle.None);

		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0", syntax));
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0; x", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0x", syntax));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MultiCharacterMarker_RequiresWhitespaceWhenRequested()
	{
		var syntax = new CommentSyntax("%", null, StringLiteralStyle.None);

		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker(
			"value = 1 + ...", syntax, "...", markerMustBePrecededByWhitespace: true));
		Assert.IsFalse(ContinuationOperations.EndsWithContinuationMarker(
			"value=1+...", syntax, "...", markerMustBePrecededByWhitespace: true));
	}

	[TestMethod]
	public void TryCreateEdit_WithoutInsertedSpace_InsertsAndRemovesTheBareDelimiter()
	{
		var syntax = new CommentSyntax("//", null, StringLiteralStyle.None);
		var snapshot = new StringTextSnapshot("value");

		bool created = TextLineCommentPlanner.TryCreateEdit(
			snapshot,
			new TextRange(0, snapshot.TextLength),
			syntax,
			TextLineCommentAction.Comment,
			insertSpaceAfterDelimiter: false,
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);
		Assert.AreEqual("//value", edit.ReplacementText);

		var commented = new StringTextSnapshot("// value");

		created = TextLineCommentPlanner.TryCreateEdit(
			commented,
			new TextRange(0, commented.TextLength),
			syntax,
			TextLineCommentAction.Uncomment,
			insertSpaceAfterDelimiter: false,
			out TextLineCommentEdit uncommentEdit);

		Assert.IsTrue(created);
		Assert.AreEqual("value", uncommentEdit.ReplacementText);
	}
}
