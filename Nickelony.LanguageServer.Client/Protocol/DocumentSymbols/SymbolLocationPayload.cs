using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the location of a flat document-symbol entry.
/// </summary>
/// <param name="Uri">The target document URI; parsed payloads always carry one.</param>
/// <param name="Range">The range of the symbol in the target document.</param>
public readonly record struct SymbolLocationPayload(
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("range")] ProtocolRangePayload Range);
