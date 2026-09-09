using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Converts definition responses between LSP location or location-link payloads and typed definition targets.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is
/// not invoked for JSON null. Malformed target entries are skipped instead of failing the whole response.
/// </remarks>
public sealed class DefinitionResponseJsonConverter : JsonConverter<DefinitionResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="DefinitionResponseJsonConverter"/> class.
	/// </summary>
	public DefinitionResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="DefinitionResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public DefinitionResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override DefinitionResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind == JsonValueKind.Array)
		{
			var targets = new List<DefinitionTargetPayload>();

			foreach (JsonElement definitionElement in root.EnumerateArray())
			{
				if (TryParseDefinitionTarget(definitionElement, out DefinitionTargetPayload target))
					targets.Add(target);
				else if (definitionElement.ValueKind == JsonValueKind.Object)
					_logger.LogWarning("Ignoring a definition target that did not contain a usable URI and range.");
			}

			return new([.. targets]);
		}

		if (TryParseDefinitionTarget(root, out DefinitionTargetPayload targetResponse))
			return new DefinitionResponse([targetResponse]);

		if (root.ValueKind == JsonValueKind.Object)
			_logger.LogWarning("Ignoring a definition target that did not contain a usable URI and range.");

		return new DefinitionResponse([]);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, DefinitionResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.Targets.Count; i++)
			WriteDefinitionTarget(writer, value.Targets[i]);

		writer.WriteEndArray();
	}

	private static void WriteDefinitionTarget(Utf8JsonWriter writer, DefinitionTargetPayload target)
	{
		if (target.Uri is null)
			throw new InvalidOperationException("A definition target without a URI cannot be serialized.");

		writer.WriteStartObject();

		if (target.SelectionRange is { } selectionRange)
		{
			// The parsed payload was a location link, so write its distinct selection range back.
			writer.WriteString("targetUri", target.Uri);

			writer.WritePropertyName("targetRange");
			ProtocolJsonWriteHelpers.WriteRange(writer, target.TargetRange);
			writer.WritePropertyName("targetSelectionRange");
			ProtocolJsonWriteHelpers.WriteRange(writer, selectionRange);

			if (target.OriginSelectionRange is { } originSelectionRange)
			{
				writer.WritePropertyName("originSelectionRange");
				ProtocolJsonWriteHelpers.WriteRange(writer, originSelectionRange);
			}
		}
		else
		{
			writer.WriteString("uri", target.Uri);

			writer.WritePropertyName("range");
			ProtocolJsonWriteHelpers.WriteRange(writer, target.TargetRange);
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Parses one usable definition target from a definition payload element.
	/// </summary>
	/// <param name="definitionElement">The definition payload element.</param>
	/// <param name="target">Receives the parsed definition target.</param>
	/// <returns><see langword="true"/> when a usable target was found.</returns>
	private static bool TryParseDefinitionTarget(JsonElement definitionElement, out DefinitionTargetPayload target)
	{
		target = default;

		if (definitionElement.ValueKind != JsonValueKind.Object)
			return false;

		// Non-string URI properties are treated as absent so one malformed entry cannot fail the response.
		string? uri = JsonElementReadHelpers.TryGetString(definitionElement, "targetUri")
			?? JsonElementReadHelpers.TryGetString(definitionElement, "uri");

		if (string.IsNullOrWhiteSpace(uri)
			|| !Uri.TryCreate(uri, UriKind.Absolute, out _))
		{
			return false;
		}

		// Location links carry a distinct selection range; plain locations carry one range that
		// serves as the target range.
		ProtocolRangePayload? selectionRange = TryParseRange(definitionElement, "targetSelectionRange");
		ProtocolRangePayload? targetRange = TryParseRange(definitionElement, "targetRange")
			?? TryParseRange(definitionElement, "range")
			?? selectionRange;

		if (targetRange is null)
			return false;

		// The optional origin range identifies the range in the requesting document that the link refers to; it is
		// preserved for hosts that highlight the origin of a location link.
		ProtocolRangePayload? originSelectionRange = TryParseRange(definitionElement, "originSelectionRange");

		target = new DefinitionTargetPayload(uri, targetRange.Value, selectionRange, originSelectionRange);
		return true;
	}

	/// <summary>
	/// Parses one protocol range property of a definition payload element.
	/// </summary>
	/// <param name="definitionElement">The definition payload element.</param>
	/// <param name="rangeProperty">The property name that carries the range.</param>
	/// <returns>The parsed zero-based protocol range, or <see langword="null"/> when the property carries none.</returns>
	private static ProtocolRangePayload? TryParseRange(JsonElement definitionElement, string rangeProperty)
	{
		if (!JsonElementReadHelpers.TryGetProperty(definitionElement, rangeProperty, out JsonElement rangeElement)
			|| rangeElement.ValueKind != JsonValueKind.Object
			|| !JsonElementReadHelpers.TryReadPosition(rangeElement, "start", out ProtocolPosition start, requireNonNegativeCoordinates: true))
		{
			return null;
		}

		// A missing or malformed end position degrades to an empty range at the start so a previously usable
		// target stays usable; an inverted range identifies no content, so it is rejected instead of turning a
		// malformed payload into a real range.
		if (!JsonElementReadHelpers.TryReadPosition(rangeElement, "end", out ProtocolPosition end, requireNonNegativeCoordinates: true))
		{
			end = start;
		}

		if (end.Line < start.Line || (end.Line == start.Line && end.Character < start.Character))
			return null;

		return new ProtocolRangePayload(start, end);
	}
}
