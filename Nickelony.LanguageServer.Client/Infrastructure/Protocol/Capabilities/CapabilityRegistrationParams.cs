using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a dynamic capability registration request from the language server.
/// </summary>
/// <param name="Registrations">The requested capability registrations.</param>
public readonly record struct CapabilityRegistrationParams(
	[property: JsonPropertyName("registrations")] CapabilityRegistrationPayload[]? Registrations);

/// <summary>
/// Represents one dynamic capability registration entry requested by the language server.
/// </summary>
/// <param name="Id">The server-defined registration identifier.</param>
/// <param name="Method">The capability method being registered.</param>
public readonly record struct CapabilityRegistrationPayload(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("method")] string? Method);
