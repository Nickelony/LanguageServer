using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides programmatic text insertion and replacement for an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// When a workspace edit target is supplied, the edit is applied to that target instead of the editor's document.
/// The editor's caret is updated separately, and the content-changed callback is invoked only after a direct edit.
/// </remarks>
public static class TextEditorEditHelper
{
	/// <summary>
	/// Inserts <paramref name="newText"/> at <paramref name="insertOffset"/> as a single edit operation
	/// and places the caret at <paramref name="caretOffset"/>, or just after the inserted text when omitted.
	/// With no workspace target, the default AvalonEdit target records the edit as one undo step.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="workspaceEditTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="insertOffset">The zero-based offset at which to insert the text.</param>
	/// <param name="newText">The text to insert.</param>
	/// <param name="caretOffset">
	/// The optional zero-based caret offset after the edit.
	/// Values above the current editor document length are capped.
	/// When omitted, the caret is placed just after the inserted text.
	/// </param>
	/// <param name="workspaceEditTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <param name="onContentChanged">
	/// The callback invoked after a direct edit operation is applied, including when the resulting document text is unchanged.
	/// It is not invoked when <paramref name="workspaceEditTarget"/> is supplied.
	/// Pass <see langword="null"/> to omit it.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.
	/// </exception>
	public static void InsertText(
		TextEditor textEditor,
		int insertOffset,
		string newText,
		int? caretOffset = null,
		ITextEditTarget? workspaceEditTarget = null,
		Action? onContentChanged = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);

		ApplyEdit(textEditor, insertOffset, 0, newText, caretOffset, workspaceEditTarget, onContentChanged);
	}

	/// <summary>
	/// Replaces the range starting at <paramref name="startOffset"/> with <paramref name="newText"/>
	/// as a single edit operation and places the caret at <paramref name="caretOffset"/>, or just after
	/// the replacement text when omitted. With no workspace target, the default AvalonEdit target records
	/// the edit as one undo step.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="workspaceEditTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="startOffset">The zero-based start offset of the replaced range.</param>
	/// <param name="length">The length of the replaced range.</param>
	/// <param name="newText">The replacement text.</param>
	/// <param name="caretOffset">
	/// The optional zero-based caret offset after the edit.
	/// Values above the current editor document length are capped.
	/// When omitted, the caret is placed just after the replacement text.
	/// </param>
	/// <param name="workspaceEditTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <param name="onContentChanged">
	/// The callback invoked after a direct edit operation is applied, including when the resulting document text is unchanged.
	/// It is not invoked when <paramref name="workspaceEditTarget"/> is supplied.
	/// Pass <see langword="null"/> to omit it.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.
	/// </exception>
	public static void ReplaceText(
		TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffset = null,
		ITextEditTarget? workspaceEditTarget = null,
		Action? onContentChanged = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);

		ApplyEdit(textEditor, startOffset, length, newText, caretOffset, workspaceEditTarget, onContentChanged);
	}

	private static void ApplyEdit(
		TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffset,
		ITextEditTarget? workspaceEditTarget,
		Action? onContentChanged)
	{
		ITextEditTarget editTarget = workspaceEditTarget ?? new AvalonEditTextEditTarget(textEditor);
		editTarget.Apply([new TextEditOperation(startOffset, startOffset + length, newText, 0)]);

		textEditor.CaretOffset = Math.Min(
			caretOffset ?? (startOffset + newText.Length),
			textEditor.Document.TextLength);

		if (workspaceEditTarget is null)
			onContentChanged?.Invoke();
	}
}
