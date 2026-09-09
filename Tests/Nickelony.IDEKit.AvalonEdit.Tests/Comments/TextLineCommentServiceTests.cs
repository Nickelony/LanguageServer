using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Comments;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// Verifies the AvalonEdit application half of line-comment editing: the editor document edit, the
/// edit-target routing, the no-op skip, and the selection restore. The edit computation itself is
/// covered by the Core <c>TextLineCommentPlannerTests</c>.
/// </summary>
[STATestClass]
public sealed class TextLineCommentServiceTests
{
	private static readonly CommentSyntax s_doubleSlashSyntax = new("//", null, StringLiteralStyle.None);

	[TestMethod]
	public void TryCreateEdit_DocumentWithoutText_ReturnsFalse()
	{
		var document = new TextDocument(string.Empty);
		var service = new TextLineCommentService();

		bool created = service.TryCreateEdit(
			document,
			new TextRange(0, 0),
			s_doubleSlashSyntax,
			TextLineCommentAction.Comment,
			insertSpaceAfterDelimiter: true,
			out TextLineCommentEdit edit);

		Assert.IsFalse(created);
		Assert.AreEqual(default, edit);
	}

	[TestMethod]
	public void TryCreateEdit_NoLineCommentDelimiter_ReturnsFalse()
	{
		var document = new TextDocument("first");
		var service = new TextLineCommentService();
		var syntaxWithoutLineComments = new CommentSyntax(null, null, StringLiteralStyle.None);

		bool created = service.TryCreateEdit(
			document,
			new TextRange(0, document.TextLength),
			syntaxWithoutLineComments,
			TextLineCommentAction.Comment,
			insertSpaceAfterDelimiter: true,
			out TextLineCommentEdit edit);

		Assert.IsFalse(created);
		Assert.AreEqual(default, edit);
	}

	[TestMethod]
	public void TryCreateEdit_CommentTransformation_ReturnsThePlannedEdit()
	{
		var document = new TextDocument("first" + "\r\n" + "  second");
		var service = new TextLineCommentService();

		bool created = service.TryCreateEdit(
			document,
			new TextRange(0, document.TextLength),
			s_doubleSlashSyntax,
			TextLineCommentAction.Comment,
			insertSpaceAfterDelimiter: true,
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);
		Assert.AreEqual(0, edit.ReplaceRange.Offset);
		Assert.AreEqual(document.TextLength, edit.ReplaceRange.Length);
		Assert.AreEqual("// first" + "\r\n" + "  // second", edit.ReplacementText);
	}

	[TestMethod]
	public void TryCreateEdit_NoOpTransformation_ReturnsAnEditWithIdenticalText()
	{
		var document = new TextDocument("first");
		var service = new TextLineCommentService();

		// Removing a delimiter the line does not have leaves the text unchanged; the planner still
		// reports the edit, and ApplyEdit is what skips the identical replacement.
		bool created = service.TryCreateEdit(
			document,
			new TextRange(0, document.TextLength),
			s_doubleSlashSyntax,
			TextLineCommentAction.Uncomment,
			insertSpaceAfterDelimiter: true,
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);
		Assert.AreEqual("first", edit.ReplacementText);
	}

