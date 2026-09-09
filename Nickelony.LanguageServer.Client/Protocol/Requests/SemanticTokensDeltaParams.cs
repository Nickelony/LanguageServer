using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a semantic tokens delta request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="PreviousResultId">The previously cached semantic tokens result identifier.</param>
public readonly record struct SemanticTokensDeltaParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("previousResultId")] string PreviousResultId);
