namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Identifies a deterministic text-edit preparation failure.
/// </summary>
public sealed record TextEditPreparationDiagnostic
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditPreparationDiagnostic"/> record.
	/// </summary>
	/// <param name="sourceIndex">
	/// The source index of the edit: the same index that <see cref="Text.TextEditOperation.SourceIndex"/>
	/// carries for a prepared operation; for a conflict, this is the edit that triggered it.
	/// </param>
	/// <param name="relatedSourceIndex">
	/// The source index of the other edit in the conflicting pair, or <see langword="null"/> when the
	/// diagnostic concerns a single edit. Multiple insertions at one offset are valid, so no
	/// duplicate-insertion diagnostic exists.
	/// </param>
	/// <param name="message">The diagnostic message.</param>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public TextEditPreparationDiagnostic(int sourceIndex, int? relatedSourceIndex, string message)
	{
		ArgumentNullException.ThrowIfNull(message);

		SourceIndex = sourceIndex;
		RelatedSourceIndex = relatedSourceIndex;
		Message = message;
	}

	/// <summary>
	/// Gets the source index of the edit the diagnostic concerns.
	/// </summary>
	public int SourceIndex { get; }

	/// <summary>
	/// Gets the source index of the other conflicting edit, or <see langword="null"/> when the
	/// diagnostic concerns a single edit.
	/// </summary>
	public int? RelatedSourceIndex { get; }

	/// <summary>
	/// Gets the human-readable description of the failure.
	/// </summary>
	public string Message { get; }
}
