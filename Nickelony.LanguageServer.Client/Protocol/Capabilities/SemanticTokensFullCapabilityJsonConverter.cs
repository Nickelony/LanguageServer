using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Converts the semantic token full capability and whether delta refresh is supported.
/// </summary>
/// <remarks>
/// Serialization writes the boolean form when no delta support was advertised, and the object form with the
/// <c>delta</c> flag otherwise.
/// </remarks>
internal sealed class SemanticTokensFullCapabilityJsonConverter : JsonConverter<SemanticTokensFullCapability>
{
	private readonly record struct SemanticTokensFullCapabilityObject(
		[property: JsonPropertyName("delta")] JsonElement Delta);

	/// <inheritdoc/>
	public override SemanticTokensFullCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new SemanticTokensFullCapability(true, false),
			JsonTokenType.False => new SemanticTokensFullCapability(false, false),
			JsonTokenType.StartObject => ReadObject(ref reader, options),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader)
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SemanticTokensFullCapability value, JsonSerializerOptions options)
	{
		if (!value.IsSupported)
		{
			writer.WriteBooleanValue(false);
			return;
		}

		if (!value.SupportsDelta)
		{
			writer.WriteBooleanValue(true);
			return;
		}

		writer.WriteStartObject();
		writer.WriteBoolean("delta", true);
		writer.WriteEndObject();
	}

	private static SemanticTokensFullCapability ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
	{
		SemanticTokensFullCapabilityObject payload = JsonSerializer.Deserialize<SemanticTokensFullCapabilityObject>(ref reader, options);
		bool supportsDelta = payload.Delta.ValueKind == JsonValueKind.True;

		return new(true, supportsDelta);
	}

	private static SemanticTokensFullCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new(false, false);
	}
}
