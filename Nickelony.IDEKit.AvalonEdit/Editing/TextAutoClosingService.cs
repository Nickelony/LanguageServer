using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies auto-closing (including overtype), pair deletion, and selection wrapping to an AvalonEdit
/// <see cref="TextEditor"/> for the pairs configured through <see cref="TextAutoClosingOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is delegated to the editor-neutral <see cref="TextAutoClosingResolver"/>: this service
/// snapshots the document, hands the snapshot and its insertion-tracking callback to the resolver, and
/// applies the resolved action as document edits. The pairs from
/// <see cref="TextAutoClosingOptions.Pairs"/> are evaluated in order; the first matching pair wins. The
/// resolution gates are documented on <see cref="ITextAutoClosingService.TryResolveAction"/>.
/// </para>
/// <para>
/// The applying members accept the same optional <see cref="ITextEditTarget"/> as the other editing
/// helpers: the typed text and the closing text (or the deleted pair) are applied through the supplied
/// target as one batch, so a single undo still removes or restores the whole pair. A supplied target
/// must satisfy the contract described by <see cref="TextEditorEditOperations"/>; the caret and
/// selection are applied to the editor's document as usual.
/// </para>
/// <para>
/// The leading-token skip of a multi-character closing text intentionally extends the convention of
/// mainstream desktop editors, which apply it to single-character closings only. It lets a pair whose
/// closing text appends a trailing token (for example <c>},</c>) still be overtyped by typing that
/// closing text's first character.
/// </para>
/// <para>
/// A quote-like token preceded by the configured escape character belongs to an escape sequence, so it
/// is neither skipped nor treated as an existing closing text.
/// </para>
/// <para>
/// Whether typing existing closing text skips it and whether Backspace removes the pair follow
/// <see cref="TextAutoClosingOptions.OvertypeMode"/> and <see cref="TextAutoClosingOptions.DeleteMode"/>.
/// Both default to <see cref="TextAutoClosingProvenance.Auto"/>, which applies them only to closing text
/// this service inserted and tracks per document; closing text written through any other path (a loaded
/// file, a host edit, or an action resolved outside <see cref="HandleTextEntering"/>) is not tracked, so
/// typing over it inserts normally and Backspace is left to the editor. The tracking state is held by the
/// service instance and released with the document. Removing the inserted closing text (for example,
/// undoing the insertion) ends its tracking; a later redo of that insertion does not restore it.
/// </para>
/// </remarks>
public sealed class TextAutoClosingService : ITextAutoClosingService
{
	// The tracking state of the closing texts this service inserted, held per document; anchors keep the
	// offsets valid across edits, and the state (with the resolver callback bound to it) is released
	// with its document.
	private readonly ConditionalWeakTable<TextDocument, DocumentTracking> _tracking = new();

	/// <inheritdoc/>
	public bool TryResolveAction(
		TextDocument document,
		int caretOffset,
		string inputText,
		TextAutoClosingOptions options,
		out TextAutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(inputText);
		ArgumentNullException.ThrowIfNull(options);

		return TextAutoClosingResolver.TryResolveAction(
			new TextAutoClosingRequest(
				new TextDocumentSnapshot(document),
				caretOffset,
				inputText,
				options)
			{
				IsTrackedClosingText = GetTrackingCallback(document)
			},
			out action);
	}

	/// <summary>
	/// Gets the resolver tracking callback over this service's recorded insertions for
	/// <paramref name="document"/>, or <see langword="null"/> when the document has no tracked
	/// closing texts.
	/// </summary>
	private Func<int, bool>? GetTrackingCallback(TextDocument document)
		=> _tracking.TryGetValue(document, out DocumentTracking? tracking)
			? tracking.IsTrackedClosingText
			: null;

	/// <inheritdoc/>
	public TextAutoClosingResult HandleTextEntering(
		TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingOptions options,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(e);
		ArgumentNullException.ThrowIfNull(options);

		// Another TextEntering subscriber (for example a completion list) already handled the input.
		if (e.Handled)
			return TextAutoClosingResult.None;

		// An editor without a document has nothing to resolve against or to insert into, so the input
		// stays unhandled and normal text input applies.
		if (editor.Document is not { } document)
			return TextAutoClosingResult.None;

		bool wrappingSelection = editor.SelectionLength > 0;

		if (!TextAutoClosingResolver.TryResolveAction(
			new TextAutoClosingRequest(
				new TextDocumentSnapshot(document),
				editor.CaretOffset,
				e.Text,
				options)
			{
				IsWrappingSelection = wrappingSelection,
				IsTrackedClosingText = GetTrackingCallback(document)
			},
			out TextAutoClosingAction action))
		{
			return TextAutoClosingResult.None;
		}

		return wrappingSelection
			? ApplySelectionAction(editor, e, action, editTarget)
			: ApplyAction(editor, e, action, editTarget);
	}

