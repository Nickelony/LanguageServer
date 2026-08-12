using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Abstractions.Completion;

/// <summary>
/// Identifies the semantic category of a completion item for icon and styling purposes.
/// </summary>
/// <remarks>
/// Well-known categories cover presentation concepts shared by language providers and editor hosts. Providers and
/// hosts may use <see cref="CreateCustom(string)"/> for additional categories without changing this assembly. Identifiers
/// are normalized by trimming surrounding whitespace, compared with ordinal case-sensitive equality, and serialized as
/// strings. A renderer that does not recognize a custom identifier should use <see cref="Generic"/> presentation while
/// preserving the original <see cref="Identifier"/> value in the model.
/// </remarks>
[JsonConverter(typeof(TextCompletionItemKindJsonConverter))]
public sealed class TextCompletionItemKind : IEquatable<TextCompletionItemKind>
{
	private const int MaximumIdentifierLength = 128;

	/// <summary>
	/// A generic completion item with no specialized category.
	/// </summary>
	public static TextCompletionItemKind Generic { get; } = new("Generic", isWellKnown: true);

	/// <summary>
	/// A property-like member.
	/// </summary>
	public static TextCompletionItemKind Property { get; } = new("Property", isWellKnown: true);

	/// <summary>
	/// An array-like value or container.
	/// </summary>
	public static TextCompletionItemKind Array { get; } = new("Array", isWellKnown: true);

	/// <summary>
	/// A section header or named block.
	/// </summary>
	public static TextCompletionItemKind Section { get; } = new("Section", isWellKnown: true);

	/// <summary>
	/// A directive or pragma-style keyword.
	/// </summary>
	public static TextCompletionItemKind Directive { get; } = new("Directive", isWellKnown: true);

	/// <summary>
	/// A constant value.
	/// </summary>
	public static TextCompletionItemKind Constant { get; } = new("Constant", isWellKnown: true);

	/// <summary>
	/// A language keyword.
	/// </summary>
	public static TextCompletionItemKind Keyword { get; } = new("Keyword", isWellKnown: true);

	/// <summary>
	/// A callable method or function.
	/// </summary>
	public static TextCompletionItemKind Method { get; } = new("Method", isWellKnown: true);

	/// <summary>
	/// A variable.
	/// </summary>
	public static TextCompletionItemKind Variable { get; } = new("Variable", isWellKnown: true);

	/// <summary>
	/// A field-like member.
	/// </summary>
	public static TextCompletionItemKind Field { get; } = new("Field", isWellKnown: true);

	/// <summary>
	/// A type or class.
	/// </summary>
	public static TextCompletionItemKind Class { get; } = new("Class", isWellKnown: true);

	/// <summary>
	/// A parameter.
	/// </summary>
	public static TextCompletionItemKind Parameter { get; } = new("Parameter", isWellKnown: true);

	/// <summary>
	/// A namespace or module scope.
	/// </summary>
	public static TextCompletionItemKind Namespace { get; } = new("Namespace", isWellKnown: true);

	/// <summary>
	/// A file system file.
	/// </summary>
	public static TextCompletionItemKind File { get; } = new("File", isWellKnown: true);

	/// <summary>
	/// A file system folder.
	/// </summary>
	public static TextCompletionItemKind Folder { get; } = new("Folder", isWellKnown: true);

	private static readonly Dictionary<string, TextCompletionItemKind> s_wellKnownKinds =
		new(StringComparer.Ordinal)
		{
			[Generic.Identifier] = Generic,
			[Property.Identifier] = Property,
			[Array.Identifier] = Array,
			[Section.Identifier] = Section,
			[Directive.Identifier] = Directive,
			[Constant.Identifier] = Constant,
			[Keyword.Identifier] = Keyword,
			[Method.Identifier] = Method,
			[Variable.Identifier] = Variable,
			[Field.Identifier] = Field,
			[Class.Identifier] = Class,
			[Parameter.Identifier] = Parameter,
			[Namespace.Identifier] = Namespace,
			[File.Identifier] = File,
			[Folder.Identifier] = Folder
		};

	private readonly bool _isWellKnown;

	private TextCompletionItemKind(string identifier, bool isWellKnown)
	{
		Identifier = identifier;
		_isWellKnown = isWellKnown;
	}

	/// <summary>
	/// Gets the stable serialized identifier for this completion category.
	/// </summary>
	public string Identifier { get; }