	[TestMethod]
	public void ApplyEdit_CommentsSelectedLines()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "  second");
		var service = new TextLineCommentService();

		editor.SelectAll();
		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment);

		string expected = "// first" + "\r\n" + "  // second";

		Assert.AreEqual(expected, editor.Text);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(expected, editor.SelectedText);
	}

	[TestMethod]
	public void ApplyEdit_NoLineCommentDelimiter_LeavesDocumentUnchanged()
	{
		var editor = WPFTestHost.CreateEditor("first");
		var service = new TextLineCommentService();
		var syntaxWithoutLineComments = new CommentSyntax(null, null, StringLiteralStyle.None);

		editor.SelectAll();
		service.ApplyEdit(editor, syntaxWithoutLineComments, TextLineCommentAction.Comment);

		Assert.AreEqual("first", editor.Text);
	}

	[TestMethod]
	public void ApplyEdit_WithContractEditTarget_AppliesOperationAndPublishesEditorDocument()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "  second");
		var service = new TextLineCommentService();
		var target = new ContractEditTarget(editor);

		int originalLength = editor.Document.TextLength;

		editor.SelectAll();
		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment, editTarget: target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];
		string expected = "// first" + "\r\n" + "  // second";

		Assert.AreEqual(0, operation.StartOffset);
		Assert.AreEqual(originalLength, operation.EndOffset);
		Assert.AreEqual(expected, operation.NewText);

		// The contract-honoring target publishes the edit to the editor document before returning,
		// so the restored selection is derived from the updated document.
		Assert.AreEqual(expected, target.Text);
		Assert.AreEqual(expected, editor.Text);
		Assert.AreEqual(expected, editor.SelectedText);
	}

	[TestMethod]
	public void ApplyEdit_CollapsedCaret_CommentsTheCaretLine()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "second");
		var service = new TextLineCommentService();

		editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset + 2;

		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment);

		Assert.AreEqual("first" + "\r\n" + "// second", editor.Text);
		Assert.AreEqual("// second", editor.SelectedText);
	}

	[TestMethod]
	public void ApplyEdit_WithAvalonEditTarget_EditsDocument()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "second");
		var service = new TextLineCommentService();

		editor.SelectAll();
		service.ApplyEdit(
			editor,
			s_doubleSlashSyntax,
			TextLineCommentAction.Comment,
			editTarget: new AvalonEditTextEditTarget(editor));

		string expected = "// first" + "\r\n" + "// second";

		Assert.AreEqual(expected, editor.Text);
		Assert.AreEqual(expected, editor.SelectedText);
	}

	[TestMethod]
	public void ApplyEdit_WithNonContractTarget_KeepsDocumentAndClampsSelection()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "second");
		var service = new TextLineCommentService();
		var target = new RecordingEditTarget();

		editor.SelectAll();
		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment, editTarget: target);

		// The target deliberately violates the contract by not publishing the edit to the editor
		// document; the documented behavior is that the document keeps its content while the restored
		// selection is clamped to the stale document length.
		Assert.HasCount(1, target.Operations);
		Assert.AreEqual("first" + "\r\n" + "second", editor.Text);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(editor.Document.TextLength, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyEdit_UncommentWithoutDelimiter_LeavesDocumentAndUndoStackUntouched()
	{
		var editor = WPFTestHost.CreateEditor("first\r\nsecond");

		editor.Document.UndoStack.ClearAll();
		editor.Select(0, editor.Document.TextLength);

		var service = new TextLineCommentService();

		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Uncomment);

		Assert.AreEqual("first\r\nsecond", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void TryCreateEdit_WithoutSpaceAfterDelimiter_ReturnsTheBareDelimiterEdit()
	{
		var document = new TextDocument("first" + "\r\n" + "  second");
		var service = new TextLineCommentService();

		bool created = service.TryCreateEdit(
			document,
			new TextRange(0, document.TextLength),
			s_doubleSlashSyntax,
			TextLineCommentAction.Comment,
			insertSpaceAfterDelimiter: false,
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);
		Assert.AreEqual("//first" + "\r\n" + "  //second", edit.ReplacementText);
	}

	[TestMethod]
	public void ApplyEdit_WithoutSpaceAfterDelimiter_CommentsBareAndUncommentsRoundTrip()
	{
		var editor = WPFTestHost.CreateEditor("first" + "\r\n" + "  second");
		var service = new TextLineCommentService();

		editor.SelectAll();
		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment, insertSpaceAfterDelimiter: false);

		string expected = "//first" + "\r\n" + "  //second";

		Assert.AreEqual(expected, editor.Text);
		Assert.AreEqual(expected, editor.SelectedText);

		// Uncommenting removes the delimiter plus one following space when one is present, so the bare
		// form round-trips even though the option is false.
		service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Uncomment, insertSpaceAfterDelimiter: false);

		Assert.AreEqual("first" + "\r\n" + "  second", editor.Text);
	}
}
