using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides programmatic text insertion and replacement for an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// When an edit target is supplied, the edit is applied to that target instead of the editor's document.
/// </para>
/// <para>
/// This is the canonical edit-target contract shared by the editing, comment, and formatting helpers:
/// </para>
/// <list type="bullet">
/// <item>The target must hold the same content as the editor's document when the call is made;</item>
/// <item>The target must apply the operations synchronously;</item>
/// <item>The target must update the editor's document before returning.</item>
/// </list>
/// <para>
/// Editing is synchronous; the editor's caret is updated separately, and a host that tracks content changes
/// updates its own state after the call.
/// </para>
/// <para>
/// The caret is always clamped to the current length of the editor's document, which does not reflect an edit
/// target's post-edit content until the host applies the change. When a target is supplied and
/// does not update the editor's document before returning, the requested caret offset is still applied but
/// clamped to that stale length; the host should set the final caret after publishing its change.
/// </para>
/// </remarks>
public static class TextEditorEditOperations
{
	// The default target is cached per editor: this path runs on every keystroke (typing, auto-closing,
	// backspace), and an uncached target would allocate and re-subscribe per call. The weak table keeps
	// the target alive exactly as long as its editor.
	private static readonly ConditionalWeakTable<TextEditor, AvalonEditTextEditTarget> s_defaultEditTargets = new();

	/// <summary>
	/// Inserts <paramref name="newText"/> at <paramref name="insertOffset"/> as a single edit operation
	/// and places the caret at <paramref name="caretOffsetAfterEdit"/>, or just after the inserted text when
	/// <paramref name="caretOffsetAfterEdit"/> is omitted.
	/// </summary>
	/// <remarks>
	/// With no edit target, the default AvalonEdit target records the edit as one undo step.
	/// </remarks>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="insertOffset">The zero-based offset at which to insert the text.</param>
	/// <param name="newText">The text to insert.</param>
	/// <param name="caretOffsetAfterEdit">
	/// The optional desired zero-based caret offset after the edit.
	/// Values outside the editor's current document are clamped.
	/// When <paramref name="caretOffsetAfterEdit"/> is omitted, the caret is placed just after the inserted text.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="insertOffset"/> is negative, or the edit target (or the editor's document when no
	/// target is supplied) rejects the requested range.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void InsertText(
		this TextEditor textEditor,
		int insertOffset,
		string newText,
		int? caretOffsetAfterEdit = null,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);
		ArgumentOutOfRangeException.ThrowIfNegative(insertOffset);

		ApplyEdit(textEditor, insertOffset, 0, newText, caretOffsetAfterEdit, editTarget);
	}

	/// <summary>
	/// Replaces the range starting at <paramref name="startOffset"/> with <paramref name="newText"/>
	/// as a single edit operation and places the caret at <paramref name="caretOffsetAfterEdit"/>, or just after
	/// the replacement text when <paramref name="caretOffsetAfterEdit"/> is omitted.
	/// </summary>
	/// <remarks>
	/// With no edit target, the default AvalonEdit target records the edit as one undo step.
	/// </remarks>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="startOffset">The zero-based start offset of the replaced range.</param>
	/// <param name="length">The length of the replaced range.</param>
	/// <param name="newText">The replacement text.</param>
	/// <param name="caretOffsetAfterEdit">
	/// The optional desired zero-based caret offset after the edit.
	/// Values outside the editor's current document are clamped.
	/// When <paramref name="caretOffsetAfterEdit"/> is omitted, the caret is placed just after the replacement text.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> or <paramref name="length"/> is negative, the range end would
	/// overflow the 32-bit offset range, or the edit target (or the editor's document when no
	/// target is supplied) rejects the requested range.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void ReplaceText(
		this TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffsetAfterEdit = null,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		ApplyEdit(textEditor, startOffset, length, newText, caretOffsetAfterEdit, editTarget);
	}

	private static void ApplyEdit(
		TextEditor textEditor,
		int startOffset,
		int length,
		string newText,
		int? caretOffsetAfterEdit,
		ITextEditTarget? editTarget)
	{
		// A range whose end overflows to a negative offset must be rejected instead of reaching a
		// target as an inverted range.
		if (startOffset > int.MaxValue - length)
			throw new ArgumentOutOfRangeException(nameof(startOffset), startOffset, "The range end must not exceed Int32.MaxValue.");

		ApplyOperations(
			textEditor,
			[new TextEditOperation(startOffset, startOffset + length, newText, 0)],
			editTarget);

		// The default caret offset is computed in 64-bit arithmetic so a large insert cannot overflow.
		long requestedCaretOffset = caretOffsetAfterEdit ?? ((long)startOffset + newText.Length);

		textEditor.CaretOffset = (int)Math.Clamp(requestedCaretOffset, 0L, textEditor.Document.TextLength);
	}

	/// <summary>
	/// Applies <paramref name="operations"/> through <paramref name="editTarget"/>,
	/// or directly to the editor's document when no target is supplied.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="operations">The validated operations to apply.</param>
	/// <param name="editTarget">
	/// The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	internal static void ApplyOperations(
		TextEditor textEditor,
		IReadOnlyList<TextEditOperation> operations,
		ITextEditTarget? editTarget)
		=> ApplyOperations(textEditor, new PreparedTextEdits(operations), editTarget);

	/// <summary>
	/// Applies an already prepared batch through <paramref name="editTarget"/>, or directly to the
	/// editor's document when no target is supplied.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="edits">The prepared batch to apply; the caller keeps owning it (for example for its
	/// offset mapping).</param>
	/// <param name="editTarget">
	/// The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	internal static void ApplyOperations(
		TextEditor textEditor,
		PreparedTextEdits edits,
		ITextEditTarget? editTarget)
	{
		// Every editing helper funnels through here; an editor without a document cannot apply edits
		// (the caret and the default target both need it), so the missing document fails fast instead
		// of surfacing as a null-reference failure mid-edit.
		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		ITextEditTarget target = editTarget
			?? s_defaultEditTargets.GetValue(textEditor, static editor => new AvalonEditTextEditTarget(editor));

		target.Apply(edits);
	}
}
