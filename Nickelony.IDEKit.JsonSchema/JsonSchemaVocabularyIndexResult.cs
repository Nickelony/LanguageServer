namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// The explicit outcome of building a <see cref="JsonSchemaVocabularyIndex"/> from a schema.
/// Build failures, such as schema text that cannot be parsed, are reported through
/// <see cref="Succeeded"/> and <see cref="Diagnostics"/> instead of being logged. A null
/// argument is still reported as an argument error by the corresponding builder method.
/// </summary>
public sealed class JsonSchemaVocabularyIndexResult
{
	/// <summary>
	/// Gets whether an index is available. A successful build may still have diagnostics for
	/// individual entries that were skipped.
	/// </summary>
	public bool Succeeded { get; }

	/// <summary>
	/// Gets the built vocabulary index, or <see langword="null"/> when no index was produced.
	/// </summary>
	public JsonSchemaVocabularyIndex? Index { get; }

	/// <summary>
	/// Gets the messages produced while building the vocabulary index. A failed build contains an
	/// error message; a successful build may contain warnings about entries that were skipped,
	/// such as a <c>definitions</c> or <c>$defs</c> entry that is not a schema object.
	/// </summary>
	public IReadOnlyList<string> Diagnostics { get; }

	internal JsonSchemaVocabularyIndexResult(JsonSchemaVocabularyIndex? index, IReadOnlyList<string> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		Succeeded = index is not null;
		Index = index;
		Diagnostics = Array.AsReadOnly([.. diagnostics]);
	}
}