	/// <inheritdoc/>
	public bool HandleBackspace(TextEditor editor, KeyEventArgs e, TextAutoClosingOptions options, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(e);
		ArgumentNullException.ThrowIfNull(options);

		if (e.Handled
			|| e.Key != Key.Back
			|| (e.KeyboardDevice.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
		{
			return false;
		}

		// An active selection is deleted by the editor's own Backspace handling; resolving a pair around
		// the selection's end would delete text outside the selection.
		if (editor.SelectionLength > 0)
			return false;

		// An editor without a document has no pair to delete; the editor's own Backspace applies.
		if (editor.Document is not { } document)
			return false;

		int caretOffset = document.ClampOffset(editor.CaretOffset);

		// Delete provenance also governs the pair deletion: with Auto, only a closing text this service
		// inserted is removed together with its opening token.
		if (!TextAutoClosingResolver.TryResolvePairDeletion(
			new TextDocumentSnapshot(document),
			caretOffset,
			options,
			GetTrackingCallback(document),
			out TextAutoClosingPair? pair))
		{
			return false;
		}

		int deleteStart = caretOffset - pair.Open.Length;
		int deleteLength = pair.Open.Length + pair.Close.Length;

		if (!IsRangeEditable(editor, deleteStart, deleteLength))
			return false;

		// The whole pair travels as one edit, so a single undo restores it.
		TextEditorEditOperations.ApplyOperations(
			editor,
			[new TextEditOperation(deleteStart, deleteStart + deleteLength, string.Empty, 0)],
			editTarget);

		editor.Select(deleteStart, 0);

		e.Handled = true;
		return true;
	}

	/// <summary>
	/// Dispatches a resolved action to the matching applier, ignoring actions that carry no closing text.
	/// </summary>
	/// <param name="editor">The editor receiving the change.</param>
	/// <param name="e">The text-composition event being handled.</param>
	/// <param name="action">The action to apply.</param>
	/// <param name="editTarget">
	/// The host-owned target to which an applied pair is routed, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	/// <returns>The applied result; <see cref="TextAutoClosingResult.None"/> when the action cannot be applied.</returns>
	private TextAutoClosingResult ApplyAction(
		TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingAction action,
		ITextEditTarget? editTarget)
	{
		// Actions without closing text are ignored: None matches no case, and a malformed action
		// carries no text to apply.
		switch (action.Kind)
		{
			case TextAutoClosingActionKind.InsertClosingText:
				if (action.ClosingText is string closingText)
					return ApplyInsertAction(editor, e, closingText, editTarget);

				break;

			case TextAutoClosingActionKind.SkipExistingClosingText:
				if (action.ClosingText is string skippedText)
				{
					ApplySkipAction(editor, e, skippedText);

					return new TextAutoClosingResult(action, DidWrapSelection: false);
				}

				break;
		}

		return TextAutoClosingResult.None;
	}

	/// <summary>
	/// Inserts the typed text and the closing text as one edit (through the supplied target when one is
	/// provided), keeping the caret between them, so a single undo removes or restores the whole pair.
	/// </summary>
	private TextAutoClosingResult ApplyInsertAction(
		TextEditor editor,
		TextCompositionEventArgs e,
		string closingText,
		ITextEditTarget? editTarget)
	{
		int caretOffset = editor.CaretOffset;

		// The editor refuses edits inside a read-only section; let normal text input apply the same
		// policy instead of inserting the pair through a direct document edit.
		if (!editor.TextArea.ReadOnlySectionProvider.CanInsert(caretOffset))
			return TextAutoClosingResult.None;

		// The typed text and the closing text are applied as one edit, so a single undo removes the pair.
		TextEditorEditOperations.ApplyOperations(
			editor,
			[new TextEditOperation(caretOffset, caretOffset, e.Text + closingText, 0)],
			editTarget);

		int documentLength = editor.Document.TextLength;
		int caretAfterPair = Math.Min(caretOffset + e.Text.Length, documentLength);

		editor.Select(caretAfterPair, 0);

		// The inserted closing text is tracked so the default provenance modes can recognize it later.
		TrackInsertedClosingText(editor.Document, caretAfterPair);

		e.Handled = true;

		return new TextAutoClosingResult(
			TextAutoClosingAction.CreateInsert(closingText), DidWrapSelection: false);
	}

	/// <summary>
	/// Moves the caret past existing closing text and marks the event handled.
	/// </summary>
	/// <param name="editor">The editor receiving the skip.</param>
	/// <param name="e">The text-composition event being handled.</param>
	/// <param name="skippedText">The existing closing text to move past.</param>
	private static void ApplySkipAction(
		TextEditor editor,
		TextCompositionEventArgs e,
		string skippedText)
	{
		editor.CaretOffset += skippedText.Length;
		e.Handled = true;
	}

	/// <summary>
	/// Applies an insert action to a non-empty selection by wrapping the selection in the typed opening token
	/// and the closing text, keeping the enclosed text selected.
	/// </summary>
	/// <remarks>
	/// A whitespace-only selection or a selection that only repeats the typed quote is replaced through
	/// normal text input instead of being wrapped, matching the convention of mainstream desktop editors.
	/// Typing over
	/// a selection replaces it, so a skip action is ignored and normal text input applies instead.
	/// A selection consisting of other quote characters is still wrapped; hosts that follow editors which
	/// disable wrapping for cross-quote cases must apply that policy in their own selection handling.
	/// Wrapping replaces the whole selection, so it is applied only when the editor's read-only section
	/// provider allows replacing the whole range; otherwise normal text input handles the input under the
	/// same read-only policy as any other typing. The wrap travels as one edit (through the supplied
	/// target when one is provided), so a single undo removes it.
	/// </remarks>
	private TextAutoClosingResult ApplySelectionAction(
		TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingAction action,
		ITextEditTarget? editTarget)
	{
		if (action.Kind != TextAutoClosingActionKind.InsertClosingText || action.ClosingText is not string closingText)
			return TextAutoClosingResult.None;

		string selectedText = editor.SelectedText;

		if (string.IsNullOrWhiteSpace(selectedText)
			|| (string.Equals(selectedText, e.Text, StringComparison.Ordinal)
				&& string.Equals(closingText, e.Text, StringComparison.Ordinal)))
		{
			return TextAutoClosingResult.None;
		}

		int selectionStart = editor.SelectionStart;
		int selectionLength = editor.SelectionLength;

		if (!IsRangeEditable(editor, selectionStart, selectionLength))
			return TextAutoClosingResult.None;

		// The enclosed text stays selected, so consecutive wraps nest around it.
		TextEditorEditOperations.ApplyOperations(
			editor,
			[new TextEditOperation(selectionStart, selectionStart + selectionLength, e.Text + selectedText + closingText, 0)],
			editTarget);

		int documentLength = editor.Document.TextLength;
		int wrappedStart = Math.Min(selectionStart + e.Text.Length, documentLength);
		int wrappedLength = Math.Min(selectedText.Length, documentLength - wrappedStart);

		editor.Select(wrappedStart, wrappedLength);

		// The inserted closing text is tracked so the default provenance modes can recognize it later.
		TrackInsertedClosingText(editor.Document, wrappedStart + wrappedLength);

		e.Handled = true;

		return new TextAutoClosingResult(
			TextAutoClosingAction.CreateInsert(closingText), DidWrapSelection: true);
	}

	/// <summary>
	/// Records the start offset of a closing text this service inserted.
	/// </summary>
	private void TrackInsertedClosingText(TextDocument document, int offset)
	{
		DocumentTracking tracking = _tracking.GetValue(document, static _ => new DocumentTracking());

		// Deleted anchors track nothing anymore, so the list is pruned as it is extended.
		tracking.Anchors.RemoveAll(static anchor => anchor.IsDeleted);

		// The anchor marks the start of the inserted closing text and must follow it across edits. The
		// default movement moves an anchor behind text inserted exactly at its position, which is where
		// the caret sits while the user types inside the pair; a BeforeInsertion anchor would stay in
		// front of the typed text and stop marking the closing text.
		TextAnchor anchor = document.CreateAnchor(offset);
		anchor.MovementType = AnchorMovementType.Default;

		tracking.Anchors.Add(anchor);
	}

	/// <summary>
	/// Determines whether the read-only section provider allows replacing the whole range, so pair
	/// deletion and selection wrapping never apply to a partially or fully read-only range.
	/// </summary>
	private static bool IsRangeEditable(TextEditor editor, int startOffset, int length)
	{
		ISegment rangeSegment = new TextSegment
		{
			StartOffset = startOffset,
			EndOffset = startOffset + length
		};

		foreach (ISegment deletable in editor.TextArea.ReadOnlySectionProvider.GetDeletableSegments(rangeSegment))
		{
			if (deletable.Offset == startOffset && deletable.EndOffset == startOffset + length)
				return true;
		}

		return false;
	}

	/// <summary>
	/// The per-document tracking state of the closing texts this service inserted: the anchors and the
	/// resolver callback bound to them.
	/// </summary>
	private sealed class DocumentTracking
	{
		/// <summary>
		/// Initializes the tracking state; the resolver callback is created once here, so passing it on
		/// the typing path allocates nothing beyond this state.
		/// </summary>
		public DocumentTracking()
		{
			Anchors = [];
			IsTrackedClosingText = IsTrackedAt;
		}

		/// <summary>
		/// Gets the anchors of the closing texts this service inserted, in insertion order.
		/// </summary>
		public List<TextAnchor> Anchors { get; }

		/// <summary>
		/// Gets the resolver callback that reports whether a recorded closing text starts at the
		/// supplied offset and is still present.
		/// </summary>
		public Func<int, bool> IsTrackedClosingText { get; }

		/// <summary>
		/// Determines whether a recorded closing text starts at the supplied offset and is still present,
		/// so the default provenance modes recognize it.
		/// </summary>
		/// <remarks>
		/// The list is walked from the most recent insertion backwards, because a probe almost always
		/// targets the closing text that was just inserted.
		/// </remarks>
		private bool IsTrackedAt(int offset)
		{
			for (int index = Anchors.Count - 1; index >= 0; index--)
			{
				TextAnchor anchor = Anchors[index];

				if (!anchor.IsDeleted && anchor.Offset == offset)
					return true;
			}

			return false;
		}
	}
}
