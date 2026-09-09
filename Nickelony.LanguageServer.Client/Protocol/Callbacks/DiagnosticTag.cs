namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies a diagnostic tag as defined by the LSP specification.
/// </summary>
public enum DiagnosticTag
{
	/// <summary>
	/// The diagnostic is unnecessary.
	/// </summary>
	Unnecessary = 1,

	/// <summary>
	/// The diagnostic is deprecated.
	/// </summary>
	Deprecated = 2
}
