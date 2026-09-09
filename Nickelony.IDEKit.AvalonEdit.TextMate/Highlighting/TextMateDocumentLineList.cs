using ICSharpCode.AvalonEdit.Document;
using TextMateSharp.Grammars;
using TextMateSharp.Model;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Provides TextMateSharp with a zero-based view of an AvalonEdit <see cref="TextDocument"/>'s lines.
/// Each line includes its terminator when present, and the view is updated when the document changes.
/// </summary>
/// <remarks>
/// <para>
/// The view is an incrementally maintained snapshot: line texts are copied so the tokenizer can read
/// them from its background thread while the document stays owned by the UI thread. The snapshot stores
/// every line including its terminator, so it duplicates the document text and carries that cost for
/// large documents. Lines outside a change keep their tokenization state; the changed region is
/// re-read and invalidated.
/// </para>
/// <para>
/// Change handling derives the affected line range from the document's own line geometry rather than
/// from the line breaks found in the changed text, because a break inside a changed fragment can pair
/// with a carriage return or line feed just outside it: removing the line feed of a CRLF pair removes
/// a break from the fragment while the line structure keeps both lines. The affected range is computed
/// from the positions that are unchanged in both the old and the new document, so the snapshot, the
/// model's line-state list, and the document always keep the same line count.
/// </para>
/// <para>
/// The snapshot members the tokenizer thread calls (<see cref="GetNumberOfLines"/>,
/// <see cref="GetLineLength"/>, and <see cref="GetLineTextIncludingTerminators"/>) are safe to call
/// from any thread: the snapshot is guarded by a private lock and those members only read it.
/// Document change handling runs on the thread that owns the document because it reads the document
/// and invalidates the model; <see cref="Dispose"/> only detaches that handler. A tokenizer-thread
/// read can observe a transient mismatch between the snapshot and the model's line-state list while
/// an update is in flight; both reads are range-guarded, and the update invalidates the changed
/// region so the following tokenization pass re-reads it.
/// </para>
/// <para>
/// A line list backs exactly one <see cref="TMModel"/> because a model binds its line list when it is
/// constructed, and a second model would replace the first model's binding. Disposing the model also
/// disposes its line list, and this list's <see cref="Dispose"/> is idempotent, so disposing the model
/// is sufficient and disposing the list first is equally safe. A list that is disposed without its
/// model keeps the model alive but stops receiving document updates; dispose the model before
/// dropping the list.
/// </para>
/// </remarks>
public sealed class TextMateDocumentLineList : AbstractLineList
{
	private readonly TextDocument _document;
	private readonly object _syncRoot = new();
	private readonly List<string> _lineTexts = [];

	// The number of line-state slots this list has added to the model's list. TextMateSharp keeps that
	// list private, so the count is tracked here and used to reconcile the slots in the self-healing
	// path of a change that detects an inconsistent state.
	private int _slotCount;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateDocumentLineList"/> class.
	/// </summary>
	/// <param name="document">The document whose lines are tracked.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> is <see langword="null"/>.
	/// </exception>
	public TextMateDocumentLineList(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		_document = document;

		InitializeSnapshot();

		_document.Changed += Document_Changed;
	}

	/// <inheritdoc/>
	public override void UpdateLine(int lineIndex)
		=> InvalidateLine(lineIndex);

	/// <inheritdoc/>
	public override int GetNumberOfLines()
	{
		lock (_syncRoot)
			return _lineTexts.Count;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields an empty line instead of throwing. The
	/// tokenizer can probe an index that a concurrent document update has already removed, and an empty
	/// line lets that read complete so the failed line is re-tokenized by the next pass.
	/// </remarks>
	public override LineText GetLineTextIncludingTerminators(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return new LineText(string.Empty);

			return new LineText(_lineTexts[lineIndex]);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields zero instead of throwing; see
	/// <see cref="GetLineTextIncludingTerminators"/> for the rationale.
	/// </remarks>
	public override int GetLineLength(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return 0;

			return _lineTexts[lineIndex].Length;
		}
	}

	/// <inheritdoc/>
	public override void Dispose()
		=> _document.Changed -= Document_Changed;

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		int invalidatedStartLineIndex;
		int invalidatedEndLineIndex;

		// Lock-order contract: TextMateSharp's model acquires its own lock before calling into
		// IModelLines, so this list's lock must never be held while calling back into the model
		// (the order is always model lock -> list lock, never the reverse).
		lock (_syncRoot)
			(invalidatedStartLineIndex, invalidatedEndLineIndex) = ApplyChange(e);

		// Outside _syncRoot: InvalidateLineRange calls TMModel.InvalidateLineRange, which marks the whole
		// range and queues it under a single model lock and signal. Every line of the changed region is
		// invalidated because new lines can land on slots that previously held removed lines: such slots
		// are neither fresh nor guaranteed to have an end state that differs from their predecessor, so
		// the model's forward walk alone could keep stale tokens. Lines after the region keep their
		// tokenization state, and the tokenizer walks forward again once an end state changes.
		if (invalidatedEndLineIndex >= invalidatedStartLineIndex)
			InvalidateLineRange(invalidatedStartLineIndex, invalidatedEndLineIndex);
	}

