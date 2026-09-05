namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Describes a property in a JSON Schema vocabulary index. The descriptor is immutable and
/// includes the property's name, JSON types, and description.
/// </summary>
public sealed class JsonSchemaVocabularyPropertyDescriptor
{
	/// <summary>
	/// Gets the property name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the JSON types supplied for the property, or an empty list when none were supplied.
	/// </summary>
	public IReadOnlyList<JsonSchemaPropertyType> Types { get; }

	/// <summary>
	/// Gets the property description, or <see langword="null"/> when none was supplied.
	/// </summary>
	public string? Description { get; }

	/// <summary>
	/// Gets whether the only type in <see cref="Types"/> is <see cref="JsonSchemaPropertyType.Array"/>.
	/// </summary>
	public bool IsArray => Types.Count == 1 && Types[0] == JsonSchemaPropertyType.Array;

	/// <summary>
	/// Initializes a new instance of the <see cref="JsonSchemaVocabularyPropertyDescriptor"/> class.
	/// </summary>
	/// <param name="name">The property name.</param>
	/// <param name="types">The declared JSON types.</param>
	/// <param name="description">The property description, which may be <see langword="null"/>.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="types"/> is null.
	/// </exception>
	public JsonSchemaVocabularyPropertyDescriptor(string name, IReadOnlyList<JsonSchemaPropertyType> types, string? description)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(types);

		Name = name;

		// Keep a read-only copy of the supplied types.
		Types = Array.AsReadOnly([.. types]);
		Description = description;
	}
}
