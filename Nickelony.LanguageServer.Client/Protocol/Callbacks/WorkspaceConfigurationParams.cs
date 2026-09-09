using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a workspace configuration request payload.
/// </summary>
/// <param name="Items">The requested configuration sections. Null entries are skipped during binding.</param>
internal readonly record struct WorkspaceConfigurationParams(
	[property: JsonPropertyName("items")] WorkspaceConfigurationItem[]? Items);

/// <summary>
/// Identifies a single configuration section requested from the host.
/// </summary>
/// <param name="Section">The dotted configuration section name.</param>
/// <remarks>
/// LSP allows a request item to carry a <c>scopeUri</c> so a server can ask for per-resource configuration. This
/// item models the section only, and the client answers every item from its single global settings snapshot, so
/// per-resource scopes are not honored.
/// </remarks>
internal readonly record struct WorkspaceConfigurationItem(
	[property: JsonPropertyName("section")] string? Section);
