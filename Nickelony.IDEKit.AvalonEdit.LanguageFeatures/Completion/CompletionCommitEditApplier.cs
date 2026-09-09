using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Applies a completion commit: the primary insertion and the item's additional text edits are
/// applied as one atomic document change.
/// </summary>
/// <remarks>
/// <para>
/// The primary replacement range is the item's edit range while that range still fits the current
/// document (the range was produced against an earlier snapshot): the edit contributes its insert
/// range normally and its replacement range while the text area's overstrike mode is on. The
/// supplied completion segment is the fallback for a missing or stale range. With additional edits
/// present, the primary insertion and every accepted edit are applied inside one document update, so
/// the whole commit is one undo unit and a single undo restores the document. Without additional edits
/// the insertion is applied directly, exactly like a commit that carries no secondary data.
/// </para>
/// <para>
/// Accepted edits are applied from the highest to the lowest offset against their original
/// coordinates, so no applied edit shifts the offsets of an edit that is still pending. An entry
/// that cannot be applied is skipped individually instead of failing the commit: an entry without
/// explicit replacement text, an entry whose range lies outside the current document (a stale
/// range), and an entry that overlaps the primary insertion or an accepted entry are all dropped,
/// matching how the response parsers treat malformed payload entries. Ranges that merely touch - an
/// edit ending at the insertion's start offset, a zero-length edit at that offset, or an edit
/// starting at the insertion's end offset - do not overlap and are applied; an edit that covers a
/// zero-length insertion point and extends beyond it is treated as overlapping and dropped, because
/// no application order can honor both the insertion and the replacement.
/// </para>
/// <para>
/// The conflict rules follow the conventions of the shared
/// <see cref="Nickelony.IDEKit.Core.Editing.TextEditKernel"/>: an insertion that only touches a
/// replacement's boundaries is applied, and a replacement is applied before an insertion that shares its
/// start offset. The failure policy is deliberately different: a malformed or conflicting additional entry
/// is skipped individually instead of failing the whole commit, because a commit must not be lost to one
/// bad secondary payload. Routing commits through the kernel would need a mode that reports skippable
/// entries; that unification is tracked in the repository backlog.
/// </para>
/// </remarks>
internal static class CompletionCommitEditApplier
{
	/// <summary>
	/// Applies a completion commit: the primary insertion and the item's additional text edits are
	/// applied as one document change.
	/// </summary>
	/// <param name="payload">The commit to apply.</param>
	internal static void Apply(CompletionCommitPayload payload)
	{
		TextDocument document = payload.TextArea.Document;
		(int primaryOffset, int primaryLength) = ResolvePrimarySegment(
			payload.TextArea,
			payload.CompletionSegment,
			payload.PrimaryEdit);

		if (payload.AdditionalTextEdits.Count == 0)
		{
			document.Replace(primaryOffset, primaryLength, payload.InsertText);
			ApplyCaretOffset(payload.TextArea, primaryOffset, payload.CaretOffsetInInsertText);

			return;
		}

		var acceptedEdits = new List<AcceptedEdit>(payload.AdditionalTextEdits.Count);

		foreach (TextCompletionTextEdit edit in payload.AdditionalTextEdits)
		{
			AcceptedEdit? acceptedEdit = PrepareAdditionalEdit(document, primaryOffset, primaryLength, edit, acceptedEdits);

			if (acceptedEdit is AcceptedEdit value)
				acceptedEdits.Add(value);
		}

		// The primary insertion is listed first, and the descending sort is stable, so it precedes an
		// accepted edit that shares its start offset; the edit's text then lands before the insertion,
		// matching the shared-offset convention of the Core text-edit kernel. An accepted edit can only
		// share the offset as a zero-length insertion: a non-zero-length range that starts there (or that
		// covers a zero-length insertion point) was dropped as overlapping by PrepareAdditionalEdit.
		var operations = new List<AcceptedEdit>(acceptedEdits.Count + 1)
		{
			new(primaryOffset, primaryLength, payload.InsertText)
		};

		operations.AddRange(acceptedEdits);

		// Accepted edits before the primary insertion are applied after it, so they shift both the
		// inserted text and the caret position that was derived from it.
		int caretShift = 0;

		foreach (AcceptedEdit edit in acceptedEdits)
		{
			if (edit.Offset + edit.Length <= primaryOffset)
				caretShift += edit.NewText.Length - edit.Length;
		}

		using (document.RunUpdate())
		{
			foreach (AcceptedEdit operation in operations.OrderByDescending(static operation => operation.Offset))
			{
				document.Replace(operation.Offset, operation.Length, operation.NewText);
			}
		}

		ApplyCaretOffset(payload.TextArea, primaryOffset + caretShift, payload.CaretOffsetInInsertText);
	}

