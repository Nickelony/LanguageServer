using Nickelony.IDEKit.IntelliSense.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Describes the editor state a code-action request is built from.
/// </summary>
/// <remarks>
/// <para>
/// A host builds one context for a request that settled after the debounce delay, from the editor's
/// document, caret, and selection, and passes it to its request-state builder. The offsets are
/// zero-based and validated against <see cref="DocumentText"/>: every offset lies within the document and the selection
/// offsets are ordered, so <see cref="SelectionStartOffset"/> is never greater than
/// <see cref="SelectionEndOffset"/>. The selection offsets equal <see cref="CaretOffset"/> when the
/// selection is empty, but the record does not require it because a selection does not have to
/// contain the caret.
/// </para>
/// <para>
/// The context is editor-state input for the host's request policy, not a provider contract: the host
/// decides which document range the provider is asked about - the caret position, the caret's
/// diagnostic span, or the selection - and returns that snapshot as a
/// <see cref="TextCodeActionRequestState"/>.
/// </para>
/// </remarks>
public readonly record struct TextCodeActionContext
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionContext"/> record struct.
	/// </summary>
	/// <param name="documentText">The document text snapshot the offsets refer to.</param>
	/// <param name="caretOffset">The zero-based offset of the caret.</param>
	/// <param name="selectionStartOffset">
	/// The zero-based start offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <param name="selectionEndOffset">
	/// The zero-based end offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An offset is negative or greater than the document text length, or
	/// <paramref name="selectionStartOffset"/> is greater than <paramref name="selectionEndOffset"/>.
	/// </exception>
	public TextCodeActionContext(
		string documentText,
		int caretOffset,
		int selectionStartOffset,
		int selectionEndOffset)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		SnapshotOffsetValidation.Validate(documentText, caretOffset, nameof(caretOffset), "caret");
		SnapshotOffsetValidation.Validate(documentText, selectionStartOffset, nameof(selectionStartOffset), "selection start");
		SnapshotOffsetValidation.Validate(documentText, selectionEndOffset, nameof(selectionEndOffset), "selection end");
		SnapshotOffsetValidation.ValidateOrderedRange(selectionStartOffset, selectionEndOffset, nameof(selectionStartOffset), "selection");

		this.DocumentText = documentText;
		this.CaretOffset = caretOffset;
		this.SelectionStartOffset = selectionStartOffset;
		this.SelectionEndOffset = selectionEndOffset;
	}

	/// <summary>Gets the document text snapshot the offsets refer to.</summary>
	public string DocumentText { get; }

	/// <summary>Gets the zero-based offset of the caret.</summary>
	public int CaretOffset { get; }

	/// <summary>
	/// Gets the zero-based start offset of the selection, or the caret offset when the selection is empty.
	/// </summary>
	public int SelectionStartOffset { get; }

	/// <summary>
	/// Gets the zero-based end offset of the selection, or the caret offset when the selection is empty.
	/// </summary>
	public int SelectionEndOffset { get; }
}
