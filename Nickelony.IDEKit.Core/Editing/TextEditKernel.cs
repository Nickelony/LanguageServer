using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Prepares neutral text edits without mutating a document or depending on a UI host or protocol.
/// </summary>
public static class TextEditKernel
{
	/// <summary>
	/// Validates source-offset edits against a text snapshot and returns descending operations.
	/// </summary>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">The edits to validate and prepare.</param>
	/// <returns>A result containing descending-offset operations when all edits are valid.</returns>
	public static TextEditPreparationResult Prepare(
		ITextSnapshot snapshot,
		IEnumerable<TextEditInput?> edits)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(edits);

		var candidates = new List<Candidate>();
		var diagnostics = new List<TextEditPreparationDiagnostic>();
		int editIndex = 0;

		foreach (TextEditInput? edit in edits)
		{
			if (edit is null)
			{
				diagnostics.Add(new TextEditPreparationDiagnostic(
					TextEditPreparationDiagnosticCode.InvalidRange,
					editIndex,
					-1,
					"The edit is null."));
			}
			else if (edit.Range.Offset > snapshot.TextLength
				|| edit.Range.Length > snapshot.TextLength - edit.Range.Offset)
			{
				diagnostics.Add(new TextEditPreparationDiagnostic(
					TextEditPreparationDiagnosticCode.InvalidRange,
					editIndex,
					-1,
					"The edit range is outside the document."));
			}
			else
			{
				candidates.Add(new Candidate(
					editIndex,
					edit.Range.Offset,
					edit.Range.EndOffset,
					edit.NewText ?? string.Empty));
			}

			editIndex++;
		}

		candidates.Sort(CandidateComparer.Instance);
		AddConflictDiagnostics(candidates, diagnostics);

		if (diagnostics.Count > 0)
			return new TextEditPreparationResult([], diagnostics);

		var operations = new List<TextEditOperation>(candidates.Count);

		for (int index = candidates.Count - 1; index >= 0; index--)
		{
			Candidate candidate = candidates[index];

			operations.Add(new TextEditOperation(
				candidate.StartOffset,
				candidate.EndOffset,
				candidate.NewText,
				candidate.SourceIndex));
		}

		return new TextEditPreparationResult(operations, []);
	}

	/// <summary>
	/// Maps an offset from the source snapshot to the text after prepared edits are applied.
	/// </summary>
	/// <param name="offset">The zero-based source offset to map.</param>
	/// <param name="operations">The valid prepared operations.</param>
	/// <returns>The corresponding zero-based offset in the edited text.</returns>
	public static int MapOffset(int offset, IReadOnlyList<TextEditOperation> operations)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentNullException.ThrowIfNull(operations);

		int cumulativeDelta = 0;

		for (int index = operations.Count - 1; index >= 0; index--)
		{
			TextEditOperation operation = operations[index];

			if (offset < operation.StartOffset)
				break;

			if (offset <= operation.EndOffset)
			{
				int relativeOffset = offset - operation.StartOffset;
				int normalizedRelativeOffset = Math.Min(relativeOffset, operation.NewText.Length);
				return operation.StartOffset + cumulativeDelta + normalizedRelativeOffset;
			}

			cumulativeDelta += operation.NewText.Length - operation.Length;
		}

		return offset + cumulativeDelta;
	}

	private static void AddConflictDiagnostics(
		IReadOnlyList<Candidate> candidates,
		List<TextEditPreparationDiagnostic> diagnostics)
	{
		for (int leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
		{
			Candidate left = candidates[leftIndex];

			for (int rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
			{
				Candidate right = candidates[rightIndex];

				if (left.StartOffset == left.EndOffset && right.StartOffset == right.EndOffset)
				{
					if (left.StartOffset == right.StartOffset)
					{
						diagnostics.Add(new TextEditPreparationDiagnostic(
							TextEditPreparationDiagnosticCode.DuplicateInsertion,
							left.SourceIndex,
							right.SourceIndex,
							"Multiple insertions target the same source offset."));
					}

					continue;
				}

				if (left.StartOffset == left.EndOffset || right.StartOffset == right.EndOffset)
				{
					Candidate insertion = left.StartOffset == left.EndOffset ? left : right;
					Candidate replacement = left.StartOffset == left.EndOffset ? right : left;

					if (insertion.StartOffset > replacement.StartOffset && insertion.StartOffset < replacement.EndOffset)
					{
						diagnostics.Add(new TextEditPreparationDiagnostic(
							TextEditPreparationDiagnosticCode.InsertionReplacementIntersection,
							insertion.SourceIndex,
							replacement.SourceIndex,
							"An insertion intersects a replacement range."));
					}

					continue;
				}

				if (left.EndOffset > right.StartOffset)
				{
					diagnostics.Add(new TextEditPreparationDiagnostic(
						TextEditPreparationDiagnosticCode.OverlappingRanges,
						left.SourceIndex,
						right.SourceIndex,
						"The edit ranges overlap."));
				}
			}
		}
	}

	private sealed record Candidate(int SourceIndex, int StartOffset, int EndOffset, string NewText);

	private sealed class CandidateComparer : IComparer<Candidate>
	{
		public static CandidateComparer Instance { get; } = new();

		public int Compare(Candidate? left, Candidate? right)
		{
			if (ReferenceEquals(left, right))
				return 0;

			if (left is null)
				return -1;

			if (right is null)
				return 1;

			int startComparison = left.StartOffset.CompareTo(right.StartOffset);

			if (startComparison != 0)
				return startComparison;

			int endComparison = left.EndOffset.CompareTo(right.EndOffset);

			return endComparison != 0
				? endComparison
				: left.SourceIndex.CompareTo(right.SourceIndex);
		}
	}
}
