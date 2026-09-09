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
	/// <remarks>
	/// <para>
	/// Insertions that touch a replacement range's boundaries are accepted. When an insertion shares
	/// a replacement's start offset, the replacement is ordered first and the host applies operations
	/// in list order, so the inserted text lands before the replacement text; an insertion at a
	/// replacement's end offset lands after it.
	/// </para>
	/// <para>
	/// Multiple insertions at one source offset are valid: the operations are ordered so that
	/// applying the list in order inserts their texts in the caller's edit order (the text of the
	/// lowest edit index appears first), matching protocol conventions such as the LSP edit array.
	/// </para>
	/// <para>
	/// A no-op edit (an empty range with an empty replacement text) contributes no operation, so a
	/// batch of no-op edits is valid and produces no operations.
	/// </para>
	/// <para>
	/// Ranges are validated first: a no-op whose range lies outside the document is still rejected
	/// with the range diagnostic instead of being ignored.
	/// </para>
	/// </remarks>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">
	/// The edits to validate and prepare. A <see langword="null"/> entry or a <see langword="null"/>
	/// replacement text is reported as a diagnostic instead of throwing.
	/// </param>
	/// <returns>
	/// <list type="bullet">
	/// <item>A result containing descending-offset operations when all edits are valid;</item>
	/// <item>A result with no operations and the collected diagnostics when any edit is rejected.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="edits"/> is <see langword="null"/>.
	/// </exception>
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
					editIndex,
					null,
					"The edit is null."));
			}
			else if (edit.NewText is null)
			{
				diagnostics.Add(new TextEditPreparationDiagnostic(
					editIndex,
					null,
					"The edit replacement text is null."));
			}
			else if (edit.Range.Offset > snapshot.TextLength
				|| edit.Range.Length > snapshot.TextLength - edit.Range.Offset)
			{
				diagnostics.Add(new TextEditPreparationDiagnostic(
					editIndex,
					null,
					"The edit range is outside the document."));
			}
			else if (!edit.IsNoOp)
			{
				// An empty range with an empty replacement text changes nothing, so it is ignored
				// instead of being classified as an insertion and rejected as an intersecting edit.
				candidates.Add(new Candidate(
					editIndex,
					edit.Range.Offset,
					edit.Range.EndOffset,
					edit.NewText));
			}

			editIndex++;
		}

		candidates.Sort(CandidateComparer.Instance);

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

		AddConflictDiagnostics(operations, diagnostics);

		if (diagnostics.Count > 0)
			return TextEditPreparationResult.Invalid(diagnostics);

		// The shared conflict detector already ran over this batch above, so the carrier is created
		// through the kernel-validated factory instead of re-detecting the same conflicts in its
		// validating constructor.
		return TextEditPreparationResult.Valid(PreparedTextEdits.CreateKernelValidated(operations));
	}

	/// <summary>
	/// Adds a diagnostic for every conflict reported by the shared conflict detector.
	/// </summary>
	/// <remarks>
	/// Each diagnostic names the edit it was detected for as <c>SourceIndex</c> and the edit it
	/// conflicts with as <c>RelatedSourceIndex</c>. The conflict order is documented on
	/// <see cref="TextEditConflictDetector.FindConflicts"/>. The shared detector is the same rule set
	/// that <see cref="PreparedTextEdits"/> validates with, so a prepared batch never fails
	/// construction.
	/// </remarks>
	private static void AddConflictDiagnostics(
		IReadOnlyList<TextEditOperation> operations,
		List<TextEditPreparationDiagnostic> diagnostics)
	{
		foreach (TextEditConflict conflict in TextEditConflictDetector.FindConflicts(operations))
		{
			diagnostics.Add(new TextEditPreparationDiagnostic(
				conflict.SourceIndex,
				conflict.RelatedSourceIndex,
				TextEditConflictMessages.GetDiagnosticMessage(conflict.Kind)));
		}
	}

	private readonly record struct Candidate(int SourceIndex, int StartOffset, int EndOffset, string NewText);

	private sealed class CandidateComparer : IComparer<Candidate>
	{
		public static CandidateComparer Instance { get; } = new();

		public int Compare(Candidate left, Candidate right)
			=> new TextEditOrderKey(left.StartOffset, left.EndOffset, left.SourceIndex)
				.CompareTo(new TextEditOrderKey(right.StartOffset, right.EndOffset, right.SourceIndex));
	}
}
