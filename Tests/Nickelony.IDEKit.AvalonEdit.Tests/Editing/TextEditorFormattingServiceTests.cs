using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Formatting;
using System.Windows;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextEditorFormattingServiceTests
{
	[TestMethod]
	public void FormatDocument_IsOneUndoStep_PreservesCaretLineAndScroll()
	{
		STATestHelper.RunInSTA(() =>
		{
			string original = string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "Line " + i + "   "));
			var editor = new ICSharpCode.AvalonEdit.TextEditor
			{
				Text = original
			};

			Window hostWindow = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				editor.Document.UndoStack.ClearAll();
				editor.CaretOffset = editor.Document.GetOffset(2, 3);
				editor.ScrollToVerticalOffset(100.0);
				editor.ScrollToHorizontalOffset(5.0);
				WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

				Vector scrollBefore = editor.TextArea.TextView.ScrollOffset;

				var service = new TextEditorFormattingService();
				service.FormatDocument(editor, new CrLfTrimFormatter());

				Assert.AreEqual(TrimTrailingWhitespace(original), editor.Text);

				// The caret remains on line 2 after the full-document replacement.
				Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);

				// Both scroll offsets are preserved.
				Assert.AreEqual(scrollBefore.X, editor.TextArea.TextView.ScrollOffset.X, 1.0);
				Assert.AreEqual(scrollBefore.Y, editor.TextArea.TextView.ScrollOffset.Y, 1.0);

				// Undo restores the original text, and redo restores the formatted text.
				editor.Undo();
				Assert.AreEqual(original, editor.Text);

				editor.Redo();
				Assert.AreEqual(TrimTrailingWhitespace(original), editor.Text);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	[TestMethod]
	public void FormatDocument_NoChanges_LeavesEditorAndUndoStackUntouched()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor
			{
				Text = "No changes here"
			};

			Window hostWindow = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				editor.Document.UndoStack.ClearAll();
				editor.CaretOffset = 3;

				var service = new TextEditorFormattingService();
				service.FormatDocument(editor, new IdentityFormatter());

				Assert.AreEqual("No changes here", editor.Text);
				Assert.AreEqual(3, editor.CaretOffset);

				// Undo leaves the unchanged content intact when formatting produces no edits.
				editor.Undo();
				Assert.AreEqual("No changes here", editor.Text);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	[TestMethod]
	public void FormatDocument_TrimOnly_OnlyRemovesTrailingWhitespace()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor
			{
				Text = "Customize= CUST_BAR,foo   \r\nLegend =1\t"
			};

			Window hostWindow = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				var service = new TextEditorFormattingService();
				service.FormatDocument(editor, new EqualsSpacingFormatter(), trimOnly: true);

				// Trim-only removes trailing whitespace without applying the formatter's spacing changes.
				Assert.AreEqual("Customize= CUST_BAR,foo" + Environment.NewLine + "Legend =1", editor.Text);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	private static string TrimTrailingWhitespace(string content)
		=> string.Join("\r\n", content.Split("\r\n").Select(line => line.TrimEnd()));

	private sealed class CrLfTrimFormatter : ITextDocumentFormatter
	{
		public string FormatDocument(string content)
			=> TrimTrailingWhitespace(content);
	}

	private sealed class IdentityFormatter : ITextDocumentFormatter
	{
		public string FormatDocument(string content) => content;
	}

	private sealed class EqualsSpacingFormatter : ITextDocumentFormatter
	{
		public string FormatDocument(string content)
			=> content.Replace("=", " = ");
	}
}
