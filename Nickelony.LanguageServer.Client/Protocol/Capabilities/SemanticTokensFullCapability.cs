using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the semantic tokens full capability advertised by the server.
/// </summary>
/// <param name="IsSupported">Whether full semantic token requests are supported.</param>
/// <param name="SupportsDelta">Whether semantic token delta requests are supported.</param>
[JsonConverter(typeof(SemanticTokensFullCapabilityJsonConverter))]
internal readonly record struct SemanticTokensFullCapability(bool IsSupported, bool SupportsDelta);