	/// <summary>
	/// Applies a document change to the snapshot and the model's line-state slots, and returns the
	/// inclusive line range whose tokenization state must be invalidated.
	/// </summary>
	/// <param name="change">The document change to apply.</param>
	/// <returns>The zero-based inclusive line range to invalidate.</returns>
	private (int StartLineIndex, int EndLineIndex) ApplyChange(DocumentChangeEventArgs change)
	{
		int oldLineCount = _lineTexts.Count;
		int newLineCount = _document.LineCount;

		if (!TryComputeAffectedLines(change, oldLineCount, newLineCount, out AffectedLines affected))
		{
			RebuildCore();
			return (0, Math.Max(0, newLineCount - 1));
		}

		int removedLineCount = affected.OldTailFirstLineIndex - affected.FirstAffectedLineIndex;
		int insertedLineCount = affected.NewTailFirstLineIndex - affected.FirstAffectedLineIndex;
		int lineDelta = newLineCount - oldLineCount;

		// The tail line counts must agree for the change to be expressible as one replaced line range;
		// a disagreement means the tracked state is not consistent with the document geometry.
		if (removedLineCount < 0
			|| insertedLineCount < 0
			|| affected.OldTailFirstLineIndex > oldLineCount
			|| affected.NewTailFirstLineIndex > newLineCount
			|| removedLineCount - insertedLineCount != oldLineCount - newLineCount
			|| _slotCount != oldLineCount)
		{
			RebuildCore();
			return (0, Math.Max(0, newLineCount - 1));
		}

		ReplaceSnapshotLines(affected.FirstAffectedLineIndex, removedLineCount, insertedLineCount);
		ApplySlotDelta(affected.OldTailFirstLineIndex, affected.NewTailFirstLineIndex, lineDelta);
		_slotCount = newLineCount;

		return (affected.FirstAffectedLineIndex, affected.NewTailFirstLineIndex - 1);
	}

	/// <summary>
	/// Computes the line ranges an edit replaces by aligning the old and the new line geometry at the
	/// unchanged text around the change.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <param name="oldLineCount">The snapshot's line count before the change.</param>
	/// <param name="newLineCount">The document's line count after the change.</param>
	/// <param name="affected">The computed line ranges when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when the affected ranges could be computed.</returns>
	private bool TryComputeAffectedLines(
		DocumentChangeEventArgs change,
		int oldLineCount,
		int newLineCount,
		out AffectedLines affected)
	{
		affected = default;

		int changeOffset = change.Offset;
		int changeEndOffset = changeOffset + change.InsertionLength;

		// The first affected line is the line that contains the first changed character. All text before
		// the change is unchanged, so that line has the same index in the old and the new document; when
		// the change starts exactly where the previous line ends, the change begins on the next line and
		// the previous line keeps its tokenization state.
		int firstAffectedLineIndex = ComputeFirstAffectedLineIndex(change);

		// The preserved tail starts where the line geometry realigns after the change end. The first new
		// line that begins at or after the change end is clean when its start position was also a line
		// start in the old document; otherwise it merged with the changed region, and the line after it
		// is the first clean tail line.
		int newTailFirstLineIndex;
		DocumentLine endLine = _document.GetLineByOffset(changeEndOffset);

		if (endLine.Offset == changeEndOffset)
		{
			newTailFirstLineIndex = OldTextEndsWithLineTerminator(change)
				? endLine.LineNumber - 1
				: endLine.LineNumber;
		}
		else
		{
			newTailFirstLineIndex = endLine.LineNumber < newLineCount
				? endLine.LineNumber
				: newLineCount;
		}

		// Old and new tail lines stay in a fixed relative order, so the old tail starts one line-count
		// delta before the new tail.
		int oldTailFirstLineIndex = oldLineCount - (newLineCount - newTailFirstLineIndex);

		if (firstAffectedLineIndex < 0
			|| firstAffectedLineIndex > oldLineCount - 1
			|| firstAffectedLineIndex > newLineCount - 1
			|| oldTailFirstLineIndex < firstAffectedLineIndex
			|| newTailFirstLineIndex < firstAffectedLineIndex)
		{
			return false;
		}

		affected = new AffectedLines(firstAffectedLineIndex, oldTailFirstLineIndex, newTailFirstLineIndex);
		return true;
	}

	/// <summary>
	/// Computes the first line whose content changes, which is the line that contains the first changed
	/// character.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <returns>The zero-based index of the first changed line.</returns>
	private int ComputeFirstAffectedLineIndex(DocumentChangeEventArgs change)
	{
		if (change.Offset == 0)
			return 0;

		int previousLineIndex = GetDocumentLineIndex(change.Offset - 1);

		if (!OldLineEndsAt(change, change.Offset))
			return previousLineIndex;

		// Inserting a line feed directly after a lone carriage return merges the two terminators into a
		// CRLF pair, which changes the previous line's delimiter: that line is part of the changed region
		// even though the change starts at a line boundary.
		if (change.RemovalLength == 0
			&& change.InsertionLength > 0
			&& change.InsertedText.Text[0] == '\n'
			&& _document.GetCharAt(change.Offset - 1) == '\r')
		{
			return previousLineIndex;
		}

		return previousLineIndex + 1;
	}

