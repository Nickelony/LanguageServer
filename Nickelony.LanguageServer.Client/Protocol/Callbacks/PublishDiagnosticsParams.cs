using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a typed diagnostics notification raised by the language server for a document URI.
/// </summary>
/// <param name="Uri">The document URI receiving diagnostics.</param>
/// <param name="Version">The document version associated with the diagnostics.</param>
/// <param name="Diagnostics">The diagnostic entries for the document.</param>
/// <remarks>
/// <see cref="Diagnostics"/> is exposed as a read-only sequence so subscribers cannot mutate shared payload
/// storage. Use <see cref="CreateSnapshot"/> to detach the sequence from the instance it was received on.
/// Malformed entries are dropped individually with a warning; see
/// <see cref="PublishDiagnosticsParamsJsonConverter"/>.
/// </remarks>
[JsonConverter(typeof(PublishDiagnosticsParamsJsonConverter))]
public readonly record struct PublishDiagnosticsParams(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("version")] int? Version,
	[property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticPayload>? Diagnostics)
{
	/// <summary>
	/// Creates a detached diagnostics snapshot so queued subscribers do not share the same sequence instance.
	/// </summary>
	/// <returns>The detached diagnostics payload.</returns>
	public PublishDiagnosticsParams CreateSnapshot()
		=> this with { Diagnostics = Diagnostics is null ? null : [.. Diagnostics] };
}

/// <summary>
/// Represents a single diagnostic entry from a publish-diagnostics notification.
/// </summary>
/// <param name="Range">The affected document range.</param>
/// <param name="Severity">
/// The typed protocol severity, or <see langword="null"/> when the server omitted it. LSP leaves a missing
/// severity to the client and this payload keeps it <see langword="null"/> instead of defaulting it, so a
/// host can apply its own interpretation. An unknown protocol value stays representable as an
/// unnamed <see cref="DiagnosticSeverity"/> value.
/// </param>
/// <param name="Message">The user-facing diagnostic message.</param>
/// <param name="Source">The diagnostic source identifier.</param>
/// <param name="Code">The optional diagnostic code value.</param>
/// <param name="Tags">The optional LSP tags (for example unnecessary or deprecated).</param>
/// <param name="CodeDescription">The optional link to the diagnostic's documentation.</param>
/// <param name="RelatedInformation">The optional related locations attached to the diagnostic.</param>
/// <param name="Data">The optional server-specific data payload.</param>
public readonly record struct DiagnosticPayload(
	[property: JsonPropertyName("range")] ProtocolRangePayload? Range,
	[property: JsonPropertyName("severity")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	DiagnosticSeverity? Severity,
	[property: JsonPropertyName("message")] string? Message,
	[property: JsonPropertyName("source")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Source,
	[property: JsonPropertyName("code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Code,
	[property: JsonPropertyName("tags")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<DiagnosticTag>? Tags = null,
	[property: JsonPropertyName("codeDescription")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	DiagnosticCodeDescriptionPayload? CodeDescription = null,
	[property: JsonPropertyName("relatedInformation")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<DiagnosticRelatedInformationPayload>? RelatedInformation = null,
	[property: JsonPropertyName("data")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Data = null);

/// <summary>
/// Represents the optional code-description link of a diagnostic.
/// </summary>
/// <param name="Href">The documentation URI.</param>
public readonly record struct DiagnosticCodeDescriptionPayload(
	[property: JsonPropertyName("href")] string? Href);

/// <summary>
/// Represents one related location attached to a diagnostic.
/// </summary>
/// <param name="Location">The related location.</param>
/// <param name="Message">The message describing the relation.</param>
public readonly record struct DiagnosticRelatedInformationPayload(
	[property: JsonPropertyName("location")] ProtocolLocationPayload? Location,
	[property: JsonPropertyName("message")] string? Message);
