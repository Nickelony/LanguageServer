namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Immutable vocabulary extracted from a JSON Schema for completion, hover, and highlighting.
/// It collects property descriptors and string values from the root schema and from schemas
/// reached through properties, array items, combinators, resolved references, and the root
/// schema's <c>definitions</c> and <c>$defs</c> sections. Duplicate names and values are
/// removed; the first occurrence in traversal order wins. Instances are produced by
/// <see cref="JsonSchemaVocabularyIndexBuilder"/>, and the constructor copies its input lists.
/// </summary>
public sealed class JsonSchemaVocabularyIndex
{
	/// <summary>
	/// Gets the property names collected from the root schema and the supported nested schema
	/// locations. Duplicate names are removed, and each descriptor retains the first occurrence's
	/// declared types and description.
	/// </summary>
	public IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> Properties { get; }

	/// <summary>
	/// Gets the distinct string values found in <c>const</c> and <c>enum</c> declarations, in
	/// first-seen order.
	/// </summary>
	public IReadOnlyList<string> Constants { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="JsonSchemaVocabularyIndex"/> class.
	/// </summary>
	/// <param name="properties">The schema-wide property vocabulary.</param>
	/// <param name="constants">The schema-derived constant string values.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="properties"/> or <paramref name="constants"/> is null.
	/// </exception>
	public JsonSchemaVocabularyIndex(IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> properties, IReadOnlyList<string> constants)
	{
		ArgumentNullException.ThrowIfNull(properties);
		ArgumentNullException.ThrowIfNull(constants);

		// Keep private read-only copies of the supplied lists.
		Properties = Array.AsReadOnly([.. properties]);
		Constants = Array.AsReadOnly([.. constants]);
	}
}
