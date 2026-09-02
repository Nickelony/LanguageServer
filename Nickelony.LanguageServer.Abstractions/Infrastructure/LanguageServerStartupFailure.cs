namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a language-server startup failure that should be surfaced to the UI.
/// </summary>
/// <param name="Message">The user-facing failure message.</param>
/// <param name="IsPersistent">Whether the failure is terminal for this provider instance and should not be retried automatically.</param>
public readonly record struct LanguageServerStartupFailure(string Message, bool IsPersistent);
