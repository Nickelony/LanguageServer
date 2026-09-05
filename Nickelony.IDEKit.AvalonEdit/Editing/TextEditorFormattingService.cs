using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Formatting;
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies formatted document changes to an AvalonEdit <see cref="TextEditor"/> in a single undo operation,
/// preserving the caret line and scroll position when possible.
/// </summary>
/// <remarks>
/// A full-document replacement invalidates existing offsets, so the selection is collapsed to the
/// end of the line with the original caret line number. If that line does not exist in the result,
/// the fallback offset is <c>TextLength - 1</c>, one position before the final UTF-16 code unit,
/// or <c>0</c> for an empty document.
/// </remarks>
[SuppressMessage(
	"Performance",
	"CA1822:Mark members as static",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextEditorFormattingService
{
	/// <summary>
	/// Formats the current editor content and applies any changes as one undo step.
	/// </summary>
	/// <param name="editor">The editor to update.</param>
	/// <param name="formatter">The formatter to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/> or <paramref name="formatter"/> is <see langword="null"/>.
	/// </exception>
	public void FormatDocument(TextEditor editor, ITextDocumentFormatter formatter)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(formatter);

		string formattedContent = formatter.FormatDocument(editor.Text);

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
