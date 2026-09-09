using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides the auto-closing operations for an individual editor.
/// </summary>
/// <remarks>
/// <see cref="TextAutoClosingService"/> is the default implementation. A host typically composes one
/// instance per editor; the default implementation keeps per-document insertion tracking for its
/// provenance modes, so a shared instance never mixes the tracking of different documents. The
/// applying members accept the same optional <see cref="ITextEditTarget"/> as the other editing
/// helpers and apply the typed pair (or the deleted pair) through it as one batch, so the
/// single-undo property survives a host-owned target; see
/// <see cref="TextEditorEditOperations"/> for the contract a supplied target must satisfy. A skip
/// action only moves the caret and does not involve a target.
/// </remarks>
public interface ITextAutoClosingService
{
	/// <summary>
	/// Tries to resolve an auto-closing action for <paramref name="inputText"/> at the caret.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pair evaluation is implemented by the editor-neutral <see cref="TextAutoClosingResolver"/>,
	/// which this member invokes over a snapshot of the document with the service's insertion-tracking
	/// callback.
	/// </para>
	/// <para>
	/// Resolution considers the caret position and the text around it:
	/// </para>
	/// <list type="bullet">
	/// <item>an opening token is not doubled in front of its closing text, and typing a pair's closing
	/// token over existing closing text can skip that text (see
	/// <see cref="TextAutoClosingOptions.OvertypeMode"/>);</item>
	/// <item>an unmatched closing text later on the caret's line suppresses the insert of a bracket-like
	/// pair;</item>
	/// <item>the character after the caret must be the end of the text, whitespace, or an allowed character
	/// (see <see cref="TextAutoClosingOptions.AutoCloseBefore"/> and the kind's preset);</item>
	/// <item>a quote-like token preceded by the configured escape character belongs to an escape sequence,
	/// so it is not skipped.</item>
	/// </list>
	/// <para>
	/// The entered opening token is compared exactly; closing text matches exactly too, or by its leading
	/// token when it spans multiple characters (so typing <c>}</c> skips an existing <c>},</c>). Input that
	/// matches neither resolves no action and the method returns <see langword="false"/>. A skip that
	/// depends on <see cref="TextAutoClosingProvenance.Auto"/> requires the insertion tracking of the
	/// default service, which only records pairs it applied itself through <see cref="HandleTextEntering"/>.
	/// </para>
	/// <para>
	/// <see cref="HandleTextEntering"/> additionally wraps a non-empty selection, which bypasses the
	/// collapsed-caret quote suppressions and the gates above.
	/// </para>
	/// </remarks>
	/// <param name="document">The document containing the caret.</param>
	/// <param name="caretOffset">
	/// The zero-based caret offset. Offsets outside the document are clamped to the document bounds.
	/// </param>
	/// <param name="inputText">The text being entered.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="action">
	/// The resolved action when the method returns <see langword="true"/>;
	/// otherwise, the <see langword="default"/> action, whose kind is
	/// <see cref="TextAutoClosingActionKind.None"/>.
	/// </param>
	/// <returns><see langword="true"/> when an action applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/>, <paramref name="inputText"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	bool TryResolveAction(
		TextDocument document,
		int caretOffset,
		string inputText,
		TextAutoClosingOptions options,
		out TextAutoClosingAction action);

	/// <summary>
	/// Applies the auto-closing action (if any) for text entering the editor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The event is ignored when it is already handled, so a subscriber that ran earlier can veto. An
	/// editor without a document is ignored as well: nothing can be resolved or applied, and the returned
	/// result is <see cref="TextAutoClosingResult.None"/> in both cases.
	/// </para>
	/// <para>
	/// With a collapsed selection, an insert action applies the typed text and the closing text as one
	/// edit (through <paramref name="editTarget"/> when one is supplied), so a single undo removes the
	/// pair, and marks the event handled. The pair is not applied when the editor's read-only section
	/// provider refuses an insertion at the caret.
	/// </para>
	/// <para>
	/// With a non-empty selection, an insert action wraps the selected text in the typed opening token and the
	/// closing text as one edit, keeps the enclosed text selected, and marks the event handled. A whitespace-only
	/// selection, or one that only repeats the typed text of a quote pair, is replaced through normal text
	/// input instead, and so is a selection that the editor's read-only section provider does not allow
	/// replacing wholesale.
	/// </para>
	/// <para>
	/// A skip action moves past existing closing text and marks the event handled; it changes no text, so
	/// no edit target is involved. With a non-empty selection, a skip action is ignored so normal text
	/// input replaces the selection.
	/// </para>
	/// </remarks>
	/// <param name="editor">The editor receiving the text.</param>
	/// <param name="e">The text-composition event being handled.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the applied pair is routed, or <see langword="null"/> to edit the
	/// editor's document directly. A supplied target must satisfy the edit-target contract described by
	/// <see cref="TextEditorEditOperations"/>.
	/// </param>
	/// <returns>
	/// The action that was applied; <see cref="TextAutoClosingResult.None"/> when no action applied.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="e"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	TextAutoClosingResult HandleTextEntering(
		TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingOptions options,
		ITextEditTarget? editTarget = null);

	/// <summary>
	/// Deletes the auto-closing pair around the caret for a plain Backspace press.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Call this from the editor's key-down handling while the event can still be marked handled, for
	/// example a <c>PreviewKeyDown</c> handler. When the opening token ends at the caret and its closing
	/// text starts there, the whole pair is removed as one edit (through <paramref name="editTarget"/> when
	/// one is supplied), so a single undo restores it, and the event is marked handled. See
	/// <see cref="TextAutoClosingOptions.DeleteMode"/> for the provenance rule that decides whether a pair
	/// qualifies; the pair matching and the provenance check are resolved by
	/// <see cref="TextAutoClosingResolver"/> against a snapshot of the document.
	/// </para>
	/// <para>
	/// The method does nothing when the event is already handled, the pressed key is not Backspace, a
	/// control or alt modifier is held, the editor has no document, the editor has an active selection
	/// (the editor's own Backspace handling deletes the selection instead), or the editor's read-only
	/// section provider refuses the deletion.
	/// </para>
	/// </remarks>
	/// <param name="editor">The editor receiving the deletion.</param>
	/// <param name="e">The keyboard event of the Backspace press.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the pair deletion is routed, or <see langword="null"/> to edit the
	/// editor's document directly. A supplied target must satisfy the edit-target contract described by
	/// <see cref="TextEditorEditOperations"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the pair was removed and <paramref name="e"/> was handled;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="e"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	bool HandleBackspace(TextEditor editor, KeyEventArgs e, TextAutoClosingOptions options, ITextEditTarget? editTarget = null);
}