	/// <summary>
	/// Resolves the primary replacement range: the edit payload's range while it still fits the current
	/// document, or the supplied completion segment otherwise.
	/// </summary>
	/// <param name="textArea">The text area whose document receives the commit.</param>
	/// <param name="completionSegment">The live segment the completion window supplies.</param>
	/// <param name="primaryEdit">The item's edit payload, when the item carries one.</param>
	/// <returns>The zero-based primary replacement range.</returns>
	private static (int Offset, int Length) ResolvePrimarySegment(
		TextArea textArea,
		ISegment completionSegment,
		TextCompletionTextEdit? primaryEdit)
	{
		var fallbackSegment = (completionSegment.Offset, completionSegment.Length);

		if (primaryEdit is not TextCompletionTextEdit edit)
			return fallbackSegment;

		// Overstrike mode replaces the edit's replace range, matching how the editor's own completion
		// window treats insert versus replace ranges.
		TextRange range = textArea.OverstrikeMode ? edit.ReplacementRange : edit.InsertRange;
		TextDocument document = textArea.Document;

		// A range outside the current document is stale: the item was produced against an earlier
		// document state, so the commit falls back to the live completion segment.
		return range.Offset <= document.TextLength && range.Length <= document.TextLength - range.Offset
			? (range.Offset, range.Length)
			: fallbackSegment;
	}

	/// <summary>
	/// Validates one additional edit against the current document, the primary replacement, and the
	/// edits accepted so far.
	/// </summary>
	/// <param name="document">The document the commit is applied to.</param>
	/// <param name="segmentOffset">The zero-based start offset of the primary replacement.</param>
	/// <param name="segmentLength">The length of the primary replacement.</param>
	/// <param name="edit">The additional edit to prepare.</param>
	/// <param name="acceptedEdits">The edits accepted before this one.</param>
	/// <returns>The prepared edit, or <see langword="null"/> when the entry is skipped.</returns>
	private static AcceptedEdit? PrepareAdditionalEdit(
		TextDocument document,
		int segmentOffset,
		int segmentLength,
		TextCompletionTextEdit edit,
		List<AcceptedEdit> acceptedEdits)
	{
		if (edit.NewText is not string newText)
			return null;

		TextRange range = edit.ReplacementRange;

		// A range outside the current document is stale: the item was produced against an earlier
		// document state, so the entry is dropped instead of failing the commit.
		if (range.Offset > document.TextLength || range.Length > document.TextLength - range.Offset)
			return null;

		int segmentEnd = segmentOffset + segmentLength;

		// A non-zero primary range is overlapped by any edit that reaches into its interior. A zero-length
		// insertion point is overlapped by an edit that covers it and extends beyond it (the edit replaces
		// text from the insertion point onward), because no application order can honor both operations:
		// applying the edit after the insertion would consume the freshly inserted text. The entry is dropped
		// like any other overlapping entry, while a zero-length edit at the insertion point and an edit
		// ending exactly at it merely touch and are applied.
		bool overlapsPrimary = segmentLength > 0
			? range.Offset < segmentEnd && range.EndOffset > segmentOffset
			: range.Offset <= segmentOffset && range.EndOffset > segmentOffset;

		if (overlapsPrimary)
			return null;

		foreach (AcceptedEdit acceptedEdit in acceptedEdits)
		{
			if (range.Offset < acceptedEdit.Offset + acceptedEdit.Length && range.EndOffset > acceptedEdit.Offset)
				return null;
		}

		return new AcceptedEdit(range.Offset, range.Length, newText);
	}

	/// <summary>
	/// Places the caret at the offset derived from the insertion text, when one was supplied.
	/// </summary>
	private static void ApplyCaretOffset(TextArea textArea, int insertionStartOffset, int? caretOffsetInInsertText)
	{
		if (caretOffsetInInsertText is int offset)
			textArea.Caret.Offset = insertionStartOffset + offset;
	}

	/// <summary>
	/// One prepared operation of the commit batch, using its original document coordinates.
	/// </summary>
	private readonly record struct AcceptedEdit(int Offset, int Length, string NewText);
}

/// <summary>
/// The inputs of one completion commit: the primary insertion, the segment it replaces, and the
/// item's optional edit payload and secondary edits.
/// </summary>
/// <param name="TextArea">The text area whose document receives the commit.</param>
/// <param name="CompletionSegment">
/// The live segment the completion window supplies as the fallback replacement range.
/// </param>
/// <param name="InsertText">The text that replaces the primary range.</param>
/// <param name="CaretOffsetInInsertText">
/// The caret offset within <paramref name="InsertText"/> after the commit, or <see langword="null"/>
/// to leave the caret where the document changes place it.
/// </param>
/// <param name="AdditionalTextEdits">The secondary edits to apply with the insertion.</param>
/// <param name="PrimaryEdit">
/// The item's edit payload; its range is the primary replacement range while the range still fits the
/// current document, or <see langword="null"/> to use <paramref name="CompletionSegment"/>.
/// </param>
internal readonly record struct CompletionCommitPayload(
	TextArea TextArea,
	ISegment CompletionSegment,
	string InsertText,
	int? CaretOffsetInInsertText,
	IReadOnlyList<TextCompletionTextEdit> AdditionalTextEdits,
	TextCompletionTextEdit? PrimaryEdit);
