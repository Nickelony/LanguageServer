namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a failure to start the language server.
/// </summary>
/// <param name="Message">The message to show to the user.</param>
/// <param name="IsPersistent">Whether the provider treats the failure as persistent and stops automatic retry for this instance.</param>
public readonly record struct LanguageServerStartupFailure(string Message, bool IsPersistent);
