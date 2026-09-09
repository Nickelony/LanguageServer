using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads collections of <typeparamref name="TElement"/> while skipping null or malformed elements with a warning
/// instead of failing the whole response.
/// </summary>
/// <typeparam name="TElement">The element type whose collections are read tolerantly.</typeparam>
/// <remarks>
/// <para>
/// Several LSP results are bare lists whose entries a server can get wrong individually. A single malformed entry
/// must not discard the remaining usable entries, so this factory applies the same skip-with-a-warning policy to
/// every <typeparamref name="TElement"/> array or read-only list the transport deserializes, including lists that
/// are nested inside other payloads. A root that is not an array degrades to an empty collection with a warning
/// because a result list has no in-band way to report an unparseable payload; embedded list members that belong to
/// a payload record keep that record's own malformed-root policy instead.
/// </para>
/// <para>
/// Only reading is tolerant; writing delegates every element to its normal contract so outbound payloads stay
/// strict. Register one instance per element type with the transport serializer options.
/// </para>
/// </remarks>
public sealed class TolerantCollectionJsonConverter<TElement> : JsonConverterFactory
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="TolerantCollectionJsonConverter{TElement}"/> class.
	/// </summary>
	public TolerantCollectionJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="TolerantCollectionJsonConverter{TElement}"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public TolerantCollectionJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override bool CanConvert(Type typeToConvert)
		=> typeToConvert == typeof(TElement[])
			|| typeToConvert == typeof(IReadOnlyList<TElement>)
			|| typeToConvert == typeof(IList<TElement>)
			|| typeToConvert == typeof(List<TElement>);

	/// <inheritdoc/>
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
		=> (JsonConverter)Activator.CreateInstance(
			typeof(TolerantCollectionConverter<>).MakeGenericType(typeof(TElement), typeToConvert),
			_logger)!;

	/// <summary>
	/// Reads and writes one concrete collection shape of <typeparamref name="TElement"/>.
	/// </summary>
	/// <typeparam name="TCollection">The collection shape to read.</typeparam>
	private sealed class TolerantCollectionConverter<TCollection> : JsonConverter<TCollection>
	{
		private readonly ILogger _logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="TolerantCollectionConverter{TCollection}"/> class.
		/// </summary>
		/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
		public TolerantCollectionConverter(ILogger logger)
			=> _logger = logger;

		/// <inheritdoc/>
		public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using JsonDocument document = JsonDocument.ParseValue(ref reader);
			JsonElement root = document.RootElement;

			if (root.ValueKind != JsonValueKind.Array)
			{
				_logger.LogWarning(
					"Ignoring malformed {ElementType} collection because its JSON kind {Kind} is not an array.",
					typeof(TElement).Name,
					root.ValueKind);

				return Materialize([]);
			}

			var elements = new List<TElement>(root.GetArrayLength());

			foreach (JsonElement element in root.EnumerateArray())
			{
				if (element.ValueKind == JsonValueKind.Null)
				{
					_logger.LogWarning("Skipping a null {ElementType} element.", typeof(TElement).Name);
					continue;
				}

				try
				{
					TElement? value = element.Deserialize<TElement>(options);

					if (value is null)
					{
						_logger.LogWarning("Skipping a {ElementType} element that deserialized to null.", typeof(TElement).Name);
						continue;
					}

					elements.Add(value);
				}
				catch (Exception exception) when (exception is JsonException or InvalidOperationException)
				{
					_logger.LogWarning(exception, "Skipping a malformed {ElementType} element.", typeof(TElement).Name);
				}
			}

			return Materialize(elements);
		}

		/// <inheritdoc/>
		public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();

			// The serializer never invokes a converter for a null reference value, so the collection is non-null here.
			foreach (TElement element in (IEnumerable<TElement>)value!)
				JsonSerializer.Serialize(writer, element, options);

			writer.WriteEndArray();
		}

		/// <summary>
		/// Converts the accumulated elements back into the requested collection shape.
		/// </summary>
		/// <param name="elements">The elements to materialize.</param>
		/// <returns>The collection instance for the requested shape.</returns>
		private static TCollection? Materialize(List<TElement> elements)
			=> typeof(TCollection) == typeof(TElement[])
				? (TCollection)(object)elements.ToArray()
				: (TCollection)(object)elements;
	}
}
