using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Contains prepared operations and deterministic diagnostics for one edit batch.
/// </summary>
public sealed class TextEditPreparationResult
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditPreparationResult"/> class.
	/// </summary>
	/// <param name="operations">The operations ordered from highest to lowest source offset.</param>
	/// <param name="diagnostics">The diagnostics found during preparation.</param>
	public TextEditPreparationResult(
		IReadOnlyList<TextEditOperation> operations,
		IReadOnlyList<TextEditPreparationDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(operations);
		ArgumentNullException.ThrowIfNull(diagnostics);

		Operations = Array.AsReadOnly([.. operations]);
		Diagnostics = Array.AsReadOnly([.. diagnostics]);
	}

	/// <summary>
	/// Gets the prepared operations. This collection is empty when diagnostics exist.
	/// </summary>
	public IReadOnlyList<TextEditOperation> Operations { get; }

	/// <summary>
	/// Gets the deterministic preparation diagnostics.
	/// </summary>
	public IReadOnlyList<TextEditPreparationDiagnostic> Diagnostics { get; }

	/// <summary>
	/// Gets a value indicating whether the edit batch is valid and ready for a host adapter.
	/// </summary>
	public bool IsValid => Diagnostics.Count == 0;
}
