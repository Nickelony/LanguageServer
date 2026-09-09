using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads and writes <see cref="PublishDiagnosticsParams"/> values, skipping malformed diagnostic
/// entries with a warning instead of failing the whole notification.
/// </summary>
/// <remarks>
/// <para>
/// A diagnostics notification has no error channel back to the server, so one malformed entry must
/// not discard the remaining diagnostics for the document. Entries whose members do not bind (an
/// incomplete range, a non-integer severity) are dropped individually; a payload whose root is not
/// an object degrades to an empty payload. The transport registers this converter with its logger;
/// standalone deserialization falls back to no logging.
/// </para>
/// <para>
/// Serialization writes the standard protocol shape: an absent required member (<c>uri</c>, <c>diagnostics</c>)
/// is written as a JSON <see langword="null"/> so a round trip keeps a missing member distinguishable from an
/// empty list, while absent optional members (<c>version</c> and the optional diagnostic members) are omitted.
/// An absent member and an explicit JSON null both read to <see langword="null"/>, so a re-serialized payload
/// cannot restore which of the two the server sent. All modeled members (including <c>tags</c>,
/// <c>codeDescription</c>, <c>relatedInformation</c>, and <c>data</c>) round-trip; unmodeled protocol members are
/// not preserved. Member lookups accept any casing, mirroring the transport's case-insensitive member binding.
/// </para>
/// </remarks>
public sealed class PublishDiagnosticsParamsJsonConverter : JsonConverter<PublishDiagnosticsParams>
{
	/// <summary>
	/// The logger that receives malformed-payload diagnostics.
	/// </summary>
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="PublishDiagnosticsParamsJsonConverter"/> class.
	/// </summary>
	public PublishDiagnosticsParamsJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="PublishDiagnosticsParamsJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public PublishDiagnosticsParamsJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override PublishDiagnosticsParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning(
				"Ignoring malformed publish-diagnostics payload because its JSON kind {Kind} is not an object.",
				root.ValueKind);

			return default;
		}

		string? uri = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "uri", out JsonElement uriElement))
		{
			if (uriElement.ValueKind == JsonValueKind.String)
				uri = uriElement.GetString();
			else if (uriElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Ignoring malformed publish-diagnostics 'uri' property with JSON kind {Kind}.", uriElement.ValueKind);
		}

		int? version = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "version", out JsonElement versionElement))
		{
			if (versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out int parsedVersion))
				version = parsedVersion;
			else if (versionElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Ignoring malformed publish-diagnostics 'version' property with JSON kind {Kind}.", versionElement.ValueKind);
		}

		IReadOnlyList<DiagnosticPayload>? diagnostics = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "diagnostics", out JsonElement diagnosticsElement))
		{
			if (diagnosticsElement.ValueKind == JsonValueKind.Array)
			{
				diagnostics = DeserializeDiagnostics(diagnosticsElement, options);
			}
			else if (diagnosticsElement.ValueKind != JsonValueKind.Null)
			{
				_logger.LogWarning(
					"Ignoring malformed publish-diagnostics 'diagnostics' property with JSON kind {Kind}.",
					diagnosticsElement.ValueKind);
			}
		}

		return new(uri, version, diagnostics);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, PublishDiagnosticsParams value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteString("uri", value.Uri);

		if (value.Version is int version)
		{
			writer.WritePropertyName("version");
			writer.WriteNumberValue(version);
		}

		writer.WritePropertyName("diagnostics");

		if (value.Diagnostics is null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStartArray();

			foreach (DiagnosticPayload diagnostic in value.Diagnostics)
				JsonSerializer.Serialize(writer, diagnostic, options);

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Deserializes the diagnostic entries, dropping entries that cannot be bound.
	/// </summary>
	/// <param name="diagnosticsElement">The diagnostics array element.</param>
	/// <param name="options">The serializer options that carry the typed member converters.</param>
	/// <returns>The entries that could be bound.</returns>
	private List<DiagnosticPayload> DeserializeDiagnostics(JsonElement diagnosticsElement, JsonSerializerOptions options)
	{
		var diagnostics = new List<DiagnosticPayload>();

		foreach (JsonElement diagnosticElement in diagnosticsElement.EnumerateArray())
		{
			try
			{
				diagnostics.Add(diagnosticElement.Deserialize<DiagnosticPayload>(options));
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception,
					"Skipping malformed diagnostic because its payload could not be deserialized.");
			}
		}

		return diagnostics;
	}
}
