using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Comments;
using Nickelony.IDEKit.Core.Comments;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextLineCommentServiceTests
{
	private static readonly CommentSyntax s_doubleSlashSyntax = new("//", null, null, StringLiteralStyle.None);

	[TestMethod]
	public void TryCreateEdit_ToggleOnUncommentedSelection_CommentsEachLine()
	{
		var document = new TextDocument("first" + Environment.NewLine + "  second");
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Toggle,
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		document.Replace(edit.ReplaceOffset, edit.ReplaceLength, edit.ReplacementText);
		Assert.AreEqual("//first" + Environment.NewLine + "  //second" + Environment.NewLine, document.Text);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnCommentedSelection_UncommentsEachLine()
	{
		var document = new TextDocument("//first" + Environment.NewLine + "  //second");
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Toggle,
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		document.Replace(edit.ReplaceOffset, edit.ReplaceLength, edit.ReplacementText);
		Assert.AreEqual("first" + Environment.NewLine + "  second" + Environment.NewLine, document.Text);
	}

	[TestMethod]
	public void TryCreateEdit_Comment_PreservesWhitespaceOnlyLines()
	{
		var document = new TextDocument("first" + Environment.NewLine + "   " + Environment.NewLine + "third");
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Comment,
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		document.Replace(edit.ReplaceOffset, edit.ReplaceLength, edit.ReplacementText);

		Assert.AreEqual(
			"//first" + Environment.NewLine + "   " + Environment.NewLine + "//third" + Environment.NewLine,
			document.Text);
	}

	[TestMethod]
	public void TryCreateEdit_Uncomment_LeavesLinesWithoutPrefixUntouched()
	{
		var document = new TextDocument("//first" + Environment.NewLine + "second");
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Uncomment,
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		document.Replace(edit.ReplaceOffset, edit.ReplaceLength, edit.ReplacementText);
		Assert.AreEqual("first" + Environment.NewLine + "second" + Environment.NewLine, document.Text);
	}

	[TestMethod]
	public void TryCreateEdit_NoLineCommentDelimiter_ReturnsFalse()
	{
		var document = new TextDocument("first");
		var service = new TextLineCommentService();
		var syntaxWithoutLineComments = new CommentSyntax(null, null, null, StringLiteralStyle.None);

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: syntaxWithoutLineComments,
			action: TextLineCommentAction.Comment,
			out TextLineCommentEdit edit);

		Assert.IsFalse(success);
	}

	[TestMethod]
	public void TryCreateEdit_EmptyDocument_ReturnsFalse()
	{
		var document = new TextDocument();
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: 0,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Comment,
			out TextLineCommentEdit edit);

		Assert.IsFalse(success);
	}

	[TestMethod]
	public void TryCreateEdit_EditDescribesReplaceableRange()
	{
		var document = new TextDocument("first" + Environment.NewLine + "second");
		var service = new TextLineCommentService();

		bool success = service.TryCreateEdit(
			document,
			selectionStart: 0,
			selectionLength: document.TextLength,
			commentSyntax: s_doubleSlashSyntax,
			action: TextLineCommentAction.Comment,
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		Assert.AreEqual(0, edit.ReplaceOffset);
		Assert.AreEqual(document.TextLength, edit.ReplaceLength);
		Assert.AreEqual("//first" + Environment.NewLine + "//second" + Environment.NewLine, edit.ReplacementText);
		Assert.AreEqual(0, edit.SelectionStart);
		Assert.AreEqual(edit.ReplacementText.Length - 1, edit.SelectionLength);
	}

	[TestMethod]
	public void ApplyEdit_CommentsSelectedLines()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("first" + Environment.NewLine + "  second");
			var service = new TextLineCommentService();

			editor.SelectAll();
			service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment);

			string expected = "//first" + Environment.NewLine + "  //second" + Environment.NewLine;

			Assert.AreEqual(expected, editor.Text);
			Assert.AreEqual(0, editor.SelectionStart);
			Assert.AreEqual(expected.TrimEnd('\r', '\n'), editor.SelectedText);
		});
	}

	[TestMethod]
	public void ApplyEdit_NoLineCommentDelimiter_LeavesDocumentUnchanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("first");
			var service = new TextLineCommentService();
			var syntaxWithoutLineComments = new CommentSyntax(null, null, null, StringLiteralStyle.None);

			editor.SelectAll();
			service.ApplyEdit(editor, syntaxWithoutLineComments, TextLineCommentAction.Comment);

			Assert.AreEqual("first", editor.Text);
		});
	}

	private static ICSharpCode.AvalonEdit.TextEditor CreateEditor(string text)
		=> new() { Document = new TextDocument(text) };
}