	/// <summary>
	/// Gets a value indicating whether this category is one of the shared well-known categories.
	/// </summary>
	public bool IsWellKnown => _isWellKnown;

	/// <summary>
	/// Creates a custom completion category.
	/// </summary>
	/// <param name="identifier">The custom identifier. Letters, digits, underscore, period, hyphen, and colon are allowed.</param>
	/// <returns>An immutable custom completion category.</returns>
	/// <exception cref="ArgumentException">The identifier is invalid or reserved for a well-known category.</exception>
	public static TextCompletionItemKind CreateCustom(string identifier)
	{
		string normalizedIdentifier = NormalizeAndValidate(identifier);

		if (s_wellKnownKinds.ContainsKey(normalizedIdentifier))
			throw new ArgumentException($"The completion-kind identifier '{normalizedIdentifier}' is reserved.", nameof(identifier));

		return new(normalizedIdentifier, isWellKnown: false);
	}

	/// <summary>
	/// Creates a category from a serialized identifier, retaining unknown identifiers as custom categories.
	/// </summary>
	/// <param name="identifier">The well-known or custom identifier.</param>
	/// <returns>The matching well-known category or a new custom category.</returns>
	/// <exception cref="ArgumentException">The identifier is invalid.</exception>
	public static TextCompletionItemKind FromIdentifier(string identifier)
	{
		string normalizedIdentifier = NormalizeAndValidate(identifier);

		return s_wellKnownKinds.TryGetValue(normalizedIdentifier, out TextCompletionItemKind? wellKnownKind)
			? wellKnownKind
			: new(normalizedIdentifier, isWellKnown: false);
	}

	/// <inheritdoc/>
	public bool Equals(TextCompletionItemKind? other)
		=> other is not null && StringComparer.Ordinal.Equals(Identifier, other.Identifier);

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is TextCompletionItemKind other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> StringComparer.Ordinal.GetHashCode(Identifier);

	/// <inheritdoc/>
	public override string ToString() => Identifier;

	/// <summary>
	/// Compares completion categories by their normalized ordinal identifiers.
	/// </summary>
	public static bool operator ==(TextCompletionItemKind? left, TextCompletionItemKind? right)
		=> ReferenceEquals(left, right) || left is not null && left.Equals(right);

	/// <summary>
	/// Compares completion categories by their normalized ordinal identifiers.
	/// </summary>
	public static bool operator !=(TextCompletionItemKind? left, TextCompletionItemKind? right)
		=> !(left == right);

	private static string NormalizeAndValidate(string identifier)
	{
		string normalizedIdentifier = identifier.Trim();

		if (normalizedIdentifier.Length == 0)
			throw new ArgumentException("A completion-kind identifier cannot be empty.", nameof(identifier));

		if (normalizedIdentifier.Length > MaximumIdentifierLength)
			throw new ArgumentException($"A completion-kind identifier cannot exceed {MaximumIdentifierLength} characters.", nameof(identifier));

		if (!IsIdentifierStart(normalizedIdentifier[0]))
			throw new ArgumentException("A completion-kind identifier must start with a letter or underscore.", nameof(identifier));

		for (int i = 1; i < normalizedIdentifier.Length; i++)
		{
			if (!IsIdentifierPart(normalizedIdentifier[i]))
				throw new ArgumentException("A completion-kind identifier may contain only letters, digits, underscore, period, hyphen, or colon.", nameof(identifier));
		}

		return normalizedIdentifier;
	}

	private static bool IsIdentifierStart(char value)
		=> value == '_' || value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

	private static bool IsIdentifierPart(char value)
		=> IsIdentifierStart(value) || value is >= '0' and <= '9' or '.' or '-' or ':';
}

/// <summary>
/// Serializes completion-kind identifiers as strings and recreates unknown values as custom categories.
/// </summary>
public sealed class TextCompletionItemKindJsonConverter : JsonConverter<TextCompletionItemKind>
{
	/// <inheritdoc/>
	public override TextCompletionItemKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException("A completion-kind value must be a string identifier.");

		try
		{
			return TextCompletionItemKind.FromIdentifier(reader.GetString() ?? string.Empty);
		}
		catch (ArgumentException exception)
		{
			throw new JsonException("The completion-kind identifier is invalid.", exception);
		}
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, TextCompletionItemKind value, JsonSerializerOptions options)
		=> writer.WriteStringValue(value.Identifier);
}