	/// <summary>
	/// Determines whether the old line that contains the given position ends exactly there, which makes
	/// that position the start of a line.
	/// </summary>
	/// <param name="change">The document change being analyzed.</param>
	/// <param name="offset">The zero-based offset just after the line content to test.</param>
	/// <returns><see langword="true"/> when the old line ends at the offset.</returns>
	private bool OldLineEndsAt(DocumentChangeEventArgs change, int offset)
	{
		// The character before an offset is unchanged by definition, so the document still holds it.
		char previousChar = _document.GetCharAt(offset - 1);

		if (previousChar == '\n')
			return true;

		if (previousChar != '\r')
			return false;

		// A carriage return ends the line when it is not followed by a line feed: a lone CR is a
		// terminator, while a CRLF pair continues into the following character. The following character
		// of the old text is the first removed character or, for a pure insertion, the unchanged character
		// after the inserted text.
		if (change.RemovalLength > 0)
			return change.RemovedText.Text[0] != '\n';

		int followingOldOffset = offset + change.InsertionLength;

		return followingOldOffset >= _document.TextLength
			|| _document.GetCharAt(followingOldOffset) != '\n';
	}

	/// <summary>
	/// Determines whether the old text ends with a line terminator at the change end, which decides
	/// whether a line boundary just after the change survives the edit.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <returns><see langword="true"/> when the old text ends with a line terminator.</returns>
	private bool OldTextEndsWithLineTerminator(DocumentChangeEventArgs change)
	{
		if (change.RemovalLength > 0)
		{
			string removedText = change.RemovedText.Text;
			return removedText.Length > 0 && removedText[^1] is '\r' or '\n';
		}

		// A pure insertion: the character before the insertion point is unchanged, so the document
		// still holds it.
		return change.Offset == 0 || _document.GetCharAt(change.Offset - 1) is '\r' or '\n';
	}

	private void InitializeSnapshot()
	{
		lock (_syncRoot)
			RebuildCore();
	}

	private void ReplaceSnapshotLines(int startLineIndex, int removedLineCount, int insertedLineCount)
	{
		var insertedLines = new List<string>(insertedLineCount);

		for (int i = 0; i < insertedLineCount; i++)
			insertedLines.Add(ReadDocumentLineText(startLineIndex + i));

		if (removedLineCount > 0)
			_lineTexts.RemoveRange(startLineIndex, removedLineCount);

		if (insertedLines.Count > 0)
			_lineTexts.InsertRange(startLineIndex, insertedLines);
	}

	private void ApplySlotDelta(int oldTailFirstLineIndex, int newTailFirstLineIndex, int lineDelta)
	{
		// The tail line states stay bound to their lines: slots ahead of the tail are inserted or
		// removed so that the old tail slots land on the new tail indexes. AddLine/RemoveLine only
		// mutate the model's private line-state list and never call back into the model.
		if (lineDelta > 0)
		{
			for (int i = 0; i < lineDelta; i++)
				AddLine(oldTailFirstLineIndex);
		}
		else if (lineDelta < 0)
		{
			for (int i = 0; i < -lineDelta; i++)
				RemoveLine(newTailFirstLineIndex);
		}
	}

	/// <summary>
	/// Rebuilds the snapshot for the whole document and reconciles the model's line-state slots with it,
	/// used for the initial snapshot and as a self-healing fallback when a change detects an
	/// inconsistent state.
	/// </summary>
	private void RebuildCore()
	{
		_lineTexts.Clear();

		for (int i = 0; i < _document.LineCount; i++)
			_lineTexts.Add(ReadDocumentLineText(i));

		while (_slotCount > _lineTexts.Count)
		{
			RemoveLine(_slotCount - 1);
			_slotCount--;
		}

		while (_slotCount < _lineTexts.Count)
		{
			AddLine(_slotCount);
			_slotCount++;
		}
	}

	private int GetDocumentLineIndex(int offset)
	{
		DocumentLine line = _document.GetLineByOffset(offset);
		return line.LineNumber - 1;
	}

	private string ReadDocumentLineText(int lineIndex)
	{
		// A TextDocument always exposes at least one line, so the index is always in range.
		DocumentLine line = _document.GetLineByNumber(lineIndex + 1);
		return _document.GetText(line.Offset, line.TotalLength);
	}

	/// <summary>
	/// Describes the line ranges a change replaces: the first affected line and the first line of the
	/// preserved tail in both the old and the new line coordinates.
	/// </summary>
	private readonly record struct AffectedLines(
		int FirstAffectedLineIndex,
		int OldTailFirstLineIndex,
		int NewTailFirstLineIndex);
}
