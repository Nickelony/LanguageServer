using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed result of the LSP initialize request.
/// </summary>
internal sealed record InitializeResponse
{
	/// <summary>
	/// Gets the server capabilities advertised during initialization.
	/// </summary>
	[JsonPropertyName("capabilities")]
	public ServerCapabilities? Capabilities { get; init; }
}

/// <summary>
/// Represents the subset of server capabilities consumed by this client.
/// </summary>
internal sealed record ServerCapabilities
{
	/// <summary>
	/// Gets the text-document synchronization capability.
	/// </summary>
	[JsonPropertyName("textDocumentSync")]
	public TextDocumentSyncCapability? TextDocumentSync { get; init; }

	/// <summary>
	/// Gets the completion provider capability.
	/// </summary>
	[JsonPropertyName("completionProvider")]
	public CompletionProviderCapability? CompletionProvider { get; init; }

	/// <summary>
	/// Gets the hover provider capability.
	/// </summary>
	[JsonPropertyName("hoverProvider")]
	public SupportedCapability? HoverProvider { get; init; }

	/// <summary>
	/// Gets the definition provider capability.
	/// </summary>
	[JsonPropertyName("definitionProvider")]
	public SupportedCapability? DefinitionProvider { get; init; }

	/// <summary>
	/// Gets the document-symbol provider capability.
	/// </summary>
	[JsonPropertyName("documentSymbolProvider")]
	public SupportedCapability? DocumentSymbolProvider { get; init; }

	/// <summary>
	/// Gets the code-action provider capability.
	/// </summary>
	[JsonPropertyName("codeActionProvider")]
	public SupportedCapability? CodeActionProvider { get; init; }

	/// <summary>
	/// Gets the references provider capability.
	/// </summary>
	[JsonPropertyName("referencesProvider")]
	public SupportedCapability? ReferencesProvider { get; init; }

	/// <summary>
	/// Gets the rename provider capability.
	/// </summary>
	[JsonPropertyName("renameProvider")]
	public SupportedCapability? RenameProvider { get; init; }

	/// <summary>
	/// Gets the document-formatting provider capability.
	/// </summary>
	[JsonPropertyName("documentFormattingProvider")]
	public SupportedCapability? DocumentFormattingProvider { get; init; }

	/// <summary>
	/// Gets the signature-help provider capability.
	/// </summary>
	[JsonPropertyName("signatureHelpProvider")]
	public SupportedCapability? SignatureHelpProvider { get; init; }

	/// <summary>
	/// Gets the semantic tokens provider capability.
	/// </summary>
	[JsonPropertyName("semanticTokensProvider")]
	public SemanticTokensProviderCapability? SemanticTokensProvider { get; init; }

	/// <summary>
	/// Gets the position encoding the server selected for this session, when it advertised one.
	/// </summary>
	[JsonPropertyName("positionEncoding")]
	public string? PositionEncoding { get; init; }
}

/// <summary>
/// Represents completion-specific capabilities advertised by the server.
/// </summary>
internal sealed record CompletionProviderCapability
{
	/// <summary>
	/// Gets a value indicating whether completion-item resolve is supported.
	/// </summary>
	[JsonPropertyName("resolveProvider")]
	public bool? ResolveProvider { get; init; }
}

/// <summary>
/// Represents semantic tokens capabilities advertised by the server.
/// </summary>
internal sealed record SemanticTokensProviderCapability
{
	/// <summary>
	/// Gets the semantic tokens full-refresh capability.
	/// </summary>
	[JsonPropertyName("full")]
	public SemanticTokensFullCapability? Full { get; init; }

	/// <summary>
	/// Gets the semantic tokens legend advertised by the server.
	/// </summary>
	[JsonPropertyName("legend")]
	public SemanticTokensLegendCapability? Legend { get; init; }
}

/// <summary>
/// Represents the semantic tokens legend advertised by the server.
/// </summary>
/// <remarks>
/// The token type and modifier arrays are caller-owned DTO storage that should be treated as read-only.
/// </remarks>
internal sealed record SemanticTokensLegendCapability
{
	/// <summary>
	/// Gets the token type names advertised by the server.
	/// </summary>
	[JsonPropertyName("tokenTypes")]
	public string[]? TokenTypes { get; init; }

	/// <summary>
	/// Gets the token modifier names advertised by the server.
	/// </summary>
	[JsonPropertyName("tokenModifiers")]
	public string[]? TokenModifiers { get; init; }
}
