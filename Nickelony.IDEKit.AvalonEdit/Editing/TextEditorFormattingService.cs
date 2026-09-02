using System.Diagnostics.CodeAnalysis;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Formatting;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies formatter output to an editor as a single undo operation while preserving the current
/// caret line and scroll position. Because a full-document rewrite changes every offset, the
/// selection is collapsed to the end of the preserved line rather than re-mapped to the formatted text.
/// </summary>
/// <remarks>
/// If formatting removes the original caret line, the caret is placed at offset <c>TextLength - 1</c>,
/// one position before the final UTF-16 code unit, or at offset <c>0</c> when the document is empty.
/// </remarks>
[SuppressMessage(
	"Performance",
	"CA1822:Mark members as static",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextEditorFormattingService
{
	/// <summary>
	/// Formats the current editor content as one undo step.
	/// </summary>
	/// <param name="editor">The editor to update.</param>
	/// <param name="formatter">The formatter to apply.</param>
	/// <param name="trimOnly">
	/// Whether only trailing whitespace should be trimmed. When <see langword="true"/>, the formatter
	/// is skipped and the trailing whitespace is trimmed directly.
	/// </param>
	public void FormatDocument(
		ICSharpCode.AvalonEdit.TextEditor editor,
		ITextDocumentFormatter formatter,
		bool trimOnly = false)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(formatter);

		string formattedContent = trimOnly
			? TrimTrailingWhitespaceFormatter.Instance.FormatDocument(editor.Text)
			: formatter.FormatDocument(editor.Text);

		if (string.Equals(editor.Text, formattedContent, StringComparison.Ordinal))
			return;

		Vector scrollOffset = editor.TextArea.TextView.ScrollOffset;
		int caretLineNumber = editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;

		editor.Document.UndoStack.StartUndoGroup();

		try
		{
			editor.SelectAll();
			editor.SelectedText = formattedContent;
		}
		finally
		{
			editor.Document.UndoStack.EndUndoGroup();
		}

		if (caretLineNumber <= editor.Document.LineCount)
		{
			DocumentLine line = editor.Document.GetLineByNumber(caretLineNumber);
			editor.Select(line.EndOffset, 0);
		}
		else
		{
			int offset = editor.Document.TextLength > 0 ? editor.Document.TextLength - 1 : 0;
			editor.Select(offset, 0);
		}

		editor.ScrollToHorizontalOffset(scrollOffset.X);
		editor.ScrollToVerticalOffset(scrollOffset.Y);
	}
}
