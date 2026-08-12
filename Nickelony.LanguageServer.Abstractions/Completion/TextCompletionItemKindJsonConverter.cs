using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Abstractions.Completion;

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
