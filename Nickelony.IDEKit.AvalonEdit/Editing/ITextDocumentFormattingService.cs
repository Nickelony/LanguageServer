using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides whole-document formatting for an individual editor.
/// </summary>
/// <remarks>
/// <see cref="TextDocumentFormattingService"/> is the default implementation. It is stateless, so a host can
/// compose one instance per editor and substitute its own implementation through the interface.
/// </remarks>
public interface ITextDocumentFormattingService
{
	/// <summary>
	/// Formats the current document content and applies the difference as minimal edits in one undo step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replacement covers only the range between the first and the last changed characters. Content
	/// outside that range is not rewritten; the caret and a selection that do not overlap the range keep
	/// their positions, and offsets after the range shift by the length difference of the replacement. The
	/// scroll position is preserved as well.
	/// </para>
	/// <para>
	/// When the changed range covers the caret or the selection, their exact positions cannot be mapped: the
	/// selection is collapsed to the end of the line with the original caret line number, and the column
	/// within that line is not preserved. If the caret line does not exist in the result, the caret is placed
	/// at the end of the resulting document.
	/// </para>
	/// <para>
	/// A supplied target must satisfy the edit-target contract described by
	/// <see cref="TextEditorEditOperations"/>.
	/// </para>
	/// </remarks>
	/// <param name="editor">The editor to update.</param>
	/// <param name="formatter">
	/// The formatter to apply. It receives the target's current content when a target is supplied;
	/// returning <see langword="null"/> declines the formatting and applies no changes.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/> or <paramref name="formatter"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	void FormatDocument(
		TextEditor editor,
		ITextDocumentFormatter formatter,
		ITextEditTarget? editTarget = null);
}
