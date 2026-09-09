using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Provides line-comment transformations for an individual editor.
/// </summary>
/// <remarks>
/// <para>
/// The edit computation is editor-neutral and normative in the Core
/// <see cref="TextLineCommentPlanner"/>: implementations preserve
/// leading whitespace, leave whitespace-only lines unchanged, keep each line's original line
/// terminator, and do not give a final unterminated line a new one.
/// </para>
/// <para>
/// <see cref="TextLineCommentService"/> is the default implementation. It delegates the computation
/// to the planner and keeps no per-call state, so a host can compose one instance per editor and
/// substitute its own implementation through the interface.
/// </para>
/// </remarks>
public interface ITextLineCommentService
{
	/// <summary>
	/// Creates an edit for the requested line-comment transformation on the selected lines.
	/// </summary>
	/// <remarks>
	/// The transformation covers every line the selection touches.
	/// A non-collapsed selection that ends exactly at the start of a line does not include that line,
	/// while a collapsed selection at the start of a line does include it.
	/// The returned edit can be a no-op when the transformation leaves the selected lines unchanged;
	/// <see cref="ApplyEdit"/> skips applying identical text, and a caller that applies edits directly
	/// should apply the same rule to avoid a pointless undo entry.
	/// The default implementation delegates the computation to
	/// <see cref="TextLineCommentPlanner"/> over a snapshot of the document.
	/// </remarks>
	/// <param name="document">The AvalonEdit <see cref="TextDocument"/> containing the selection.</param>
	/// <param name="selection">The zero-based selection range within the document.</param>
	/// <param name="commentSyntax">The comment syntax whose line-comment delimiter is applied.</param>
	/// <param name="action">The line-comment transformation to apply.</param>
	/// <param name="insertSpaceAfterDelimiter">
	/// <see langword="true"/> to follow the delimiter with the single space that mainstream desktop
	/// editors insert; <see langword="false"/> to insert the bare delimiter. Uncommenting always
	/// removes the delimiter plus one following space when present, so both settings round-trip.
	/// </param>
	/// <param name="edit">
	/// The created edit when the method returns <see langword="true"/>; otherwise, the default edit.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the document contains text and
	/// <paramref name="commentSyntax"/> provides a non-blank line-comment delimiter;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	bool TryCreateEdit(
		TextDocument document,
		TextRange selection,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		bool insertSpaceAfterDelimiter,
		out TextLineCommentEdit edit);

	/// <summary>
	/// Applies the requested line-comment transformation to the editor's current selection.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A transformation that would not change the selected text is not applied, so the document and
	/// undo stack stay untouched when, for example, no selected line has the delimiter to remove.
	/// </para>
	/// <para>
	/// A supplied target must satisfy the edit-target contract described by
	/// <see cref="TextEditorEditOperations"/>.
	/// </para>
	/// </remarks>
	/// <param name="editor">The editor whose current selection is transformed.</param>
	/// <param name="commentSyntax">The syntax providing the line-comment delimiter.</param>
	/// <param name="action">The transformation to apply.</param>
	/// <param name="insertSpaceAfterDelimiter">
	/// <see langword="true"/> to follow the delimiter with the single space that mainstream desktop
	/// editors insert; <see langword="false"/> to insert the bare delimiter.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	void ApplyEdit(
		TextEditor editor,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		bool insertSpaceAfterDelimiter = true,
		ITextEditTarget? editTarget = null);
}
