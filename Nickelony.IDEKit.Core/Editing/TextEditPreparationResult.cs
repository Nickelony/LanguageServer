namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Contains prepared edits and deterministic diagnostics for one edit batch.
/// </summary>
/// <remarks>
/// A result is either valid - no diagnostics, editable operations ready to apply - or rejected -
/// diagnostics and no operations. Create results through <see cref="Valid"/> and <see cref="Invalid"/>
/// so the contradictory combination cannot be represented. <see cref="Edits"/> is a
/// <see cref="PreparedTextEdits"/> batch, so it is already ordered and validated at construction;
/// apply or map it without re-validating.
/// </remarks>
public sealed class TextEditPreparationResult
{
	// A rejected batch has no operations, so every rejected result shares one immutable empty batch
	// instead of allocating a fresh carrier per rejection.
	private static readonly PreparedTextEdits s_emptyEdits = new([]);

	private TextEditPreparationResult(
		PreparedTextEdits edits,
		IReadOnlyList<TextEditPreparationDiagnostic> diagnostics)
	{
		Edits = edits;
		Diagnostics = Array.AsReadOnly([.. diagnostics]);
	}

	/// <summary>
	/// Creates a result for a valid edit batch.
	/// </summary>
	/// <param name="edits">The prepared, validated operations; the batch may be empty when the input contained no effective edits.</param>
	/// <returns>A valid result without diagnostics.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	public static TextEditPreparationResult Valid(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		return new TextEditPreparationResult(edits, []);
	}

	/// <summary>
	/// Creates a result for a rejected edit batch.
	/// </summary>
	/// <param name="diagnostics">The diagnostics that rejected the batch; must not be empty.</param>
	/// <returns>A rejected result without operations.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="diagnostics"/> is empty.</exception>
	public static TextEditPreparationResult Invalid(IReadOnlyList<TextEditPreparationDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		if (diagnostics.Count == 0)
			throw new ArgumentException("At least one diagnostic is required for a rejected batch.", nameof(diagnostics));

		return new TextEditPreparationResult(s_emptyEdits, diagnostics);
	}

	/// <summary>
	/// Gets the prepared, validated edits. Empty when the batch was rejected.
	/// </summary>
	public PreparedTextEdits Edits { get; }

	/// <summary>
	/// Gets the deterministic preparation diagnostics.
	/// </summary>
	/// <remarks>
	/// Diagnostics for invalid edits appear first in edit order. Conflict diagnostics follow a
	/// deterministic order: pairwise range conflicts are ordered by the left operation under the
	/// candidate order and then by the triggering operation. The collection is deterministic for a
	/// given input.
	/// </remarks>
	public IReadOnlyList<TextEditPreparationDiagnostic> Diagnostics { get; }

	/// <summary>
	/// Gets a value indicating whether the edit batch is valid and ready to be applied.
	/// </summary>
	public bool IsValid => Diagnostics.Count == 0;
}
