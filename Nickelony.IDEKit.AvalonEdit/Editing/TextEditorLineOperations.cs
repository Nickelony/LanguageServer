using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides line-based editing operations for an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The replacement operations edit the editor's document directly by default; pass a host-owned
/// <see cref="ITextEditTarget"/> to route the replacement through it instead. A supplied target must
/// satisfy the edit-target contract described by <see cref="TextEditorEditOperations"/>.
/// </para>
/// <para>
/// The selection and caret helpers never use an edit target, so a host that owns document authority
/// must observe those calls and keep its own content in sync.
/// </para>
/// </remarks>
public static class TextEditorLineOperations
{
	/// <summary>
	/// Selects a document line's content, excluding its line terminator.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line whose content is selected.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="line"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void SelectLine(this TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		textEditor.Select(line.Offset, line.Length);
	}

	/// <summary>
	/// Replaces a document line's content and places the caret immediately after the replacement text.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replaced line's terminator is preserved, so the caret ends up in front of it when one is present.
	/// The replacement may span multiple lines; the caret is always placed at the end of the replacement text,
	/// which is the end of the last replacement line.
	/// </para>
	/// <para>
	/// When <paramref name="selectReplacement"/> is <see langword="true"/>, the replacement stays selected
	/// instead of collapsing the selection, and the caret remains at the end of the selection.
	/// </para>
	/// <para>
	/// Replacing a line with identical text leaves the document and its undo stack untouched;
	/// only the selection or caret is updated.
	/// </para>
	/// <para>
	/// When <paramref name="editTarget"/> is supplied, the replacement is applied through it under the
	/// edit-target contract described by <see cref="TextEditorEditOperations"/>, and the post-edit selection
	/// is applied to the editor's document.
	/// </para>
	/// </remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="line">The line whose content is replaced.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="selectReplacement">Whether the replacement stays selected afterwards.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="line"/>, or <paramref name="replacement"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void ReplaceLine(
		this TextEditor textEditor,
		DocumentLine line,
		string replacement,
		bool selectReplacement = false,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(replacement);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		if (!string.Equals(textEditor.Document.GetText(line), replacement, StringComparison.Ordinal))
		{
			TextEditorEditOperations.ApplyOperations(
				textEditor,
				[new TextEditOperation(line.Offset, line.EndOffset, replacement, 0)],
				editTarget);
		}

		// The range is anchored to the editor's document, so the final selection is clamped against it;
		// a target that does not update the editor's document leaves the final view state to the host.
		int documentLength = textEditor.Document.TextLength;
		int selectionStart = Math.Min(line.Offset, documentLength);

		if (selectReplacement)
		{
			int selectionLength = Math.Min(replacement.Length, documentLength - selectionStart);
			textEditor.Select(selectionStart, selectionLength);
		}
		else
		{
			textEditor.Select(Math.Min(selectionStart + replacement.Length, documentLength), 0);
		}
	}

	/// <summary>
	/// Replaces the entire document content and places the caret at the end of the resulting document.
	/// </summary>
	/// <remarks>
	/// Setting identical content leaves the document and its undo stack untouched;
	/// the caret still moves to the document end. When <paramref name="editTarget"/> is supplied,
	/// the replacement is applied through it under the edit-target contract described by
	/// <see cref="TextEditorEditOperations"/>, and the caret is placed against the editor's document.
	/// </remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="newContent">The content to set.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newContent"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void ReplaceContent(this TextEditor textEditor, string newContent, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newContent);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		if (!string.Equals(textEditor.Document.Text, newContent, StringComparison.Ordinal))
		{
			TextEditorEditOperations.ApplyOperations(
				textEditor,
				[new TextEditOperation(0, textEditor.Document.TextLength, newContent, 0)],
				editTarget);
		}

		textEditor.Select(textEditor.Document.TextLength, 0);
	}
}
