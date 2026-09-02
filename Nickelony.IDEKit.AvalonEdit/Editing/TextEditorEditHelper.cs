using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies programmatic document edits without re-entering keyboard-only language handlers.
/// </summary>
/// <remarks>
/// When a workspace edit target is supplied, the operation is applied to that target rather than the
/// editor's document. The editor's caret is still updated, but the content-changed callback is invoked
/// only when the editor document is updated directly.
/// </remarks>
public static class TextEditorEditHelper
{
	/// <summary>
	/// Inserts <paramref name="newText"/> at <paramref name="insertOffset"/> as one undo step
	/// and places the caret at <paramref name="caretOffset"/> (defaults to just after the inserted text).
	/// </summary>
	/// <param name="textEditor">The editor whose document should be updated.</param>
	/// <param name="insertOffset">The zero-based offset at which to insert the text.</param>
	/// <param name="newText">The text to insert.</param>
	/// <param name="caretOffset">The caret offset after the edit; defaults to just after the inserted text.</param>
	/// <param name="workspaceEditTarget">
	/// The host workspace target to apply through, or <see langword="null"/> to apply to the editor document directly.
	/// </param>
	/// <param name="contentChanged">
	/// The callback invoked when the edit was applied directly to the editor document, or <see langword="null"/> for none.
	/// </param>
	public static void InsertText(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		int insertOffset,
		string newText,
		int? caretOffset = null,
		ITextEditTarget? workspaceEditTarget = null,
		Action? contentChanged = null)
		=> ApplyEdit(textEditor, insertOffset, 0, newText, caretOffset, workspaceEditTarget, contentChanged);

	/// <summary>
	/// Replaces the range starting at <paramref name="startOffset"/> with <paramref name="newText"/>
	/// as one undo step and places the caret at <paramref name="caretOffset"/>
	/// (defaults to just after the inserted text).
	/// </summary>
	/// <param name="textEditor">The editor whose document should be updated.</param>
	/// <param name="startOffset">The zero-based start offset of the replaced range.</param>
	/// <param name="length">The length of the replaced range.</param>
	/// <param name="newText">The replacement text.</param>
	/// <param name="caretOffset">The caret offset after the edit; defaults to just after the inserted text.</param>
	/// <param name="workspaceEditTarget">
	/// The host workspace target to apply through, or <see langword="null"/> to apply to the editor document directly.
	/// </param>
	/// <param name="contentChanged">
	/// The callback invoked when the edit was applied directly to the editor document, or <see langword="null"/> for none.
	/// </param>
	public static void ReplaceText(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffset = null,
		ITextEditTarget? workspaceEditTarget = null,
		Action? contentChanged = null)
		=> ApplyEdit(textEditor, startOffset, length, newText, caretOffset, workspaceEditTarget, contentChanged);

	private static void ApplyEdit(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffset,
		ITextEditTarget? workspaceEditTarget,
		Action? contentChanged)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		ITextEditTarget editTarget = workspaceEditTarget ?? new AvalonEditTextEditTarget(textEditor);
		editTarget.Apply([new TextEditOperation(startOffset, startOffset + length, newText, 0)]);

		textEditor.CaretOffset = Math.Min(
			caretOffset ?? startOffset + newText.Length,
			textEditor.Document.TextLength);

		if (workspaceEditTarget is null)
			contentChanged?.Invoke();
	}
}
